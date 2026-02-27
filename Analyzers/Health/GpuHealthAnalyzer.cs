using System.Diagnostics.Eventing.Reader;

namespace ComputerPerformanceReview.Analyzers.Health;

public sealed class GpuHealthAnalyzer : IHealthSubAnalyzer
{
    public string Domain => "GPU";

    private int _consecutiveHighGpu;
    private int _consecutiveHighVram;
    private DateTime _lastTdrPoll = DateTime.MinValue;
    private int _cachedTdrEvents;

    public void Collect(MonitorSampleBuilder builder)
    {
        try
        {
            var gpuData = WmiHelper.Query(
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");

            var gpuEngines = gpuData
                .Where(d => (WmiHelper.GetValue<string>(d, "Name") ?? string.Empty).Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                .Select(d => WmiHelper.GetValue<double>(d, "UtilizationPercentage"));

            builder.GpuUtilizationPercent = gpuEngines.Any() ? gpuEngines.Sum() : 0;
        }
        catch { }

        try
        {
            var memData = WmiHelper.Query(
                "SELECT DedicatedUsage, SharedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory");

            if (memData.Count > 0)
            {
                long dedicatedUsage = memData.Sum(d => WmiHelper.GetValue<long>(d, "DedicatedUsage"));
                long sharedUsage = memData.Sum(d => WmiHelper.GetValue<long>(d, "SharedUsage"));
                builder.GpuDedicatedUsageBytes = dedicatedUsage;
                builder.GpuDedicatedLimitBytes = Math.Max(dedicatedUsage + sharedUsage, 1);
            }
        }
        catch { }

        if (DateTime.UtcNow - _lastTdrPoll > TimeSpan.FromMinutes(1))
        {
            _cachedTdrEvents = QueryRecentTdrEvents(15);
            _lastTdrPoll = DateTime.UtcNow;
        }

        builder.TdrEventsLast15Min = _cachedTdrEvents;
    }

    public HealthAssessment Analyze(MonitorSample current, IReadOnlyList<MonitorSample> history)
    {
        var events = new List<MonitorEvent>();
        int healthScore = 100;

        if (current.GpuUtilizationPercent > 95)
        {
            _consecutiveHighGpu++;
            if (_consecutiveHighGpu == 2)
            {
                healthScore -= 15;
                events.Add(new MonitorEvent(
                    DateTime.Now,
                    "GpuSaturation",
                    $"GPU saturation: {current.GpuUtilizationPercent:F0}%",
                    "Warning",
                    "GPU is under heavy load which can cause rendering delays and UI stutter. " +
                    "ACTIONS: " +
                    "1) Lower graphics settings in games/apps: Reduce resolution, disable ray-tracing, lower shadow quality. " +
                    "2) Close GPU-heavy applications in the background (e.g., video encoding, 3D rendering). " +
                    "3) Update GPU driver: NVIDIA (GeForce Experience), AMD (Radeon Software), Intel (Intel Driver & Support Assistant). " +
                    "4) Check GPU temperature with e.g. GPU-Z — overheating can throttle performance."));
            }
        }
        else
        {
            _consecutiveHighGpu = 0;
        }

        if (current.GpuDedicatedLimitBytes > 0)
        {
            double vramRatio = (double)current.GpuDedicatedUsageBytes / current.GpuDedicatedLimitBytes;
            if (vramRatio > 0.9)
            {
                _consecutiveHighVram++;
                if (_consecutiveHighVram == 2)
                {
                    healthScore -= 15;
                    events.Add(new MonitorEvent(
                        DateTime.Now,
                        "GpuVramPressure",
                        $"VRAM pressure: {vramRatio * 100:F0}%",
                        vramRatio > 0.98 ? "Critical" : "Warning",
                        "VRAM (video memory) is nearly full which can cause hitching, freezes, and stuttering. " +
                        "ACTIONS: " +
                        "1) Reduce texture quality and resolution in games/apps. " +
                        "2) Close other GPU-intensive programs. " +
                        "3) If possible, upgrade to a graphics card with more VRAM. " +
                        "4) In some games: Enable 'Texture Streaming' to reduce VRAM usage."));
                }
            }
            else
            {
                _consecutiveHighVram = 0;
            }
        }

        if (current.TdrEventsLast15Min > 0)
        {
            healthScore -= current.TdrEventsLast15Min > 3 ? 30 : 15;
            events.Add(new MonitorEvent(
                DateTime.Now,
                "GpuTdr",
                $"GPU TDR/resets: {current.TdrEventsLast15Min} in last 15 min",
                current.TdrEventsLast15Min > 3 ? "Critical" : "Warning",
                "Display driver is crashing and recovering (TDR - Timeout Detection and Recovery, Event ID 4101). This causes screen flickering and can freeze programs. " +
                "ACTIONS: " +
                "1) Update GPU driver: For NVIDIA go to geforce.com/drivers, for AMD to amd.com/support, for Intel to intel.com/content/www/us/en/support/detect.html. " +
                "2) Use DDU (Display Driver Uninstaller) for a clean driver install: Boot in safe mode → Run DDU → Uninstall driver → Restart → Install new driver. " +
                "3) If recently updated: Roll back to previously working driver via Device Manager. " +
                "4) Check GPU temperature — overheating can cause TDR. " +
                "5) If GPU is overclocked: Reset to factory settings. " +
                "6) Test GPU stability with tools like FurMark or 3DMark."));
        }

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = history.Count >= 3 ? 1.0 : history.Count / 3.0;
        string? hint = current.TdrEventsLast15Min > 0 ? "GPU driver unstable (TDR events)" : null;

        return new HealthAssessment(new HealthScore(Domain, healthScore, confidence, hint), events);
    }

    private static int QueryRecentTdrEvents(int windowMinutes)
    {
        try
        {
            string xPath = "*[System[(Provider[@Name='Display']) and (EventID=4101) and "
                + $"TimeCreated[timediff(@SystemTime) <= {windowMinutes * 60 * 1000}]]]";

            var query = new EventLogQuery("System", PathType.LogName, xPath)
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            int count = 0;
            while (reader.ReadEvent() is EventRecord rec)
            {
                using (rec)
                {
                    count++;
                    if (count >= 50)
                        break;
                }
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }
}
