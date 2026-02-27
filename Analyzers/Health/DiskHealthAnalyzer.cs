using System.Diagnostics.Eventing.Reader;

namespace ComputerPerformanceReview.Analyzers.Health;

public sealed class DiskHealthAnalyzer : IHealthSubAnalyzer
{
    public string Domain => "Disk";

    private const double DiskQueueThreshold = 2.0;
    private const int DiskQueueConsecutive = 2;
    private const double DiskLatencyWarningMs = 50;
    private const double DiskLatencyCriticalMs = 200;
    private const int DiskLatencyConsecutive = 2;

    private int _consecutiveHighDiskQueue;
    private int _consecutiveHighDiskLatency;

    private DateTime _lastStoragePoll = DateTime.MinValue;
    private int _cachedStorageErrors;
    private List<StorageErrorDetail>? _cachedStorageErrorDetails;

    // Sysinternals DiskExt integration
    private DateTime _lastDiskExtRun = DateTime.MinValue;
    private const int DiskExtCooldownSeconds = 120; // Run DiskExt at most every 2 minutes
    private string? _cachedDiskExtOutput;
    private Task? _diskExtTask; // Track running DiskExt task

    public void Collect(MonitorSampleBuilder builder)
    {
        try
        {
            var allDisks = WmiHelper.Query(
                "SELECT Name, CurrentDiskQueueLength, AvgDiskSecPerRead, AvgDiskSecPerWrite, PercentDiskTime, " +
                "PercentIdleTime, DiskReadsPerSec, DiskWritesPerSec, DiskReadBytesPerSec, DiskWriteBytesPerSec " +
                "FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk");

            if (allDisks.Count > 0)
            {
                var instances = new List<DiskInstanceStat>();
                foreach (var disk in allDisks)
                {
                    string name = WmiHelper.GetValue<string>(disk, "Name") ?? "Unknown";
                    double queue = WmiHelper.GetValue<double>(disk, "CurrentDiskQueueLength");
                    double readSec = WmiHelper.GetValue<double>(disk, "AvgDiskSecPerRead");
                    double writeSec = WmiHelper.GetValue<double>(disk, "AvgDiskSecPerWrite");
                    double busy = WmiHelper.GetValue<double>(disk, "PercentDiskTime");
                    double idle = WmiHelper.GetValue<double>(disk, "PercentIdleTime");
                    double readIops = WmiHelper.GetValue<double>(disk, "DiskReadsPerSec");
                    double writeIops = WmiHelper.GetValue<double>(disk, "DiskWritesPerSec");
                    double readBytes = WmiHelper.GetValue<double>(disk, "DiskReadBytesPerSec");
                    double writeBytes = WmiHelper.GetValue<double>(disk, "DiskWriteBytesPerSec");

                    if (name.Equals("_Total", StringComparison.OrdinalIgnoreCase))
                    {
                        builder.DiskQueueLength = queue;
                        builder.AvgDiskSecRead = readSec;
                        builder.AvgDiskSecWrite = writeSec;
                    }
                    else if (!name.StartsWith("HarddiskVolume", StringComparison.OrdinalIgnoreCase))
                    {
                        instances.Add(new DiskInstanceStat(name, queue, readSec * 1000, writeSec * 1000, busy,
                            idle, readIops, writeIops, readBytes, writeBytes));
                    }
                }

                builder.DiskInstances = instances.OrderByDescending(DiskSeverityScore).Take(6).ToList();
            }
        }
        catch { }

        if (DateTime.UtcNow - _lastStoragePoll > TimeSpan.FromMinutes(1))
        {
            var (count, details) = QueryRecentStorageErrorsWithDetails(15);
            _cachedStorageErrors = count;
            _cachedStorageErrorDetails = details;
            _lastStoragePoll = DateTime.UtcNow;
        }

        builder.StorageErrorsLast15Min = _cachedStorageErrors;
        builder.StorageErrorDetails = _cachedStorageErrorDetails;

        if (!string.IsNullOrWhiteSpace(_cachedDiskExtOutput))
            builder.SysinternalsDiskExtOutput = _cachedDiskExtOutput;

        double maxLatencyMs = Math.Max(builder.AvgDiskSecRead, builder.AvgDiskSecWrite) * 1000;
        bool shouldRunDiskExt = builder.DiskQueueLength > DiskQueueThreshold
            || maxLatencyMs > DiskLatencyWarningMs
            || builder.StorageErrorsLast15Min > 0;

        if (shouldRunDiskExt && (DateTime.Now - _lastDiskExtRun).TotalSeconds >= DiskExtCooldownSeconds)
        {
            _lastDiskExtRun = DateTime.Now;
            _diskExtTask = Task.Run(async () =>
            {
                try
                {
                    var output = await SysinternalsHelper.RunDiskExtAsync();
                    var summary = SummarizeDiskExtOutput(output);
                    if (!string.IsNullOrWhiteSpace(summary))
                        _cachedDiskExtOutput = summary;
                }
                catch { /* Ignore Sysinternals failures - not critical */ }
            });
        }
    }

