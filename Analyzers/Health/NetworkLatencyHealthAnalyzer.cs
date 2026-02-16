using System.Net;

namespace ComputerPerformanceReview.Analyzers.Health;

public sealed class NetworkLatencyHealthAnalyzer : IHealthSubAnalyzer
{
    public string Domain => "NetworkLatency";

    private DateTime _lastProbe = DateTime.MinValue;
    private double _cachedDnsLatencyMs;
    private int _consecutiveHighLatency;

    public void Collect(MonitorSampleBuilder builder)
    {
        if (DateTime.UtcNow - _lastProbe > TimeSpan.FromSeconds(15))
        {
            _cachedDnsLatencyMs = MeasureDnsLatency("www.microsoft.com");
            _lastProbe = DateTime.UtcNow;
        }

        builder.DnsLatencyMs = _cachedDnsLatencyMs;
    }

    public HealthAssessment Analyze(MonitorSample current, IReadOnlyList<MonitorSample> history)
    {
        var events = new List<MonitorEvent>();
        int healthScore = 100;

        if (current.DnsLatencyMs > 300)
        {
            _consecutiveHighLatency++;
            if (_consecutiveHighLatency == 2)
            {
                bool critical = current.DnsLatencyMs > 1000;
                healthScore -= critical ? 20 : 10;
                events.Add(new MonitorEvent(
                    DateTime.Now,
                    "DnsLatency",
                    $"Hög DNS-latens: {current.DnsLatencyMs:F0} ms",
                    critical ? "Critical" : "Warning",
                    "Hög DNS-latens kan ge UI-häng i appar som väntar på nätverk. " +
                    "ÅTGÄRDER: " +
                    "1) Byt DNS-server till snabbare alternativ: Öppna Nätverksinställningar → Ändra adapterkonfiguration → Högerklicka på nätverket → Egenskaper → Internet Protocol Version 4 → Använd följande DNS-servrar: Primär 1.1.1.1 (Cloudflare) eller 8.8.8.8 (Google), Sekundär 1.0.0.1 eller 8.8.4.4. " +
                    "2) Kontrollera routern: Starta om routern, uppdatera firmware. " +
                    "3) Rensa DNS-cache: Öppna Kommandotolken som admin → Kör 'ipconfig /flushdns'. " +
                    "4) Inaktivera VPN tillfälligt för att se om det är orsaken. " +
                    "5) Kontrollera om antivirusprogram blockerar eller skannar nätverkstrafik."));
            }
        }
        else
        {
            _consecutiveHighLatency = 0;
        }

        double confidence = history.Count >= 3 ? 1.0 : history.Count / 3.0;
        string? hint = current.DnsLatencyMs > 300 ? "Hög nätverkslatens (DNS)" : null;
        return new HealthAssessment(new HealthScore(Domain, Math.Clamp(healthScore, 0, 100), confidence, hint), events);
    }

    private static double MeasureDnsLatency(string host)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            _ = Dns.GetHostAddresses(host);
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }
        catch
        {
            return 0;
        }
    }
}
