namespace ComputerPerformanceReview.Analyzers;

public sealed class MemoryAnalyzer : IAnalyzer
{
    public string Name => "Memory Analysis";

    public async Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        AnalyzeRamUsage(results);
        AnalyzeTopMemoryProcesses(results);
        await DetectMemoryLeaks(results);
        AnalyzeCommittedMemory(results);

        return new AnalysisReport("MEMORY ANALYSIS", results);
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

        string desc = $"Total RAM: {ConsoleHelper.FormatBytes(totalKb * 1024)} | " +
                      $"Used: {ConsoleHelper.FormatBytes(usedKb * 1024)} | " +
                      $"Available: {ConsoleHelper.FormatBytes(freeKb * 1024)} ({usedPercent:F1}% used)";

        var severity = usedPercent switch
        {
            > 90 => Severity.Critical,
            > 75 => Severity.Warning,
            _ => Severity.Ok
        };

        string? rec = severity switch
        {
            Severity.Critical => "Close unused programs to free memory",
            Severity.Warning => "Memory usage is high, consider closing programs",
            _ => null
        };

        results.Add(new AnalysisResult("Memory", "RAM usage", desc, severity, rec));
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

        results.Add(new AnalysisResult("Memory", "Top memory consumers",
            $"Top 5: {topList}", Severity.Ok));

        foreach (var proc in topProcesses.Where(p => p.Memory > 4L * 1024 * 1024 * 1024))
        {
            results.Add(new AnalysisResult("Memory", "High memory usage",
                $"{proc.Name} is using {ConsoleHelper.FormatBytes(proc.Memory)}",
                Severity.Critical,
                $"Restart {proc.Name} to free memory"));
        }

        foreach (var proc in topProcesses.Where(p => p.Memory > 2L * 1024 * 1024 * 1024 && p.Memory <= 4L * 1024 * 1024 * 1024))
        {
            results.Add(new AnalysisResult("Memory", "High memory usage",
                $"{proc.Name} is using {ConsoleHelper.FormatBytes(proc.Memory)}",
                Severity.Warning,
                $"Consider restarting {proc.Name}"));
        }
    }

    private static async Task DetectMemoryLeaks(List<AnalysisResult> results)
    {
        ConsoleHelper.WriteProgress("Measuring memory leaks (10 seconds)...");

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
            results.Add(new AnalysisResult("Memory", "Memory leaks",
                "No potential memory leaks detected during the measurement period", Severity.Ok));
        }
        else
        {
            foreach (var leak in leaks.OrderByDescending(l => l.GrowthBytes))
            {
                var severity = leak.GrowthBytes > 200 * 1024 * 1024 ? Severity.Critical : Severity.Warning;
                results.Add(new AnalysisResult("Memory", "Potential memory leak",
                    $"{leak.Name} grew by {ConsoleHelper.FormatBytes(leak.GrowthBytes)} in 10 seconds " +
                    $"({ConsoleHelper.FormatBytes(leak.MemoryBytesFirst)} → {ConsoleHelper.FormatBytes(leak.MemoryBytesSecond)})",
                    severity,
                    $"Restart {leak.Name} to fix the memory leak"));
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

        results.Add(new AnalysisResult("Memory", "Committed memory",
            $"Committed: {ConsoleHelper.FormatBytes(usedKb * 1024)} / {ConsoleHelper.FormatBytes(totalKb * 1024)} ({usedPercent:F1}%)",
            severity,
            severity != Severity.Ok ? "System virtual memory is under heavy load" : null));
    }
}
