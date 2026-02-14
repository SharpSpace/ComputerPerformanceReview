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
                    ? $" Webbläsare: {string.Join(", ", browserProcs.Select(p => $"{p.Name} ({ConsoleHelper.FormatBytes(p.MemoryBytes)})"))}."
                    : "";

                events.Add(new MonitorEvent(
                    DateTime.Now,
                    "HighBrowserUsage",
                    $"Många webbläsarprocesser: {current.BrowserProcessCount} processer använder totalt {ConsoleHelper.FormatBytes(current.BrowserMemoryBytes)}{procHint}",
                    isCritical ? "Critical" : "Warning",
                    $"Många öppna webbläsarflikar eller extensioner belastar systemet. ÅTGÄRDER: " +
                    $"1) Stäng oanvända flikar: Ctrl+W för att stänga aktuell flik. " +
                    $"2) Använd 'Tab Suspender' eller 'The Great Suspender' extension för att automatiskt avlasta inaktiva flikar. " +
                    $"3) Inaktivera tunga extensioner: chrome://extensions eller edge://extensions → Stäng av oanvända. " +
                    $"4) Använd 'Shift+Esc' i Chrome/Edge för att se Aktivitetshanteraren och identifiera tunga flikar. " +
                    $"5) Överväg att använda flera webbläsarprofiler för att separera arbete/privat."));
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
                    $"Webbläsare använder mycket minne: {ConsoleHelper.FormatBytes(current.BrowserMemoryBytes)} över {current.BrowserProcessCount} processer",
                    "Warning",
                    $"Webbläsaren använder ovanligt mycket minne. TIPS: " +
                    $"1) Tryck Shift+Esc i Chrome/Edge för att se vilka flikar som använder mest minne. " +
                    $"2) Stäng tunga flikar eller ladda om dem för att frigöra minne. " +
                    $"3) Rensa webbläsarcache: Inställningar → Sekretess → Rensa webbläsardata. " +
                    $"4) Inaktivera hårdvaruacceleration om problem kvarstår: Inställningar → System → Stäng av 'Använd maskinvaruacceleration'."));
            }
        }

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = history.Count >= 2 ? 1.0 : history.Count / 2.0;

        string? hint = healthScore < 80
            ? "Webbläsaren använder mycket resurser"
            : null;

        return new HealthAssessment(
            new HealthScore(Domain, healthScore, confidence, hint),
            events);
    }
}
