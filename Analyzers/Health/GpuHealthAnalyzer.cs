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
                    $"GPU-mättnad: {current.GpuUtilizationPercent:F0}%",
                    "Warning",
                    "GPU är hårt belastad vilket kan ge renderingsfördröjningar och UI-stutter. " +
                    "ÅTGÄRDER: " +
                    "1) Sänk grafikinställningar i spel/program: Minska upplösning, inaktivera ray-tracing, sänk skuggkvalitet. " +
                    "2) Stäng GPU-tunga applikationer i bakgrunden (t.ex. videoencoding, 3D-rendering). " +
                    "3) Uppdatera GPU-drivrutinen: NVIDIA (GeForce Experience), AMD (Radeon Software), Intel (Intel Driver & Support Assistant). " +
                    "4) Kontrollera GPU-temperatur med t.ex. GPU-Z — överhettning kan throttla prestanda."));
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
                        $"VRAM-tryck: {vramRatio * 100:F0}%",
                        vramRatio > 0.98 ? "Critical" : "Warning",
                        "VRAM (videominne) är nästan fullt vilket kan ge hitching, frysningar och stuttering. " +
                        "ÅTGÄRDER: " +
                        "1) Minska texturkvalitet och upplösning i spel/program. " +
                        "2) Stäng andra GPU-intensiva program. " +
                        "3) Om möjligt, uppgradera till grafikkort med mer VRAM. " +
                        "4) I vissa spel: Aktivera 'Texture Streaming' för att minska VRAM-användning."));
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
                $"GPU TDR/återställningar: {current.TdrEventsLast15Min} senaste 15 min",
                current.TdrEventsLast15Min > 3 ? "Critical" : "Warning",
                "Display-drivrutinen kraschar och återställs (TDR - Timeout Detection and Recovery, Event ID 4101). Detta orsakar skärmflimmer och kan frysa program. " +
                "ÅTGÄRDER: " +
                "1) Uppdatera GPU-drivrutinen: För NVIDIA gå till geforce.com/drivers, för AMD till amd.com/support, för Intel till intel.com/content/www/us/en/support/detect.html. " +
                "2) Använd DDU (Display Driver Uninstaller) för att göra en ren drivrutinsinstallation: Boota i felsäkert läge → Kör DDU → Avinstallera drivrutin → Starta om → Installera ny drivrutin. " +
                "3) Om nyligen uppdaterad: Rulla tillbaka till tidigare fungerande drivrutin via Enhetshanteraren. " +
                "4) Kontrollera GPU-temperatur — överhettning kan orsaka TDR. " +
                "5) Om GPU är överklockat: Återställ till fabriksinställningar. " +
                "6) Testa GPU-stabilitet med verktyg som FurMark eller 3DMark."));
        }

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = history.Count >= 3 ? 1.0 : history.Count / 3.0;
        string? hint = current.TdrEventsLast15Min > 0 ? "GPU-drivrutin instabil (TDR-events)" : null;

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
