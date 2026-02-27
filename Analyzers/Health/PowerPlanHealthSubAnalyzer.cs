namespace ComputerPerformanceReview.Analyzers.Health;

/// <summary>
/// Analyzes active power plan and warns if power saver mode is active on AC power
/// </summary>
public sealed class PowerPlanHealthSubAnalyzer : IHealthSubAnalyzer
{
    public string Domain => "PowerPlan";

    private const int CooldownSeconds = 300; // Only warn every 5 minutes
    private DateTime _lastPowerPlanWarning = DateTime.MinValue;

    public void Collect(MonitorSampleBuilder builder)
    {
        try
        {
            // Get active power scheme via WMI
            var powerPlans = WmiHelper.Query(
                "SELECT ElementName, IsActive FROM Win32_PowerPlan WHERE IsActive = true", 
                "root\\cimv2\\power");
            
            if (powerPlans.Count > 0)
            {
                var elementName = WmiHelper.GetValue<string>(powerPlans[0], "ElementName");
                builder.ActivePowerPlan = elementName ?? "Unknown";
            }
        }
        catch 
        { 
            // If WMI fails, try powercfg command as fallback
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
                        builder.ActivePowerPlan = match.Groups[1].Value.Trim();
                    }
                }
            }
            catch { }
        }
    }

    public HealthAssessment Analyze(MonitorSample current, IReadOnlyList<MonitorSample> history)
    {
        var events = new List<MonitorEvent>();
        int healthScore = 100;

        if (string.IsNullOrEmpty(current.ActivePowerPlan))
        {
            return new HealthAssessment(
                new HealthScore(Domain, healthScore, 0, null),
                events);
        }

        var powerPlanLower = current.ActivePowerPlan.ToLowerInvariant();
        
        // Check for power saver mode
        bool isPowerSaver = powerPlanLower.Contains("power saver") || 
                           powerPlanLower.Contains("energispar") ||
                           powerPlanLower.Contains("batterispar");

        if (isPowerSaver)
        {
            // Check if on AC power (heuristic: if not a laptop or if high CPU clock speed)
            bool likelyOnAcPower = current.CpuClockMHz > 0 && 
                                   current.CpuMaxClockMHz > 0 &&
                                   (current.CpuClockMHz / current.CpuMaxClockMHz) > 0.7;

            // Also check if it's a desktop (no battery)
            bool isDesktop = !IsLaptop();

            if ((isDesktop || likelyOnAcPower) && 
                (DateTime.Now - _lastPowerPlanWarning).TotalSeconds > CooldownSeconds)
            {
                _lastPowerPlanWarning = DateTime.Now;
                healthScore -= 25;

                events.Add(new MonitorEvent(
                    DateTime.Now,
                    "PowerSaverMode",
                    $"Power saver mode active: '{current.ActivePowerPlan}' may limit CPU performance",
                    "Warning",
                    $"You are running power saver mode on a desktop computer or AC power. This lowers CPU frequency and can make the system slower. " +
                    $"ACTIONS: " +
                    $"1) Open Power Options: Search for 'Power Plan' in the Start menu. " +
                    $"2) Select 'Balanced' or 'High performance' instead. " +
                    $"3) Or press Win+X → Power Options → Select a performance mode. " +
                    $"4) Alternatively run: powercfg.exe /setactive SCHEME_BALANCED in administrator mode."));
            }
        }

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = history.Count >= 2 ? 1.0 : history.Count / 2.0;

        string? hint = healthScore < 80
            ? $"Power saver mode may limit performance"
            : null;

        return new HealthAssessment(
            new HealthScore(Domain, healthScore, confidence, hint),
            events);
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
