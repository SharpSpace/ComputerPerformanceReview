using System.Diagnostics;
using System.Net.NetworkInformation;

namespace ComputerPerformanceReview.Analyzers.Health;

/// <summary>
/// Orchestrates all sub-analyzers, calculates composite scores and aggregates health data.
/// Replaces the monolithic CollectSample/DetectEvents logic in MonitorAnalyzer.
/// </summary>
public sealed class SystemHealthEngine
{
    private readonly IHealthSubAnalyzer[] _analyzers;
    private readonly ProcessHealthAnalyzer _processAnalyzer;
    private readonly List<MonitorSample> _history = [];

    // Network throughput tracking (simple, no sub-analyzer needed)
    private long _prevNetworkBytesTotal;
    private DateTime _prevNetworkTime = DateTime.MinValue;

    // Network spike detection
    private const double NetworkSpikeMbps = 100;
    private const int NetworkSpikeConsecutive = 2;
    private int _consecutiveHighNetwork;

    // Compact running statistics — replace 22 unbounded List<T> fields.
    // RunningStat holds a running count/sum/max with zero heap allocation per sample.
    private const int MaxHistory = 20;   // ~60 s of context at 3 s/sample
    private const int MaxEvents  = 500;  // generous cap for multi-hour sessions

    private RunningStat     _cpuStat;
    private RunningStat     _memoryStat;
    private RunningStat     _diskQueueStat;
    private RunningStat     _networkStat;
    private RunningStatLong _handleStat;
    private RunningStat     _pageFaultStat;
    private RunningStatLong _committedBytesStat;
    private RunningStat     _pagesInputStat;
    private RunningStat     _dpcTimeStat;
    private RunningStat     _interruptTimeStat;
    private RunningStat     _contextSwitchStat;
    private RunningStatInt  _procQueueStat;
    private RunningStat     _diskSecReadStat;
    private RunningStat     _diskSecWriteStat;
    private RunningStatLong _poolNonpagedStat;
    private RunningStatLong _poolPagedStat;
    private RunningStatInt  _memPressureStat;
    private RunningStatInt  _latencyScoreStat;
    private RunningStat     _gpuUtilStat;
    private RunningStat     _dnsLatencyStat;
    private RunningStatInt  _storageErrorStat;
    private int _freezeCount;

    // Sysinternals ProcDump integration
    private DateTime _lastProcDumpRun = DateTime.MinValue;
    private const int ProcDumpCooldownSeconds = 300; // Run ProcDump at most every 5 minutes
    private string? _cachedProcDumpPath;
    private string? _lastReportedProcDumpPath; // tracks which dump we already emitted an event for
    private MiniDumpAnalysis? _cachedProcDumpAnalysis;
    private Task? _procDumpTask;

    // Event deduplication: key = EventType, value = (index into AllEvents, time first fired)
    private readonly Dictionary<string, (int Idx, DateTime FirstFired)> _activeEvents = new();

    public List<MonitorEvent> AllEvents { get; } = [];

    public SystemHealthEngine()
    {
        _processAnalyzer = new ProcessHealthAnalyzer();
        _analyzers =
        [
            new MemoryHealthAnalyzer(),
            new CpuSchedulerHealthAnalyzer(),
            new DiskHealthAnalyzer(),
            new GpuHealthAnalyzer(),
            new NetworkLatencyHealthAnalyzer(),
            _processAnalyzer,
            new DiskSpaceHealthSubAnalyzer(),
            new BrowserHealthSubAnalyzer(),
            new PowerPlanHealthSubAnalyzer(),
            new StartupHealthSubAnalyzer()
        ];
        InitializeNetworkBaseline();
    }

