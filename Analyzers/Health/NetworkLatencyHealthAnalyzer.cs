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
                    $"High DNS latency: {current.DnsLatencyMs:F0} ms",
                    critical ? "Critical" : "Warning",
                    "High DNS latency can cause UI hangs in apps waiting for network. " +
                    "ACTIONS: " +
                    "1) Switch DNS server to a faster alternative: Open Network Settings → Change adapter options → Right-click network → Properties → Internet Protocol Version 4 → Use the following DNS servers: Primary 1.1.1.1 (Cloudflare) or 8.8.8.8 (Google), Secondary 1.0.0.1 or 8.8.4.4. " +
                    "2) Check router: Restart the router, update firmware. " +
                    "3) Flush DNS cache: Open Command Prompt as admin → Run 'ipconfig /flushdns'. " +
                    "4) Temporarily disable VPN to see if it's the cause. " +
                    "5) Check if antivirus is blocking or scanning network traffic."));
            }
        }
        else
        {
            _consecutiveHighLatency = 0;
        }

        double confidence = history.Count >= 3 ? 1.0 : history.Count / 3.0;
        string? hint = current.DnsLatencyMs > 300 ? "High network latency (DNS)" : null;
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
