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

    // Cooldown — max 1 event per typ per 60 sek
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
                    
                    // Fire and forget - results will be available in next iteration
                    _ = Task.Run(async () =>
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
    }

    public HealthAssessment Analyze(MonitorSample current, IReadOnlyList<MonitorSample> history)
    {
        var events = new List<MonitorEvent>();
        int healthScore = 100;

        // 1. Page fault storm (nu baserat på PagesInput istället för PageFaults)
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
                string procHint = topMem.Count > 0 ? $" Störst i minnet: {string.Join(", ", topMem)}." : "";

                events.Add(new MonitorEvent(
                    DateTime.Now, "PageFaultStorm",
                    $"Hård paging-storm: {current.PagesInputPerSec:F0} sidor/s läses från disk i {_consecutiveHighPagesInput * 3} sek",
                    isCritical ? "Critical" : "Warning",
                    $"Systemet läser intensivt från sidfilen — fysiskt RAM räcker inte.{procHint} "
                    + "ÅTGÄRDER: 1) Stäng tunga program och webbläsarflikar. "
                    + "2) Öka sidfilen: Inställningar → System → Om → Avancerade systeminställningar → Virtuellt minne. "
                    + "3) Om det händer ofta: uppgradera RAM. "
                    + "4) Kontrollera att sidfilen ligger på SSD, inte HDD."));
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
                string procList = topMem.Count > 0 ? $" Störst just nu: {string.Join(", ", topMem)}." : "";

                events.Add(new MonitorEvent(
                    DateTime.Now, "MemorySpike",
                    $"Minnesökning: {ConsoleHelper.FormatBytes(drop)} förbrukat på 30 sek.{procList}",
                    isCritical ? "Critical" : "Warning",
                    $"Stäng onödiga flikar i webbläsaren och tunga program du inte använder.{procList} "
                    + "Om minnet konstant är högt: öka sidfilen eller överväg mer RAM."));
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
                    string procHint = topMem.Count > 0 ? $" Störst just nu: {string.Join(", ", topMem)}." : "";

                    events.Add(new MonitorEvent(
                        DateTime.Now, "CommitExhaustion",
                        $"Commit-gräns: {commitRatio * 100:F0}% av tillgängligt minne använt ({ConsoleHelper.FormatBytes(current.CommittedBytes)}/{ConsoleHelper.FormatBytes(current.CommitLimit)})",
                        isCritical ? "Critical" : "Warning",
                        $"Systemets minnesåtagande närmar sig gränsen. Om gränsen nås kan program krascha.{procHint} "
                        + "Öka sidfilen: Systeminställningar → Avancerat → Prestanda → Virtuellt minne. "
                        + "Stäng program du inte använder."));
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
                    driverHint = $" Topp pool-taggar: {string.Join(", ", topPool)}.";
                }
                
                events.Add(new MonitorEvent(
                    DateTime.Now, "PoolExhaustion",
                    $"Kernel pool: Nonpaged pool är {ConsoleHelper.FormatBytes(current.PoolNonpagedBytes)}{driverHint}",
                    isCritical ? "Critical" : "Warning",
                    "Kernel nonpaged pool är ovanligt hög. Detta orsakas vanligen av en drivrutin som läcker minne. "
                    + "Verktyg: Kör 'poolmon.exe' (Sysinternals) för att identifiera vilken drivrutin. "
                    + "Uppdatera nätverks-, GPU- och lagringsdrivrutiner."));
            }
        }
        else
        {
            _consecutiveHighPool = 0;
        }

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = history.Count >= 3 ? 1.0 : history.Count / 3.0;

        string? hint = healthScore < 50
            ? "Minnestryck: systemet pagar aktivt och committed memory är högt"
            : null;

        return new HealthAssessment(
            new HealthScore(Domain, healthScore, confidence, hint),
            events);
    }
}
