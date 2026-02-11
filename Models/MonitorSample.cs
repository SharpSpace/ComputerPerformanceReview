using System.Text.Json.Serialization;

namespace ComputerPerformanceReview.Models;

public sealed record MonitorSample(
    // --- Grundläggande (befintliga) ---
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
    List<HangingProcessInfo> HangingProcesses,

    // --- Nya minnesfält ---
    double PagesInputPerSec = 0,
    double PagesOutputPerSec = 0,
    long CommitLimit = 0,
    long AvailableMBytes = 0,
    long PoolNonpagedBytes = 0,
    long PoolPagedBytes = 0,

    // --- Nya CPU/scheduler-fält ---
    double ContextSwitchesPerSec = 0,
    int ProcessorQueueLength = 0,
    double DpcTimePercent = 0,
    double InterruptTimePercent = 0,

    // --- Nya diskfält ---
    double AvgDiskSecRead = 0,
    double AvgDiskSecWrite = 0,

    // --- Composite scores (beräknade, inte insamlade) ---
    int MemoryPressureIndex = 0,
    int SystemLatencyScore = 0,
    FreezeClassification? FreezeInfo = null
);

public sealed record HangingProcessInfo(
    string Name,
    double HangSeconds
);

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
    // Befintliga
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
    // Nya
    double PeakPagesInputPerSec = 0,
    double PeakDpcTimePercent = 0,
    double PeakInterruptTimePercent = 0,
    double PeakContextSwitchesPerSec = 0,
    int PeakProcessorQueueLength = 0,
    double PeakAvgDiskSecRead = 0,
    double PeakAvgDiskSecWrite = 0,
    long PeakPoolNonpagedBytes = 0,
    long PeakPoolPagedBytes = 0,
    int AvgMemoryPressureIndex = 0,
    int PeakMemoryPressureIndex = 0,
    int AvgSystemLatencyScore = 0,
    int PeakSystemLatencyScore = 0,
    int FreezeCount = 0
);

[JsonSerializable(typeof(MonitorReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class MonitorReportJsonContext : JsonSerializerContext;
