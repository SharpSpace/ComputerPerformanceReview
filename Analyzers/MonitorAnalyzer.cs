using System.Text.Json;
using ComputerPerformanceReview.Analyzers.Health;

namespace ComputerPerformanceReview.Analyzers;

public sealed class MonitorAnalyzer
{
    private const int SampleIntervalMs = 3000;

    public async Task RunAsync(int durationMinutes)
    {
        var startTime = DateTime.Now;
        var endTime = startTime.AddMinutes(durationMinutes);
        int sampleCount = 0;

        var engine = new SystemHealthEngine();

        // Första samplet ger sub-analyzers en CPU-baseline
        engine.CollectAndAnalyze();
        await Task.Delay(SampleIntervalMs);

        while (DateTime.Now < endTime)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Q) break;
            }

            var sample = engine.CollectAndAnalyze();
            sampleCount++;

            MonitorDisplay.DrawDashboard(sample, engine.AllEvents, startTime, durationMinutes);

            await Task.Delay(SampleIntervalMs);
        }

        // Build and show report
        var report = engine.BuildReport(startTime, DateTime.Now, sampleCount);
        MonitorDisplay.DrawSummary(report);

        // Save to JSON
        await SaveReportAsync(report);
    }

    private static async Task SaveReportAsync(MonitorReport report)
    {
        try
        {
            var dir = LogHelper.GetLogDir();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var fileName = $"monitor_{report.StartTime:yyyy-MM-dd_HH-mm-ss}.json";
            var path = Path.Combine(dir, fileName);

            var json = JsonSerializer.Serialize(report, MonitorReportJsonContext.Default.MonitorReport);
            await File.WriteAllTextAsync(path, json);

            Console.WriteLine();
            ConsoleHelper.WriteInfo($"  Rapport sparad till: {Path.GetFullPath(path)}");
        }
        catch (Exception ex)
        {
            ConsoleHelper.WriteWarning($"Kunde inte spara övervakningsrapport: {ex.Message}");
        }
    }
}
