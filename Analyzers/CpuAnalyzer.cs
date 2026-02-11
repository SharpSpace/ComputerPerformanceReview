namespace ComputerPerformanceReview.Analyzers;

public sealed class CpuAnalyzer : IAnalyzer
{
    public string Name => "CPU-analys";

    public async Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        await AnalyzeCpuUsage(results);
        AnalyzeThreadCounts(results);

        return new AnalysisReport("CPU-ANALYS", results);
    }

    private static async Task AnalyzeCpuUsage(List<AnalysisResult> results)
    {
        ConsoleHelper.WriteProgress("Mäter CPU-användning (2 sekunder)...");

        // Overall CPU via WMI (more reliable than PerformanceCounter on newer .NET)
        double overallCpu = 0;
        try
        {
            var cpuData = WmiHelper.Query("SELECT LoadPercentage FROM Win32_Processor");
            if (cpuData.Count > 0)
            {
                overallCpu = cpuData.Average(d => WmiHelper.GetValue<double>(d, "LoadPercentage"));
            }
        }
        catch { }

        var overallSeverity = overallCpu switch
        {
            > 90 => Severity.Critical,
            > 70 => Severity.Warning,
            _ => Severity.Ok
        };

        results.Add(new AnalysisResult("CPU", "Total CPU-användning",
            $"CPU-belastning: {overallCpu:F1}%", overallSeverity,
            overallSeverity != Severity.Ok ? "Hög CPU-belastning kan orsaka att gränssnittet hänger sig" : null));

        // Per-process CPU sampling
        var processCpuTimes = new Dictionary<int, (string Name, TimeSpan CpuTime)>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                processCpuTimes[proc.Id] = (proc.ProcessName, proc.TotalProcessorTime);
            }
            catch { }
            finally { proc.Dispose(); }
        }

        await Task.Delay(2000);

        int cpuCount = Environment.ProcessorCount;
        var cpuUsages = new List<(string Name, double Percent)>();

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (processCpuTimes.TryGetValue(proc.Id, out var prev))
                {
                    var cpuDelta = proc.TotalProcessorTime - prev.CpuTime;
                    double percent = cpuDelta.TotalMilliseconds / (2000.0 * cpuCount) * 100;
                    if (percent > 0.5)
                    {
                        cpuUsages.Add((proc.ProcessName, percent));
                    }
                }
            }
            catch { }
            finally { proc.Dispose(); }
        }

        ConsoleHelper.ClearProgress();

        var topCpu = cpuUsages
            .OrderByDescending(p => p.Percent)
            .Take(5)
            .ToList();

        if (topCpu.Count > 0)
        {
            var topList = string.Join(", ", topCpu.Select(p => $"{p.Name} ({p.Percent:F1}%)"));

            var topSeverity = topCpu[0].Percent > 50 ? Severity.Warning : Severity.Ok;
            results.Add(new AnalysisResult("CPU", "Topp CPU-användare",
                $"Topp 5: {topList}", topSeverity,
                topSeverity != Severity.Ok ? $"Processen {topCpu[0].Name} använder mycket CPU" : null));
        }
    }

    private static void AnalyzeThreadCounts(List<AnalysisResult> results)
    {
        var threadData = new List<(string Name, int Threads, int Pid)>();

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                threadData.Add((proc.ProcessName, proc.Threads.Count, proc.Id));
            }
            catch { }
            finally { proc.Dispose(); }
        }

        var highThreads = threadData
            .OrderByDescending(p => p.Threads)
            .Where(p => p.Threads > 100)
            .Take(5)
            .ToList();

        if (highThreads.Count == 0)
        {
            results.Add(new AnalysisResult("CPU", "Trådantal",
                "Inga processer med onormalt högt antal trådar", Severity.Ok));
        }
        else
        {
            foreach (var p in highThreads)
            {
                var severity = p.Threads switch
                {
                    > 1000 => Severity.Critical,
                    > 500 => Severity.Warning,
                    _ => Severity.Ok
                };

                if (severity != Severity.Ok)
                {
                    results.Add(new AnalysisResult("CPU", "Högt trådantal",
                        $"{p.Name} (PID {p.Pid}) har {p.Threads} trådar",
                        severity,
                        $"Potentiell trådläcka i {p.Name} - starta om programmet"));
                }
            }
        }
    }
}
