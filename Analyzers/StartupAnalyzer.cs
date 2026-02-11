using Microsoft.Win32;

namespace ComputerPerformanceReview.Analyzers;

public sealed class StartupAnalyzer : IAnalyzer
{
    public string Name => "Startprogramanalys";

    public Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();
        var startupItems = new List<(string Name, string Command, string Location)>();

        ReadRegistryStartup(startupItems, Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM");
        ReadRegistryStartup(startupItems, Registry.CurrentUser,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU");

        ReadWmiStartup(startupItems);

        // Deduplicate by name
        var unique = startupItems
            .DistinctBy(s => s.Name.ToLowerInvariant())
            .OrderBy(s => s.Name)
            .ToList();

        var severity = unique.Count switch
        {
            > 25 => Severity.Critical,
            > 15 => Severity.Warning,
            _ => Severity.Ok
        };

        results.Add(new AnalysisResult("Startprogram", "Antal startprogram",
            $"{unique.Count} startprogram hittades",
            severity,
            severity != Severity.Ok
                ? "Minska antalet startprogram via Aktivitetshanteraren (Ctrl+Shift+Esc → Autostart)"
                : null));

        foreach (var item in unique.Take(20))
        {
            results.Add(new AnalysisResult("Startprogram", item.Name,
                $"{item.Name} ({item.Location}): {TruncatePath(item.Command, 60)}",
                Severity.Ok));
        }

        if (unique.Count > 20)
        {
            results.Add(new AnalysisResult("Startprogram", "Fler",
                $"... och {unique.Count - 20} till", Severity.Ok));
        }

        return Task.FromResult(new AnalysisReport("STARTPROGRAM", results));
    }

    private static void ReadRegistryStartup(
        List<(string Name, string Command, string Location)> items,
        RegistryKey root, string path, string locationLabel)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key is null) return;

            foreach (var name in key.GetValueNames())
            {
                var value = key.GetValue(name)?.ToString() ?? "";
                items.Add((name, value, locationLabel));
            }
        }
        catch { }
    }

    private static void ReadWmiStartup(List<(string Name, string Command, string Location)> items)
    {
        try
        {
            var wmiData = WmiHelper.Query("SELECT Name, Command, Location FROM Win32_StartupCommand");
            foreach (var entry in wmiData)
            {
                var name = WmiHelper.GetValue<string>(entry, "Name") ?? "Okänd";
                var command = WmiHelper.GetValue<string>(entry, "Command") ?? "";
                var location = WmiHelper.GetValue<string>(entry, "Location") ?? "";
                items.Add((name, command, location));
            }
        }
        catch { }
    }

    private static string TruncatePath(string path, int maxLen)
    {
        return path.Length > maxLen ? path[..(maxLen - 3)] + "..." : path;
    }
}
