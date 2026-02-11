namespace ComputerPerformanceReview.Models;

public sealed record ProcessMemorySnapshot(
    int Pid,
    string Name,
    long MemoryBytesFirst,
    long MemoryBytesSecond,
    long GrowthBytes
);
