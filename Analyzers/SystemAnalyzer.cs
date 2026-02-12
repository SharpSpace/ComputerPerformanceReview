using Microsoft.Win32;

namespace ComputerPerformanceReview.Analyzers;

public sealed class SystemAnalyzer : IAnalyzer
{
    public string Name => "Systemkontroller";

    public Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        AnalyzeUptime(results);
        AnalyzePageFile(results);
        AnalyzePageFileLocation(results);
        CheckPendingRestart(results);
        CheckLastUpdate(results);
        CheckAdminStatus(results);

        return Task.FromResult(new AnalysisReport("SYSTEMKONTROLLER", results));
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
                "Växlingsfil plats",
                $"Pagefile: {string.Join(", ", locations)}",
                Severity.Ok,
                "Om växlingsfilen ligger på HDD kan det orsaka tröghet vid minnestryck."));
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
                ? $"{uptime.Days} dagar, {uptime.Hours} timmar"
                : $"{uptime.Hours} timmar, {uptime.Minutes} minuter";

            var severity = uptime.TotalDays switch
            {
                > 30 => Severity.Critical,
                > 7 => Severity.Warning,
                _ => Severity.Ok
            };

            results.Add(new AnalysisResult("System", "Drifttid",
                $"Datorn har varit igång i {uptimeStr} sedan {lastBoot:yyyy-MM-dd HH:mm}",
                severity,
                severity != Severity.Ok
                    ? "Starta om datorn regelbundet för att frigöra resurser och installera uppdateringar"
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

                results.Add(new AnalysisResult("System", "Växlingsfil",
                    $"Växlingsfilanvändning: {current} MB / {allocated} MB ({usedPercent:F1}%)",
                    severity,
                    severity != Severity.Ok
                        ? "Hög växlingsfilanvändning indikerar minnesbrist - stäng program eller utöka RAM"
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
                results.Add(new AnalysisResult("System", "Väntande omstart",
                    "En omstart krävs för att slutföra uppdateringar",
                    Severity.Critical,
                    "Starta om datorn för att slutföra väntande uppdateringar"));
            }
            else
            {
                results.Add(new AnalysisResult("System", "Väntande omstart",
                    "Ingen omstart krävs", Severity.Ok));
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
                    "Kunde inte läsa uppdateringshistorik", Severity.Warning));
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

                results.Add(new AnalysisResult("System", "Senaste Windows Update",
                    $"Senaste uppdatering installerades {latestDate:yyyy-MM-dd} ({daysSince:F0} dagar sedan)",
                    severity,
                    severity != Severity.Ok ? "Sök efter och installera Windows-uppdateringar" : null));
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
                results.Add(new AnalysisResult("System", "Behörighet",
                    "Körs utan administratörsbehörighet - viss information kan vara begränsad",
                    Severity.Warning,
                    "Kör som administratör för fullständig analys"));
            }
        }
        catch { }
    }
}
