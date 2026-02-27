namespace ComputerPerformanceReview.Analyzers.Health;

public sealed class MemoryHealthAnalyzer : IHealthSubAnalyzer
{
    public string Domain => "Memory";

    // Thresholds
    private const long MemoryGrowthThresholdBytes = 500L * 1024 * 1024;
    private const int MemoryGrowthWindowSamples = 10; // ~30 seconds
    private const double PagesInputStormThreshold = 300; // pages/sec from disk
    private const int PagesInputStormConsecutive = 2;
    private const double CommitExhaustionWarning = 0.90;
    private const double CommitExhaustionCritical = 0.95;
    private const long PoolNonpagedWarning = 200L * 1024 * 1024;  // 200 MB
    private const long PoolNonpagedCritical = 400L * 1024 * 1024;  // 400 MB
    
    // Sysinternals PoolMon integration
    private DateTime _lastPoolMonRun = DateTime.MinValue;
    private const int PoolMonCooldownSeconds = 120; // Run PoolMon at most every 2 minutes
    private List<SysinternalsPoolAllocation>? _cachedPoolData;
    private Task? _poolMonTask; // Track running PoolMon task

    // Sysinternals RAMMap integration
    private DateTime _lastRamMapRun = DateTime.MinValue;
    private const int RamMapCooldownSeconds = 300; // Run RAMMap at most every 5 minutes
    private bool? _cachedRamMapAvailable;
    private Task? _ramMapTask; // Track running RAMMap task

    // Cooldown — max 1 event per type per 60 sec
    private const int CooldownSeconds = 60;

    // State
    private int _consecutiveHighPagesInput;
    private int _consecutiveHighCommit;
    private int _consecutiveHighPool;
    private DateTime _lastMemorySpikeTime = DateTime.MinValue;
    private DateTime _lastPageFaultStormTime = DateTime.MinValue;
    private DateTime _lastCommitExhaustionTime = DateTime.MinValue;
    private DateTime _lastPoolExhaustionTime = DateTime.MinValue;

