namespace ComputerPerformanceReview.Models;

/// <summary>
/// Hälsopoäng för en specifik domän (Memory, CPU, Disk, Process, Overall).
/// Score 0-100 där 100 = fullt friskt. Confidence anger hur pålitlig mätningen är.
/// </summary>
public sealed record HealthScore(
    string Domain,
    int Score,
    double Confidence,
    string? RootCauseHint
);
