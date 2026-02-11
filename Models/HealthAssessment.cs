namespace ComputerPerformanceReview.Models;

/// <summary>
/// Resultat från en sub-analyzer: hälsopoäng + eventuella nya händelser.
/// </summary>
public sealed record HealthAssessment(
    HealthScore Score,
    List<MonitorEvent> NewEvents
);
