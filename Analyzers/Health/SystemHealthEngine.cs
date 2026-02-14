using System.Diagnostics;
using System.Net.NetworkInformation;

namespace ComputerPerformanceReview.Analyzers.Health;

/// <summary>
/// Orkestrerar alla sub-analyzers, beräknar composite scores och aggregerar hälsodata.
/// Ersätter den monolitiska CollectSample/DetectEvents-logiken i MonitorAnalyzer.
/// </summary>
public sealed class SystemHealthEngine
{
    private readonly IHealthSubAnalyzer[] _analyzers;
    private readonly ProcessHealthAnalyzer _processAnalyzer;
    private readonly List<MonitorSample> _history = [];

    // Network throughput tracking (enkel, ingen sub-analyzer behövs)
    private long _prevNetworkBytesTotal;
    private DateTime _prevNetworkTime = DateTime.MinValue;

    // Network spike detection
    private const double NetworkSpikeMbps = 100;
    private const int NetworkSpikeConsecutive = 2;
    private int _consecutiveHighNetwork;

    // Tracking for report
    private readonly List<double> _cpuSamples = [];
    private readonly List<double> _memorySamples = [];
    private readonly List<double> _diskQueueSamples = [];
    private readonly List<double> _networkSamples = [];
    private readonly List<long> _handleSamples = [];
    private readonly List<double> _pageFaultSamples = [];
    private readonly List<long> _committedBytesSamples = [];
    private readonly List<double> _pagesInputSamples = [];
    private readonly List<double> _dpcTimeSamples = [];
    private readonly List<double> _interruptTimeSamples = [];
    private readonly List<double> _contextSwitchSamples = [];
    private readonly List<int> _procQueueSamples = [];
    private readonly List<double> _diskSecReadSamples = [];
    private readonly List<double> _diskSecWriteSamples = [];
    private readonly List<long> _poolNonpagedSamples = [];
    private readonly List<long> _poolPagedSamples = [];
    private readonly List<int> _memPressureSamples = [];
    private readonly List<int> _latencyScoreSamples = [];
    private readonly List<double> _gpuUtilSamples = [];
    private readonly List<double> _dnsLatencySamples = [];
    private readonly List<int> _storageErrorSamples = [];
    private int _freezeCount;

