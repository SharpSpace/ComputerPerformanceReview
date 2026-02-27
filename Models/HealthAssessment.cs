namespace ComputerPerformanceReview.Models;

/// <summary>
/// Result from a sub-analyzer: health score + any new events.
/// </summary>
public sealed record HealthAssessment(
    HealthScore Score,
    List<MonitorEvent> NewEvents
);
