namespace ComputerPerformanceReview.Interfaces;

public interface IAnalyzer
{
    string Name { get; }
    Task<AnalysisReport> AnalyzeAsync();
}
