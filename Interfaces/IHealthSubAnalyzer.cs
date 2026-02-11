namespace ComputerPerformanceReview.Interfaces;

/// <summary>
/// Interface för domänspecifika hälsoanalysatorer i SystemHealthEngine.
/// Varje sub-analyzer ansvarar för att samla in och analysera en specifik domän.
/// </summary>
public interface IHealthSubAnalyzer
{
    string Domain { get; }

    /// <summary>
    /// Samla in rådata via WMI/Process API och populera relevanta fält i buildern.
    /// </summary>
    void Collect(MonitorSampleBuilder builder);

    /// <summary>
    /// Analysera aktuellt sample mot historiken, returnera hälsopoäng och eventuella händelser.
    /// </summary>
    HealthAssessment Analyze(MonitorSample current, IReadOnlyList<MonitorSample> history);
}
