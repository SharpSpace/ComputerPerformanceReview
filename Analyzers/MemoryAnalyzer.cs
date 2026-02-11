namespace ComputerPerformanceReview.Analyzers;

public sealed class MemoryAnalyzer : IAnalyzer
{
    public string Name => "Minnesanalys";

    public async Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        AnalyzeRamUsage(results);
        AnalyzeTopMemoryProcesses(results);
        await DetectMemoryLeaks(results);
        AnalyzeCommittedMemory(results);

        return new AnalysisReport("MINNESANALYS", results);
    }

    private static void AnalyzeRamUsage(List<AnalysisResult> results)
    {
        var osData = WmiHelper.Query("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
        if (osData.Count == 0) return;

        var totalKb = WmiHelper.GetValue<long>(osData[0], "TotalVisibleMemorySize");
        var freeKb = WmiHelper.GetValue<long>(osData[0], "FreePhysicalMemory");

        if (totalKb <= 0) return;

        long usedKb = totalKb - freeKb;
        double usedPercent = (double)usedKb / totalKb * 100;

        string desc = $"Totalt RAM: {ConsoleHelper.FormatBytes(totalKb * 1024)} | " +
                      $"Använt: {ConsoleHelper.FormatBytes(usedKb * 1024)} | " +
                      $"Tillgängligt: {ConsoleHelper.FormatBytes(freeKb * 1024)} ({usedPercent:F1}% använt)";

        var severity = usedPercent switch
        {
            > 90 => Severity.Critical,
            > 75 => Severity.Warning,
            _ => Severity.Ok
        };

        string? rec = severity switch
        {
            Severity.Critical => "Stäng oanvända program för att frigöra minne",
            Severity.Warning => "Minnesanvändningen är hög, överväg att stänga program",
            _ => null
        };

        results.Add(new AnalysisResult("Minne", "RAM-användning", desc, severity, rec));
    }

    private static void AnalyzeTopMemoryProcesses(List<AnalysisResult> results)
    {
        var processes = new List<(string Name, long Memory)>();

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                processes.Add((proc.ProcessName, proc.WorkingSet64));
            }
            catch { }
            finally { proc.Dispose(); }
        }

        var topProcesses = processes
            .OrderByDescending(p => p.Memory)
            .Take(10)
            .ToList();

        var topList = string.Join(", ", topProcesses.Take(5)
            .Select(p => $"{p.Name} ({ConsoleHelper.FormatBytes(p.Memory)})"));

        results.Add(new AnalysisResult("Minne", "Topp minnesanvändare",
            $"Topp 5: {topList}", Severity.Ok));

        foreach (var proc in topProcesses.Where(p => p.Memory > 4L * 1024 * 1024 * 1024))
        {
            results.Add(new AnalysisResult("Minne", "Hög minnesanvändning",
                $"{proc.Name} använder {ConsoleHelper.FormatBytes(proc.Memory)}",
                Severity.Critical,
                $"Starta om {proc.Name} för att frigöra minne"));
        }

        foreach (var proc in topProcesses.Where(p => p.Memory > 2L * 1024 * 1024 * 1024 && p.Memory <= 4L * 1024 * 1024 * 1024))
        {
            results.Add(new AnalysisResult("Minne", "Hög minnesanvändning",
                $"{proc.Name} använder {ConsoleHelper.FormatBytes(proc.Memory)}",
                Severity.Warning,
                $"Överväg att starta om {proc.Name}"));
        }
    }

    private static async Task DetectMemoryLeaks(List<AnalysisResult> results)
    {
        ConsoleHelper.WriteProgress("Mäter minnesläckor (10 sekunder)...");

        var snapshot1 = new Dictionary<int, (string Name, long Memory)>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                snapshot1[proc.Id] = (proc.ProcessName, proc.WorkingSet64);
            }
            catch { }
            finally { proc.Dispose(); }
        }

        await Task.Delay(TimeSpan.FromSeconds(10));

        var leaks = new List<ProcessMemorySnapshot>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (snapshot1.TryGetValue(proc.Id, out var prev))
                {
                    long growth = proc.WorkingSet64 - prev.Memory;
                    if (growth > 50 * 1024 * 1024) // > 50 MB growth
                    {
                        leaks.Add(new ProcessMemorySnapshot(
                            proc.Id, proc.ProcessName, prev.Memory, proc.WorkingSet64, growth));
                    }
                }
            }
            catch { }
            finally { proc.Dispose(); }
        }

        ConsoleHelper.ClearProgress();

        if (leaks.Count == 0)
        {
            results.Add(new AnalysisResult("Minne", "Minnesläckor",
                "Inga potentiella minnesläckor upptäcktes under mätperioden", Severity.Ok));
        }
        else
        {
            foreach (var leak in leaks.OrderByDescending(l => l.GrowthBytes))
            {
                var severity = leak.GrowthBytes > 200 * 1024 * 1024 ? Severity.Critical : Severity.Warning;
                results.Add(new AnalysisResult("Minne", "Potentiell minnesläcka",
                    $"{leak.Name} ökade med {ConsoleHelper.FormatBytes(leak.GrowthBytes)} på 10 sekunder " +
                    $"({ConsoleHelper.FormatBytes(leak.MemoryBytesFirst)} → {ConsoleHelper.FormatBytes(leak.MemoryBytesSecond)})",
                    severity,
                    $"Starta om {leak.Name} för att åtgärda minnesläckan"));
            }
        }
    }

    private static void AnalyzeCommittedMemory(List<AnalysisResult> results)
    {
        var osData = WmiHelper.Query("SELECT TotalVirtualMemorySize, FreeVirtualMemory FROM Win32_OperatingSystem");
        if (osData.Count == 0) return;

        var totalKb = WmiHelper.GetValue<long>(osData[0], "TotalVirtualMemorySize");
        var freeKb = WmiHelper.GetValue<long>(osData[0], "FreeVirtualMemory");

        if (totalKb <= 0) return;

        long usedKb = totalKb - freeKb;
        double usedPercent = (double)usedKb / totalKb * 100;

        var severity = usedPercent switch
        {
            > 90 => Severity.Critical,
            > 75 => Severity.Warning,
            _ => Severity.Ok
        };

        results.Add(new AnalysisResult("Minne", "Committed minne",
            $"Committed: {ConsoleHelper.FormatBytes(usedKb * 1024)} / {ConsoleHelper.FormatBytes(totalKb * 1024)} ({usedPercent:F1}%)",
            severity,
            severity != Severity.Ok ? "Systemets virtuella minne är högt belastat" : null));
    }
}
