namespace ComputerPerformanceReview.Analyzers.Health;

/// <summary>
/// Klassificerar VARFÖR en process hänger sig baserat på systemets tillstånd
/// och per-process-data. Korrelerar CPU, disk, minne, GPU, nätverk, trådar,
/// handles och I/O för att ge en detaljerad orsaksanalys.
/// </summary>
public static class FreezeDetector
{
    public static FreezeClassification? Classify(
        string processName,
        MonitorSample sample)
    {
        double diskLatencyMs = Math.Max(sample.AvgDiskSecRead, sample.AvgDiskSecWrite) * 1000;

        // ── 1. Lagringsfel — disken har hårdvaruproblem ──
        if (sample.StorageErrorsLast15Min > 0)
        {
            var evidence = new List<string>
            {
                $"Systemloggen visar {sample.StorageErrorsLast15Min} lagringsfel senaste 15 min",
                $"Disklatens: R {sample.AvgDiskSecRead * 1000:F1}ms / W {sample.AvgDiskSecWrite * 1000:F1}ms",
                $"Diskkö: {sample.DiskQueueLength:F1}"
            };

            return new FreezeClassification(
                processName,
                "Lagringsfel",
                $"Systemloggen visar {sample.StorageErrorsLast15Min} lagringsfel senaste 15 min. "
                + "ÅTGÄRDER: Kontrollera SMART-status och uppdatera lagringsdrivrutiner.",
                evidence);
        }

        // ── 2. Diskbunden — hög disklatens eller lång kö ──
        if (diskLatencyMs > 100 || sample.DiskQueueLength > 4)
        {
            var evidence = new List<string>
            {
                $"Disklatens: R {sample.AvgDiskSecRead * 1000:F1}ms / W {sample.AvgDiskSecWrite * 1000:F1}ms",
                $"Diskkö: {sample.DiskQueueLength:F1}"
            };

            // Check if the hanging process itself has high I/O
            var procIo = sample.TopIoProcesses.FirstOrDefault(p =>
                p.Name.Equals(processName, StringComparison.OrdinalIgnoreCase));
            if (procIo is not null)
                evidence.Add($"{processName} I/O: {ConsoleHelper.FormatBytes((long)procIo.TotalBytes)} totalt");

            var worstDisk = sample.DiskInstances
                .OrderByDescending(d => Math.Max(d.ReadLatencyMs, d.WriteLatencyMs))
                .FirstOrDefault();
            if (worstDisk is not null)
                evidence.Add($"Värst disk: {worstDisk.Name} (R {worstDisk.ReadLatencyMs:F0}ms, W {worstDisk.WriteLatencyMs:F0}ms)");

            return new FreezeClassification(
                processName,
                "Diskbunden",
                $"Diskens svarstid är {diskLatencyMs:F0}ms (kö: {sample.DiskQueueLength:F1}). "
                + "Processen väntar troligen på disk-I/O.",
                evidence);
        }

        // ── 3. GPU-mättnad — grafikbearbetning blockerar ──
        if (sample.GpuUtilizationPercent > 95)
        {
            var evidence = new List<string>
            {
                $"GPU: {sample.GpuUtilizationPercent:F0}%"
            };
            if (sample.GpuDedicatedUsageBytes > 0 && sample.GpuDedicatedLimitBytes > 0)
            {
                double vramPercent = (double)sample.GpuDedicatedUsageBytes / sample.GpuDedicatedLimitBytes * 100;
                evidence.Add($"VRAM: {vramPercent:F0}% ({ConsoleHelper.FormatBytes(sample.GpuDedicatedUsageBytes)}/{ConsoleHelper.FormatBytes(sample.GpuDedicatedLimitBytes)})");
            }
            if (sample.TdrEventsLast15Min > 0)
                evidence.Add($"TDR-events: {sample.TdrEventsLast15Min} senaste 15 min");

            return new FreezeClassification(
                processName,
                "GPU-mättnad",
                $"GPU ligger på {sample.GpuUtilizationPercent:F0}%. UI-rendering kan blockeras. "
                + (sample.TdrEventsLast15Min > 0
                    ? "ÅTGÄRD: Uppdatera eller rulla tillbaka GPU-drivrutinen. "
                    : "ÅTGÄRD: Sänk grafikbelastningen och uppdatera GPU-drivrutinen. "),
                evidence);
        }

        // ── 4. Drivrutinsproblem — DPC blockerar processorn ──
        if (sample.DpcTimePercent > 15)
        {
            var evidence = new List<string>
            {
                $"DPC-tid: {sample.DpcTimePercent:F1}%",
                $"IRQ-tid: {sample.InterruptTimePercent:F1}%"
            };

            return new FreezeClassification(
                processName,
                "Drivrutinsproblem",
                $"DPC-tid: {sample.DpcTimePercent:F1}%. En drivrutin blockerar processorn. "
                + "ÅTGÄRDER: Uppdatera GPU-, nätverks- och USB-drivrutiner.",
                evidence);
        }

        // ── 5. Nätverkslatens — DNS-uppslag tar lång tid ──
        if (sample.DnsLatencyMs > 300)
        {
            var evidence = new List<string>
            {
                $"DNS-latens: {sample.DnsLatencyMs:F0}ms",
                $"Nätverksbelastning: {sample.NetworkMbps:F1} Mbps"
            };

            return new FreezeClassification(
                processName,
                "Nätverkslatens",
                $"DNS-latens är {sample.DnsLatencyMs:F0}ms. Processen kan vänta på nätverkssvar. "
                + "ÅTGÄRD: Kontrollera nätverket eller byt DNS-server.",
                evidence);
        }

        // ── 6. CPU-mättnad — processorn orkar inte ──
        if (sample.CpuPercent > 80)
        {
            var evidence = new List<string>
            {
                $"CPU: {sample.CpuPercent:F0}%",
                $"Processorkö: {sample.ProcessorQueueLength}"
            };
            if (sample.TopCpuProcesses.Count > 0)
            {
                evidence.Add("Topp CPU: " + string.Join(", ",
                    sample.TopCpuProcesses.Take(3).Select(p => $"{p.Name} ({p.CpuPercent:F0}%)")));
            }
            if (sample.CpuMaxClockMHz > 0)
            {
                double ratio = sample.CpuClockMHz / sample.CpuMaxClockMHz;
                evidence.Add($"Klockfrekvens: {sample.CpuClockMHz:F0}/{sample.CpuMaxClockMHz:F0} MHz ({ratio * 100:F0}%)");
                if (ratio < 0.6)
                    evidence.Add("⚠ CPU throttlar — kontrollera temperatur!");
            }

            return new FreezeClassification(
                processName,
                "CPU-mättnad",
                $"CPU ligger på {sample.CpuPercent:F0}%. Processen får lite CPU-tid.",
                evidence);
        }

        // ── 7. Minnestryck — systemet pagar intensivt ──
        if (sample.MemoryPressureIndex > 70)
        {
            var evidence = new List<string>
            {
                $"Minnestrycksindex: {sample.MemoryPressureIndex}/100",
                $"PagesInput: {sample.PagesInputPerSec:F0}/s",
                $"RAM: {sample.MemoryUsedPercent:F0}%"
            };
            if (sample.CommitLimit > 0)
                evidence.Add($"Commit: {ConsoleHelper.FormatBytes(sample.CommittedBytes)}/{ConsoleHelper.FormatBytes(sample.CommitLimit)}");

            return new FreezeClassification(
                processName,
                "Minnestryck",
                $"Minnestrycksindex: {sample.MemoryPressureIndex}/100. Systemet paging-ar mycket. "
                + "ÅTGÄRD: Stäng program/flikar eller öka RAM.",
                evidence);
        }

        // ── 8. Detaljerad låskonflikt-analys ──
        // CPU och disk är normala men processen hänger. Undersök per-process-data
        // för att ge en mer exakt diagnos.
        if (sample.CpuPercent < 30 && sample.DiskQueueLength < 2 && diskLatencyMs < 50)
        {
            return ClassifyLockContention(processName, sample);
        }

        // ── 9. Okänd orsak ──
        return ClassifyUnknown(processName, sample);
    }

