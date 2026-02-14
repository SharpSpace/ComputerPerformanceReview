namespace ComputerPerformanceReview.Analyzers;

/// <summary>
/// Analyzes installed programs and flags potential bloatware
/// </summary>
public sealed class InstalledProgramsAnalyzer : IAnalyzer
{
    public string Name => "Installerade program";

    // Known bloatware patterns
    private static readonly Dictionary<string, string> BloatwarePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        // OEM Bloatware
        ["HP Support Assistant"] = "HP bloatware - kan avinstalleras",
        ["HP Documentation"] = "HP bloatware - kan avinstalleras",
        ["HP JumpStart"] = "HP bloatware - kan avinstalleras",
        ["Lenovo Vantage"] = "Lenovo verktyg - kan avinstalleras om ej använt",
        ["Lenovo Welcome"] = "Lenovo bloatware - kan avinstalleras",
        ["Dell SupportAssist"] = "Dell verktyg - behålls endast om du använder support",
        ["Asus Giftbox"] = "Asus bloatware - kan avinstalleras",
        
        // Trial antivirus
        ["McAfee"] = "Trial-antivirus - avinstallera om du har annat skydd",
        ["Norton"] = "Trial-antivirus - avinstallera om du har annat skydd",
        ["AVG"] = "Antivirus - behåll endast ett antivirusprogram",
        ["Avast"] = "Antivirus - behåll endast ett antivirusprogram",
        
        // Unnecessary utilities
        ["CCleaner"] = "Diskrensnare - Windows inbyggda verktyg räcker ofta",
        ["PC Cleaner"] = "Potentiell skräpprogram",
        ["Driver Booster"] = "Driver-uppdaterare - ofta onödig",
        ["WinZip"] = "Arkiverare - Windows har inbyggt stöd",
        ["WinRAR"] = "Arkiverare - 7-Zip är gratis alternativ",
        
        // Launchers (optional - not bloat per se)
        ["Steam"] = "Spellauncher - kan inaktiveras från autostart",
        ["Epic Games Launcher"] = "Spellauncher - kan inaktiveras från autostart",
        ["Origin"] = "Spellauncher - kan inaktiveras från autostart",
        ["Battle.net"] = "Spellauncher - kan inaktiveras från autostart",
        
        // Updaters
        ["Adobe Updater"] = "Updater - kan inaktiveras från autostart",
        ["Java Update"] = "Updater - kan inaktiveras från autostart",
        ["QuickTime"] = "Media player - sällan nödvändig idag"
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
            "Program",
            "Totalt antal program",
            $"{unique.Count} installerade program hittades",
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
                "Program",
                "Flera antivirusprogram",
                $"Flera antivirusprogram upptäckta: {string.Join(", ", antivirusPrograms)}",
                Severity.Critical,
                "Att ha flera antivirusprogram samtidigt kan ge konflikter och sänkt prestanda. " +
                "Behåll endast ett (eller använd Windows Defender). " +
                "Avinstallera via: Inställningar → Appar → Installerade appar → Sök efter programmet → Avinstallera.",
                new List<ActionStep>
                {
                    new("Öppna Inställningar → Appar → Installerade appar"),
                    new("Sök efter antivirusprogrammet", null, "Lätt"),
                    new("Klicka på de tre punkterna → Avinstallera", null, "Lätt"),
                    new("Följ avinstallationsguiden", null, "Lätt")
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
                "Program",
                "Potentiell bloatware",
                $"{bloatwareFound.Count} program som kan vara onödiga",
                severity,
                severity != Severity.Ok
                    ? "Dessa program kan ofta avinstalleras för att frigöra diskutrymme och systemresurser."
                    : null));

            // List first 10 bloatware items
            foreach (var (name, reason) in bloatwareFound.Take(10))
            {
                results.Add(new AnalysisResult(
                    "Program",
                    name,
                    reason,
                    Severity.Ok));
            }

            if (bloatwareFound.Count > 10)
            {
                results.Add(new AnalysisResult(
                    "Program",
                    "Fler program",
                    $"... och {bloatwareFound.Count - 10} till",
                    Severity.Ok));
            }
        }
        else
        {
            results.Add(new AnalysisResult(
                "Program",
                "Bloatware-kontroll",
                "Inga uppenbara bloatware-program hittades",
                Severity.Ok));
        }

        return new AnalysisReport("INSTALLERADE PROGRAM", results);
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
