using System.Text.Json;

namespace ComputerPerformanceReview.Helpers;

public static class LogHelper
{
    private const int MaxHistoryEntries = 10;
    private const string LogFolder = "Logs";

    public static string GetLogDir()
    {
        // Get the directory where the executable is located
        var exeDir = AppContext.BaseDirectory;
        
        // Check if we're running from a bin folder (development environment)
        // In development: C:\...\bin\Debug\net10.0-windows\
        // In production: C:\...\MyApp\ (no bin folder in path)
        var dirInfo = new DirectoryInfo(exeDir);
        
        // Walk up the directory tree to check if there's a bin folder in the path
        var current = dirInfo;
        DirectoryInfo? binFolder = null;
        
        while (current != null)
        {
            if (current.Name.Equals("bin", StringComparison.OrdinalIgnoreCase))
            {
                binFolder = current;
                break;
            }
            current = current.Parent;
        }
        
        // If we found a bin folder in the path, use the parent of the bin folder
        if (binFolder?.Parent != null)
        {
            exeDir = binFolder.Parent.FullName;
        }
        
        return Path.Combine(exeDir, LogFolder);
    }

    public static string GetLogFilePath(DateTime timestamp)
    {
        var fileName = $"run_{timestamp:yyyy-MM-dd_HH-mm-ss}.json";
        return Path.Combine(GetLogDir(), fileName);
    }

    public static async Task<List<RunLog>> LoadHistoryAsync()
    {
        var dir = GetLogDir();
        if (!Directory.Exists(dir))
            return [];

        var files = Directory.GetFiles(dir, "run_*.json")
            .OrderBy(f => f)
            .TakeLast(MaxHistoryEntries)
            .ToList();

        var history = new List<RunLog>();
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var run = JsonSerializer.Deserialize(json, RunLogJsonContext.Default.RunLog);
                if (run is not null)
                    history.Add(run);
            }
            catch { }
        }

        return history;
    }

    public static async Task<string> SaveRunAsync(List<AnalysisReport> reports, int healthScore)
    {
        var runLog = CreateRunLog(reports, healthScore);

        var dir = GetLogDir();
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var path = GetLogFilePath(runLog.Timestamp);
        var json = JsonSerializer.Serialize(runLog, RunLogJsonContext.Default.RunLog);
        await File.WriteAllTextAsync(path, json);

        return path;
    }

    public static RunLog CreateRunLog(List<AnalysisReport> reports, int healthScore)
    {
        var allResults = reports.SelectMany(r => r.Results).ToList();

        var reportLogs = reports.Select(r => new ReportLog(
            r.Title,
            r.Results.Select(res => new ResultLog(
                res.Category,
                res.CheckName,
                res.Description,
                res.Severity.ToString(),
                res.Recommendation
            )).ToList()
        )).ToList();

        return new RunLog(
            Timestamp: DateTime.Now,
            HealthScore: healthScore,
            CriticalCount: allResults.Count(r => r.Severity == Models.Severity.Critical),
            WarningCount: allResults.Count(r => r.Severity == Models.Severity.Warning),
            OkCount: allResults.Count(r => r.Severity == Models.Severity.Ok),
            Reports: reportLogs
        );
    }
}
