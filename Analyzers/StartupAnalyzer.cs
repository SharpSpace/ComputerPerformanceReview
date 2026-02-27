using Microsoft.Win32;

namespace ComputerPerformanceReview.Analyzers;

public sealed class StartupAnalyzer : IAnalyzer
{
    public string Name => "Startup Program Analysis";

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

        results.Add(new AnalysisResult("Startup programs", "Startup program count",
            $"{unique.Count} startup programs found",
            severity,
            severity != Severity.Ok
                ? "Reduce the number of startup programs via Task Manager (Ctrl+Shift+Esc → Startup)"
                : null,
            severity != Severity.Ok ? new List<ActionStep>
            {
                new("Open Task Manager: Ctrl+Shift+Esc", null, "Easy"),
                new("Go to the 'Startup' tab", null, "Easy"),
                new("Right-click programs you don't need → Disable", null, "Easy"),
                new("Focus on updaters, launchers and cloud sync", null, "Medium")
            } : null));

        // Check for potentially unnecessary startup items
        var unnecessary = unique
            .Where(item => BloatwarePatterns.CommonStartupBloatware.Any(pattern =>
                item.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (unnecessary.Count > 0)
        {
            results.Add(new AnalysisResult(
                "Startup programs",
                "Potentially unnecessary programs",
                $"{unnecessary.Count} programs that often don't need to start automatically",
                unnecessary.Count > 5 ? Severity.Warning : Severity.Ok,
                "These programs can usually be disabled from startup without issues. " +
                "You can start them manually when you need them."));

            foreach (var item in unnecessary.Take(10))
            {
                results.Add(new AnalysisResult("Startup programs", item.Name,
                    $"[Can be disabled] {item.Name} ({item.Location})",
                    Severity.Ok));
            }

            if (unnecessary.Count > 10)
            {
                results.Add(new AnalysisResult("Startup programs", "More",
                    $"... and {unnecessary.Count - 10} more to review", Severity.Ok));
            }
        }

        // List remaining essential startup items
        var essential = unique.Except(unnecessary).ToList();
        if (essential.Count > 0)
        {
            results.Add(new AnalysisResult(
                "Startup programs",
                "Other startup programs",
                $"{essential.Count} other startup programs (may include system-critical ones)",
                Severity.Ok));

            foreach (var item in essential.Take(10))
            {
                results.Add(new AnalysisResult("Startup programs", item.Name,
                    $"{item.Name} ({item.Location}): {TruncatePath(item.Command, 60)}",
                    Severity.Ok));
            }

            if (essential.Count > 10)
            {
                results.Add(new AnalysisResult("Startup programs", "More",
                    $"... and {essential.Count - 10} more", Severity.Ok));
            }
        }

        return Task.FromResult(new AnalysisReport("STARTUP PROGRAMS", results));
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
                var name = WmiHelper.GetValue<string>(entry, "Name") ?? "Unknown";
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
