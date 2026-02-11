namespace ComputerPerformanceReview.Analyzers.Health;

public sealed class DiskHealthAnalyzer : IHealthSubAnalyzer
{
    public string Domain => "Disk";

    // Thresholds
    private const double DiskQueueThreshold = 2.0;
    private const int DiskQueueConsecutive = 2;
    private const double DiskLatencyWarningMs = 50;   // 50ms for SSD concern
    private const double DiskLatencyCriticalMs = 200;  // 200ms = serious
    private const int DiskLatencyConsecutive = 2;

    // State
    private int _consecutiveHighDiskQueue;
    private int _consecutiveHighDiskLatency;

    public void Collect(MonitorSampleBuilder builder)
    {
        try
        {
            var data = WmiHelper.Query(
                "SELECT CurrentDiskQueueLength, AvgDiskSecPerRead, AvgDiskSecPerWrite " +
                "FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'");
            if (data.Count > 0)
            {
                builder.DiskQueueLength = WmiHelper.GetValue<double>(data[0], "CurrentDiskQueueLength");
                builder.AvgDiskSecRead = WmiHelper.GetValue<double>(data[0], "AvgDiskSecPerRead");
                builder.AvgDiskSecWrite = WmiHelper.GetValue<double>(data[0], "AvgDiskSecPerWrite");
            }
        }
        catch { }
    }

    public HealthAssessment Analyze(MonitorSample current, IReadOnlyList<MonitorSample> history)
    {
        var events = new List<MonitorEvent>();
        int healthScore = 100;

        // 1. Disk queue bottleneck
        if (current.DiskQueueLength > DiskQueueThreshold)
        {
            _consecutiveHighDiskQueue++;
            if (_consecutiveHighDiskQueue == DiskQueueConsecutive)
            {
                bool isCritical = current.DiskQueueLength > 5;
                healthScore -= isCritical ? 25 : 10;
                events.Add(new MonitorEvent(
                    DateTime.Now, "DiskBottleneck",
                    $"Disk I/O-flaskhals: kölängd {current.DiskQueueLength:F1} i {_consecutiveHighDiskQueue * 3} sekunder",
                    isCritical ? "Critical" : "Warning",
                    "Kontrollera om Windows Update, antivirus eller indexering körs i bakgrunden. "
                    + "Öppna Aktivitetshanteraren → Disk-kolumnen för att se vilken process som läser/skriver mest. "
                    + "Om du har HDD: överväg att uppgradera till SSD. Rensa temp-filer med Diskrensning."));
            }
        }
        else
        {
            _consecutiveHighDiskQueue = 0;
        }

        // 2. Disk latency warning (the real indicator of "slow disk")
        double maxLatencyMs = Math.Max(current.AvgDiskSecRead, current.AvgDiskSecWrite) * 1000;
        if (maxLatencyMs > DiskLatencyWarningMs)
        {
            _consecutiveHighDiskLatency++;
            if (_consecutiveHighDiskLatency == DiskLatencyConsecutive)
            {
                bool isCritical = maxLatencyMs > DiskLatencyCriticalMs;
                healthScore -= isCritical ? 30 : 15;

                string diskType = maxLatencyMs > 100 ? "Om detta är en SSD kan det tyda på firmware-problem eller TRIM som inte fungerar. " : "";
                events.Add(new MonitorEvent(
                    DateTime.Now, "DiskLatency",
                    $"Disk-latens: Läs {current.AvgDiskSecRead * 1000:F1}ms, Skriv {current.AvgDiskSecWrite * 1000:F1}ms",
                    isCritical ? "Critical" : "Warning",
                    $"Diskens svarstid är onormalt hög ({maxLatencyMs:F0}ms). Normal SSD: <5ms, HDD: <20ms. "
                    + diskType
                    + "ÅTGÄRDER: 1) Kontrollera diskens hälsa med CrystalDiskInfo. "
                    + "2) Kör TRIM manuellt: 'Optimize-Volume -DriveLetter C -ReTrim' i admin PowerShell. "
                    + "3) Om HDD → uppgradera till SSD. "
                    + "4) Kontrollera att AHCI är aktiverat i BIOS."));
            }
        }
        else
        {
            _consecutiveHighDiskLatency = 0;
        }

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = history.Count >= 3 ? 1.0 : history.Count / 3.0;

        string? hint = maxLatencyMs > DiskLatencyWarningMs
            ? "Hög disklatens: I/O-operationer tar för lång tid"
            : null;

        return new HealthAssessment(
            new HealthScore(Domain, healthScore, confidence, hint),
            events);
    }
}
