using Microsoft.Win32;

namespace ComputerPerformanceReview.Analyzers.Health;

/// <summary>
/// Analyzes startup programs and background apps during monitoring
/// </summary>
public sealed class StartupHealthSubAnalyzer : IHealthSubAnalyzer
{
    public string Domain => "Startup";

    private const int CooldownSeconds = 600; // Only check every 10 minutes
    private DateTime _lastStartupCheck = DateTime.MinValue;
    private int _cachedStartupCount;

    // Known bloatware patterns
    private static readonly string[] BloatwarePatterns = 
    {
        "mcafee", "norton", "avast", "avg", "ccleaner", "utorrent",
        "java update", "adobe updater", "quicktime", "realtimes", 
        "spotify web helper", "skype", "itunes helper", "steam", 
        "origin", "epic games", "discord", "teams", "onedrive"
    };

    public void Collect(MonitorSampleBuilder builder)
    {
        // Only collect startup info periodically to avoid overhead
        if ((DateTime.Now - _lastStartupCheck).TotalSeconds < CooldownSeconds && _cachedStartupCount > 0)
        {
            return;
        }

        try
        {
            var startupItems = new List<string>();

            ReadRegistryStartup(startupItems, Registry.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            ReadRegistryStartup(startupItems, Registry.CurrentUser,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");

            _cachedStartupCount = startupItems.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            _lastStartupCheck = DateTime.Now;
        }
        catch { }
    }

    public HealthAssessment Analyze(MonitorSample current, IReadOnlyList<MonitorSample> history)
    {
        var events = new List<MonitorEvent>();
        int healthScore = 100;

        if (_cachedStartupCount == 0)
        {
            return new HealthAssessment(
                new HealthScore(Domain, healthScore, 0, null),
                events);
        }

        // Warn about high startup program count
        if (_cachedStartupCount > 25)
        {
            healthScore -= 20;
            events.Add(new MonitorEvent(
                DateTime.Now,
                "HighStartupCount",
                $"Många startprogram: {_cachedStartupCount} program startar automatiskt vid inloggning",
                "Critical",
                $"För många program startar automatiskt, vilket förlänger uppstartstiden och belastar systemet. " +
                $"ÅTGÄRDER: " +
                $"1) Öppna Aktivitetshanteraren: Ctrl+Shift+Esc → Fliken 'Autostart'. " +
                $"2) Högerklicka på program du inte behöver vid start → Välj 'Inaktivera'. " +
                $"3) Fokusera på: Uppdateringstjänster (Adobe, Java), launcherprogramvara (Steam, Origin, Discord), moln-sync (OneDrive, Dropbox). " +
                $"4) Behåll bara kritiska program: Antivirusprogram, drivrutiner för kringutrustning. " +
                $"5) Program kan fortfarande startas manuellt när du behöver dem."));
        }
        else if (_cachedStartupCount > 15)
        {
            healthScore -= 10;
            events.Add(new MonitorEvent(
                DateTime.Now,
                "ModerateStartupCount",
                $"Flera startprogram: {_cachedStartupCount} program startar automatiskt",
                "Warning",
                $"Flera program startar automatiskt. Överväg att inaktivera oanvända. " +
                $"TIPS: Öppna Aktivitetshanteraren (Ctrl+Shift+Esc) → Autostart-fliken → Inaktivera program du inte behöver direkt vid start."));
        }

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = _cachedStartupCount > 0 ? 1.0 : 0.0;

        string? hint = healthScore < 85
            ? $"{_cachedStartupCount} startprogram kan fördröja uppstart"
            : null;

        return new HealthAssessment(
            new HealthScore(Domain, healthScore, confidence, hint),
            events);
    }

    private static void ReadRegistryStartup(List<string> items, RegistryKey root, string path)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key is null) return;

            foreach (var name in key.GetValueNames())
            {
                items.Add(name);
            }
        }
        catch { }
    }
}