    /// <summary>
    /// Collects all data, calculates composite scores, runs freeze detector, returns a complete sample.
    /// </summary>
    public MonitorSample CollectAndAnalyze()
    {
        // 1. Collect — each sub-analyzer fills its fields in the builder
        var builder = new MonitorSampleBuilder();
        foreach (var analyzer in _analyzers)
            analyzer.Collect(builder);

        // 2. Network (simple, no sub-analyzer)
        builder.NetworkMbps = CollectNetworkMbps();

        // 3. Build intermediate sample (without composite scores)
        var rawSample = builder.Build();

        // 4. Compute composite scores
        int memPressure = ComputeMemoryPressureIndex(rawSample);
        int latencyScore = ComputeSystemLatencyScore(rawSample);
        builder.MemoryPressureIndex = memPressure;
        builder.SystemLatencyScore = latencyScore;

        if (!string.IsNullOrWhiteSpace(_cachedProcDumpPath))
            builder.SysinternalsProcDumpPath = _cachedProcDumpPath;
        if (_cachedProcDumpAnalysis is not null)
            builder.SysinternalsProcDumpAnalysis = _cachedProcDumpAnalysis;

        // 5. Freeze detector — classify each hanging process
        FreezeClassification? freezeInfo = null;
        FreezeReport? deepFreezeReport = null;
        var sampleWithScores = builder.Build();
        if (sampleWithScores.HangingProcesses.Count > 0)
        {
            var firstHang = sampleWithScores.HangingProcesses[0];
            freezeInfo = FreezeDetector.Classify(firstHang.Name, sampleWithScores);
            builder.FreezeInfo = freezeInfo;
            
            // Deep freeze investigation for freezes > 5 seconds
            if (firstHang.HangSeconds > 5)
            {
                // Try to find the process ID
                var procInfo = sampleWithScores.TopCpuProcesses
                    .Concat(sampleWithScores.TopMemoryProcesses)
                    .FirstOrDefault(p => p.Name.Equals(firstHang.Name, StringComparison.OrdinalIgnoreCase));
                var procId = procInfo?.Pid;

                if (procId is null)
                {
                    Process[] processes = [];
                    try
                    {
                        processes = Process.GetProcessesByName(firstHang.Name);
                        var match = processes.FirstOrDefault();
                        if (match is not null)
                            procId = match.Id;
                    }
                    catch
                    {
                    }
                    finally
                    {
                        foreach (var process in processes)
                            process.Dispose();
                    }
                }

                if (procId is not null)
                {
                    deepFreezeReport = FreezeInvestigator.Investigate(
                        firstHang.Name, 
                        procId.Value, 
                        TimeSpan.FromSeconds(firstHang.HangSeconds), 
                        sampleWithScores);
                    builder.DeepFreezeReport = deepFreezeReport;

                    if (firstHang.HangSeconds >= 15
                        && (DateTime.Now - _lastProcDumpRun).TotalSeconds >= ProcDumpCooldownSeconds)
                    {
                        _lastProcDumpRun = DateTime.Now;
                        _procDumpTask = Task.Run(async () =>
                        {
                            try
                            {
                                var path = await SysinternalsHelper.RunProcDumpAsync(procId.Value, firstHang.Name, "hang");
                                if (!string.IsNullOrWhiteSpace(path))
                                {
                                    _cachedProcDumpPath = path;
                                    _cachedProcDumpAnalysis = MiniDumpHelper.AnalyzeMiniDump(path);
                                }
                            }
                            catch { /* Ignore Sysinternals failures - not critical */ }
                        });
                    }
                }
            }
            
            _freezeCount++;
        }

        var finalSample = builder.Build();

        // 6. Analyze — each sub-analyzer generates events based on sample + history
        // Deduplicate: one live event per EventType; update duration, resolve when gone
        var firedTypes = new HashSet<string>();
        foreach (var analyzer in _analyzers)
        {
            var assessment = analyzer.Analyze(finalSample, _history);
            foreach (var ev in assessment.NewEvents)
            {
                // Hang events are managed by UpdateHangingEvents below — don't deduplicate them
                if (ev.EventType == "Hang")
                {
                    if (AllEvents.Count < MaxEvents) AllEvents.Add(ev);
                    continue;
                }

                firedTypes.Add(ev.EventType);

                if (!_activeEvents.TryGetValue(ev.EventType, out var active))
                {
                    // First occurrence of this event type
                    if (AllEvents.Count < MaxEvents)
                    {
                        AllEvents.Add(ev);
                        _activeEvents[ev.EventType] = (AllEvents.Count - 1, ev.Timestamp);
                    }
                }
                else
                {
                    // Already active — update the existing event with an "ongoing" duration note
                    var durSec = (int)(DateTime.Now - active.FirstFired).TotalSeconds;
                    var existing = AllEvents[active.Idx];
                    string baseDesc = StripStatusSuffix(existing.Description);
                    AllEvents[active.Idx] = existing with
                    {
                        Description = durSec >= 6
                            ? $"{baseDesc} (ongoing {durSec}s)"
                            : baseDesc
                    };
                }
            }
        }

        // Resolve event types that no longer fire this cycle
        foreach (var key in _activeEvents.Keys.ToList())
        {
            if (!firedTypes.Contains(key))
            {
                var active = _activeEvents[key];
                var durSec = (int)(DateTime.Now - active.FirstFired).TotalSeconds;
                var existing = AllEvents[active.Idx];
                string baseDesc = StripStatusSuffix(existing.Description);
                AllEvents[active.Idx] = existing with
                {
                    Description = durSec >= 6
                        ? $"{baseDesc} (resolved after {durSec}s)"
                        : baseDesc
                };
                _activeEvents.Remove(key);
            }
        }

        // 7. Network spike detection (simple)
        DetectNetworkSpike(finalSample);

        // 8. Update hanging events with live duration and freeze info
        UpdateHangingEvents(finalSample);

        // 9. ProcDump event — emit once when a new dump file arrives from the background task
        if (_cachedProcDumpPath is not null && _cachedProcDumpPath != _lastReportedProcDumpPath)
        {
            _lastReportedProcDumpPath = _cachedProcDumpPath;
            if (AllEvents.Count < MaxEvents)
            {
                var dumpFile = Path.GetFileName(_cachedProcDumpPath);
                AllEvents.Add(new MonitorEvent(
                    DateTime.Now, "ProcDump",
                    $"Process dump created: {dumpFile}",
                    "Warning",
                    $"Dump saved to: {_cachedProcDumpPath}. "
                    + "Open with WinDbg or Visual Studio for analysis. "
                    + "For .NET processes: 'dotnet-dump analyze <path>'."));
            }
        }

        // 10. Track history (capped, trimmed) and running stats for report
        _history.Add(TrimForHistory(finalSample));
        if (_history.Count > MaxHistory)
            _history.RemoveAt(0);
        TrackSamplesForReport(finalSample);

        return finalSample;
    }

