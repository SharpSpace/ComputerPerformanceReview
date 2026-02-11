namespace ComputerPerformanceReview.Models;

/// <summary>
/// Mutabel builder för MonitorSample. Sub-analyzers populerar sina fält,
/// sedan anropas Build() för att skapa det immutabla recordet.
/// </summary>
public sealed class MonitorSampleBuilder
{
    // Grundläggande
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public double CpuPercent { get; set; }
    public double MemoryUsedPercent { get; set; }
    public long MemoryAvailableBytes { get; set; }
    public double DiskQueueLength { get; set; }
    public double NetworkMbps { get; set; }
    public long TotalSystemHandles { get; set; }
    public double PageFaultsPerSec { get; set; }
    public long CommittedBytes { get; set; }
    public List<MonitorProcessInfo> TopCpuProcesses { get; set; } = [];
    public List<MonitorProcessInfo> TopMemoryProcesses { get; set; } = [];
    public List<MonitorProcessInfo> TopGdiProcesses { get; set; } = [];
    public List<HangingProcessInfo> HangingProcesses { get; set; } = [];

    // Nya minnesfält
    public double PagesInputPerSec { get; set; }
    public double PagesOutputPerSec { get; set; }
    public long CommitLimit { get; set; }
    public long AvailableMBytes { get; set; }
    public long PoolNonpagedBytes { get; set; }
    public long PoolPagedBytes { get; set; }

    // Nya CPU/scheduler-fält
    public double ContextSwitchesPerSec { get; set; }
    public int ProcessorQueueLength { get; set; }
    public double DpcTimePercent { get; set; }
    public double InterruptTimePercent { get; set; }

    // Nya diskfält
    public double AvgDiskSecRead { get; set; }
    public double AvgDiskSecWrite { get; set; }

    // Composite scores (sätts av engine efter Collect)
    public int MemoryPressureIndex { get; set; }
    public int SystemLatencyScore { get; set; }
    public FreezeClassification? FreezeInfo { get; set; }

    public MonitorSample Build() => new(
        Timestamp: Timestamp,
        CpuPercent: CpuPercent,
        MemoryUsedPercent: MemoryUsedPercent,
        MemoryAvailableBytes: MemoryAvailableBytes,
        DiskQueueLength: DiskQueueLength,
        NetworkMbps: NetworkMbps,
        TotalSystemHandles: TotalSystemHandles,
        PageFaultsPerSec: PageFaultsPerSec,
        CommittedBytes: CommittedBytes,
        TopCpuProcesses: TopCpuProcesses,
        TopMemoryProcesses: TopMemoryProcesses,
        TopGdiProcesses: TopGdiProcesses,
        HangingProcesses: HangingProcesses,
        PagesInputPerSec: PagesInputPerSec,
        PagesOutputPerSec: PagesOutputPerSec,
        CommitLimit: CommitLimit,
        AvailableMBytes: AvailableMBytes,
        PoolNonpagedBytes: PoolNonpagedBytes,
        PoolPagedBytes: PoolPagedBytes,
        ContextSwitchesPerSec: ContextSwitchesPerSec,
        ProcessorQueueLength: ProcessorQueueLength,
        DpcTimePercent: DpcTimePercent,
        InterruptTimePercent: InterruptTimePercent,
        AvgDiskSecRead: AvgDiskSecRead,
        AvgDiskSecWrite: AvgDiskSecWrite,
        MemoryPressureIndex: MemoryPressureIndex,
        SystemLatencyScore: SystemLatencyScore,
        FreezeInfo: FreezeInfo
    );
}
