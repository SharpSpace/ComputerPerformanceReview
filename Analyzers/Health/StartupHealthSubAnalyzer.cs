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
                $"Many startup programs: {_cachedStartupCount} programs start automatically at login",
                "Critical",
                $"Too many programs start automatically, which lengthens boot time and strains the system. " +
                $"ACTIONS: " +
                $"1) Open Task Manager: Ctrl+Shift+Esc → 'Startup' tab. " +
                $"2) Right-click programs you don't need at startup → Select 'Disable'. " +
                $"3) Focus on: Update services (Adobe, Java), launcher software (Steam, Origin, Discord), cloud sync (OneDrive, Dropbox). " +
                $"4) Keep only critical programs: Antivirus, peripheral drivers. " +
                $"5) Programs can still be started manually when you need them."));
        }
        else if (_cachedStartupCount > 15)
        {
            healthScore -= 10;
            events.Add(new MonitorEvent(
                DateTime.Now,
                "ModerateStartupCount",
                $"Several startup programs: {_cachedStartupCount} programs start automatically",
                "Warning",
                $"Several programs start automatically. Consider disabling unused ones. " +
                $"TIPS: Open Task Manager (Ctrl+Shift+Esc) → Startup tab → Disable programs you don't need at startup."));
        }

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = _cachedStartupCount > 0 ? 1.0 : 0.0;

        string? hint = healthScore < 85
            ? $"{_cachedStartupCount} startup programs may delay boot"
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