    public MonitorReport BuildReport(DateTime startTime, DateTime endTime, int sampleCount)
    {
        return new MonitorReport(
            StartTime: startTime,
            EndTime: endTime,
            TotalSamples: sampleCount,
            Events: AllEvents.ToList(),
            AvgCpu:                    _cpuStat.Avg,
            PeakCpu:                   _cpuStat.Max,
            AvgMemory:                 _memoryStat.Avg,
            PeakMemory:                _memoryStat.Max,
            AvgDiskQueue:              _diskQueueStat.Avg,
            PeakDiskQueue:             _diskQueueStat.Max,
            AvgNetworkMbps:            _networkStat.Avg,
            PeakNetworkMbps:           _networkStat.Max,
            PeakSystemHandles:         _handleStat.Max,
            PeakPageFaults:            _pageFaultStat.Max,
            PeakCommittedBytes:        _committedBytesStat.Max,
            PeakPagesInputPerSec:      _pagesInputStat.Max,
            PeakDpcTimePercent:        _dpcTimeStat.Max,
            PeakInterruptTimePercent:  _interruptTimeStat.Max,
            PeakContextSwitchesPerSec: _contextSwitchStat.Max,
            PeakProcessorQueueLength:  _procQueueStat.Max,
            PeakAvgDiskSecRead:        _diskSecReadStat.Max,
            PeakAvgDiskSecWrite:       _diskSecWriteStat.Max,
            PeakPoolNonpagedBytes:     _poolNonpagedStat.Max,
            PeakPoolPagedBytes:        _poolPagedStat.Max,
            PeakGpuUtilizationPercent: _gpuUtilStat.Max,
            PeakDnsLatencyMs:          _dnsLatencyStat.Max,
            PeakStorageErrorsLast15Min: _storageErrorStat.Max,
            AvgMemoryPressureIndex:    _memPressureStat.Avg,
            PeakMemoryPressureIndex:   _memPressureStat.Max,
            AvgSystemLatencyScore:     _latencyScoreStat.Avg,
            PeakSystemLatencyScore:    _latencyScoreStat.Max,
            FreezeCount: _freezeCount
        );
    }

