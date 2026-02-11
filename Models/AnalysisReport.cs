namespace ComputerPerformanceReview.Models;

public sealed class AnalysisReport(string title, List<AnalysisResult> results)
{
    public string Title { get; } = title;
    public List<AnalysisResult> Results { get; } = results;
}
