namespace ComputerPerformanceReview.Models;

/// <summary>
/// Represents a concrete action step that a user can take to resolve a performance issue.
/// </summary>
public sealed record ActionStep(
    string Title,
    string? CommandHint = null,
    string? Difficulty = null
);
