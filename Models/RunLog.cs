using System.Text.Json.Serialization;

namespace ComputerPerformanceReview.Models;

public sealed record RunLog(
    DateTime Timestamp,
    int HealthScore,
    int CriticalCount,
    int WarningCount,
    int OkCount,
    List<ReportLog> Reports
);

public sealed record ReportLog(
    string Title,
    List<ResultLog> Results
);

public sealed record ResultLog(
    string Category,
    string CheckName,
    string Description,
    string Severity,
    string? Recommendation
);

[JsonSerializable(typeof(RunLog))]
[JsonSerializable(typeof(List<RunLog>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RunLogJsonContext : JsonSerializerContext;
