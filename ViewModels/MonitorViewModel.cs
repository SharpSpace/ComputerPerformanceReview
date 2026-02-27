using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComputerPerformanceReview.Analyzers.Health;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;

namespace ComputerPerformanceReview.ViewModels;

public partial class MonitorViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly SystemHealthEngine _engine = new();
    private readonly DispatcherTimer _timer;
    private readonly DateTime _startTime;
    private readonly int _durationMinutes;
    private bool _baselineTaken;

    // ── Top gauges ──
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _memoryUsedPercent;
    [ObservableProperty] private double _diskQueueLength;
    [ObservableProperty] private double _gpuUtilizationPercent;
    [ObservableProperty] private double _networkMbps;

    // ── Health scores ──
    [ObservableProperty] private int _memoryPressureIndex;
    [ObservableProperty] private int _systemLatencyScore;
    [ObservableProperty] private string _memoryPressureLabel = "Healthy";
    [ObservableProperty] private string _systemLatencyLabel = "Responsive";

    // ── System details ──
    [ObservableProperty] private long _totalSystemHandles;
    [ObservableProperty] private double _pagesInputPerSec;
    [ObservableProperty] private long _committedBytes;
    [ObservableProperty] private long _commitLimit;
    [ObservableProperty] private long _poolNonpagedBytes;
    [ObservableProperty] private double _dpcTimePercent;
    [ObservableProperty] private double _interruptTimePercent;
    [ObservableProperty] private double _contextSwitchesPerSec;
    [ObservableProperty] private int _processorQueueLength;
    [ObservableProperty] private double _avgDiskSecRead;
    [ObservableProperty] private double _avgDiskSecWrite;
    [ObservableProperty] private double _cpuClockMHz;
    [ObservableProperty] private double _cpuMaxClockMHz;
    [ObservableProperty] private double _dnsLatencyMs;
    [ObservableProperty] private int _storageErrorsLast15Min;

    // ── Worst disk ──
    [ObservableProperty] private string _worstDiskName = "-";
    [ObservableProperty] private double _worstDiskReadLatency;
    [ObservableProperty] private double _worstDiskWriteLatency;
    [ObservableProperty] private double _worstDiskQueue;
    [ObservableProperty] private double _worstDiskIops;
    [ObservableProperty] private double _worstDiskIdlePercent = 100;
    [ObservableProperty] private string _worstDiskReadThroughput = "0 B/s";
    [ObservableProperty] private string _worstDiskWriteThroughput = "0 B/s";

    // ── Hanging ──
    [ObservableProperty] private string _hangingStatus = "None";
    [ObservableProperty] private bool _hasHanging;

    // ── Timer ──
    [ObservableProperty] private string _elapsed = "0:00";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private bool _isRunning;

    // ── Collections ──
    public ObservableCollection<ProcessDisplayItem> TopCpuProcesses { get; } = [];
    public ObservableCollection<ProcessDisplayItem> TopMemoryProcesses { get; } = [];
    public ObservableCollection<ProcessDisplayItem> TopIoProcesses { get; } = [];
    public ObservableCollection<EventDisplayItem> Events { get; } = [];

    // ── Chart history ──
    public ObservableCollection<double> CpuHistory { get; } = [];
    public ObservableCollection<double> MemHistory { get; } = [];
    public ObservableCollection<double> DiskQHistory { get; } = [];
    public ObservableCollection<double> NetHistory { get; } = [];

    // ── Chart series (kept for runtime binding) ──
    private readonly ISeries[] _cpuSeries;
    private readonly ISeries[] _memSeries;
    private readonly ISeries[] _diskQSeries;
    private readonly ISeries[] _netSeries;

    // ── Pre-built CartesianChart instances exposed as object to avoid XAML-compiler issues ──
    public object CpuChartView   { get; }
    public object MemChartView   { get; }
    public object DiskQChartView { get; }
    public object NetChartView   { get; }

    public int DurationMinutes => _durationMinutes;

    public MonitorViewModel(MainViewModel main, int durationMinutes)
    {
        _main = main;
        _durationMinutes = durationMinutes;
        _startTime = DateTime.Now;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += OnTimerTick;

        // Initialise series
        _cpuSeries   = [MakeSeries(CpuHistory,   "#4DA6F0")];
        _memSeries   = [MakeSeries(MemHistory,   "#F0904D")];
        _diskQSeries = [MakeSeries(DiskQHistory, "#E05C5C")];
        _netSeries   = [MakeSeries(NetHistory,   "#4DC66E")];

        // Build CartesianChart instances once; exposed as object so the WPF XAML compiler
        // doesn't need to resolve LiveChartsCore types from code-behind files.
        CpuChartView   = MakeChart(_cpuSeries,   [MakeAxis()], [MakeAxis(0, 100)]);
        MemChartView   = MakeChart(_memSeries,   [MakeAxis()], [MakeAxis(0, 100)]);
        DiskQChartView = MakeChart(_diskQSeries, [MakeAxis()], [MakeAxis(0)]);
        NetChartView   = MakeChart(_netSeries,   [MakeAxis()], [MakeAxis(0)]);
    }

    public void Start()
    {
        IsRunning = true;
        // Take baseline on background thread, then start timer
        Task.Run(() => _engine.CollectAndAnalyze()).ContinueWith(_ =>
        {
            _baselineTaken = true;
            System.Windows.Application.Current.Dispatcher.Invoke(() => _timer.Start());
        });
    }

    [RelayCommand]
    public void Stop()
    {
        _timer.Stop();
        IsRunning = false;
        _main.NavigateToStartup();
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (!_baselineTaken) return;

        // Check time limit
        var elapsed = DateTime.Now - _startTime;
        if (elapsed.TotalMinutes >= _durationMinutes)
        {
            Stop();
            return;
        }

        // Update timer display
        Elapsed = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
        ProgressPercent = elapsed.TotalMinutes / _durationMinutes * 100;

        try
        {
            var sample = await Task.Run(() => _engine.CollectAndAnalyze());
            UpdateFromSample(sample);
        }
        catch
        {
            // WMI failure — skip this tick
        }
    }

    private void UpdateFromSample(MonitorSample sample)
    {
        // Top gauges
        CpuPercent = sample.CpuPercent;
        MemoryUsedPercent = sample.MemoryUsedPercent;
        DiskQueueLength = sample.DiskQueueLength;
        GpuUtilizationPercent = sample.GpuUtilizationPercent;
        NetworkMbps = sample.NetworkMbps;

        // Health scores
        MemoryPressureIndex = sample.MemoryPressureIndex;
        SystemLatencyScore = sample.SystemLatencyScore;
        MemoryPressureLabel = sample.MemoryPressureIndex switch
        {
            > 75 => "Critical",
            > 50 => "High",
            > 25 => "Pressure",
            _ => "Healthy"
        };
        SystemLatencyLabel = sample.SystemLatencyScore switch
        {
            > 75 => "Critical",
            > 50 => "High",
            > 25 => "Some latency",
            _ => "Responsive"
        };

        // System details
        TotalSystemHandles = sample.TotalSystemHandles;
        PagesInputPerSec = sample.PagesInputPerSec;
        CommittedBytes = sample.CommittedBytes;
        CommitLimit = sample.CommitLimit;
        PoolNonpagedBytes = sample.PoolNonpagedBytes;
        DpcTimePercent = sample.DpcTimePercent;
        InterruptTimePercent = sample.InterruptTimePercent;
        ContextSwitchesPerSec = sample.ContextSwitchesPerSec;
        ProcessorQueueLength = sample.ProcessorQueueLength;
        AvgDiskSecRead = sample.AvgDiskSecRead;
        AvgDiskSecWrite = sample.AvgDiskSecWrite;
        CpuClockMHz = sample.CpuClockMHz;
        CpuMaxClockMHz = sample.CpuMaxClockMHz;
        DnsLatencyMs = sample.DnsLatencyMs;
        StorageErrorsLast15Min = sample.StorageErrorsLast15Min;

        // Worst disk
        var worst = sample.DiskInstances
            .OrderByDescending(DiskHealthAnalyzer.DiskSeverityScore)
            .FirstOrDefault();
        if (worst is not null)
        {
            WorstDiskName = worst.Name;
            WorstDiskReadLatency = worst.ReadLatencyMs;
            WorstDiskWriteLatency = worst.WriteLatencyMs;
            WorstDiskQueue = worst.QueueLength;
            WorstDiskIops = worst.ReadIops + worst.WriteIops;
            WorstDiskIdlePercent = worst.IdlePercent;
            WorstDiskReadThroughput = FormatHelper.FormatBytes((long)worst.ReadBytesPerSec) + "/s";
            WorstDiskWriteThroughput = FormatHelper.FormatBytes((long)worst.WriteBytesPerSec) + "/s";
        }

        // Top processes
        UpdateCollection(TopCpuProcesses, sample.TopCpuProcesses.Take(5)
            .Select(p => new ProcessDisplayItem(p.Name, $"{p.CpuPercent:F0}%", "CPU")));
        UpdateCollection(TopMemoryProcesses, sample.TopMemoryProcesses.Take(5)
            .Select(p => new ProcessDisplayItem(p.Name, FormatHelper.FormatBytes(p.MemoryBytes), "RAM")));
        UpdateCollection(TopIoProcesses, sample.TopIoProcesses.Take(3)
            .Select(p => new ProcessDisplayItem(p.Name, FormatHelper.FormatBytes((long)p.TotalBytes), "I/O")));

        // Events (deduplicated view)
        UpdateEvents(_engine.AllEvents);

        // Hanging
        if (sample.HangingProcesses.Count > 0)
        {
            HasHanging = true;
            var h = sample.HangingProcesses[0];
            HangingStatus = $"{h.Name} ({h.HangSeconds:F0}s)";
        }
        else
        {
            HasHanging = false;
            HangingStatus = "None";
        }

        // Chart history (cap at 60 points = 3 min)
        AddToHistory(CpuHistory, sample.CpuPercent, 60);
        AddToHistory(MemHistory, sample.MemoryUsedPercent, 60);
        AddToHistory(DiskQHistory, sample.DiskQueueLength, 60);
        AddToHistory(NetHistory, sample.NetworkMbps, 60);
    }

    private void UpdateEvents(List<MonitorEvent> allEvents)
    {
        // Show most recent unique events (dedup by EventType)
        var seen = new HashSet<string>();
        var unique = new List<EventDisplayItem>();
        foreach (var evt in allEvents.AsEnumerable().Reverse())
        {
            if (seen.Add(evt.EventType))
            {
                unique.Add(new EventDisplayItem(
                    evt.Timestamp.ToString("HH:mm:ss"),
                    evt.Severity,
                    evt.Description,
                    evt.Tip));
            }
            if (unique.Count >= 10) break;
        }
        unique.Reverse();

        Events.Clear();
        foreach (var item in unique)
            Events.Add(item);
    }

    private static void UpdateCollection(ObservableCollection<ProcessDisplayItem> collection,
        IEnumerable<ProcessDisplayItem> items)
    {
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
    }

    private static void AddToHistory(ObservableCollection<double> history, double value, int max)
    {
        history.Add(value);
        while (history.Count > max)
            history.RemoveAt(0);
    }

    // ── Chart helpers ──────────────────────────────────────────────────
    private static LineSeries<double> MakeSeries(ObservableCollection<double> values, string hex)
    {
        var color = SKColor.Parse(hex);
        return new LineSeries<double>
        {
            Values         = values,
            Stroke         = new SolidColorPaint(color, 2),
            Fill           = new SolidColorPaint(color.WithAlpha(0x28)),
            GeometrySize   = 0,
            LineSmoothness = 0.4,
        };
    }

    private static Axis MakeAxis(double? min = null, double? max = null) => new Axis
    {
        MinLimit        = min,
        MaxLimit        = max,
        LabelsPaint     = null,
        SeparatorsPaint = null,
        TicksPaint      = null,
    };

    private static CartesianChart MakeChart(ISeries[] series, Axis[] xAxes, Axis[] yAxes) =>
        new CartesianChart
        {
            Series          = series,
            XAxes           = xAxes,
            YAxes           = yAxes,
            AnimationsSpeed = TimeSpan.Zero,
            Background      = Brushes.Transparent,
        };
}

public record ProcessDisplayItem(string Name, string Value, string Category);
public record EventDisplayItem(string Time, string Severity, string Description, string Tip);
