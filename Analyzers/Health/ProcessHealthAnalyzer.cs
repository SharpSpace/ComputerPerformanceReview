namespace ComputerPerformanceReview.Analyzers.Health;

public sealed class ProcessHealthAnalyzer : IHealthSubAnalyzer
{
    public string Domain => "Process";

    private const int SampleIntervalMs = 3000;

    // Handle leak
    private const int HandleLeakThreshold = 1000;
    private readonly Dictionary<int, (string Name, int HandleCount)> _prevHandleCounts = new();
    
    // Sysinternals integration
    private DateTime _lastSysinternalsRun = DateTime.MinValue;
    private const int SysinternalsIntervalSeconds = 30; // Run Handle.exe every 30 seconds
    private List<SysinternalsHandleInfo>? _cachedHandleData;
    private Task? _handleExeTask; // Track running Handle.exe task

    // GDI
    private const int GdiWarningThreshold = 5000;
    private const int GdiCriticalThreshold = 8000;

    // Thread explosion
    private const int ThreadExplosionThreshold = 200;
    private const int ThreadExplosionCritical = 500;
    private readonly Dictionary<int, (string Name, int ThreadCount)> _prevThreadCounts = new();

    // Hanging
    private readonly Dictionary<string, DateTime> _hangingTracker = new();

    // CPU baseline for per-process CPU calculation
    private readonly Dictionary<int, (string Name, TimeSpan CpuTime)> _prevCpuTimes = new();
    private readonly Dictionary<int, long> _prevPageFaults = new();
    private bool _hasPrevCpuTimes;

