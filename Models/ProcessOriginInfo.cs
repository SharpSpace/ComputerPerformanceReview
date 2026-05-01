namespace ComputerPerformanceReview.Models;

/// <summary>
/// Information about which process started another process and which user owns it.
/// Gathered via a fallback chain of three algorithms.
/// </summary>
/// <param name="ParentPid">PID of the parent (spawning) process</param>
/// <param name="ParentName">Name of the parent process, if it is still running</param>
/// <param name="UserName">Account name running the process</param>
/// <param name="Domain">Domain or machine name for the account</param>
/// <param name="Algorithm">Which algorithm succeeded (NtQueryInformationProcess / WMI / ToolHelp32)</param>
public sealed record ProcessOriginInfo(
    int ParentPid,
    string? ParentName,
    string? UserName,
    string? Domain,
    string Algorithm
)
{
    /// <summary>Full account string, e.g. "DESKTOP-ABC\Jerry" or "NT AUTHORITY\SYSTEM".</summary>
    public string AccountDisplay =>
        (Domain, UserName) switch
        {
            ({ Length: > 0 } d, { Length: > 0 } u) => $@"{d}\{u}",
            (_, { Length: > 0 } u) => u,
            _ => "unknown"
        };
}