    // Sysinternals ProcDump integration
    private DateTime _lastProcDumpRun = DateTime.MinValue;
    private const int ProcDumpCooldownSeconds = 300; // Run ProcDump at most every 5 minutes
    private string? _cachedProcDumpPath;
    private MiniDumpAnalysis? _cachedProcDumpAnalysis;
    private Task? _procDumpTask;

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
            _processAnalyzer
        ];
        InitializeNetworkBaseline();
    }

    /// <summary>
    /// Samlar in all data, beräknar composite scores, kör freeze detector, returnerar ett komplett sample.
    /// </summary>
    public MonitorSample CollectAndAnalyze()
    {
        // 1. Collect — varje sub-analyzer fyller sina fält i buildern
        var builder = new MonitorSampleBuilder();
        foreach (var analyzer in _analyzers)
            analyzer.Collect(builder);

        // 2. Network (enkel, ingen sub-analyzer)
        builder.NetworkMbps = CollectNetworkMbps();

        // 3. Build intermediate sample (utan composite scores)
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

        // 5. Freeze detector — klassificera varje hängande process
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

        // 6. Analyze — varje sub-analyzer genererar events baserat på sample + historik
        foreach (var analyzer in _analyzers)
        {
            var assessment = analyzer.Analyze(finalSample, _history);
            if (assessment.NewEvents.Count > 0)
                AllEvents.AddRange(assessment.NewEvents);
        }

        // 7. Network spike detection (enkel)
        DetectNetworkSpike(finalSample);

        // 8. Update hanging events with live duration and freeze info
        UpdateHangingEvents(finalSample);

        // 9. Track history and samples for report
        _history.Add(finalSample);
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
            // Befintliga
            AvgCpu: SafeAvg(_cpuSamples),
            PeakCpu: SafeMax(_cpuSamples),
            AvgMemory: SafeAvg(_memorySamples),
            PeakMemory: SafeMax(_memorySamples),
            AvgDiskQueue: SafeAvg(_diskQueueSamples),
            PeakDiskQueue: SafeMax(_diskQueueSamples),
            AvgNetworkMbps: SafeAvg(_networkSamples),
            PeakNetworkMbps: SafeMax(_networkSamples),
            PeakSystemHandles: _handleSamples.Count > 0 ? _handleSamples.Max() : 0,
            PeakPageFaults: SafeMax(_pageFaultSamples),
            PeakCommittedBytes: _committedBytesSamples.Count > 0 ? _committedBytesSamples.Max() : 0,
            // Nya
            PeakPagesInputPerSec: SafeMax(_pagesInputSamples),
            PeakDpcTimePercent: SafeMax(_dpcTimeSamples),
            PeakInterruptTimePercent: SafeMax(_interruptTimeSamples),
            PeakContextSwitchesPerSec: SafeMax(_contextSwitchSamples),
            PeakProcessorQueueLength: _procQueueSamples.Count > 0 ? _procQueueSamples.Max() : 0,
            PeakAvgDiskSecRead: SafeMax(_diskSecReadSamples),
            PeakAvgDiskSecWrite: SafeMax(_diskSecWriteSamples),
            PeakPoolNonpagedBytes: _poolNonpagedSamples.Count > 0 ? _poolNonpagedSamples.Max() : 0,
            PeakPoolPagedBytes: _poolPagedSamples.Count > 0 ? _poolPagedSamples.Max() : 0,
            PeakGpuUtilizationPercent: SafeMax(_gpuUtilSamples),
            PeakDnsLatencyMs: SafeMax(_dnsLatencySamples),
            PeakStorageErrorsLast15Min: _storageErrorSamples.Count > 0 ? _storageErrorSamples.Max() : 0,
            AvgMemoryPressureIndex: _memPressureSamples.Count > 0 ? (int)_memPressureSamples.Average() : 0,
            PeakMemoryPressureIndex: _memPressureSamples.Count > 0 ? _memPressureSamples.Max() : 0,
            AvgSystemLatencyScore: _latencyScoreSamples.Count > 0 ? (int)_latencyScoreSamples.Average() : 0,
            PeakSystemLatencyScore: _latencyScoreSamples.Count > 0 ? _latencyScoreSamples.Max() : 0,
            FreezeCount: _freezeCount
        );
    }

    // ── Composite Scores ─────────────────────────────────

    /// <summary>
    /// Memory Pressure Index (0–100):
    /// scoreA = CommittedBytes/CommitLimit × 100    [vikt 0.40]
    /// scoreB = Clamp(PagesInputPerSec/50, 0, 100)  [vikt 0.35]
    /// scoreC = 100 - (AvailableMBytes/TotalMB×100)  [vikt 0.25]
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
    /// scoreA = Clamp((DPC%+IRQ%)/0.20×100, 0, 100)            [vikt 0.30]
    /// scoreB = Clamp(MaxDiskLatencyMs/2, 0, 100)               [vikt 0.35]
    /// scoreC = Clamp(ProcQueueLength/(2×cores)×100, 0, 100)    [vikt 0.35]
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
            if (_consecutiveHighNetwork == NetworkSpikeConsecutive)
            {
                AllEvents.Add(new MonitorEvent(
                    DateTime.Now, "NetworkSpike",
                    $"Nätverksspik: {sample.NetworkMbps:F0} Mbps i {_consecutiveHighNetwork * 3} sekunder",
                    sample.NetworkMbps > 500 ? "Critical" : "Warning",
                    "Kontrollera om något laddar ned/upp i bakgrunden (Windows Update, OneDrive, Steam, etc). "
                    + "Öppna Aktivitetshanteraren → Nätverk-kolumnen. "
                    + "Stäng av automatiska uppdateringar tillfälligt om det stör."));
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
                    !e.Description.Contains("svarade inte"));

                if (hangEvent is not null)
                {
                    int idx = AllEvents.LastIndexOf(hangEvent);
                    string freezeHint = sample.FreezeInfo is not null && sample.FreezeInfo.ProcessName == hang.Name
                        ? $" → Trolig orsak: {sample.FreezeInfo.LikelyCause}"
                        : "";
                    AllEvents[idx] = hangEvent with
                    {
                        Description = $"UI-hängning: {hang.Name} svarar inte ({hang.HangSeconds:F0} sek){freezeHint}"
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
            if (ev.EventType == "Hang" && !ev.Description.Contains("svarade inte"))
            {
                // Check if this hang's process is resolved
                foreach (var name in tracker.Keys.Where(k => !currentHangNames.Contains(k)))
                {
                    if (ev.Description.Contains(name))
                    {
                        var hangDuration = (DateTime.Now - tracker[name]).TotalSeconds;
                        AllEvents[i] = ev with
                        {
                            Description = $"UI-hängning: {name} svarade inte ({hangDuration:F0} sek, återhämtad)"
                        };
                    }
                }
            }
        }
    }

    // ── Report tracking ──────────────────────────────────

    private void TrackSamplesForReport(MonitorSample s)
    {
        _cpuSamples.Add(s.CpuPercent);
        _memorySamples.Add(s.MemoryUsedPercent);
        _diskQueueSamples.Add(s.DiskQueueLength);
        _networkSamples.Add(s.NetworkMbps);
        _handleSamples.Add(s.TotalSystemHandles);
        _pageFaultSamples.Add(s.PageFaultsPerSec);
        _committedBytesSamples.Add(s.CommittedBytes);
        _pagesInputSamples.Add(s.PagesInputPerSec);
        _dpcTimeSamples.Add(s.DpcTimePercent);
        _interruptTimeSamples.Add(s.InterruptTimePercent);
        _contextSwitchSamples.Add(s.ContextSwitchesPerSec);
        _procQueueSamples.Add(s.ProcessorQueueLength);
        _diskSecReadSamples.Add(s.AvgDiskSecRead);
        _diskSecWriteSamples.Add(s.AvgDiskSecWrite);
        _poolNonpagedSamples.Add(s.PoolNonpagedBytes);
        _poolPagedSamples.Add(s.PoolPagedBytes);
        _memPressureSamples.Add(s.MemoryPressureIndex);
        _latencyScoreSamples.Add(s.SystemLatencyScore);
        _gpuUtilSamples.Add(s.GpuUtilizationPercent);
        _dnsLatencySamples.Add(s.DnsLatencyMs);
        _storageErrorSamples.Add(s.StorageErrorsLast15Min);
    }

    private static double SafeAvg(List<double> list) => list.Count > 0 ? list.Average() : 0;
    private static double SafeMax(List<double> list) => list.Count > 0 ? list.Max() : 0;
}
