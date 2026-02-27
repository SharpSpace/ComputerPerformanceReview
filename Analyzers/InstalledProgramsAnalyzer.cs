namespace ComputerPerformanceReview.Analyzers;

/// <summary>
/// Analyzes installed programs and flags potential bloatware
/// </summary>
public sealed class InstalledProgramsAnalyzer : IAnalyzer
{
    public string Name => "Installed Programs";

    // Known bloatware patterns
    private static readonly Dictionary<string, string> BloatwarePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        // OEM Bloatware
        ["HP Support Assistant"] = "HP bloatware - can be uninstalled",
        ["HP Documentation"] = "HP bloatware - can be uninstalled",
        ["HP JumpStart"] = "HP bloatware - can be uninstalled",
        ["Lenovo Vantage"] = "Lenovo tool - can be uninstalled if not used",
        ["Lenovo Welcome"] = "Lenovo bloatware - can be uninstalled",
        ["Dell SupportAssist"] = "Dell tool - keep only if you use support",
        ["Asus Giftbox"] = "Asus bloatware - can be uninstalled",

        // Trial antivirus
        ["McAfee"] = "Trial antivirus - uninstall if you have other protection",
        ["Norton"] = "Trial antivirus - uninstall if you have other protection",
        ["AVG"] = "Antivirus - keep only one antivirus program",
        ["Avast"] = "Antivirus - keep only one antivirus program",

        // Unnecessary utilities
        ["CCleaner"] = "Disk cleaner - Windows built-in tools are often sufficient",
        ["PC Cleaner"] = "Potentially unwanted program",
        ["Driver Booster"] = "Driver updater - often unnecessary",
        ["WinZip"] = "Archiver - Windows has built-in support",
        ["WinRAR"] = "Archiver - 7-Zip is a free alternative",

        // Launchers (optional - not bloat per se)
        ["Steam"] = "Game launcher - can be disabled from startup",
        ["Epic Games Launcher"] = "Game launcher - can be disabled from startup",
        ["Origin"] = "Game launcher - can be disabled from startup",
        ["Battle.net"] = "Game launcher - can be disabled from startup",

        // Updaters
        ["Adobe Updater"] = "Updater - can be disabled from startup",
        ["Java Update"] = "Updater - can be disabled from startup",
        ["QuickTime"] = "Media player - rarely needed today"
    };

    public async Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();
        var installedPrograms = new List<(string Name, string Publisher)>();

        // Query installed programs from registry
        ReadInstalledPrograms(installedPrograms);

        // Also try WMI
        await Task.Run(() => ReadInstalledProgramsWmi(installedPrograms));

        // Deduplicate
        var unique = installedPrograms
            .DistinctBy(p => p.Name.ToLowerInvariant())
            .OrderBy(p => p.Name)
            .ToList();

        results.Add(new AnalysisResult(
            "Programs",
            "Total program count",
            $"{unique.Count} installed programs found",
            Severity.Ok));

        // Check for bloatware
        var bloatwareFound = new List<(string Name, string Reason)>();
        var antivirusPrograms = new List<string>();

        foreach (var program in unique)
        {
            foreach (var pattern in BloatwarePatterns)
            {
                if (program.Name.Contains(pattern.Key, StringComparison.OrdinalIgnoreCase))
                {
                    bloatwareFound.Add((program.Name, pattern.Value));
                    
                    // Track antivirus programs separately
                    if (pattern.Value.Contains("antivirus", StringComparison.OrdinalIgnoreCase))
                    {
                        antivirusPrograms.Add(program.Name);
                    }
                    break;
                }
            }
        }

        // Warn about multiple antivirus
        if (antivirusPrograms.Count > 1)
        {
            results.Add(new AnalysisResult(
                "Programs",
                "Multiple antivirus programs",
                $"Multiple antivirus programs detected: {string.Join(", ", antivirusPrograms)}",
                Severity.Critical,
                "Having multiple antivirus programs simultaneously can cause conflicts and reduced performance. " +
                "Keep only one (or use Windows Defender). " +
                "Uninstall via: Settings → Apps → Installed apps → Search for the program → Uninstall.",
                new List<ActionStep>
                {
                    new("Open Settings → Apps → Installed apps"),
                    new("Search for the antivirus program", null, "Easy"),
                    new("Click the three dots → Uninstall", null, "Easy"),
                    new("Follow the uninstall wizard", null, "Easy")
                }));
        }

        // Report bloatware
        if (bloatwareFound.Count > 0)
        {
            var severity = bloatwareFound.Count switch
            {
                > 10 => Severity.Critical,
                > 5 => Severity.Warning,
                _ => Severity.Ok
            };

            results.Add(new AnalysisResult(
                "Programs",
                "Potential bloatware",
                $"{bloatwareFound.Count} programs that may be unnecessary",
                severity,
                severity != Severity.Ok
                    ? "These programs can often be uninstalled to free disk space and system resources."
                    : null));

            // List first 10 bloatware items
            foreach (var (name, reason) in bloatwareFound.Take(10))
            {
                results.Add(new AnalysisResult(
                    "Programs",
                    name,
                    reason,
                    Severity.Ok));
            }

            if (bloatwareFound.Count > 10)
            {
                results.Add(new AnalysisResult(
                    "Programs",
                    "More programs",
                    $"... and {bloatwareFound.Count - 10} more",
                    Severity.Ok));
            }
        }
        else
        {
            results.Add(new AnalysisResult(
                "Programs",
                "Bloatware check",
                "No obvious bloatware programs found",
                Severity.Ok));
        }

        return new AnalysisReport("INSTALLED PROGRAMS", results);
    }

    private static void ReadInstalledPrograms(List<(string Name, string Publisher)> programs)
    {
        ReadRegistryPrograms(programs, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        ReadRegistryPrograms(programs, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
    }

    private static void ReadRegistryPrograms(List<(string Name, string Publisher)> programs, string keyPath)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
            if (key == null) return;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    var displayName = subKey.GetValue("DisplayName")?.ToString();
                    var publisher = subKey.GetValue("Publisher")?.ToString() ?? "Unknown";
                    var systemComponent = subKey.GetValue("SystemComponent");

                    // Skip if no name or is system component
                    if (string.IsNullOrWhiteSpace(displayName) || 
                        (systemComponent is int component && component == 1))
                        continue;

                    programs.Add((displayName, publisher));
                }
                catch { }
            }
        }
        catch { }
    }

    private static void ReadInstalledProgramsWmi(List<(string Name, string Publisher)> programs)
    {
        try
        {
            var wmiData = WmiHelper.Query("SELECT Name, Vendor FROM Win32_Product");
            foreach (var entry in wmiData)
            {
                var name = WmiHelper.GetValue<string>(entry, "Name");
                var vendor = WmiHelper.GetValue<string>(entry, "Vendor") ?? "Unknown";
                
                if (!string.IsNullOrWhiteSpace(name))
                {
                    programs.Add((name, vendor));
                }
            }
        }
        catch { }
    }
}
