namespace ComputerPerformanceReview.Models;

/// <summary>
/// Detailed freeze analysis report containing thread state information and root cause analysis.
/// Generated when a process becomes unresponsive for more than 5 seconds.
/// </summary>
public sealed record FreezeReport(
    string ProcessName,
    int ProcessId,
    TimeSpan FreezeDuration,
    int TotalThreads,
    int RunningThreads,
    Dictionary<string, int> WaitReasonCounts,
    string? DominantWaitReason,
    string LikelyRootCause,
    string? MiniDumpPath = null,
    MiniDumpAnalysis? MiniDumpAnalysis = null
);

/// <summary>
/// Analysis results from a minidump file.
/// </summary>
public sealed record MiniDumpAnalysis(
    string? FaultingModule,
    string? ExceptionCode,
    int? FaultingThreadId,
    List<string> LoadedModules,
    List<string> StackTraces,
    List<string> FlaggedModules
);
