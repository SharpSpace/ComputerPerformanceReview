namespace ComputerPerformanceReview.Models;

public sealed record AnalysisResult(
    string Category,
    string CheckName,
    string Description,
    Severity Severity,
    string? Recommendation = null,
    List<ActionStep>? ActionSteps = null
);
