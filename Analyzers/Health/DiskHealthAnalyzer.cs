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
                "SELECT Name, CurrentDiskQueueLength, AvgDiskSecPerRead, AvgDiskSecPerWrite, PercentDiskTime " +
                "FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk");

            if (allDisks.Count > 0)
            {
                var instances = new List<DiskInstanceStat>();
                foreach (var disk in allDisks)
                {
                    string name = WmiHelper.GetValue<string>(disk, "Name") ?? "Okänd";
                    double queue = WmiHelper.GetValue<double>(disk, "CurrentDiskQueueLength");
                    double readSec = WmiHelper.GetValue<double>(disk, "AvgDiskSecPerRead");
                    double writeSec = WmiHelper.GetValue<double>(disk, "AvgDiskSecPerWrite");
                    double busy = WmiHelper.GetValue<double>(disk, "PercentDiskTime");

                    if (name.Equals("_Total", StringComparison.OrdinalIgnoreCase))
                    {
                        builder.DiskQueueLength = queue;
                        builder.AvgDiskSecRead = readSec;
                        builder.AvgDiskSecWrite = writeSec;
                    }
                    else if (!name.StartsWith("HarddiskVolume", StringComparison.OrdinalIgnoreCase))
                    {
                        instances.Add(new DiskInstanceStat(name, queue, readSec * 1000, writeSec * 1000, busy));
                    }
                }

                builder.DiskInstances = instances.OrderByDescending(d => Math.Max(d.ReadLatencyMs, d.WriteLatencyMs)).Take(6).ToList();
            }
        }
        catch { }

        if (DateTime.UtcNow - _lastStoragePoll > TimeSpan.FromMinutes(1))
        {
            _cachedStorageErrors = QueryRecentStorageErrors(15);
            _lastStoragePoll = DateTime.UtcNow;
        }

        builder.StorageErrorsLast15Min = _cachedStorageErrors;

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
            .OrderByDescending(d => Math.Max(d.ReadLatencyMs, d.WriteLatencyMs))
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
                    $"Disk I/O-flaskhals: kölängd {current.DiskQueueLength:F1} i {_consecutiveHighDiskQueue * 3} sekunder",
                    isCritical ? "Critical" : "Warning",
                    "Kontrollera bakgrundsprocesser. "
                    + GetTopIoHint(current)
                    + GetWorstDiskHint(worstDisk)));
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
                    $"Disk-latens: Läs {current.AvgDiskSecRead * 1000:F1}ms, Skriv {current.AvgDiskSecWrite * 1000:F1}ms",
                    isCritical ? "Critical" : "Warning",
                    $"Diskens svarstid är hög ({maxLatencyMs:F0}ms). "
                    + GetTopIoHint(current)
                    + GetWorstDiskHint(worstDisk)));
            }
        }
        else
        {
            _consecutiveHighDiskLatency = 0;
        }

        if (current.StorageErrorsLast15Min > 0)
        {
            healthScore -= current.StorageErrorsLast15Min > 5 ? 30 : 15;
            events.Add(new MonitorEvent(
                DateTime.Now,
                "StorageReset",
                $"Lagringsfel i systemloggen: {current.StorageErrorsLast15Min} senaste 15 min",
                current.StorageErrorsLast15Min > 5 ? "Critical" : "Warning",
                "Disk/controller rapporterar fel eller timeout (ex. Event ID 129/153/7/51). Kontrollera kablar, firmware, drivrutiner och diskhälsa."));
        }

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = history.Count >= 3 ? 1.0 : history.Count / 3.0;

        string? hint = maxLatencyMs > DiskLatencyWarningMs
            ? "Hög disklatens: I/O-operationer tar för lång tid"
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
            return "Ingen I/O-process kunde identifieras i detta sample. ";

        var top = sample.TopIoProcesses.Take(3)
            .Select(proc => $"{proc.Name} ({ConsoleHelper.FormatBytes((long)proc.TotalBytes)})");

        return $"Största I/O-processer: {string.Join(", ", top)}. ";
    }

    private static string GetWorstDiskHint(DiskInstanceStat? disk)
    {
        if (disk is null)
            return string.Empty;

        return $"Värst disk: {disk.Name} (R {disk.ReadLatencyMs:F1}ms, W {disk.WriteLatencyMs:F1}ms, kö {disk.QueueLength:F1}, busy {disk.BusyPercent:F0}%).";
    }

    private static int QueryRecentStorageErrors(int windowMinutes)
    {
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
                    if (count >= 50)
                        break;
                }
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }
}
