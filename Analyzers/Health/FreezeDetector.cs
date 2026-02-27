namespace ComputerPerformanceReview.Analyzers.Health;

/// <summary>
/// Classifies WHY a process hangs based on system state and per-process data.
/// Correlates CPU, disk, memory, GPU, network, threads, handles, and I/O
/// for a detailed root cause analysis.
/// </summary>
public static class FreezeDetector
{
    public static FreezeClassification? Classify(
        string processName,
        MonitorSample sample)
    {
        double diskLatencyMs = Math.Max(sample.AvgDiskSecRead, sample.AvgDiskSecWrite) * 1000;

        // ── 1. Storage errors — disk has hardware problems ──
        if (sample.StorageErrorsLast15Min > 0)
        {
            var evidence = new List<string>
            {
                $"System log shows {sample.StorageErrorsLast15Min} storage errors in last 15 min",
                $"Disk latency: R {sample.AvgDiskSecRead * 1000:F1}ms / W {sample.AvgDiskSecWrite * 1000:F1}ms",
                $"Disk queue: {sample.DiskQueueLength:F1}"
            };

            return new FreezeClassification(
                processName,
                "Storage error",
                $"System log shows {sample.StorageErrorsLast15Min} storage errors in last 15 min. "
                + "ACTIONS: Check SMART status and update storage drivers.",
                evidence);
        }

        // ── 2. Disk-bound — high disk latency or long queue ──
        if (diskLatencyMs > 100 || sample.DiskQueueLength > 4)
        {
            var evidence = new List<string>
            {
                $"Disk latency: R {sample.AvgDiskSecRead * 1000:F1}ms / W {sample.AvgDiskSecWrite * 1000:F1}ms",
                $"Disk queue: {sample.DiskQueueLength:F1}"
            };

            // Check if the hanging process itself has high I/O
            var procIo = sample.TopIoProcesses.FirstOrDefault(p =>
                p.Name.Equals(processName, StringComparison.OrdinalIgnoreCase));
            if (procIo is not null)
                evidence.Add($"{processName} I/O: {ConsoleHelper.FormatBytes((long)procIo.TotalBytes)} total");

            var worstDisk = sample.DiskInstances
                .OrderByDescending(DiskHealthAnalyzer.DiskSeverityScore)
                .FirstOrDefault();
            if (worstDisk is not null)
            {
                double totalIops = worstDisk.ReadIops + worstDisk.WriteIops;
                evidence.Add($"Worst disk: {worstDisk.Name} (R {worstDisk.ReadLatencyMs:F0}ms, W {worstDisk.WriteLatencyMs:F0}ms, " +
                    $"IOPS {totalIops:F0}, idle {worstDisk.IdlePercent:F0}%)");
            }

            return new FreezeClassification(
                processName,
                "Disk-bound",
                $"Disk response time is {diskLatencyMs:F0}ms (queue: {sample.DiskQueueLength:F1}). "
                + "Process is likely waiting on disk I/O.",
                evidence);
        }

        // ── 3. GPU saturation — graphics processing blocking ──
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
                evidence.Add($"TDR events: {sample.TdrEventsLast15Min} in last 15 min");

            return new FreezeClassification(
                processName,
                "GPU saturation",
                $"GPU is at {sample.GpuUtilizationPercent:F0}%. UI rendering may be blocked. "
                + (sample.TdrEventsLast15Min > 0
                    ? "ACTION: Update or roll back the GPU driver. "
                    : "ACTION: Reduce graphics load and update the GPU driver. "),
                evidence);
        }

        // ── 4. Driver problem — DPC blocking the processor ──
        if (sample.DpcTimePercent > 15)
        {
            var evidence = new List<string>
            {
                $"DPC time: {sample.DpcTimePercent:F1}%",
                $"IRQ time: {sample.InterruptTimePercent:F1}%"
            };

            return new FreezeClassification(
                processName,
                "Driver problem",
                $"DPC time: {sample.DpcTimePercent:F1}%. A driver is blocking the processor. "
                + "ACTIONS: Update GPU, network, and USB drivers.",
                evidence);
        }

