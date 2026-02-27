namespace ComputerPerformanceReview.Models;

/// <summary>
/// Classification of why a process is hanging, based on the system state.
/// </summary>
public sealed record FreezeClassification(
    string ProcessName,
    string LikelyCause,
    string Description,
    List<string>? Evidence = null
);
