using System.Text.Json.Serialization;

namespace ComputerPerformanceReview.Models;

public sealed record MonitorSample(
    DateTime Timestamp,
    double CpuPercent,
    double MemoryUsedPercent,
    long MemoryAvailableBytes,
    double DiskQueueLength,
    double NetworkMbps,
    long TotalSystemHandles,
    double PageFaultsPerSec,
    long CommittedBytes,
    List<MonitorProcessInfo> TopCpuProcesses,
    List<MonitorProcessInfo> TopMemoryProcesses,
    List<MonitorProcessInfo> TopGdiProcesses,
    List<MonitorIoProcessInfo> TopIoProcesses,
    List<MonitorFaultProcessInfo> TopFaultProcesses,
    List<DiskInstanceStat> DiskInstances,
    List<HangingProcessInfo> HangingProcesses,
    double PagesInputPerSec = 0,
    double PagesOutputPerSec = 0,
    long CommitLimit = 0,
    long AvailableMBytes = 0,
    long PoolNonpagedBytes = 0,
    long PoolPagedBytes = 0,
    double ContextSwitchesPerSec = 0,
    int ProcessorQueueLength = 0,
    double DpcTimePercent = 0,
    double InterruptTimePercent = 0,
    double AvgDiskSecRead = 0,
    double AvgDiskSecWrite = 0,
    double CpuClockMHz = 0,
    double CpuMaxClockMHz = 0,
    double GpuUtilizationPercent = 0,
    long GpuDedicatedUsageBytes = 0,
    long GpuDedicatedLimitBytes = 0,
    double DnsLatencyMs = 0,
    int StorageErrorsLast15Min = 0,
    int TdrEventsLast15Min = 0,
    int MemoryPressureIndex = 0,
    int SystemLatencyScore = 0,
    FreezeClassification? FreezeInfo = null,
    FreezeReport? DeepFreezeReport = null,
    List<SysinternalsHandleInfo>? SysinternalsHandleData = null,
    string? SysinternalsProcDumpPath = null,
    List<SysinternalsPoolAllocation>? SysinternalsPoolData = null,
    List<DiskSpaceInfo>? DiskSpaces = null,
    string? ActivePowerPlan = null,
    int BrowserProcessCount = 0,
    long BrowserMemoryBytes = 0,
    string? SysinternalsDiskExtOutput = null,
    bool? SysinternalsRamMapAvailable = null,
    MiniDumpAnalysis? SysinternalsProcDumpAnalysis = null
);

public sealed record HangingProcessInfo(string Name, double HangSeconds);

public sealed record MonitorProcessInfo(
    string Name,
    int Pid,
    double CpuPercent,
    long MemoryBytes,
    int HandleCount,
    int GdiObjects,
    int UserObjects,
    int ThreadCount = 0
);

public sealed record MonitorIoProcessInfo(string Name, int Pid, ulong ReadBytes, ulong WriteBytes, ulong TotalBytes);
public sealed record MonitorFaultProcessInfo(string Name, int Pid, double PageFaultsPerSec);
public sealed record DiskInstanceStat(string Name, double QueueLength, double ReadLatencyMs, double WriteLatencyMs, double BusyPercent);

/// <summary>
/// Sysinternals Handle.exe data for a process
/// </summary>
public sealed record SysinternalsHandleInfo(
    int ProcessId,
    string ProcessName,
    int TotalHandles,
    Dictionary<string, int> HandleTypeBreakdown
);

/// <summary>
/// Sysinternals PoolMon.exe data for kernel pool allocations
/// </summary>
public sealed record SysinternalsPoolAllocation(
    string Tag,
    string Type,
    long Bytes
);

public sealed record MonitorEvent(
    DateTime Timestamp,
    string EventType,
    string Description,
    string Severity,
    string Tip = ""
);

public sealed record MonitorReport(
    DateTime StartTime,
    DateTime EndTime,
    int TotalSamples,
    List<MonitorEvent> Events,
    double AvgCpu,
    double PeakCpu,
    double AvgMemory,
    double PeakMemory,
    double AvgDiskQueue,
    double PeakDiskQueue,
    double AvgNetworkMbps,
    double PeakNetworkMbps,
    long PeakSystemHandles,
    double PeakPageFaults,
    long PeakCommittedBytes,
    double PeakPagesInputPerSec = 0,
    double PeakDpcTimePercent = 0,
    double PeakInterruptTimePercent = 0,
    double PeakContextSwitchesPerSec = 0,
    int PeakProcessorQueueLength = 0,
    double PeakAvgDiskSecRead = 0,
    double PeakAvgDiskSecWrite = 0,
    long PeakPoolNonpagedBytes = 0,
    long PeakPoolPagedBytes = 0,
    double PeakGpuUtilizationPercent = 0,
    double PeakDnsLatencyMs = 0,
    int PeakStorageErrorsLast15Min = 0,
    int AvgMemoryPressureIndex = 0,
    int PeakMemoryPressureIndex = 0,
    int AvgSystemLatencyScore = 0,
    int PeakSystemLatencyScore = 0,
    int FreezeCount = 0
);

[JsonSerializable(typeof(MonitorReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class MonitorReportJsonContext : JsonSerializerContext;
