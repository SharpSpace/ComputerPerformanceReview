namespace ComputerPerformanceReview.Models;

public sealed class MonitorSampleBuilder
{
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
    public List<MonitorIoProcessInfo> TopIoProcesses { get; set; } = [];
    public List<MonitorFaultProcessInfo> TopFaultProcesses { get; set; } = [];
    public List<DiskInstanceStat> DiskInstances { get; set; } = [];
    public List<HangingProcessInfo> HangingProcesses { get; set; } = [];

    public double PagesInputPerSec { get; set; }
    public double PagesOutputPerSec { get; set; }
    public long CommitLimit { get; set; }
    public long AvailableMBytes { get; set; }
    public long PoolNonpagedBytes { get; set; }
    public long PoolPagedBytes { get; set; }

    public double ContextSwitchesPerSec { get; set; }
    public int ProcessorQueueLength { get; set; }
    public double DpcTimePercent { get; set; }
    public double InterruptTimePercent { get; set; }

    public double AvgDiskSecRead { get; set; }
    public double AvgDiskSecWrite { get; set; }

    public double CpuClockMHz { get; set; }
    public double CpuMaxClockMHz { get; set; }
    public double GpuUtilizationPercent { get; set; }
    public long GpuDedicatedUsageBytes { get; set; }
    public long GpuDedicatedLimitBytes { get; set; }
    public double DnsLatencyMs { get; set; }
    public int StorageErrorsLast15Min { get; set; }
    public int TdrEventsLast15Min { get; set; }

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
        TopIoProcesses: TopIoProcesses,
        TopFaultProcesses: TopFaultProcesses,
        DiskInstances: DiskInstances,
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
        CpuClockMHz: CpuClockMHz,
        CpuMaxClockMHz: CpuMaxClockMHz,
        GpuUtilizationPercent: GpuUtilizationPercent,
        GpuDedicatedUsageBytes: GpuDedicatedUsageBytes,
        GpuDedicatedLimitBytes: GpuDedicatedLimitBytes,
        DnsLatencyMs: DnsLatencyMs,
        StorageErrorsLast15Min: StorageErrorsLast15Min,
        TdrEventsLast15Min: TdrEventsLast15Min,
        MemoryPressureIndex: MemoryPressureIndex,
        SystemLatencyScore: SystemLatencyScore,
        FreezeInfo: FreezeInfo
    );
}
