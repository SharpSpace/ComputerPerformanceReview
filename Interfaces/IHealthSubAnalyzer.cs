namespace ComputerPerformanceReview.Interfaces;

/// <summary>
/// Interface for domain-specific health analyzers in SystemHealthEngine.
/// Each sub-analyzer is responsible for collecting and analyzing a specific domain.
/// </summary>
public interface IHealthSubAnalyzer
{
    string Domain { get; }

    /// <summary>
    /// Collect raw data via WMI/Process API and populate relevant fields in the builder.
    /// </summary>
    void Collect(MonitorSampleBuilder builder);

    /// <summary>
    /// Analyze the current sample against the history, return health score and any new events.
    /// </summary>
    HealthAssessment Analyze(MonitorSample current, IReadOnlyList<MonitorSample> history);
}
