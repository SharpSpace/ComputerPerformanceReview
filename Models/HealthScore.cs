namespace ComputerPerformanceReview.Models;

/// <summary>
/// Health score for a specific domain (Memory, CPU, Disk, Process, Overall).
/// Score 0-100 where 100 = fully healthy. Confidence indicates how reliable the measurement is.
/// </summary>
public sealed record HealthScore(
    string Domain,
    int Score,
    double Confidence,
    string? RootCauseHint
);
