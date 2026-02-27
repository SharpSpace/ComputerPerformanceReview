using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComputerPerformanceReview.Analyzers;
using ComputerPerformanceReview.Helpers;

namespace ComputerPerformanceReview.ViewModels;

public partial class SnapshotViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private bool _isRunning = true;
    [ObservableProperty] private string _statusText = "Starting snapshot analysis...";
    [ObservableProperty] private double _progressPercent;

    /// <summary>Short comparison summary shown in header after analysis — e.g. "2 new, 1 fixed".</summary>
    [ObservableProperty] private string _comparisonText = "";
    [ObservableProperty] private bool _hasComparison;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredGroups))]
    [NotifyPropertyChangedFor(nameof(FilterLabel))]
    private bool _showOnlyIssues;

    public ObservableCollection<SnapshotGroupItem> Groups { get; } = [];

    /// <summary>Groups filtered by the current ShowOnlyIssues toggle.</summary>
    public IEnumerable<SnapshotGroupItem> FilteredGroups =>
        ShowOnlyIssues
            ? Groups
                .Select(g => new SnapshotGroupItem(g.Title,
                    g.Items.Where(i => i.Severity != Severity.Ok).ToList()))
                .Where(g => g.Items.Count > 0)
            : Groups;

    public string FilterLabel => ShowOnlyIssues ? "Show All" : "Issues Only";

    public SnapshotViewModel(MainViewModel main)
    {
        _main = main;
        Groups.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FilteredGroups));
    }

    public async Task RunAsync()
    {
        // ── 1. Load history BEFORE analysis so we can tag new/fixed issues ──
        var history    = await LogHelper.LoadHistoryAsync();
        var prevRun    = history.Count > 0 ? history[^1] : null;

        // Build lookup: "Category|CheckName" → Severity string from previous run
        var prevSeverities = new Dictionary<string, string>(StringComparer.Ordinal);
        if (prevRun is not null)
        {
            foreach (var rpt in prevRun.Reports)
            foreach (var res in rpt.Results)
                prevSeverities[$"{res.Category}|{res.CheckName}"] = res.Severity;
        }

        // ── 2. Run analyzers ──
        IAnalyzer[] analyzers =
        [
            new SystemAnalyzer(),
            new CpuAnalyzer(),
            new MemoryAnalyzer(),
            new DiskAnalyzer(),
            new NetworkAnalyzer(),
            new PowerPlanAnalyzer(),
        ];

        int done = 0;
        Groups.Clear();
        var reports = new List<AnalysisReport>(); // kept for saving to disk

        foreach (var analyzer in analyzers)
        {
            StatusText      = $"Analyzing: {analyzer.Name}...";
            ProgressPercent = (double)done / analyzers.Length * 100;

            try
            {
                var report = await analyzer.AnalyzeAsync();
                reports.Add(report);

                var items = report.Results
                    .Select(r =>
                    {
                        var  key          = $"{r.Category}|{r.CheckName}";
                        bool prevWasIssue = prevSeverities.TryGetValue(key, out var prevSev)
                                            && prevSev is "Warning" or "Critical";
                        bool nowIsIssue   = r.Severity is Severity.Warning or Severity.Critical;

                        // Only mark NEW when there's actual history to compare against
                        bool isNew   = nowIsIssue  && !prevWasIssue && prevRun is not null;
                        bool isFixed = !nowIsIssue && prevWasIssue;

                        return new SnapshotResultItem(
                            r.CheckName,
                            r.Description,
                            r.Severity,
                            r.Recommendation,
                            (r.ActionSteps ?? [])
                                .Select(a => new SnapshotActionItem(a.Title, a.CommandHint, a.Difficulty))
                                .ToList(),
                            IsNew:   isNew,
                            IsFixed: isFixed);
                    })
                    .ToList();

                Groups.Add(new SnapshotGroupItem(report.Title, items));
            }
            catch { }

            done++;
        }

        ProgressPercent = 100;
        IsRunning       = false;

        // ── 3. Compute summary ──
        int totalChecks = Groups.Sum(g => g.Items.Count);
        int warnings    = Groups.Sum(g => g.Items.Count(i => i.Severity == Severity.Warning));
        int criticals   = Groups.Sum(g => g.Items.Count(i => i.Severity == Severity.Critical));
        int healthScore = Math.Max(0, 100 - criticals * 15 - warnings * 5);

        StatusText = criticals > 0
            ? $"{totalChecks} checks — {criticals} critical, {warnings} warnings"
            : warnings > 0
                ? $"{totalChecks} checks — {warnings} warnings"
                : $"{totalChecks} checks — no issues found";

        // ── 4. Build comparison text ──
        if (prevRun is not null)
        {
            int newCount   = Groups.Sum(g => g.Items.Count(i => i.IsNew));
            int fixedCount = Groups.Sum(g => g.Items.Count(i => i.IsFixed));

            var parts = new List<string>();
            if (newCount   > 0) parts.Add($"{newCount} new");
            if (fixedCount > 0) parts.Add($"{fixedCount} fixed");

            ComparisonText = parts.Count > 0
                ? $"vs. {prevRun.Timestamp:yyyy-MM-dd HH:mm}: {string.Join(", ", parts)}"
                : $"vs. {prevRun.Timestamp:yyyy-MM-dd HH:mm}: no changes";
            HasComparison = true;
        }

        // ── 5. Save this run to disk ──
        _ = LogHelper.SaveRunAsync(reports, healthScore);
    }

    [RelayCommand]
    private void ToggleFilter() => ShowOnlyIssues = !ShowOnlyIssues;

    [RelayCommand]
    private void Back()
    {
        _main.NavigateToStartup();
    }
}

public record SnapshotGroupItem(string Title, List<SnapshotResultItem> Items);

public record SnapshotResultItem(
    string CheckName,
    string Description,
    Severity Severity,
    string? Recommendation,
    IReadOnlyList<SnapshotActionItem> ActionSteps,
    bool IsNew   = false,
    bool IsFixed = false)
{
    public bool HasActionSteps => ActionSteps.Count > 0;
}

/// <summary>
/// A single actionable step (e.g. a command to run) surfaced in the Snapshot results UI.
/// </summary>
public sealed class SnapshotActionItem
{
    public string    Title       { get; }
    public string?   Command     { get; }
    public string?   Difficulty  { get; }
    public bool      HasCommand  => Command is not null;

    public RelayCommand CopyCommand { get; }
    public RelayCommand RunCommand  { get; }

    public SnapshotActionItem(string title, string? command, string? difficulty)
    {
        Title      = title;
        Command    = command;
        Difficulty = difficulty;

        CopyCommand = new RelayCommand(
            () => System.Windows.Clipboard.SetText(Command!),
            () => Command is not null);

        RunCommand = new RelayCommand(
            () =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName        = "cmd.exe",
                        Arguments       = $"/k {Command}",
                        UseShellExecute = true,
                    });
                }
                catch { }
            },
            () => Command is not null);
    }
}