        // ── 5. Network latency — DNS lookups taking too long ──
        if (sample.DnsLatencyMs > 300)
        {
            var evidence = new List<string>
            {
                $"DNS latency: {sample.DnsLatencyMs:F0}ms",
                $"Network load: {sample.NetworkMbps:F1} Mbps"
            };

            return new FreezeClassification(
                processName,
                "Network latency",
                $"DNS latency is {sample.DnsLatencyMs:F0}ms. Process may be waiting for network response. "
                + "ACTION: Check the network or change DNS server.",
                evidence);
        }

        // ── 6. CPU saturation — processor overloaded ──
        if (sample.CpuPercent > 80)
        {
            var evidence = new List<string>
            {
                $"CPU: {sample.CpuPercent:F0}%",
                $"Processor queue: {sample.ProcessorQueueLength}"
            };
            if (sample.TopCpuProcesses.Count > 0)
            {
                evidence.Add("Top CPU: " + string.Join(", ",
                    sample.TopCpuProcesses.Take(3).Select(p => $"{p.Name} ({p.CpuPercent:F0}%)")));
            }
            if (sample.CpuMaxClockMHz > 0)
            {
                double ratio = sample.CpuClockMHz / sample.CpuMaxClockMHz;
                evidence.Add($"Clock speed: {sample.CpuClockMHz:F0}/{sample.CpuMaxClockMHz:F0} MHz ({ratio * 100:F0}%)");
                if (ratio < 0.6)
                    evidence.Add("⚠ CPU is throttling — check temperature!");
            }

            return new FreezeClassification(
                processName,
                "CPU saturation",
                $"CPU is at {sample.CpuPercent:F0}%. Process is getting insufficient CPU time.",
                evidence);
        }

        // ── 7. Memory pressure — system is paging heavily ──
        if (sample.MemoryPressureIndex > 70)
        {
            var evidence = new List<string>
            {
                $"Memory pressure index: {sample.MemoryPressureIndex}/100",
                $"PagesInput: {sample.PagesInputPerSec:F0}/s",
                $"RAM: {sample.MemoryUsedPercent:F0}%"
            };
            if (sample.CommitLimit > 0)
                evidence.Add($"Commit: {ConsoleHelper.FormatBytes(sample.CommittedBytes)}/{ConsoleHelper.FormatBytes(sample.CommitLimit)}");

            return new FreezeClassification(
                processName,
                "Memory pressure",
                $"Memory pressure index: {sample.MemoryPressureIndex}/100. System is paging heavily. "
                + "ACTION: Close programs/tabs or increase RAM.",
                evidence);
        }

        // ── 8. Detailed lock contention analysis ──
        // CPU and disk are normal but process is hanging. Examine per-process data
        // for a more precise diagnosis.
        if (sample.CpuPercent < 30 && sample.DiskQueueLength < 2 && diskLatencyMs < 50)
        {
            return ClassifyLockContention(processName, sample);
        }