    public HealthAssessment Analyze(MonitorSample current, IReadOnlyList<MonitorSample> history)
    {
        var events = new List<MonitorEvent>();
        int healthScore = 100;

        var worstDisk = current.DiskInstances
            .OrderByDescending(DiskSeverityScore)
            .FirstOrDefault();

        if (current.DiskQueueLength > DiskQueueThreshold)
        {
            _consecutiveHighDiskQueue++;
            if (_consecutiveHighDiskQueue == DiskQueueConsecutive)
            {
                bool isCritical = current.DiskQueueLength > 5;
                healthScore -= isCritical ? 25 : 10;
                events.Add(new MonitorEvent(
                    DateTime.Now,
                    "DiskBottleneck",
                    $"Disk I/O bottleneck: queue length {current.DiskQueueLength:F1} for {_consecutiveHighDiskQueue * 3} seconds",
                    isCritical ? "Critical" : "Warning",
                    "The disk is overloaded with I/O requests causing slow response. ACTIONS: " +
                    "1) Identify heavy I/O: " + GetTopIoHint(current) +
                    "2) Close heavy downloads, backup jobs or file sync (OneDrive, Dropbox). " +
                    "3) If HDD: Upgrade to SSD for the system disk (C:). " +
                    "4) Move large games/programs to a separate SSD. " +
                    "5) Check disk health: Run 'wmic diskdrive get status' in Command Prompt — all disks should show 'OK'. " +
                    GetWorstDiskHint(worstDisk)));
            }
        }
        else
        {
            _consecutiveHighDiskQueue = 0;
        }

        double maxLatencyMs = Math.Max(current.AvgDiskSecRead, current.AvgDiskSecWrite) * 1000;
        if (maxLatencyMs > DiskLatencyWarningMs)
        {
            _consecutiveHighDiskLatency++;
            if (_consecutiveHighDiskLatency == DiskLatencyConsecutive)
            {
                bool isCritical = maxLatencyMs > DiskLatencyCriticalMs;
                healthScore -= isCritical ? 30 : 15;

                events.Add(new MonitorEvent(
                    DateTime.Now,
                    "DiskLatency",
                    $"Disk latency: Read {current.AvgDiskSecRead * 1000:F1}ms, Write {current.AvgDiskSecWrite * 1000:F1}ms",
                    isCritical ? "Critical" : "Warning",
                    $"Disk response time is high ({maxLatencyMs:F0}ms) causing sluggish system response. ACTIONS: " +
                    "1) Identify I/O load: " + GetTopIoHint(current) +
                    "2) If HDD: Upgrade to SSD for significantly better latency (<1ms instead of 10-20ms). " +
                    "3) Run disk check: Open Command Prompt as admin → Run 'chkdsk C: /F /R' (replace C: with the current disk) → Accept restart. " +
                    "4) Check SMART status: Use tools like CrystalDiskInfo to check disk health. " +
                    "5) Disable Windows Search indexing temporarily: services.msc → Windows Search → Stop. " +
                    GetWorstDiskHint(worstDisk)));
            }
        }
        else
        {
            _consecutiveHighDiskLatency = 0;
        }

        if (current.StorageErrorsLast15Min > 0)
        {
            healthScore -= current.StorageErrorsLast15Min > 5 ? 30 : 15;

            string detailSuffix = "";
            if (current.StorageErrorDetails is { Count: > 0 })
            {
                var latest = current.StorageErrorDetails[0];
                detailSuffix = $" Latest: [{latest.Source}] Event {latest.EventId}: {latest.Message}";
            }

            events.Add(new MonitorEvent(
                DateTime.Now,
                "StorageReset",
                $"Storage errors in system log: {current.StorageErrorsLast15Min} in last 15 min.{detailSuffix}",
                current.StorageErrorsLast15Min > 5 ? "Critical" : "Warning",
                "Disk/controller is reporting errors or timeouts (Event ID 129/153/7/51) which may indicate hardware problems. " +
                "ACTIONS: " +
                "1) Check SMART status: Download CrystalDiskInfo and check disk health — 'Caution' or 'Bad' indicates disk failure. " +
                "2) Update storage drivers: Device Manager → IDE ATA/ATAPI controllers → Right-click → Update driver. " +
                "3) Check cables: Replace SATA cable or check M.2 connector. " +
                "4) Run disk check: chkdsk C: /F /R in Command Prompt as admin. " +
                "5) Update disk/controller firmware from the manufacturer's website. " +
                "6) If problems persist: Back up data immediately — the disk may be failing."));
        }

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = history.Count >= 3 ? 1.0 : history.Count / 3.0;

        string? hint = maxLatencyMs > DiskLatencyWarningMs
            ? "High disk latency: I/O operations are taking too long"
            : null;

        return new HealthAssessment(new HealthScore(Domain, healthScore, confidence, hint), events);
    }

