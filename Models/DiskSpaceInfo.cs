namespace ComputerPerformanceReview.Models;

/// <summary>
/// Information about disk space on a volume
/// </summary>
public sealed record DiskSpaceInfo(
    string DriveLetter,
    long TotalBytes,
    long FreeBytes,
    double FreePercent
);