        // ── 9. Unknown cause ──
        return ClassifyUnknown(processName, sample);
    }

    /// <summary>
    /// Detailed analysis of the "Lock contention" scenario — CPU and disk are normal
    /// but process is not responding. Uses per-process data for sub-classification.
    /// </summary>
    private static FreezeClassification ClassifyLockContention(string processName, MonitorSample sample)
    {
        var evidence = new List<string>
        {
            $"CPU: {sample.CpuPercent:F0}% (normal)",
            $"Disk: queue {sample.DiskQueueLength:F1}, latency R {sample.AvgDiskSecRead * 1000:F1}ms W {sample.AvgDiskSecWrite * 1000:F1}ms (normal)"
        };

        // Find the process's own data
        var procCpu = FindProcess(processName, sample.TopCpuProcesses);
        var procMem = FindProcess(processName, sample.TopMemoryProcesses);
        var procIo = sample.TopIoProcesses.FirstOrDefault(p =>
            p.Name.Equals(processName, StringComparison.OrdinalIgnoreCase));
        var procFaults = sample.TopFaultProcesses.FirstOrDefault(p =>
            p.Name.Equals(processName, StringComparison.OrdinalIgnoreCase));

        // Collect process-specific evidence
        var proc = procCpu ?? procMem;
        int threadCount = proc?.ThreadCount ?? 0;
        int handleCount = proc?.HandleCount ?? 0;
        double procCpuPct = procCpu?.CpuPercent ?? 0;
        long procMemBytes = procMem?.MemoryBytes ?? 0;

        if (proc is not null)
        {
            evidence.Add($"{processName}: {procCpuPct:F0}% CPU, {ConsoleHelper.FormatBytes(procMemBytes)} RAM, {threadCount} threads, {handleCount} handles");
        }

        if (procIo is not null)
            evidence.Add($"{processName} I/O: R {ConsoleHelper.FormatBytes((long)procIo.ReadBytes)} / W {ConsoleHelper.FormatBytes((long)procIo.WriteBytes)}");

        if (procFaults is not null)
            evidence.Add($"{processName} page faults: {procFaults.PageFaultsPerSec:F0}/s");

        // ── Sub-classification based on signals ──

        // A. Thread pool starvation: many threads but low CPU → all threads waiting
        if (threadCount > 100 && procCpuPct < 5)
        {
            evidence.Add($"⚠ {threadCount} threads but only {procCpuPct:F0}% CPU → all threads blocked");

            return new FreezeClassification(
                processName,
                "Thread pool starvation",
                $"{processName} has {threadCount} threads but almost no CPU usage ({procCpuPct:F0}%). "
                + "Likely thread pool starvation. ACTION: Restart the program.",
                evidence);
        }

        // B. Synchronous network wait: program does I/O but disk is fine → likely network
        // DNS may be under 300ms threshold but process is still waiting
        if (procIo is not null && procIo.TotalBytes > 0 && procCpuPct < 5)
        {
            bool networkActive = sample.NetworkMbps > 0.5;
            bool dnsElevated = sample.DnsLatencyMs > 50; // Not critical but elevated

            if (networkActive || dnsElevated)
            {
                evidence.Add($"Network activity: {sample.NetworkMbps:F1} Mbps, DNS: {sample.DnsLatencyMs:F0}ms");
                evidence.Add($"⚠ Process has active I/O but low CPU → likely network wait");

                return new FreezeClassification(
                    processName,
                    "Network wait (blocked UI thread)",
                    $"{processName} is doing I/O with low CPU ({procCpuPct:F0}%) and network is active. "
                    + "Likely synchronous network wait on the UI thread.",
                    evidence);
            }
        }

        // C. Handle starvation / resource contention: very many handles → process waiting on resource
        if (handleCount > 5000 && procCpuPct < 5)
        {
            evidence.Add($"⚠ {handleCount} handles with low CPU → likely resource starvation");

            return new FreezeClassification(
                processName,
                "Resource starvation (handles)",
                $"{processName} has {handleCount} handles but barely using CPU ({procCpuPct:F0}%). "
                + "Likely resource starvation. ACTION: Restart the program.",
                evidence);
        }

        // D. High context switches with low CPU → processes competing for locks
        if (sample.ContextSwitchesPerSec > 30000 && procCpuPct < 10)
        {
            evidence.Add($"Context switches: {sample.ContextSwitchesPerSec:F0}/s (high)");
            evidence.Add($"⚠ High context switches with low CPU → lock contention in system");

            return new FreezeClassification(
                processName,
                "Lock contention",
                $"System has {sample.ContextSwitchesPerSec:F0} context switches/s but CPU is {sample.CpuPercent:F0}%. "
                + $"Likely lock contention affecting {processName}.",
                evidence);
        }

        // E. Page faults: process waiting on memory pages
        if (procFaults is not null && procFaults.PageFaultsPerSec > 100 && procCpuPct < 5)
        {
            evidence.Add($"⚠ {procFaults.PageFaultsPerSec:F0} page faults/s → process waiting on memory pages");

            return new FreezeClassification(
                processName,
                "Paging wait",
                $"{processName} generates {procFaults.PageFaultsPerSec:F0} page faults/s with low CPU ({procCpuPct:F0}%). "
                + "Likely paging wait.",
                evidence);
        }

        // F. Elevated DPC (below threshold but still noticeable) + low process CPU
        if (sample.DpcTimePercent > 5 && procCpuPct < 5)
        {
            evidence.Add($"DPC: {sample.DpcTimePercent:F1}% (elevated but below warning level)");
            evidence.Add($"⚠ DPC time stealing CPU time from {processName}");

            return new FreezeClassification(
                processName,
                "Driver latency (elevated DPC)",
                $"DPC time is at {sample.DpcTimePercent:F1}% — elevated but below warning level. "
                + $"DPC may be stealing CPU time from {processName}.",
                evidence);
        }

        // G. Processor queue but CPU < 30% → congestion despite spare capacity
        int cores = Environment.ProcessorCount;
        if (sample.ProcessorQueueLength > cores && procCpuPct < 5)
        {
            evidence.Add($"Processor queue: {sample.ProcessorQueueLength} (cores: {cores})");
            evidence.Add($"⚠ Queue > core count → processes waiting for scheduling");

            return new FreezeClassification(
                processName,
                "Scheduler congestion",
                $"Processor queue is {sample.ProcessorQueueLength} with {cores} cores and {sample.CpuPercent:F0}% total CPU. "
                + $"Likely scheduler congestion affecting {processName}.",
                evidence);
        }

        // H. Pure internal blocking — nothing points to system-level problems
        // Process has ~0% CPU, normal I/O, normal handles → classic UI thread blocking
        string subCause;
        string subDescription;

        if (procCpuPct >= 10 && procCpuPct < 30)
        {
            // Process is doing work but UI thread is not responding → computation on UI thread
            evidence.Add($"⚠ {processName} using {procCpuPct:F0}% CPU but still not responding → UI thread blocked by computation");
            subCause = "UI thread blocked by computation";
            subDescription = $"{processName} is using {procCpuPct:F0}% CPU but the UI is not responding. "
                + "Likely heavy computation on the UI thread.";
        }
        else
        {
            // Process doing almost nothing but still not responding → pure internal blocking
            evidence.Add($"⚠ System looks healthy but {processName} is not responding → internal blocking");
            subCause = "Internal blocking (deadlock/wait)";
            subDescription = $"{processName} is barely using CPU ({procCpuPct:F0}%) and system looks healthy "
                + $"(CPU {sample.CpuPercent:F0}%, disk latency {Math.Max(sample.AvgDiskSecRead, sample.AvgDiskSecWrite) * 1000:F0}ms). "
                + "Likely internal blocking (deadlock/wait).";
        }

        return new FreezeClassification(processName, subCause, subDescription, evidence);
    }

    /// <summary>
    /// Unknown cause — no clear pattern matches.
    /// </summary>
    private static FreezeClassification ClassifyUnknown(string processName, MonitorSample sample)
    {
        var evidence = new List<string>
        {
            $"CPU: {sample.CpuPercent:F0}%",
            $"Disk: queue {sample.DiskQueueLength:F1}, latency R {sample.AvgDiskSecRead * 1000:F1}ms W {sample.AvgDiskSecWrite * 1000:F1}ms",
            $"DPC: {sample.DpcTimePercent:F1}%",
            $"Processor queue: {sample.ProcessorQueueLength}",
            $"Memory pressure: {sample.MemoryPressureIndex}/100"
        };

        return new FreezeClassification(
            processName,
            "Unknown",
            $"Could not determine a clear cause for {processName} hanging. "
            + $"System: CPU {sample.CpuPercent:F0}%, disk queue {sample.DiskQueueLength:F1}, "
            + $"DPC {sample.DpcTimePercent:F1}%, memory pressure {sample.MemoryPressureIndex}/100.",
            evidence);
    }

    private static MonitorProcessInfo? FindProcess(string name, List<MonitorProcessInfo> processes)
    {
        return processes.FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
