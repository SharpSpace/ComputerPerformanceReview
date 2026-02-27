namespace ComputerPerformanceReview.Analyzers;

/// <summary>
/// Analyzes active power plan in snapshot mode
/// </summary>
public sealed class PowerPlanAnalyzer : IAnalyzer
{
    public string Name => "Power Plan";

    public Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        string? activePowerPlan = GetActivePowerPlan();
        bool isLaptop = IsLaptop();
        string deviceType = isLaptop ? "Laptop" : "Desktop";

        if (string.IsNullOrEmpty(activePowerPlan))
        {
            results.Add(new AnalysisResult(
                "Power",
                "Power Plan",
                "Could not read active power plan",
                Severity.Warning,
                "Run the program as administrator for full analysis"));

            return Task.FromResult(new AnalysisReport("POWER PLAN", results));
        }

        var powerPlanLower = activePowerPlan.ToLowerInvariant();

        // Check for power saver mode
        bool isPowerSaver = powerPlanLower.Contains("power saver") ||
                           powerPlanLower.Contains("energispar") ||
                           powerPlanLower.Contains("batterispar");

        bool isHighPerformance = powerPlanLower.Contains("high performance") ||
                                powerPlanLower.Contains("hög prestanda") ||
                                powerPlanLower.Contains("ultimate");

        bool isBalanced = powerPlanLower.Contains("balanced") ||
                         powerPlanLower.Contains("balanserad");

        results.Add(new AnalysisResult(
            "Power",
            "Device type",
            deviceType,
            Severity.Ok));

        if (isPowerSaver && !isLaptop)
        {
            results.Add(new AnalysisResult(
                "Power",
                "Active power plan",
                $"'{activePowerPlan}' — NOT RECOMMENDED for desktop",
                Severity.Critical,
                "Power Saver mode on a desktop unnecessarily limits CPU performance. " +
                "Switch to 'Balanced' or 'High Performance' for better responsiveness.",
                new List<ActionStep>
                {
                    new("Search for 'Power Plan' in the Start menu", null, "Easy"),
                    new("Select 'Balanced' or 'High Performance'", null, "Easy"),
                    new("Alternative: Run 'powercfg.exe /setactive SCHEME_BALANCED' as admin",
                        "powercfg.exe /setactive SCHEME_BALANCED", "Medium")
                }));
        }
        else if (isPowerSaver && isLaptop)
        {
            results.Add(new AnalysisResult(
                "Power",
                "Active power plan",
                $"'{activePowerPlan}' — Power Saver may reduce performance",
                Severity.Warning,
                "Power Saver mode can limit performance even on AC power. " +
                "Consider 'Balanced' for better performance when the charger is connected."));
        }
        else if (isHighPerformance)
        {
            results.Add(new AnalysisResult(
                "Power",
                "Active power plan",
                $"'{activePowerPlan}' — Maximum performance",
                Severity.Ok,
                isLaptop
                    ? "High Performance gives maximum responsiveness but drains the battery faster."
                    : null));
        }
        else if (isBalanced)
        {
            results.Add(new AnalysisResult(
                "Power",
                "Active power plan",
                $"'{activePowerPlan}' — Recommended setting",
                Severity.Ok));
        }
        else
        {
            results.Add(new AnalysisResult(
                "Power",
                "Active power plan",
                $"'{activePowerPlan}'",
                Severity.Ok));
        }

        // Additional recommendations
        if (!isHighPerformance && !isLaptop)
        {
            results.Add(new AnalysisResult(
                "Power",
                "Performance tip",
                "For maximum performance on a desktop, consider 'High Performance'",
                Severity.Ok,
                "This prevents CPU throttling and can give better responsiveness in demanding applications."));
        }

        return Task.FromResult(new AnalysisReport("POWER PLAN", results));
    }

    private static string? GetActivePowerPlan()
    {
        try
        {
            // Try WMI first
            var powerPlans = WmiHelper.Query(
                "SELECT ElementName, IsActive FROM Win32_PowerPlan WHERE IsActive = true", 
                "root\\cimv2\\power");
            
            if (powerPlans.Count > 0)
            {
                return WmiHelper.GetValue<string>(powerPlans[0], "ElementName");
            }
        }
        catch { }

        // Fallback to powercfg command
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = "/GETACTIVESCHEME",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Parse output like: "Power Scheme GUID: ... (Balanced)"
                var match = System.Text.RegularExpressions.Regex.Match(
                    output, @"\(([^)]+)\)\s*$");
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
            }
        }
        catch { }

        return null;
    }

    private static bool IsLaptop()
    {
        try
        {
            // Check for battery presence
            var batteries = WmiHelper.Query("SELECT * FROM Win32_Battery");
            return batteries.Count > 0;
        }
        catch
        {
            return false;
        }
    }
}
