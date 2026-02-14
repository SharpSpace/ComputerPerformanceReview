namespace ComputerPerformanceReview.Analyzers;

/// <summary>
/// Analyzes active power plan in snapshot mode
/// </summary>
public sealed class PowerPlanAnalyzer : IAnalyzer
{
    public string Name => "Energischema";

    public Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        string? activePowerPlan = GetActivePowerPlan();
        bool isLaptop = IsLaptop();
        string deviceType = isLaptop ? "Bärbar dator" : "Stationär dator";

        if (string.IsNullOrEmpty(activePowerPlan))
        {
            results.Add(new AnalysisResult(
                "Energi",
                "Energischema",
                "Kunde inte läsa aktivt energischema",
                Severity.Warning,
                "Kör programmet som administratör för fullständig analys"));
            
            return Task.FromResult(new AnalysisReport("ENERGISCHEMA", results));
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
            "Energi",
            "Enhetstyp",
            deviceType,
            Severity.Ok));

        if (isPowerSaver && !isLaptop)
        {
            results.Add(new AnalysisResult(
                "Energi",
                "Aktivt energischema",
                $"'{activePowerPlan}' — INTE REKOMMENDERAT för stationär dator",
                Severity.Critical,
                "Energisparläge på stationär dator begränsar CPU-prestanda onödigt. " +
                "Byt till 'Balanserad' eller 'Hög prestanda' för bättre respons.",
                new List<ActionStep>
                {
                    new("Sök efter 'Energischema' i Start-menyn"),
                    new("Välj 'Balanserad' eller 'Hög prestanda'", null, "Lätt"),
                    new("Alternativ: Kör 'powercfg.exe /setactive SCHEME_BALANCED' som admin", 
                        "powercfg.exe /setactive SCHEME_BALANCED", "Medel")
                }));
        }
        else if (isPowerSaver && isLaptop)
        {
            results.Add(new AnalysisResult(
                "Energi",
                "Aktivt energischema",
                $"'{activePowerPlan}' — Energispar kan ge lägre prestanda",
                Severity.Warning,
                "Energisparläge kan begränsa prestanda även på nätström. " +
                "Överväg 'Balanserad' för bättre prestanda när laddaren är inkopplad."));
        }
        else if (isHighPerformance)
        {
            results.Add(new AnalysisResult(
                "Energi",
                "Aktivt energischema",
                $"'{activePowerPlan}' — Maximala prestanda",
                Severity.Ok,
                isLaptop 
                    ? "Hög prestanda ger maximal respons men dränerar batteriet snabbare."
                    : null));
        }
        else if (isBalanced)
        {
            results.Add(new AnalysisResult(
                "Energi",
                "Aktivt energischema",
                $"'{activePowerPlan}' — Rekommenderad inställning",
                Severity.Ok));
        }
        else
        {
            results.Add(new AnalysisResult(
                "Energi",
                "Aktivt energischema",
                $"'{activePowerPlan}'",
                Severity.Ok));
        }

        // Additional recommendations
        if (!isHighPerformance && !isLaptop)
        {
            results.Add(new AnalysisResult(
                "Energi",
                "Prestandatips",
                "För maximal prestanda på stationär dator, överväg 'Hög prestanda'",
                Severity.Ok,
                "Detta förhindrar CPU-throttling och kan ge bättre respons i krävande applikationer."));
        }

        return Task.FromResult(new AnalysisReport("ENERGISCHEMA", results));
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
