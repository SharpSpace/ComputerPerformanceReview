namespace ComputerPerformanceReview.Analyzers.Health;

/// <summary>
/// Analyzes browser process count and memory usage, providing tips about tabs and extensions
/// </summary>
public sealed class BrowserHealthSubAnalyzer : IHealthSubAnalyzer
{
    public string Domain => "Browser";

    // Thresholds
    private const int HighBrowserProcessCount = 15;
    private const long HighBrowserMemoryBytes = 4L * 1024 * 1024 * 1024; // 4 GB
    private const int CooldownSeconds = 120;

    // Known browser process names
    private static readonly string[] BrowserProcessNames = 
    {
        "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "iexplore"
    };

    // State
    private DateTime _lastBrowserWarning = DateTime.MinValue;

    public void Collect(MonitorSampleBuilder builder)
    {
        try
        {
            var processes = Process.GetProcesses();
            int browserCount = 0;
            long browserMemory = 0;

            foreach (var proc in processes)
            {
                try
                {
                    var processNameLower = proc.ProcessName.ToLowerInvariant();
                    bool isBrowser = BrowserProcessNames.Any(b => processNameLower.Contains(b));

                    if (isBrowser)
                    {
                        browserCount++;
                        browserMemory += proc.WorkingSet64;
                    }
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }

            builder.BrowserProcessCount = browserCount;
            builder.BrowserMemoryBytes = browserMemory;
        }
        catch { }
    }

    public HealthAssessment Analyze(MonitorSample current, IReadOnlyList<MonitorSample> history)
    {
        var events = new List<MonitorEvent>();
        int healthScore = 100;

        // Check for high browser process count
        if (current.BrowserProcessCount >= HighBrowserProcessCount)
        {
            if ((DateTime.Now - _lastBrowserWarning).TotalSeconds > CooldownSeconds)
            {
                _lastBrowserWarning = DateTime.Now;
                
                bool isCritical = current.BrowserProcessCount > 25 || 
                                  current.BrowserMemoryBytes > HighBrowserMemoryBytes * 1.5;
                
                healthScore -= isCritical ? 20 : 10;

                var browserProcs = current.TopMemoryProcesses
                    .Where(p => BrowserProcessNames.Any(b => 
                        p.Name.ToLowerInvariant().Contains(b)))
                    .Take(3)
                    .ToList();

                string procHint = browserProcs.Count > 0
                    ? $" Browsers: {string.Join(", ", browserProcs.Select(p => $"{p.Name} ({ConsoleHelper.FormatBytes(p.MemoryBytes)})"))}."
                    : "";

                events.Add(new MonitorEvent(
                    DateTime.Now,
                    "HighBrowserUsage",
                    $"Many browser processes: {current.BrowserProcessCount} processes using {ConsoleHelper.FormatBytes(current.BrowserMemoryBytes)} total{procHint}",
                    isCritical ? "Critical" : "Warning",
                    $"Many open browser tabs or extensions are straining the system. ACTIONS: " +
                    $"1) Close unused tabs: Ctrl+W to close the current tab. " +
                    $"2) Use 'Tab Suspender' or 'The Great Suspender' extension to automatically suspend inactive tabs. " +
                    $"3) Disable heavy extensions: chrome://extensions or edge://extensions → Turn off unused ones. " +
                    $"4) Use 'Shift+Esc' in Chrome/Edge to open the Task Manager and identify heavy tabs. " +
                    $"5) Consider using multiple browser profiles to separate work/personal."));
            }
        }
        // Check for high browser memory even with fewer processes
        else if (current.BrowserMemoryBytes > HighBrowserMemoryBytes)
        {
            if ((DateTime.Now - _lastBrowserWarning).TotalSeconds > CooldownSeconds)
            {
                _lastBrowserWarning = DateTime.Now;
                healthScore -= 10;

                events.Add(new MonitorEvent(
                    DateTime.Now,
                    "HighBrowserMemory",
                    $"Browser using a lot of memory: {ConsoleHelper.FormatBytes(current.BrowserMemoryBytes)} across {current.BrowserProcessCount} processes",
                    "Warning",
                    $"Browser is using an unusually large amount of memory. TIPS: " +
                    $"1) Press Shift+Esc in Chrome/Edge to see which tabs use the most memory. " +
                    $"2) Close heavy tabs or reload them to free memory. " +
                    $"3) Clear browser cache: Settings → Privacy → Clear browsing data. " +
                    $"4) Disable hardware acceleration if the problem persists: Settings → System → Turn off 'Use hardware acceleration'."));
            }
        }

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = history.Count >= 2 ? 1.0 : history.Count / 2.0;

        string? hint = healthScore < 80
            ? "Browser is using a lot of resources"
            : null;

        return new HealthAssessment(
            new HealthScore(Domain, healthScore, confidence, hint),
            events);
    }
}