    // ── Composite Scores ─────────────────────────────────

    /// <summary>
    /// Memory Pressure Index (0–100):
    /// scoreA = CommittedBytes/CommitLimit × 100    [weight 0.40]
    /// scoreB = Clamp(PagesInputPerSec/50, 0, 100)  [weight 0.35]
    /// scoreC = 100 - (AvailableMBytes/TotalMB×100)  [weight 0.25]
    /// </summary>
    private static int ComputeMemoryPressureIndex(MonitorSample s)
    {
        double scoreA = s.CommitLimit > 0
            ? (double)s.CommittedBytes / s.CommitLimit * 100
            : 0;

        double scoreB = Math.Clamp(s.PagesInputPerSec / 50.0, 0, 100);

        // AvailableMBytes is from WMI, MemoryAvailableBytes from OS query
        // Use MemoryUsedPercent as proxy for scoreC
        double scoreC = s.MemoryUsedPercent;

        int index = (int)(scoreA * 0.40 + scoreB * 0.35 + scoreC * 0.25);
        return Math.Clamp(index, 0, 100);
    }

    /// <summary>
    /// System Latency Score (0–100):
    /// scoreA = Clamp((DPC%+IRQ%)/0.20×100, 0, 100)            [weight 0.30]
    /// scoreB = Clamp(MaxDiskLatencyMs/2, 0, 100)               [weight 0.35]
    /// scoreC = Clamp(ProcQueueLength/(2×cores)×100, 0, 100)    [weight 0.35]
    /// </summary>
    private static int ComputeSystemLatencyScore(MonitorSample s)
    {
        double scoreA = Math.Clamp((s.DpcTimePercent + s.InterruptTimePercent) / 20.0 * 100, 0, 100);

        double maxLatencyMs = Math.Max(s.AvgDiskSecRead, s.AvgDiskSecWrite) * 1000;
        double scoreB = Math.Clamp(maxLatencyMs / 2.0, 0, 100);

        int cores = Environment.ProcessorCount;
        double scoreC = cores > 0
            ? Math.Clamp((double)s.ProcessorQueueLength / (2.0 * cores) * 100, 0, 100)
            : 0;

        int score = (int)(scoreA * 0.30 + scoreB * 0.35 + scoreC * 0.35);
        return Math.Clamp(score, 0, 100);
    }

    // ── Network ──────────────────────────────────────────

