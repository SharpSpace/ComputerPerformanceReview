namespace ComputerPerformanceReview.Analyzers.Health;

/// <summary>
/// Klassificerar VARFÖR en process hänger sig baserat på systemets tillstånd.
/// Korrelerar CPU, disk, minne och scheduler-data för att ge en trolig orsak.
/// </summary>
public static class FreezeDetector
{
    public static FreezeClassification? Classify(
        string processName,
        MonitorSample sample)
    {
        double diskLatencyMs = Math.Max(sample.AvgDiskSecRead, sample.AvgDiskSecWrite) * 1000;

        // Diskbunden: hög disklatens eller lång kö
        if (diskLatencyMs > 100 || sample.DiskQueueLength > 4)
        {
            return new FreezeClassification(
                processName,
                "Diskbunden",
                $"Diskens svarstid är {diskLatencyMs:F0}ms (kö: {sample.DiskQueueLength:F1}). "
                + "Processen väntar troligen på disk-I/O. "
                + "Om SSD: kontrollera TRIM och firmware. Om HDD: överväg SSD-uppgradering.");
        }

        // CPU-mättnad: processorn är fullt belastad
        if (sample.CpuPercent > 80)
        {
            return new FreezeClassification(
                processName,
                "CPU-mättnad",
                $"CPU ligger på {sample.CpuPercent:F0}% — alla kärnor är hårt belastade. "
                + "Processen kan inte få CPU-tid. Stäng tunga program eller vänta tills lasten minskar.");
        }

        // Minnestryck: systemet pagar intensivt
        if (sample.MemoryPressureIndex > 70)
        {
            return new FreezeClassification(
                processName,
                "Minnestryck",
                $"Minnestrycksindex: {sample.MemoryPressureIndex}/100. "
                + "Systemet tvingas använda sidfilen intensivt. "
                + "Stäng program/flikar för att frigöra RAM, eller uppgradera minnet.");
        }

        // Låskonflikt: CPU och disk är lugna men processen hänger ändå
        if (sample.CpuPercent < 30 && sample.DiskQueueLength < 2 && diskLatencyMs < 50)
        {
            return new FreezeClassification(
                processName,
                "Låskonflikt",
                "CPU och disk är normala men processen svarar inte. "
                + "Trolig orsak: intern låskonflikt (deadlock), blockerad UI-tråd, "
                + "väntar på nätverk/drivrutin, eller thread pool starvation. "
                + "Prova vänta 30 sek. Om det inte löser sig: starta om programmet.");
        }

        // DPC/drivrutinsproblem
        if (sample.DpcTimePercent > 15)
        {
            return new FreezeClassification(
                processName,
                "Drivrutinsproblem",
                $"DPC-tid: {sample.DpcTimePercent:F1}%. En drivrutin blockerar processorn. "
                + "Uppdatera GPU-, nätverks- och USB-drivrutiner. Kör LatencyMon för detaljer.");
        }

        // Okänd orsak
        return new FreezeClassification(
            processName,
            "Okänd",
            "Kunde inte fastställa orsaken. Möjliga förklaringar: "
            + "nätverksbaserad väntan, GPU-drivrutin, eller internt programfel. "
            + "Kontrollera nätverksanslutningen och uppdatera drivrutiner.");
    }
}