    public void Collect(MonitorSampleBuilder builder)
    {
        // Consolidated memory perf counter query
        try
        {
            var data = WmiHelper.Query(
                "SELECT PageFaultsPersec, PagesInputPersec, PagesOutputPersec, " +
                "CommittedBytes, CommitLimit, AvailableMBytes, " +
                "PoolNonpagedBytes, PoolPagedBytes " +
                "FROM Win32_PerfFormattedData_PerfOS_Memory");
            if (data.Count > 0)
            {
                builder.PageFaultsPerSec = WmiHelper.GetValue<double>(data[0], "PageFaultsPersec");
                builder.PagesInputPerSec = WmiHelper.GetValue<double>(data[0], "PagesInputPersec");
                builder.PagesOutputPerSec = WmiHelper.GetValue<double>(data[0], "PagesOutputPersec");
                builder.CommittedBytes = WmiHelper.GetValue<long>(data[0], "CommittedBytes");
                builder.CommitLimit = WmiHelper.GetValue<long>(data[0], "CommitLimit");
                builder.AvailableMBytes = WmiHelper.GetValue<long>(data[0], "AvailableMBytes");
                builder.PoolNonpagedBytes = WmiHelper.GetValue<long>(data[0], "PoolNonpagedBytes");
                builder.PoolPagedBytes = WmiHelper.GetValue<long>(data[0], "PoolPagedBytes");
                
                // Use cached data from previous async run
                if (_cachedPoolData != null && _cachedPoolData.Count > 0)
                {
                    builder.SysinternalsPoolData = _cachedPoolData;
                }
                
                // Run PoolMon asynchronously if kernel pool is high and cooldown has expired
                if (builder.PoolNonpagedBytes > PoolNonpagedWarning 
                    && (DateTime.Now - _lastPoolMonRun).TotalSeconds >= PoolMonCooldownSeconds)
                {
                    _lastPoolMonRun = DateTime.Now;
                    
                    // Start background task to collect pool data. Results will be available in next iteration.
                    // This is intentionally fire-and-forget to avoid blocking monitoring.
                    // Any exceptions are caught and ignored within the task.
                    _poolMonTask = Task.Run(async () =>
                    {
                        try
                        {
                            var poolMonInfo = await SysinternalsHelper.RunPoolMonAsync();
                            if (poolMonInfo != null && poolMonInfo.TopAllocations.Count > 0)
                            {
                                _cachedPoolData = poolMonInfo.TopAllocations
                                    .Select(p => new SysinternalsPoolAllocation(p.Tag, p.Type, p.Bytes))
                                    .ToList();
                            }
                        }
                        catch { /* Ignore Sysinternals failures - not critical */ }
                    });
                }
            }
        }
        catch { }

        // OS-level memory for percent calculation
        try
        {
            var osData = WmiHelper.Query(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            if (osData.Count > 0)
            {
                var totalKb = WmiHelper.GetValue<long>(osData[0], "TotalVisibleMemorySize");
                var freeKb = WmiHelper.GetValue<long>(osData[0], "FreePhysicalMemory");
                builder.MemoryAvailableBytes = freeKb * 1024;
                if (totalKb > 0)
                    builder.MemoryUsedPercent = (double)(totalKb - freeKb) / totalKb * 100;
            }
        }
        catch { }

        if (_cachedRamMapAvailable.HasValue)
            builder.SysinternalsRamMapAvailable = _cachedRamMapAvailable;

        bool shouldRunRamMap = builder.MemoryUsedPercent > 85
            || (builder.CommitLimit > 0 && (double)builder.CommittedBytes / builder.CommitLimit > CommitExhaustionWarning)
            || builder.PagesInputPerSec > PagesInputStormThreshold;

        if (shouldRunRamMap && (DateTime.Now - _lastRamMapRun).TotalSeconds >= RamMapCooldownSeconds)
        {
            _lastRamMapRun = DateTime.Now;
            _ramMapTask = Task.Run(async () =>
            {
                try
                {
                    var ramMapInfo = await SysinternalsHelper.RunRamMapAsync();
                    _cachedRamMapAvailable = ramMapInfo?.IsAvailable;
                }
                catch { /* Ignore Sysinternals failures - not critical */ }
            });
        }
    }

    public HealthAssessment Analyze(MonitorSample current, IReadOnlyList<MonitorSample> history)
    {
        var events = new List<MonitorEvent>();
        int healthScore = 100;

        // 1. Page fault storm (now based on PagesInput instead of PageFaults)
        if (current.PagesInputPerSec > PagesInputStormThreshold)
        {
            _consecutiveHighPagesInput++;
            if (_consecutiveHighPagesInput == PagesInputStormConsecutive
                && (DateTime.Now - _lastPageFaultStormTime).TotalSeconds > CooldownSeconds)
            {
                _lastPageFaultStormTime = DateTime.Now;
                bool isCritical = current.PagesInputPerSec > 1000;
                healthScore -= isCritical ? 30 : 15;

                var topMem = current.TopMemoryProcesses.Take(3)
                    .Select(p => $"{p.Name} ({ConsoleHelper.FormatBytes(p.MemoryBytes)})")
                    .ToList();
                string procHint = topMem.Count > 0 ? $" Largest in memory: {string.Join(", ", topMem)}." : "";

                events.Add(new MonitorEvent(
                    DateTime.Now, "PageFaultStorm",
                    $"Hard paging storm: {current.PagesInputPerSec:F0} pages/s read from disk for {_consecutiveHighPagesInput * 3} sec",
                    isCritical ? "Critical" : "Warning",
                    $"System is reading intensively from the page file — physical RAM is insufficient.{procHint} "
                    + "ACTIONS: 1) Close heavy programs and browser tabs. "
                    + "2) Increase page file: Settings → System → About → Advanced system settings → Virtual memory. "
                    + "3) If this happens frequently: upgrade RAM. "
                    + "4) Ensure the page file is on an SSD, not HDD."));
            }
        }
        else
        {
            _consecutiveHighPagesInput = 0;
        }

        // 2. Memory growth detection
        if (history.Count >= MemoryGrowthWindowSamples)
        {
            long oldest = history[^MemoryGrowthWindowSamples].MemoryAvailableBytes;
            long newest = current.MemoryAvailableBytes;
            long drop = oldest - newest;
            if (drop > MemoryGrowthThresholdBytes
                && (DateTime.Now - _lastMemorySpikeTime).TotalSeconds > CooldownSeconds)
            {
                _lastMemorySpikeTime = DateTime.Now;
                bool isCritical = drop > 1024L * 1024 * 1024;
                healthScore -= isCritical ? 20 : 10;

                var topMem = current.TopMemoryProcesses.Take(3)
                    .Select(p => $"{p.Name} ({ConsoleHelper.FormatBytes(p.MemoryBytes)})")
                    .ToList();
                string procList = topMem.Count > 0 ? $" Largest right now: {string.Join(", ", topMem)}." : "";

                events.Add(new MonitorEvent(
                    DateTime.Now, "MemorySpike",
                    $"Memory growth: {ConsoleHelper.FormatBytes(drop)} consumed in 30 sec.{procList}",
                    isCritical ? "Critical" : "Warning",
                    $"Close unnecessary browser tabs and heavy programs you're not using.{procList} "
                    + "If memory is constantly high: increase the page file or consider more RAM."));
            }
        }

        // 3. Commit exhaustion
        if (current.CommitLimit > 0)
        {
            double commitRatio = (double)current.CommittedBytes / current.CommitLimit;
            if (commitRatio > CommitExhaustionWarning)
            {
                _consecutiveHighCommit++;
                if (_consecutiveHighCommit == 2
                    && (DateTime.Now - _lastCommitExhaustionTime).TotalSeconds > CooldownSeconds)
                {
                    _lastCommitExhaustionTime = DateTime.Now;
                    bool isCritical = commitRatio > CommitExhaustionCritical;
                    healthScore -= isCritical ? 25 : 10;
                    var topMem = current.TopMemoryProcesses.Take(3)
                        .Select(p => $"{p.Name} ({ConsoleHelper.FormatBytes(p.MemoryBytes)})")
                        .ToList();
                    string procHint = topMem.Count > 0 ? $" Largest right now: {string.Join(", ", topMem)}." : "";

                    events.Add(new MonitorEvent(
                        DateTime.Now, "CommitExhaustion",
                        $"Commit limit: {commitRatio * 100:F0}% of available memory used ({ConsoleHelper.FormatBytes(current.CommittedBytes)}/{ConsoleHelper.FormatBytes(current.CommitLimit)})",
                        isCritical ? "Critical" : "Warning",
                        $"System memory commitment is approaching the limit. If the limit is reached, programs may crash.{procHint} "
                        + "Increase page file: System Settings → Advanced → Performance → Virtual memory. "
                        + "Close programs you're not using."));
                }
            }
            else
            {
                _consecutiveHighCommit = 0;
            }
        }

        // 4. Kernel pool exhaustion
        if (current.PoolNonpagedBytes > PoolNonpagedWarning)
        {
            _consecutiveHighPool++;
            if (_consecutiveHighPool == 2
                && (DateTime.Now - _lastPoolExhaustionTime).TotalSeconds > CooldownSeconds)
            {
                _lastPoolExhaustionTime = DateTime.Now;
                bool isCritical = current.PoolNonpagedBytes > PoolNonpagedCritical;
                healthScore -= isCritical ? 20 : 10;
                
                // Try to get PoolMon data from cached results
                string driverHint = "";
                if (current.SysinternalsPoolData is { Count: > 0 })
                {
                    var topPool = current.SysinternalsPoolData.Take(3)
                        .Select(p => $"{p.Tag} ({ConsoleHelper.FormatBytes(p.Bytes)})")
                        .ToList();
                    driverHint = $" Top pool tags: {string.Join(", ", topPool)}.";
                }

                events.Add(new MonitorEvent(
                    DateTime.Now, "PoolExhaustion",
                    $"Kernel pool: Nonpaged pool is {ConsoleHelper.FormatBytes(current.PoolNonpagedBytes)}{driverHint}",
                    isCritical ? "Critical" : "Warning",
                    "Kernel nonpaged pool is unusually high. This is typically caused by a driver leaking memory. "
                    + "Tool: Run 'poolmon.exe' (Sysinternals) to identify which driver. "
                    + "Update network, GPU, and storage drivers."));
            }
        }
        else
        {
            _consecutiveHighPool = 0;
        }

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = history.Count >= 3 ? 1.0 : history.Count / 3.0;

        string? hint = healthScore < 50
            ? "Memory pressure: system is actively paging and committed memory is high"
            : null;

        return new HealthAssessment(
            new HealthScore(Domain, healthScore, confidence, hint),
            events);
    }
}
