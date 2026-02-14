namespace ComputerPerformanceReview.Helpers;

public static class MonitorDisplay
{
    private const int BarWidth = 20;
    private const int MaxVisibleEvents = 8;

    public static void DrawDashboard(
        MonitorSample sample,
        List<MonitorEvent> events,
        DateTime startTime,
        int durationMinutes)
    {
        Console.CursorVisible = false;
        Console.SetCursorPosition(0, 0);
        Console.Clear();

        var elapsed = DateTime.Now - startTime;
        var elapsedMin = (int)elapsed.TotalMinutes;

        // Header
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
        var title = $"ÖVERVAKNINGSLÄGE ({DateTime.Now:HH:mm} - {elapsedMin}/{durationMinutes} min)";
        var padded = title.PadLeft((70 + title.Length) / 2).PadRight(70);
        Console.WriteLine("║" + padded + "║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();

        // CPU & RAM bars
        DrawBarPair("CPU", sample.CpuPercent, "RAM", sample.MemoryUsedPercent);

        // Disk & Network bars
        string diskStr = $"{sample.DiskQueueLength:F1} kö";
        var diskPercent = Math.Min(sample.DiskQueueLength / 10.0 * 100, 100);
        Console.Write("  Disk: ");
        DrawBar(diskPercent, ConsoleColor.Magenta);
        Console.Write($" {diskStr,-8}");

        string netStr = $"{sample.NetworkMbps:F1} Mbps";
        var netPercent = Math.Min(sample.NetworkMbps / 1000.0 * 100, 100);
        Console.Write("  Net:  ");
        DrawBar(netPercent, GetNetColor(sample.NetworkMbps));
        Console.Write($" {netStr}");
        Console.WriteLine();
        Console.WriteLine();

        // ── SYSTEMHÄLSA ──────────────────────────────────
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  ── SYSTEMHÄLSA ────────────────────────────────────────────────────");
        Console.ResetColor();

        // Minnestryck bar
        DrawCompositeScoreBar("Minnestryck", sample.MemoryPressureIndex, GetPressureLabel(sample.MemoryPressureIndex));

        // Systemlatens bar
        DrawCompositeScoreBar("Systemlatens", sample.SystemLatencyScore, GetLatencyLabel(sample.SystemLatencyScore));

        Console.WriteLine();

        // Extended stats row 1: Handles, PagesIn, Commit, PoolNP
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  Handles: ");
        Console.ForegroundColor = sample.TotalSystemHandles > 100000 ? ConsoleColor.Yellow : ConsoleColor.White;
        Console.Write($"{sample.TotalSystemHandles,6}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("   PagesIn: ");
        Console.ForegroundColor = sample.PagesInputPerSec > 300 ? ConsoleColor.Red
            : sample.PagesInputPerSec > 50 ? ConsoleColor.Yellow : ConsoleColor.White;
        Console.Write($"{sample.PagesInputPerSec,5:F0}/s");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("   Commit: ");
        Console.ForegroundColor = ConsoleColor.White;
        if (sample.CommitLimit > 0)
            Console.Write($"{ConsoleHelper.FormatBytes(sample.CommittedBytes)}/{ConsoleHelper.FormatBytes(sample.CommitLimit)}");
        else
            Console.Write($"{ConsoleHelper.FormatBytes(sample.CommittedBytes)}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  PoolNP: ");
        Console.ForegroundColor = sample.PoolNonpagedBytes > 200L * 1024 * 1024 ? ConsoleColor.Yellow : ConsoleColor.White;
        Console.Write($"{ConsoleHelper.FormatBytes(sample.PoolNonpagedBytes)}");
        Console.ResetColor();
        Console.WriteLine();

        // Extended stats row 2: DPC, IRQ, CtxSw, ProcQueue, DiskLat
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  DPC: ");
        Console.ForegroundColor = sample.DpcTimePercent > 15 ? ConsoleColor.Red
            : sample.DpcTimePercent > 5 ? ConsoleColor.Yellow : ConsoleColor.White;
        Console.Write($"{sample.DpcTimePercent,4:F1}%");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  IRQ: ");
        Console.ForegroundColor = sample.InterruptTimePercent > 10 ? ConsoleColor.Yellow : ConsoleColor.White;
        Console.Write($"{sample.InterruptTimePercent,4:F1}%");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  CtxSw: ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"{sample.ContextSwitchesPerSec,7:F0}/s");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  ProcQueue: ");
        int cores = Environment.ProcessorCount;
        Console.ForegroundColor = sample.ProcessorQueueLength > 2 * cores ? ConsoleColor.Yellow : ConsoleColor.White;
        Console.Write($"{sample.ProcessorQueueLength}");
        Console.ForegroundColor = ConsoleColor.DarkGray;

        double diskLatR = sample.AvgDiskSecRead * 1000;
        double diskLatW = sample.AvgDiskSecWrite * 1000;
        Console.Write("  DiskLat: ");
        Console.ForegroundColor = Math.Max(diskLatR, diskLatW) > 50 ? ConsoleColor.Yellow : ConsoleColor.White;
        Console.Write($"R {diskLatR:F1}ms  W {diskLatW:F1}ms");
        Console.ResetColor();
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  CPUClk: ");
        double clockRatio = sample.CpuMaxClockMHz > 0 ? sample.CpuClockMHz / sample.CpuMaxClockMHz : 0;
        Console.ForegroundColor = clockRatio > 0 && clockRatio < 0.6 && sample.CpuPercent > 40 ? ConsoleColor.Yellow : ConsoleColor.White;
        if (sample.CpuMaxClockMHz > 0)
            Console.Write($"{sample.CpuClockMHz:F0}/{sample.CpuMaxClockMHz:F0} MHz");
        else
            Console.Write("n/a");

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  GPU: ");
        Console.ForegroundColor = sample.GpuUtilizationPercent > 95 ? ConsoleColor.Yellow : ConsoleColor.White;
        Console.Write($"{sample.GpuUtilizationPercent:F0}%");

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  DNS: ");
        Console.ForegroundColor = sample.DnsLatencyMs > 300 ? ConsoleColor.Yellow : ConsoleColor.White;
        Console.Write($"{sample.DnsLatencyMs:F0} ms");

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  StorageErr(15m): ");
        Console.ForegroundColor = sample.StorageErrorsLast15Min > 0 ? ConsoleColor.Yellow : ConsoleColor.White;
        Console.Write($"{sample.StorageErrorsLast15Min}");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine();

        // Top CPU processes
        if (sample.TopCpuProcesses.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("  Topp CPU: ");
            Console.ResetColor();
            var topStr = string.Join(", ",
                sample.TopCpuProcesses.Take(5)
                    .Select(p => $"{p.Name} ({p.CpuPercent:F0}%)"));
            Console.WriteLine(topStr);
        }

        // Top memory processes
        if (sample.TopMemoryProcesses.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("  Topp RAM: ");
            Console.ResetColor();
            var topStr = string.Join(", ",
                sample.TopMemoryProcesses.Take(5)
                    .Select(p => $"{p.Name} ({ConsoleHelper.FormatBytes(p.MemoryBytes)})"));
            Console.WriteLine(topStr);
        }

        if (sample.TopIoProcesses.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("  Topp I/O: ");
            Console.ResetColor();
            var topStr = string.Join(", ",
                sample.TopIoProcesses.Take(3)
                    .Select(p => $"{p.Name} ({ConsoleHelper.FormatBytes((long)p.TotalBytes)})"));
            Console.WriteLine(topStr);
        }


        if (sample.TopFaultProcesses.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("  Topp Faults: ");
            Console.ResetColor();
            var topStr = string.Join(", ",
                sample.TopFaultProcesses.Take(3)
                    .Select(p => $"{p.Name} ({p.PageFaultsPerSec:F0}/s)"));
            Console.WriteLine(topStr);
        }

        if (sample.DiskInstances.Count > 0)
        {
            var worst = sample.DiskInstances.OrderByDescending(d => Math.Max(d.ReadLatencyMs, d.WriteLatencyMs)).First();
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("  Värst disk: ");
            Console.ResetColor();
            Console.WriteLine($"{worst.Name} (R {worst.ReadLatencyMs:F1}ms, W {worst.WriteLatencyMs:F1}ms, kö {worst.QueueLength:F1}, busy {worst.BusyPercent:F0}%)");
        }
        // GDI warnings
        var highGdi = sample.TopGdiProcesses
            .Where(p => p.GdiObjects > 2000)
            .OrderByDescending(p => p.GdiObjects)
            .Take(3)
            .ToList();
        if (highGdi.Count > 0)
        {
            Console.Write("  ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("GDI:    ");
            Console.ResetColor();
            var gdiStr = string.Join(", ",
                highGdi.Select(p =>
                {
                    var tag = p.GdiObjects > 8000 ? "KRITISKT" : p.GdiObjects > 5000 ? "varning" : "";
                    var suffix = tag.Length > 0 ? $" ({tag})" : "";
                    return $"{p.Name} {p.GdiObjects} obj{suffix}";
                }));
            Console.WriteLine(gdiStr);
        }

        // Sysinternals Handle.exe data
        if (sample.SysinternalsHandleData is { Count: > 0 })
        {
            Console.Write("  ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Handle: ");
            Console.ResetColor();
            var handleStr = string.Join(", ", sample.SysinternalsHandleData.Take(2).Select(h =>
            {
                var topType = h.HandleTypeBreakdown.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
                return $"{h.ProcessName} ({h.TotalHandles} total, topp: {topType.Key} {topType.Value})";
            }));
            Console.WriteLine(handleStr);
        }

        // Sysinternals PoolMon data
        if (sample.SysinternalsPoolData is { Count: > 0 })
        {
            Console.Write("  ");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("Pool:   ");
            Console.ResetColor();
            var poolStr = string.Join(", ", sample.SysinternalsPoolData.Take(3).Select(p =>
                $"{p.Tag} ({ConsoleHelper.FormatBytes(p.Bytes)})"));
            Console.WriteLine(poolStr);
        }

        // Hanging processes with live duration + freeze classification
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("  Hängande: ");
        if (sample.HangingProcesses.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            var hangStr = string.Join(", ", sample.HangingProcesses.Select(h =>
            {
                if (h.HangSeconds > 0)
                    return $"{h.Name} ({FormatDuration(h.HangSeconds)})";
                return h.Name;
            }));
            Console.WriteLine(hangStr);

            // Freeze classification
            if (sample.FreezeInfo is not null)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("            → Trolig orsak: ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(sample.FreezeInfo.LikelyCause);

                // Evidence list — visar bevis för klassificeringen
                if (sample.FreezeInfo.Evidence is { Count: > 0 })
                {
                    foreach (var ev in sample.FreezeInfo.Evidence)
                    {
                        Console.ForegroundColor = ev.StartsWith("⚠")
                            ? ConsoleColor.Yellow
                            : ConsoleColor.DarkGray;
                        Console.WriteLine($"              {ev}");
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                WriteWrapped($"              {sample.FreezeInfo.Description}", 72);
                Console.ResetColor();
            }

            // Deep freeze analysis (for freezes > 5 seconds)
            if (sample.DeepFreezeReport is not null)
            {
                var report = sample.DeepFreezeReport;
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("            ┌─ DJUPANALYS (Deep Freeze Investigation) ─────────────┐");
                Console.ResetColor();
                
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("              Process: ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"{report.ProcessName} (PID {report.ProcessId})");
                
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("              Frysduration: ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"{report.FreezeDuration.TotalSeconds:F1}s");
                
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("              Trådar: ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"{report.TotalThreads} totalt, {report.RunningThreads} körande");
                
                if (report.WaitReasonCounts.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("              Vänteorsaker:");
                    foreach (var kvp in report.WaitReasonCounts.OrderByDescending(x => x.Value))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write("                ");
                        Console.ForegroundColor = kvp.Key == report.DominantWaitReason 
                            ? ConsoleColor.Yellow 
                            : ConsoleColor.White;
                        Console.WriteLine($"{kvp.Key}: {kvp.Value} trådar");
                    }
                }
                
                if (report.DominantWaitReason != null)
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write("              Dominerande: ");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(report.DominantWaitReason);
                }
                
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("              Trolig grundorsak: ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(report.LikelyRootCause);
                
                // MiniDump info
                if (report.MiniDumpPath != null)
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write("              MiniDump: ");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(Path.GetFileName(report.MiniDumpPath));
                    
                    if (report.MiniDumpAnalysis != null)
                    {
                        var analysis = report.MiniDumpAnalysis;
                        if (analysis.FaultingModule != null)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine($"                - {analysis.FaultingModule}");
                        }
                        if (analysis.ExceptionCode != null)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine($"                - Exception: {analysis.ExceptionCode}");
                        }
                    }
                }
                
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("            └──────────────────────────────────────────────────────┘");
                Console.ResetColor();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Inga");
        }
        Console.ResetColor();

        Console.WriteLine();

        // Events with tips
        if (events.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  HÄNDELSER ({events.Count}):");
            Console.ResetColor();

            var visibleEvents = events
                .OrderByDescending(e => e.Timestamp)
                .Take(MaxVisibleEvents)
                .Reverse()
                .ToList();

            foreach (var ev in visibleEvents)
            {
                var color = ev.Severity == "Critical" ? ConsoleColor.Red : ConsoleColor.Yellow;
                var label = ev.Severity == "Critical" ? "KRITISKT" : "VARNING";

                Console.Write($"  {ev.Timestamp:HH:mm:ss}  ");
                Console.ForegroundColor = color;
                Console.Write($"[{label}]");
                Console.ResetColor();
                Console.WriteLine($"  {ev.Description}");

                // Show tip for the most recent event
                if (ev == visibleEvents[^1] && !string.IsNullOrEmpty(ev.Tip))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    WriteWrapped($"           → TIPS: {ev.Tip}", 72);
                    Console.ResetColor();
                }
            }

            if (events.Count > MaxVisibleEvents)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  ... och {events.Count - MaxVisibleEvents} tidigare händelser");
                Console.ResetColor();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Inga händelser registrerade ännu...");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Tryck Q för att avsluta övervakningen");
        Console.ResetColor();
    }

    public static void DrawSummary(MonitorReport report)
    {
        Console.Clear();
        Console.CursorVisible = true;

        ConsoleHelper.WriteHeader("ÖVERVAKNINGSRAPPORT");

        var duration = report.EndTime - report.StartTime;
        ConsoleHelper.WriteInfo($"  Övervakningstid: {duration.TotalMinutes:F0} minuter ({report.TotalSamples} samples)");
        ConsoleHelper.WriteInfo($"  Period: {report.StartTime:yyyy-MM-dd HH:mm} → {report.EndTime:HH:mm}");
        Console.WriteLine();

        // Stats table
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  {0,-25} {1,-15} {2,-15}", "Mätvärde", "Genomsnitt", "Peak");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  " + new string('─', 55));
        Console.ResetColor();

        WriteStatRow("CPU", report.AvgCpu, report.PeakCpu, "%", 80, 95);
        WriteStatRow("RAM", report.AvgMemory, report.PeakMemory, "%", 75, 90);
        WriteStatRow("Disk-kö", report.AvgDiskQueue, report.PeakDiskQueue, "", 2, 5);
        WriteStatRow("Nätverk", report.AvgNetworkMbps, report.PeakNetworkMbps, " Mbps", 100, 500);
        WriteStatRow("Minnestryck-index", (double)report.AvgMemoryPressureIndex, (double)report.PeakMemoryPressureIndex, "/100", 60, 80);
        WriteStatRow("Systemlatens-score", (double)report.AvgSystemLatencyScore, (double)report.PeakSystemLatencyScore, "/100", 50, 75);

        Console.WriteLine();

        // Extended peak stats
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  Detaljerade toppar:");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  " + new string('─', 55));
        Console.ResetColor();

        WritePeakStat("Handles (peak)", $"{report.PeakSystemHandles,6}", report.PeakSystemHandles > 100000);
        WritePeakStat("Committed (peak)", ConsoleHelper.FormatBytes(report.PeakCommittedBytes), false);
        WritePeakStat("PagesInput (peak)", $"{report.PeakPagesInputPerSec:F0}/s", report.PeakPagesInputPerSec > 300);
        WritePeakStat("DPC-tid (peak)", $"{report.PeakDpcTimePercent:F1}%", report.PeakDpcTimePercent > 15);
        WritePeakStat("IRQ-tid (peak)", $"{report.PeakInterruptTimePercent:F1}%", report.PeakInterruptTimePercent > 10);
        WritePeakStat("Ctx Switches (peak)", $"{report.PeakContextSwitchesPerSec:F0}/s", false);
        WritePeakStat("Proc Queue (peak)", $"{report.PeakProcessorQueueLength}", report.PeakProcessorQueueLength > 2 * Environment.ProcessorCount);
        WritePeakStat("Disk Read Lat (peak)", $"{report.PeakAvgDiskSecRead * 1000:F1} ms", report.PeakAvgDiskSecRead * 1000 > 50);
        WritePeakStat("Disk Write Lat (peak)", $"{report.PeakAvgDiskSecWrite * 1000:F1} ms", report.PeakAvgDiskSecWrite * 1000 > 50);
        WritePeakStat("Pool Nonpaged (peak)", ConsoleHelper.FormatBytes(report.PeakPoolNonpagedBytes), report.PeakPoolNonpagedBytes > 200L * 1024 * 1024);
        WritePeakStat("Pool Paged (peak)", ConsoleHelper.FormatBytes(report.PeakPoolPagedBytes), false);
        WritePeakStat("GPU (peak)", $"{report.PeakGpuUtilizationPercent:F0}%", report.PeakGpuUtilizationPercent > 95);
        WritePeakStat("DNS Latens (peak)", $"{report.PeakDnsLatencyMs:F0} ms", report.PeakDnsLatencyMs > 300);
        WritePeakStat("Storage errors (15m peak)", $"{report.PeakStorageErrorsLast15Min}", report.PeakStorageErrorsLast15Min > 0);

        if (report.FreezeCount > 0)
        {
            Console.Write($"  {"Freeze-klassificeringar",-25} ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{report.FreezeCount}");
            Console.ResetColor();
        }

        Console.WriteLine();

        // Events summary with tips
        if (report.Events.Count > 0)
        {
            var criticalCount = report.Events.Count(e => e.Severity == "Critical");
            var warningCount = report.Events.Count(e => e.Severity == "Warning");

            var byType = report.Events.GroupBy(e => e.EventType).OrderByDescending(g => g.Count());

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  HÄNDELSER ({report.Events.Count} totalt: {criticalCount} kritiska, {warningCount} varningar):");
            Console.ForegroundColor = ConsoleColor.DarkGray;

            Console.Write("  Fördelning: ");
            Console.ResetColor();
            Console.WriteLine(string.Join(", ", byType.Select(g =>
            {
                var label = TypeLabels.GetValueOrDefault(g.Key, g.Key);
                return $"{label}: {g.Count()}";
            })));
            Console.WriteLine();

            foreach (var ev in report.Events)
            {
                var color = ev.Severity == "Critical" ? ConsoleColor.Red : ConsoleColor.Yellow;
                var label = ev.Severity == "Critical" ? "KRITISKT" : "VARNING";

                Console.Write($"  {ev.Timestamp:HH:mm:ss}  ");
                Console.ForegroundColor = color;
                Console.Write($"[{label}]");
                Console.ResetColor();
                Console.WriteLine($"  {ev.Description}");
            }

            // REKOMMENDATIONER
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  ╔══════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("  ║                    REKOMMENDATIONER                             ║");
            Console.WriteLine("  ╚══════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();

            var tipsByType = report.Events
                .Where(e => !string.IsNullOrEmpty(e.Tip))
                .GroupBy(e => e.EventType)
                .Select(g => (
                    Type: g.Key,
                    Label: TypeLabels.GetValueOrDefault(g.Key, g.Key),
                    Count: g.Count(),
                    Tip: g.First().Tip,
                    IsCritical: g.Any(e => e.Severity == "Critical")
                ))
                .OrderByDescending(t => t.IsCritical)
                .ThenByDescending(t => t.Count)
                .ToList();

            int tipNum = 1;
            foreach (var tip in tipsByType)
            {
                var headerColor = tip.IsCritical ? ConsoleColor.Red : ConsoleColor.Yellow;
                Console.ForegroundColor = headerColor;
                Console.Write($"  {tipNum}. {tip.Label}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" ({tip.Count} händelser)");

                Console.ForegroundColor = ConsoleColor.Cyan;
                WriteWrapped($"     {tip.Tip}", 72);
                Console.ResetColor();
                Console.WriteLine();

                tipNum++;
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Inga problem registrerades under övervakningen!");
            Console.ResetColor();
        }
    }

    // ── Event type labels ────────────────────────────────

    private static readonly Dictionary<string, string> TypeLabels = new()
    {
        ["CpuSpike"] = "CPU-spikar",
        ["Hang"] = "UI-hängningar",
        ["MemorySpike"] = "Minnesökningar",
        ["DiskBottleneck"] = "Disk I/O",
        ["DiskLatency"] = "Disk-latens",
        ["NetworkSpike"] = "Nätverksspikar",
        ["PageFaultStorm"] = "Paging-stormar",
        ["HandleLeak"] = "Handle-läckor",
        ["GdiLeak"] = "GDI-varningar",
        ["ThreadExplosion"] = "Tråd-explosioner",
        ["DpcStorm"] = "DPC-stormar",
        ["CpuThrottle"] = "CPU-throttling",
        ["GpuSaturation"] = "GPU-mättnad",
        ["GpuVramPressure"] = "VRAM-tryck",
        ["GpuTdr"] = "GPU TDR",
        ["StorageReset"] = "Lagringsfel",
        ["DnsLatency"] = "DNS-latens",
        ["SchedulerContention"] = "Scheduler-trängsel",
        ["CommitExhaustion"] = "Commit-gräns",
        ["PoolExhaustion"] = "Kernel pool"
    };

    // ── Composite score helpers ──────────────────────────

    private static void DrawCompositeScoreBar(string label, int value, string status)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"  {label + ":",-18}");

        Console.Write("[");
        int filled = (int)(value / 100.0 * BarWidth);
        filled = Math.Clamp(filled, 0, BarWidth);
        int empty = BarWidth - filled;

        var color = value switch
        {
            > 80 => ConsoleColor.Red,
            > 60 => ConsoleColor.Yellow,
            > 30 => ConsoleColor.DarkYellow,
            _ => ConsoleColor.Green
        };

        Console.ForegroundColor = color;
        Console.Write(new string('█', filled));
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string('░', empty));
        Console.ResetColor();
        Console.Write("]  ");

        Console.ForegroundColor = color;
        Console.Write($"{value,3}/100");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"  {status}");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static string GetPressureLabel(int index) => index switch
    {
        <= 30 => "Friskt",
        <= 60 => "Tryck",
        <= 80 => "Högt",
        _ => "Kritiskt"
    };

    private static string GetLatencyLabel(int score) => score switch
    {
        <= 20 => "Responsivt",
        <= 50 => "Viss latens",
        <= 75 => "Hög",
        _ => "Kritiskt"
    };

    // ── Drawing helpers ──────────────────────────────────

    private static void DrawBarPair(string label1, double percent1, string label2, double percent2)
    {
        Console.Write($"  {label1}: ");
        DrawBar(percent1, GetBarColor(percent1));
        Console.Write($" {percent1,4:F0}%");

        Console.Write($"    {label2}: ");
        DrawBar(percent2, GetBarColor(percent2));
        Console.Write($" {percent2,4:F0}%");
        Console.WriteLine();
    }

    private static void DrawBar(double percent, ConsoleColor color)
    {
        int filled = (int)(percent / 100.0 * BarWidth);
        filled = Math.Clamp(filled, 0, BarWidth);
        int empty = BarWidth - filled;

        Console.ForegroundColor = color;
        Console.Write(new string('█', filled));
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string('░', empty));
        Console.ResetColor();
    }