    private static string? SummarizeDiskExtOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                return trimmed;
        }

        return null;
    }

    private static string GetTopIoHint(MonitorSample sample)
    {
        if (sample.TopIoProcesses.Count == 0)
            return "No I/O process could be identified in this sample. ";

        var top = sample.TopIoProcesses.Take(3)
            .Select(proc => $"{proc.Name} ({ConsoleHelper.FormatBytes((long)proc.TotalBytes)})");

        return $"Top I/O processes: {string.Join(", ", top)}. ";
    }

    private static string GetWorstDiskHint(DiskInstanceStat? disk)
    {
        if (disk is null)
            return string.Empty;

        double totalIops = disk.ReadIops + disk.WriteIops;
        string readTp = ConsoleHelper.FormatBytes((long)disk.ReadBytesPerSec) + "/s";
        string writeTp = ConsoleHelper.FormatBytes((long)disk.WriteBytesPerSec) + "/s";

        return $"Worst disk: {disk.Name} (R {disk.ReadLatencyMs:F1}ms, W {disk.WriteLatencyMs:F1}ms, " +
               $"queue {disk.QueueLength:F1}, IOPS {totalIops:F0}, R {readTp} W {writeTp}, idle {disk.IdlePercent:F0}%).";
    }

    /// <summary>
    /// Composite score for ranking disk instances by severity.
    /// Higher score = worse performance.
    /// </summary>
    internal static double DiskSeverityScore(DiskInstanceStat d) =>
        Math.Max(d.ReadLatencyMs, d.WriteLatencyMs) * 10
        + (100 - d.IdlePercent)
        + d.QueueLength * 5
        + (d.ReadIops + d.WriteIops) / 100.0;

    private static (int Count, List<StorageErrorDetail> Details) QueryRecentStorageErrorsWithDetails(int windowMinutes)
    {
        var details = new List<StorageErrorDetail>();
        try
        {
            string xPath = "*[System[(Level=2 or Level=3) and "
                + "(EventID=7 or EventID=51 or EventID=129 or EventID=153) and "
                + $"TimeCreated[timediff(@SystemTime) <= {windowMinutes * 60 * 1000}]]]";

            var query = new EventLogQuery("System", PathType.LogName, xPath)
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            int count = 0;
            while (reader.ReadEvent() is EventRecord rec)
            {
                using (rec)
                {
                    count++;
                    if (details.Count < 10)
                    {
                        string message;
                        try { message = rec.FormatDescription() ?? "No description"; }
                        catch { message = "No description"; }

                        if (message.Length > 120)
                            message = message[..120] + "...";

                        details.Add(new StorageErrorDetail(
                            rec.TimeCreated ?? DateTime.Now,
                            rec.Id,
                            rec.ProviderName ?? "Unknown",
                            message));
                    }
                    if (count >= 50)
                        break;
                }
            }

            return (count, details);
        }
        catch
        {
            return (0, details);
        }
    }
}
