namespace ComputerPerformanceReview.Models;

public sealed record ProcessInfo(
    int Pid,
    string Name,
    long MemoryBytes,
    double CpuPercent,
    int ThreadCount
);
