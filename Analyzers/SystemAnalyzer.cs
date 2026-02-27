using Microsoft.Win32;

namespace ComputerPerformanceReview.Analyzers;

public sealed class SystemAnalyzer : IAnalyzer
{
    public string Name => "System Checks";

    public Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        AnalyzeUptime(results);
        AnalyzePageFile(results);
        AnalyzePageFileLocation(results);
        CheckPendingRestart(results);
        CheckLastUpdate(results);
        CheckAdminStatus(results);

        return Task.FromResult(new AnalysisReport("SYSTEM CHECKS", results));
    }

    private static void AnalyzePageFileLocation(List<AnalysisResult> results)
    {
        try
        {
            var settings = WmiHelper.Query("SELECT Name FROM Win32_PageFileSetting");
            if (settings.Count == 0)
                return;

            var locations = settings
                .Select(s => WmiHelper.GetValue<string>(s, "Name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (locations.Count == 0)
                return;

            results.Add(new AnalysisResult(
                "System",
                "Page file location",
                $"Pagefile: {string.Join(", ", locations)}",
                Severity.Ok,
                "If the page file is on an HDD it may cause sluggishness under memory pressure."));
        }
        catch { }
    }

    private static void AnalyzeUptime(List<AnalysisResult> results)
    {
        try
        {
            var osData = WmiHelper.Query("SELECT LastBootUpTime FROM Win32_OperatingSystem");
            if (osData.Count == 0) return;

            var lastBootStr = WmiHelper.GetValue<string>(osData[0], "LastBootUpTime");
            if (lastBootStr is null) return;

            var lastBoot = ManagementDateTimeConverter.ToDateTime(lastBootStr);
            var uptime = DateTime.Now - lastBoot;

            string uptimeStr = uptime.Days > 0
                ? $"{uptime.Days} days, {uptime.Hours} hours"
                : $"{uptime.Hours} hours, {uptime.Minutes} minutes";

            var severity = uptime.TotalDays switch
            {
                > 30 => Severity.Critical,
                > 7 => Severity.Warning,
                _ => Severity.Ok
            };

            results.Add(new AnalysisResult("System", "Uptime",
                $"Computer has been running for {uptimeStr} since {lastBoot:yyyy-MM-dd HH:mm}",
                severity,
                severity != Severity.Ok
                    ? "Restart the computer regularly to free resources and install updates"
                    : null));
        }
        catch { }
    }

    private static void AnalyzePageFile(List<AnalysisResult> results)
    {
        try
        {
            var pfData = WmiHelper.Query("SELECT AllocatedBaseSize, CurrentUsage FROM Win32_PageFileUsage");
            if (pfData.Count == 0) return;

            foreach (var pf in pfData)
            {
                var allocated = WmiHelper.GetValue<long>(pf, "AllocatedBaseSize"); // MB
                var current = WmiHelper.GetValue<long>(pf, "CurrentUsage"); // MB

                if (allocated <= 0) continue;

                double usedPercent = (double)current / allocated * 100;

                var severity = usedPercent switch
                {
                    > 90 => Severity.Critical,
                    > 70 => Severity.Warning,
                    _ => Severity.Ok
                };

                results.Add(new AnalysisResult("System", "Page file",
                    $"Page file usage: {current} MB / {allocated} MB ({usedPercent:F1}%)",
                    severity,
                    severity != Severity.Ok
                        ? "High page file usage indicates memory shortage - close programs or add more RAM"
                        : null));
            }
        }
        catch { }
    }

    private static void CheckPendingRestart(List<AnalysisResult> results)
    {
        try
        {
            bool rebootPending =
                Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") is not null ||
                Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") is not null;

            if (rebootPending)
            {
                results.Add(new AnalysisResult("System", "Pending restart",
                    "A restart is required to complete updates",
                    Severity.Critical,
                    "Restart the computer to complete pending updates",
                    [
                        new ActionStep(
                            "Schedule a restart in 60 seconds (runs in cmd)",
                            "shutdown.exe /r /t 60",
                            "Easy"),
                        new ActionStep(
                            "Cancel a scheduled restart",
                            "shutdown.exe /a",
                            "Easy"),
                    ]));
            }
            else
            {
                results.Add(new AnalysisResult("System", "Pending restart",
                    "No restart required", Severity.Ok));
            }
        }
        catch { }
    }

    private static void CheckLastUpdate(List<AnalysisResult> results)
    {
        try
        {
            var updates = WmiHelper.Query(
                "SELECT HotFixID, InstalledOn FROM Win32_QuickFixEngineering");

            if (updates.Count == 0)
            {
                results.Add(new AnalysisResult("System", "Windows Update",
                    "Could not read update history", Severity.Warning));
                return;
            }

            DateTime? latestDate = null;
            foreach (var update in updates)
            {
                var dateStr = WmiHelper.GetValue<string>(update, "InstalledOn");
                if (dateStr is not null && DateTime.TryParse(dateStr, out var date))
                {
                    if (latestDate is null || date > latestDate)
                        latestDate = date;
                }
            }

            if (latestDate is not null)
            {
                var daysSince = (DateTime.Now - latestDate.Value).TotalDays;
                var severity = daysSince switch
                {
                    > 60 => Severity.Critical,
                    > 30 => Severity.Warning,
                    _ => Severity.Ok
                };

                results.Add(new AnalysisResult("System", "Latest Windows Update",
                    $"Latest update installed {latestDate:yyyy-MM-dd} ({daysSince:F0} days ago)",
                    severity,
                    severity != Severity.Ok ? "Search for and install Windows updates" : null,
                    severity != Severity.Ok
                        ?
                        [
                            new ActionStep(
                                "Open Windows Update settings",
                                "start ms-settings:windowsupdate",
                                "Easy"),
                        ]
                        : null));
            }
        }
        catch { }
    }

    private static void CheckAdminStatus(List<AnalysisResult> results)
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            bool isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

            if (!isAdmin)
            {
                results.Add(new AnalysisResult("System", "Permissions",
                    "Running without administrator privileges - some information may be limited",
                    Severity.Warning,
                    "Run as administrator for full analysis"));
            }
        }
        catch { }
    }
}