    private static ConsoleColor GetBarColor(double percent) => percent switch
    {
        > 90 => ConsoleColor.Red,
        > 70 => ConsoleColor.Yellow,
        _ => ConsoleColor.Green
    };

    private static ConsoleColor GetNetColor(double mbps) => mbps switch
    {
        > 500 => ConsoleColor.Red,
        > 100 => ConsoleColor.Yellow,
        > 10 => ConsoleColor.Cyan,
        _ => ConsoleColor.Green
    };

    private static void WriteStatRow(string label, double avg, double peak, string unit, double warnThreshold, double critThreshold)
    {
        Console.Write($"  {label,-25} ");

        if (avg > 0)
        {
            var avgColor = avg >= critThreshold ? ConsoleColor.Red
                : avg >= warnThreshold ? ConsoleColor.Yellow
                : ConsoleColor.Green;
            Console.ForegroundColor = avgColor;
            Console.Write($"{avg,6:F1}{unit,-9}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"{"—",-15}");
        }

        var peakColor = peak >= critThreshold ? ConsoleColor.Red
            : peak >= warnThreshold ? ConsoleColor.Yellow
            : ConsoleColor.Green;
        Console.ForegroundColor = peakColor;
        Console.Write($"{peak,6:F1}{unit}");

        Console.ResetColor();
        Console.WriteLine();
    }

    private static void WritePeakStat(string label, string value, bool isWarning)
    {
        Console.Write($"  {label,-25} ");
        Console.ForegroundColor = isWarning ? ConsoleColor.Yellow : ConsoleColor.White;
        Console.WriteLine(value);
        Console.ResetColor();
    }

    private static string FormatDuration(double totalSeconds)
    {
        if (totalSeconds < 60)
            return $"{totalSeconds:F0} sek";
        if (totalSeconds < 3600)
        {
            int min = (int)(totalSeconds / 60);
            int sec = (int)(totalSeconds % 60);
            return $"{min} min {sec} sek";
        }
        int hours = (int)(totalSeconds / 3600);
        int mins = (int)((totalSeconds % 3600) / 60);
        return $"{hours} tim {mins} min";
    }

    private static void WriteWrapped(string text, int maxWidth)
    {
        if (text.Length <= maxWidth)
        {
            Console.WriteLine(text);
            return;
        }

        int indent = 0;
        while (indent < text.Length && text[indent] == ' ')
            indent++;
        string indentStr = new(' ', indent);

        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int currentLineLen = indent;
        Console.Write(indentStr);

        foreach (var word in words)
        {
            if (currentLineLen + word.Length + 1 > maxWidth && currentLineLen > indent)
            {
                Console.WriteLine();
                Console.Write(indentStr);
                currentLineLen = indent;
            }

            if (currentLineLen > indent)
            {
                Console.Write(' ');
                currentLineLen++;
            }

            Console.Write(word);
            currentLineLen += word.Length;
        }
        Console.WriteLine();
    }
}