    private void InitializeNetworkBaseline()
    {
        try
        {
            long totalBytes = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var stats = ni.GetIPv4Statistics();
                    totalBytes += stats.BytesReceived + stats.BytesSent;
                }
            }
            _prevNetworkBytesTotal = totalBytes;
            _prevNetworkTime = DateTime.Now;
        }
        catch
        {
            _prevNetworkBytesTotal = 0;
            _prevNetworkTime = DateTime.Now;
        }
    }

    private double CollectNetworkMbps()
    {
        try
        {
            long totalBytes = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var stats = ni.GetIPv4Statistics();
                    totalBytes += stats.BytesReceived + stats.BytesSent;
                }
            }

            var now = DateTime.Now;
            var elapsed = (now - _prevNetworkTime).TotalSeconds;
            if (elapsed <= 0) return 0;

            long delta = totalBytes - _prevNetworkBytesTotal;
            _prevNetworkBytesTotal = totalBytes;
            _prevNetworkTime = now;

            if (delta < 0) return 0;
            double mbps = (delta * 8.0) / (elapsed * 1_000_000);
            return Math.Round(mbps, 1);
        }
        catch { return 0; }
    }

    private void DetectNetworkSpike(MonitorSample sample)
    {
        if (sample.NetworkMbps > NetworkSpikeMbps)
        {
            _consecutiveHighNetwork++;
            if (_consecutiveHighNetwork == NetworkSpikeConsecutive && AllEvents.Count < MaxEvents)
            {
                AllEvents.Add(new MonitorEvent(
                    DateTime.Now, "NetworkSpike",
                    $"Network spike: {sample.NetworkMbps:F0} Mbps for {_consecutiveHighNetwork * 3} seconds",
                    sample.NetworkMbps > 500 ? "Critical" : "Warning",
                    "Check if something is downloading/uploading in the background (Windows Update, OneDrive, Steam, etc). "
                    + "Open Task Manager → Network column. "
                    + "Temporarily disable automatic updates if it's interfering."));
            }
        }
        else
        {
            _consecutiveHighNetwork = 0;
        }
    }

    // ── Hanging event management ─────────────────────────

    private void UpdateHangingEvents(MonitorSample sample)
    {
        // Update live duration on existing hang events
        foreach (var hang in sample.HangingProcesses)
        {
            if (hang.HangSeconds > 3)
            {
                var hangEvent = AllEvents.LastOrDefault(e =>
                    e.EventType == "Hang" &&
                    e.Description.Contains(hang.Name) &&
                    !e.Description.Contains("was not responding"));

                if (hangEvent is not null)
                {
                    int idx = AllEvents.LastIndexOf(hangEvent);

                    // Prefer the richer DeepFreezeReport root cause over the quick FreezeClassification
                    string freezeHint = "";
                    if (sample.DeepFreezeReport is not null
                        && sample.DeepFreezeReport.ProcessName.Equals(hang.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        freezeHint = $" → {sample.DeepFreezeReport.LikelyRootCause}";
                        if (sample.DeepFreezeReport.MiniDumpPath is not null)
                            freezeHint += $" (dump: {Path.GetFileName(sample.DeepFreezeReport.MiniDumpPath)})";
                    }
                    else if (sample.FreezeInfo is not null
                        && sample.FreezeInfo.ProcessName.Equals(hang.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        freezeHint = $" → Likely cause: {sample.FreezeInfo.LikelyCause}";
                    }

                    AllEvents[idx] = hangEvent with
                    {
                        Description = $"UI hang: {hang.Name} not responding ({hang.HangSeconds:F0} sec){freezeHint}"
                    };
                }
            }
        }

        // Resolve hangs that are no longer hanging
        var currentHangNames = sample.HangingProcesses.Select(h => h.Name).ToHashSet();
        var tracker = _processAnalyzer.HangingTracker;
        // Find events that refer to processes no longer hanging
        for (int i = AllEvents.Count - 1; i >= 0; i--)
        {
            var ev = AllEvents[i];
            if (ev.EventType == "Hang" && !ev.Description.Contains("was not responding"))
            {
                // Check if this hang's process is resolved
                foreach (var name in tracker.Keys.Where(k => !currentHangNames.Contains(k)))
                {
                    if (ev.Description.Contains(name))
                    {
                        var hangDuration = (DateTime.Now - tracker[name]).TotalSeconds;
                        AllEvents[i] = ev with
                        {
                            Description = $"UI hang: {name} was not responding ({hangDuration:F0} sec, recovered)"
                        };
                    }
                }
            }
        }
    }

    // ── Report tracking ──────────────────────────────────

    private void TrackSamplesForReport(MonitorSample s)
    {
        _cpuStat.Add(s.CpuPercent);
        _memoryStat.Add(s.MemoryUsedPercent);
        _diskQueueStat.Add(s.DiskQueueLength);
        _networkStat.Add(s.NetworkMbps);
        _handleStat.Add(s.TotalSystemHandles);
        _pageFaultStat.Add(s.PageFaultsPerSec);
        _committedBytesStat.Add(s.CommittedBytes);
        _pagesInputStat.Add(s.PagesInputPerSec);
        _dpcTimeStat.Add(s.DpcTimePercent);
        _interruptTimeStat.Add(s.InterruptTimePercent);
        _contextSwitchStat.Add(s.ContextSwitchesPerSec);
        _procQueueStat.Add(s.ProcessorQueueLength);
        _diskSecReadStat.Add(s.AvgDiskSecRead);
        _diskSecWriteStat.Add(s.AvgDiskSecWrite);
        _poolNonpagedStat.Add(s.PoolNonpagedBytes);
        _poolPagedStat.Add(s.PoolPagedBytes);
        _memPressureStat.Add(s.MemoryPressureIndex);
        _latencyScoreStat.Add(s.SystemLatencyScore);
        _gpuUtilStat.Add(s.GpuUtilizationPercent);
        _dnsLatencyStat.Add(s.DnsLatencyMs);
        _storageErrorStat.Add(s.StorageErrorsLast15Min);
    }

    /// <summary>
    /// Returns a lightweight copy of the sample for history storage.
    /// Strips process lists and Sysinternals blobs — sub-analyzers only read
    /// scalar fields from historical samples (e.g. MemoryAvailableBytes for
    /// trend detection). Reduces per-sample memory by ~80%.
    /// </summary>
    private static MonitorSample TrimForHistory(MonitorSample s) => s with
    {
        TopCpuProcesses          = [],
        TopMemoryProcesses       = [],
        TopGdiProcesses          = [],
        TopIoProcesses           = [],
        TopFaultProcesses        = [],
        StorageErrorDetails      = null,
        SysinternalsHandleData   = null,
        SysinternalsPoolData     = null,
        DiskSpaces               = null,
        FreezeInfo               = null,
        DeepFreezeReport         = null,
        SysinternalsDiskExtOutput    = null,
        SysinternalsProcDumpPath     = null,
        SysinternalsProcDumpAnalysis = null,
    };

    /// <summary>
    /// Strips any trailing "(ongoing Xs)" or "(resolved after Xs)" suffix added by deduplication.
    /// </summary>
    private static string StripStatusSuffix(string desc)
    {
        var m = System.Text.RegularExpressions.Regex.Match(
            desc, @" \((ongoing|resolved after) \d+s\)$");
        return m.Success ? desc[..m.Index] : desc;
    }

    // ── Value-type running statistics — zero heap allocation per sample ──

    /// <summary>Tracks count, running sum, and running max for double values.</summary>
    private struct RunningStat
    {
        private int    _count;
        private double _sum;
        private double _max;

        public void Add(double value)
        {
            _count++;
            _sum += value;
            if (value > _max) _max = value;
        }

        public readonly double Avg => _count > 0 ? _sum / _count : 0;
        public readonly double Max => _max;
    }

    /// <summary>Tracks running max for long values (no avg needed).</summary>
    private struct RunningStatLong
    {
        private long _max;

        public void Add(long value) { if (value > _max) _max = value; }

        public readonly long Max => _max;
    }

    /// <summary>Tracks count, running sum, and running max for int values.</summary>
    private struct RunningStatInt
    {
        private int  _count;
        private long _sum;
        private int  _max;

        public void Add(int value)
        {
            _count++;
            _sum += value;
            if (value > _max) _max = value;
        }

        public readonly int Avg => _count > 0 ? (int)(_sum / _count) : 0;
        public readonly int Max => _max;
    }
}