    /// <summary>
    /// Detaljerad analys av "Låskonflikt"-scenariot — CPU och disk är normala
    /// men processen svarar inte. Använder per-process-data för att sub-klassificera.
    /// </summary>
    private static FreezeClassification ClassifyLockContention(string processName, MonitorSample sample)
    {
        var evidence = new List<string>
        {
            $"CPU: {sample.CpuPercent:F0}% (normalt)",
            $"Disk: kö {sample.DiskQueueLength:F1}, latens R {sample.AvgDiskSecRead * 1000:F1}ms W {sample.AvgDiskSecWrite * 1000:F1}ms (normalt)"
        };

        // Hitta processens egna data
        var procCpu = FindProcess(processName, sample.TopCpuProcesses);
        var procMem = FindProcess(processName, sample.TopMemoryProcesses);
        var procIo = sample.TopIoProcesses.FirstOrDefault(p =>
            p.Name.Equals(processName, StringComparison.OrdinalIgnoreCase));
        var procFaults = sample.TopFaultProcesses.FirstOrDefault(p =>
            p.Name.Equals(processName, StringComparison.OrdinalIgnoreCase));

        // Samla processpecifik evidence
        var proc = procCpu ?? procMem;
        int threadCount = proc?.ThreadCount ?? 0;
        int handleCount = proc?.HandleCount ?? 0;
        double procCpuPct = procCpu?.CpuPercent ?? 0;
        long procMemBytes = procMem?.MemoryBytes ?? 0;

        if (proc is not null)
        {
            evidence.Add($"{processName}: {procCpuPct:F0}% CPU, {ConsoleHelper.FormatBytes(procMemBytes)} RAM, {threadCount} trådar, {handleCount} handles");
        }

        if (procIo is not null)
            evidence.Add($"{processName} I/O: R {ConsoleHelper.FormatBytes((long)procIo.ReadBytes)} / W {ConsoleHelper.FormatBytes((long)procIo.WriteBytes)}");

        if (procFaults is not null)
            evidence.Add($"{processName} page faults: {procFaults.PageFaultsPerSec:F0}/s");

        // ── Sub-klassificering baserat på signaler ──

        // A. Tråd-pool starvation: mycket trådar men låg CPU → alla trådar väntar
        if (threadCount > 100 && procCpuPct < 5)
        {
            evidence.Add($"⚠ {threadCount} trådar men bara {procCpuPct:F0}% CPU → alla trådar blockerade");

            return new FreezeClassification(
                processName,
                "Tråd-pool starvation",
                $"{processName} har {threadCount} trådar men nästan ingen CPU-användning ({procCpuPct:F0}%). "
                + "Trolig thread pool starvation. ÅTGÄRD: Starta om programmet.",
                evidence);
        }

        // B. Synkron nätverksväntan: programmet gör I/O men disk är fin → troligen nätverk
        // DNS kan vara under 300ms-tröskeln men processen väntar ändå
        if (procIo is not null && procIo.TotalBytes > 0 && procCpuPct < 5)
        {
            bool networkActive = sample.NetworkMbps > 0.5;
            bool dnsElevated = sample.DnsLatencyMs > 50; // Inte kritiskt men förhöjt

            if (networkActive || dnsElevated)
            {
                evidence.Add($"Nätverksaktivitet: {sample.NetworkMbps:F1} Mbps, DNS: {sample.DnsLatencyMs:F0}ms");
                evidence.Add($"⚠ Processen har aktiv I/O men låg CPU → trolig nätverksväntan");

                return new FreezeClassification(
                    processName,
                    "Nätverksväntan (blockerad UI-tråd)",
                    $"{processName} gör I/O med låg CPU ({procCpuPct:F0}%) och nätverket är aktivt. "
                    + "Trolig synkron nätverksväntan på UI-tråden.",
                    evidence);
            }
        }

        // C. Handle-svält / resurskonflikt: väldigt många handles → process väntar på resurs
        if (handleCount > 5000 && procCpuPct < 5)
        {
            evidence.Add($"⚠ {handleCount} handles med låg CPU → trolig resurssvält");

            return new FreezeClassification(
                processName,
                "Resurssvält (handles)",
                $"{processName} har {handleCount} handles men använder knappt CPU ({procCpuPct:F0}%). "
                + "Trolig resurssvält. ÅTGÄRD: Starta om programmet.",
                evidence);
        }

        // D. Hög kontext-switch med låg CPU → processer konkurrerar om lås
        if (sample.ContextSwitchesPerSec > 30000 && procCpuPct < 10)
        {
            evidence.Add($"Context switches: {sample.ContextSwitchesPerSec:F0}/s (högt)");
            evidence.Add($"⚠ Höga context switches med låg CPU → lås-konkurrens i systemet");

            return new FreezeClassification(
                processName,
                "Lås-konkurrens (contention)",
                $"Systemet har {sample.ContextSwitchesPerSec:F0} context switches/s men CPU är {sample.CpuPercent:F0}%. "
                + $"Trolig lås-konkurrens som påverkar {processName}.",
                evidence);
        }

        // E. Page faults: processen väntar på minnessidor
        if (procFaults is not null && procFaults.PageFaultsPerSec > 100 && procCpuPct < 5)
        {
            evidence.Add($"⚠ {procFaults.PageFaultsPerSec:F0} page faults/s → programmet väntar på minnessidor");

            return new FreezeClassification(
                processName,
                "Paging-väntan",
                $"{processName} genererar {procFaults.PageFaultsPerSec:F0} page faults/s med låg CPU ({procCpuPct:F0}%). "
                + "Trolig paging-väntan.",
                evidence);
        }

        // F. Förhöjd DPC (under tröskeln men ändå märkbar) + låg process-CPU
        if (sample.DpcTimePercent > 5 && procCpuPct < 5)
        {
            evidence.Add($"DPC: {sample.DpcTimePercent:F1}% (förhöjt men under varningsnivå)");
            evidence.Add($"⚠ DPC-tid stjäl CPU-tid från {processName}");

            return new FreezeClassification(
                processName,
                "Drivrutinslatens (förhöjd DPC)",
                $"DPC-tid ligger på {sample.DpcTimePercent:F1}% — förhöjt men under varningsnivå. "
                + $"DPC kan stjäla CPU-tid från {processName}.",
                evidence);
        }

        // G. Processorkö men CPU < 30% → trängsel trots ledig kapacitet
        int cores = Environment.ProcessorCount;
        if (sample.ProcessorQueueLength > cores && procCpuPct < 5)
        {
            evidence.Add($"Processorkö: {sample.ProcessorQueueLength} (kärnor: {cores})");
            evidence.Add($"⚠ Kö > antal kärnor → processer väntar på schemaläggning");

            return new FreezeClassification(
                processName,
                "Scheduler-trängsel",
                $"Processorkön är {sample.ProcessorQueueLength} med {cores} kärnor och {sample.CpuPercent:F0}% total CPU. "
                + $"Trolig scheduler-trängsel som påverkar {processName}.",
                evidence);
        }

        // H. Ren intern blockering — inget pekar på system-nivå-problem
        // Process har ~0% CPU, normalt I/O, normala handles → klassisk UI-tråd-blockering
        string subCause;
        string subDescription;

        if (procCpuPct >= 10 && procCpuPct < 30)
        {
            // Processen gör arbete men UI-tråden svarar inte → bakgrundsarbete på UI-tråd
            evidence.Add($"⚠ {processName} använder {procCpuPct:F0}% CPU men svarar ändå inte → UI-tråd blockerad av beräkning");
            subCause = "UI-tråd blockerad av beräkning";
            subDescription = $"{processName} använder {procCpuPct:F0}% CPU men UI:t svarar inte. "
                + "Trolig tung beräkning på UI-tråden.";
        }
        else
        {
            // Processen gör nästan ingenting men svarar ändå inte → ren intern blockering
            evidence.Add($"⚠ Systemet ser friskt ut men {processName} svarar inte → intern blockering");
            subCause = "Intern blockering (deadlock/väntan)";
            subDescription = $"{processName} använder knappt CPU ({procCpuPct:F0}%) och systemet ser friskt ut "
                + $"(CPU {sample.CpuPercent:F0}%, disklatens {Math.Max(sample.AvgDiskSecRead, sample.AvgDiskSecWrite) * 1000:F0}ms). "
                + "Trolig intern blockering (deadlock/väntan).";
        }

        return new FreezeClassification(processName, subCause, subDescription, evidence);
    }

    /// <summary>
    /// Okänd orsak — inget tydligt mönster matchar.
    /// </summary>
    private static FreezeClassification ClassifyUnknown(string processName, MonitorSample sample)
    {
        var evidence = new List<string>
        {
            $"CPU: {sample.CpuPercent:F0}%",
            $"Disk: kö {sample.DiskQueueLength:F1}, latens R {sample.AvgDiskSecRead * 1000:F1}ms W {sample.AvgDiskSecWrite * 1000:F1}ms",
            $"DPC: {sample.DpcTimePercent:F1}%",
            $"Processorkö: {sample.ProcessorQueueLength}",
            $"Minnestryck: {sample.MemoryPressureIndex}/100"
        };

        return new FreezeClassification(
            processName,
            "Okänd",
            $"Kunde inte fastställa en tydlig orsak till att {processName} hänger. "
            + $"System: CPU {sample.CpuPercent:F0}%, disk-kö {sample.DiskQueueLength:F1}, "
            + $"DPC {sample.DpcTimePercent:F1}%, minnestryck {sample.MemoryPressureIndex}/100.",
            evidence);
    }

    private static MonitorProcessInfo? FindProcess(string name, List<MonitorProcessInfo> processes)
    {
        return processes.FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
