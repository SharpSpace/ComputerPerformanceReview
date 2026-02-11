namespace ComputerPerformanceReview.Models;

/// <summary>
/// Klassificering av varför en process hänger sig, baserat på systemets tillstånd.
/// </summary>
public sealed record FreezeClassification(
    string ProcessName,
    string LikelyCause,
    string Description
);