    public void Collect(MonitorSampleBuilder builder)
    {
        var topCpu = new List<MonitorProcessInfo>();
        var topMemory = new List<MonitorProcessInfo>();
        var topGdi = new List<MonitorProcessInfo>();
        var topIo = new List<MonitorIoProcessInfo>();
        var topFaults = new List<MonitorFaultProcessInfo>();
        var hangingNames = new List<string>();
        long totalSystemHandles = 0;
        int cpuCount = Environment.ProcessorCount;

        var currentCpuTimes = new Dictionary<int, (string Name, TimeSpan CpuTime)>();
        var currentPageFaults = new Dictionary<int, long>();

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                // Hanging check
                try
                {
                    if (proc.MainWindowHandle != IntPtr.Zero && !proc.Responding)
                        hangingNames.Add(proc.ProcessName);
                }
                catch { }

                // Handle count
                int handleCount = 0;
                try
                {
                    handleCount = proc.HandleCount;
                    totalSystemHandles += handleCount;
                }
                catch { }

                // GDI/USER objects
                int gdiObjects = 0, userObjects = 0;
                try
                {
                    var handle = proc.Handle;
                    (gdiObjects, userObjects) = NativeMethods.GetGuiResourceCounts(handle);
                }
                catch { }

                // Thread count
                int threadCount = 0;
                try { threadCount = proc.Threads.Count; } catch { }

                // CPU delta
                double procCpu = 0;
                try
                {
                    var cpuTime = proc.TotalProcessorTime;
                    currentCpuTimes[proc.Id] = (proc.ProcessName, cpuTime);

                    if (_hasPrevCpuTimes && _prevCpuTimes.TryGetValue(proc.Id, out var prev))
                    {
                        var delta = cpuTime - prev.CpuTime;
                        procCpu = delta.TotalMilliseconds / (SampleIntervalMs * (double)cpuCount) * 100;
                        procCpu = Math.Max(0, Math.Min(100, procCpu));
                    }
                }
                catch { }

                long memBytes = 0;
                try { memBytes = proc.WorkingSet64; } catch { }

                try
                {
                    if (proc.TryGetPageFaultCount(out var pf))
                    {
                        currentPageFaults[proc.Id] = pf;
                        if (_prevPageFaults.TryGetValue(proc.Id, out var prevPf) && pf > prevPf)
                        {
                            double perSec = (pf - prevPf) / (SampleIntervalMs / 1000.0);
                            if (perSec > 10)
                                topFaults.Add(new MonitorFaultProcessInfo(proc.ProcessName, proc.Id, perSec));
                        }
                    }
                }
                catch { }

                if (proc.TryGetIoCounters(out var io))
                {
                    ulong totalIo = io.ReadTransferCount + io.WriteTransferCount;
                    if (totalIo > 0)
                    {
                        topIo.Add(new MonitorIoProcessInfo(
                            proc.ProcessName,
                            proc.Id,
                            io.ReadTransferCount,
                            io.WriteTransferCount,
                            totalIo));
                    }
                }

                var info = new MonitorProcessInfo(
                    proc.ProcessName, proc.Id, procCpu, memBytes,
                    handleCount, gdiObjects, userObjects, threadCount);

                if (procCpu > 1) topCpu.Add(info);
                topMemory.Add(info);
                if (gdiObjects > 100 || userObjects > 100) topGdi.Add(info);
            }
            catch { }
            finally { proc.Dispose(); }
        }

        // Update CPU baseline
        _prevCpuTimes.Clear();
        foreach (var kvp in currentCpuTimes)
            _prevCpuTimes[kvp.Key] = kvp.Value;
        _hasPrevCpuTimes = true;

        _prevPageFaults.Clear();
        foreach (var kvp in currentPageFaults)
            _prevPageFaults[kvp.Key] = kvp.Value;

        // Build hanging process info with duration from tracker
        var hangingProcesses = hangingNames.Distinct().Select(name =>
        {
            double hangSec = 0;
            if (_hangingTracker.TryGetValue(name, out var startTime))
                hangSec = (DateTime.Now - startTime).TotalSeconds;
            return new HangingProcessInfo(name, hangSec);
        }).ToList();

        builder.TotalSystemHandles = totalSystemHandles;
        builder.TopCpuProcesses = topCpu.OrderByDescending(p => p.CpuPercent).Take(5).ToList();
        builder.TopMemoryProcesses = topMemory.OrderByDescending(p => p.MemoryBytes).Take(5).ToList();
        builder.TopGdiProcesses = topGdi.OrderByDescending(p => p.GdiObjects).Take(5).ToList();
        builder.TopIoProcesses = topIo.OrderByDescending(p => p.TotalBytes).Take(5).ToList();
        builder.TopFaultProcesses = topFaults.OrderByDescending(p => p.PageFaultsPerSec).Take(5).ToList();
        builder.HangingProcesses = hangingProcesses;

        // Use cached data from previous async run
        if (_cachedHandleData != null && _cachedHandleData.Count > 0)
        {
            builder.SysinternalsHandleData = _cachedHandleData;
        }

        // Sysinternals Handle.exe integration - run asynchronously without blocking
        if ((DateTime.Now - _lastSysinternalsRun).TotalSeconds >= SysinternalsIntervalSeconds)
        {
            _lastSysinternalsRun = DateTime.Now;

            // Run processes with suspiciously high handle counts
            var highHandleProcs = topMemory
                .Where(p => p.HandleCount > 1000)
                .OrderByDescending(p => p.HandleCount)
                .Take(3)
                .ToList();

            if (highHandleProcs.Count > 0)
            {
                // Start background task to collect handle data. Results will be available in next iteration.
                // This is intentionally fire-and-forget to avoid blocking monitoring.
                // Any exceptions are caught and ignored within the task.
                _handleExeTask = Task.Run(async () =>
                {
                    try
                    {
                        var handleDataList = new List<SysinternalsHandleInfo>();
                        
                        foreach (var proc in highHandleProcs)
                        {
                            try
                            {
                                var handleInfo = await SysinternalsHelper.RunHandleAsync(proc.Pid, proc.Name);
                                if (handleInfo != null)
                                {
                                    handleDataList.Add(new SysinternalsHandleInfo(
                                        handleInfo.ProcessId,
                                        handleInfo.ProcessName,
                                        handleInfo.TotalHandles,
                                        handleInfo.HandleTypeBreakdown
                                    ));
                                }
                            }
                            catch { /* Ignore individual process failures */ }
                        }

                        if (handleDataList.Count > 0)
                            _cachedHandleData = handleDataList;
                    }
                    catch { /* Ignore Sysinternals failures - not critical */ }
                });
            }
        }
    }

    public HealthAssessment Analyze(MonitorSample current, IReadOnlyList<MonitorSample> history)
    {
        var events = new List<MonitorEvent>();
        int healthScore = 100;

        // 1. Hanging process detection + live duration update
        var hangingNames = current.HangingProcesses.Select(h => h.Name).ToList();
        foreach (var proc in hangingNames)
        {
            if (!_hangingTracker.ContainsKey(proc))
            {
                _hangingTracker[proc] = DateTime.Now;
                healthScore -= 20;
                
                // Note: ProcDump is handled separately by FreezeInvestigator which already has
                // proper dump creation logic. We don't duplicate it here to avoid multiple dumps.
                
                events.Add(new MonitorEvent(
                    DateTime.Now, "Hang",
                    $"UI hang: {proc} not responding",
                    "Critical",
                    $"Try waiting 30 sec. If {proc} doesn't recover → Task Manager → End task. "
                    + "Also check that drivers (especially GPU) are up to date. "
                    + "If it repeats: run 'sfc /scannow' in admin CMD."));
            }
        }

        // Clear resolved hangs
        var resolved = _hangingTracker.Keys.Where(k => !hangingNames.Contains(k)).ToList();
        foreach (var proc in resolved)
        {
            var hangDuration = DateTime.Now - _hangingTracker[proc];
            _hangingTracker.Remove(proc);
        }

        // 2. Handle leak detection
        DetectHandleLeaks(current, events, ref healthScore);

        // 3. GDI leak detection
        DetectGdiLeaks(current, events, ref healthScore);

        // 4. Thread explosion detection
        DetectThreadExplosion(current, events, ref healthScore);

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = _hasPrevCpuTimes ? 1.0 : 0.3;

        string? hint = hangingNames.Count > 0
            ? $"Process hang: {string.Join(", ", hangingNames)} not responding"
            : null;

        return new HealthAssessment(
            new HealthScore(Domain, healthScore, confidence, hint),
            events);
    }

    /// <summary>Provides access to hangingTracker for FreezeDetector</summary>
    public IReadOnlyDictionary<string, DateTime> HangingTracker => _hangingTracker;

    private void DetectHandleLeaks(MonitorSample sample, List<MonitorEvent> events, ref int healthScore)
    {
        var currentHandles = new Dictionary<int, (string Name, int HandleCount)>();
        foreach (var proc in sample.TopCpuProcesses.Concat(sample.TopMemoryProcesses).Concat(sample.TopGdiProcesses))
            currentHandles.TryAdd(proc.Pid, (proc.Name, proc.HandleCount));

        if (_prevHandleCounts.Count > 0)
        {
            foreach (var (pid, current) in currentHandles)
            {
                if (_prevHandleCounts.TryGetValue(pid, out var prev))
                {
                    int growth = current.HandleCount - prev.HandleCount;
                    if (growth > HandleLeakThreshold)
                    {
                        healthScore -= growth > 5000 ? 20 : 10;
                        
                        // Try to get detailed handle breakdown from Sysinternals data
                        string handleDetails = "";
                        var sysinternalsData = sample.SysinternalsHandleData?.FirstOrDefault(h => h.ProcessId == pid);
                        if (sysinternalsData != null && sysinternalsData.HandleTypeBreakdown.Count > 0)
                        {
                            var topTypes = sysinternalsData.HandleTypeBreakdown
                                .OrderByDescending(kvp => kvp.Value)
                                .Take(3)
                                .Select(kvp => $"{kvp.Key}: {kvp.Value}")
                                .ToList();
                            handleDetails = $" Top types: {string.Join(", ", topTypes)}.";
                        }
                        
                        events.Add(new MonitorEvent(
                            DateTime.Now, "HandleLeak",
                            $"Handle leak: {current.Name} +{growth} handles (now: {current.HandleCount}){handleDetails}",
                            growth > 5000 ? "Critical" : "Warning",
                            $"Program {current.Name} is leaking resources (handles). "
                            + "Try restarting the program. If it repeats: update to the latest version. "
                            + "Run 'Handle.exe' from Sysinternals for deeper analysis."));
                    }
                }
            }
        }

        _prevHandleCounts.Clear();
        foreach (var (pid, info) in currentHandles)
            _prevHandleCounts[pid] = info;
    }

    private static void DetectGdiLeaks(MonitorSample sample, List<MonitorEvent> events, ref int healthScore)
    {
        foreach (var proc in sample.TopGdiProcesses)
        {
            if (proc.GdiObjects > GdiCriticalThreshold)
            {
                healthScore -= 25;
                events.Add(new MonitorEvent(
                    DateTime.Now, "GdiLeak",
                    $"GDI critical: {proc.Name} has {proc.GdiObjects} GDI objects (crashes at 10000!)",
                    "Critical",
                    $"URGENT: {proc.Name} approaching GDI limit (10000)! "
                    + "Save your work and restart the program IMMEDIATELY. "
                    + "This is a bug in the program — report to the developer."));
            }
            else if (proc.GdiObjects > GdiWarningThreshold)
            {
                healthScore -= 10;
                events.Add(new MonitorEvent(
                    DateTime.Now, "GdiLeak",
                    $"GDI warning: {proc.Name} has {proc.GdiObjects} GDI objects",
                    "Warning",
                    $"{proc.Name} is using an unusually high number of GDI objects. "
                    + "If it rises toward 10000 the Windows UI will crash. "
                    + "Try restarting the program."));
            }
        }
    }

    private void DetectThreadExplosion(MonitorSample sample, List<MonitorEvent> events, ref int healthScore)
    {
        var currentThreads = new Dictionary<int, (string Name, int ThreadCount)>();
        foreach (var proc in sample.TopCpuProcesses.Concat(sample.TopMemoryProcesses))
            currentThreads.TryAdd(proc.Pid, (proc.Name, proc.ThreadCount));

        if (_prevThreadCounts.Count > 0)
        {
            foreach (var (pid, current) in currentThreads)
            {
                if (_prevThreadCounts.TryGetValue(pid, out var prev) && current.ThreadCount > 50)
                {
                    int growth = current.ThreadCount - prev.ThreadCount;
                    if (growth > ThreadExplosionThreshold)
                    {
                        bool isCritical = growth > ThreadExplosionCritical;
                        healthScore -= isCritical ? 20 : 10;
                        events.Add(new MonitorEvent(
                            DateTime.Now, "ThreadExplosion",
                            $"Thread explosion: {current.Name} +{growth} threads (now: {current.ThreadCount})",
                            isCritical ? "Critical" : "Warning",
                            $"{current.Name} is creating threads uncontrollably (+{growth} in 3 sec). "
                            + "This can cause thread pool starvation and system freeze. "
                            + "Restart the program and report the bug to the developer. "
                            + "If it's a .NET app: check Task.Run calls and async patterns."));
                    }
                }
            }
        }

        _prevThreadCounts.Clear();
        foreach (var (pid, info) in currentThreads)
            _prevThreadCounts[pid] = info;
    }
}
