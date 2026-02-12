namespace ComputerPerformanceReview.Models;

/// <summary>
/// Detailed freeze analysis report containing thread state information and root cause analysis.
/// Generated when a process becomes unresponsive for more than 5 seconds.
/// </summary>
/// <param name="ProcessName">Name of the frozen process</param>
/// <param name="ProcessId">Process ID (PID)</param>
/// <param name="FreezeDuration">How long the process has been frozen</param>
/// <param name="TotalThreads">Total number of threads in the process</param>
/// <param name="RunningThreads">Number of threads in Running state</param>
/// <param name="WaitReasonCounts">Dictionary mapping wait reasons to the count of threads in that wait state</param>
/// <param name="DominantWaitReason">The wait reason affecting >60% of waiting threads, if any</param>
/// <param name="LikelyRootCause">Heuristically determined root cause of the freeze</param>
/// <param name="MiniDumpPath">Path to the generated minidump file, if created (null if not generated or freeze &lt; 15s)</param>
/// <param name="MiniDumpAnalysis">Analysis results from the minidump, if available</param>
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
/// Contains diagnostic information extracted from the dump.
/// </summary>
/// <param name="FaultingModule">Name of the module that caused the fault, if identified</param>
/// <param name="ExceptionCode">Exception code from the crash, if available</param>
/// <param name="FaultingThreadId">ID of the thread that caused the fault, if identified</param>
/// <param name="LoadedModules">List of modules loaded in the process at the time of the dump</param>
/// <param name="StackTraces">Stack traces from relevant threads</param>
/// <param name="FlaggedModules">Modules flagged as potentially problematic or known to cause instability</param>
public sealed record MiniDumpAnalysis(
    string? FaultingModule,
    string? ExceptionCode,
    int? FaultingThreadId,
    List<string> LoadedModules,
    List<string> StackTraces,
    List<string> FlaggedModules
);
