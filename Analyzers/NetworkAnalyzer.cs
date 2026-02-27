using System.Net.NetworkInformation;

namespace ComputerPerformanceReview.Analyzers;

public sealed class NetworkAnalyzer : IAnalyzer
{
    public string Name => "Network Analysis";

    public async Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        await AnalyzeNetworkThroughput(results);
        AnalyzeActiveConnections(results);
        CheckBackgroundNetworkProcesses(results);

        return new AnalysisReport("NETWORK ANALYSIS", results);
    }

    private static async Task AnalyzeNetworkThroughput(List<AnalysisResult> results)
    {
        try
        {
            ConsoleHelper.WriteProgress("Measuring network traffic (2 seconds)...");

            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                             && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            if (interfaces.Count == 0)
            {
                results.Add(new AnalysisResult("Network", "Network interfaces",
                    "No active network interfaces found", Severity.Warning));
                ConsoleHelper.ClearProgress();
                return;
            }

            // First sample
            var sample1 = interfaces.Select(ni => new
            {
                ni.Name,
                Stats = ni.GetIPv4Statistics(),
                Speed = ni.Speed
            }).ToList();

            await Task.Delay(2000);

            // Second sample
            var sample2 = interfaces.Select(ni => new
            {
                ni.Name,
                Stats = ni.GetIPv4Statistics(),
                Speed = ni.Speed
            }).ToList();

            ConsoleHelper.ClearProgress();

            for (int i = 0; i < Math.Min(sample1.Count, sample2.Count); i++)
            {
                long bytesSent = sample2[i].Stats.BytesSent - sample1[i].Stats.BytesSent;
                long bytesReceived = sample2[i].Stats.BytesReceived - sample1[i].Stats.BytesReceived;
                long totalBytesPerSec = (bytesSent + bytesReceived) / 2;
                double mbps = totalBytesPerSec * 8.0 / 1_000_000;

                double linkSpeedMbps = sample1[i].Speed / 1_000_000.0;
                double utilization = linkSpeedMbps > 0 ? (mbps / linkSpeedMbps * 100) : 0;

                var severity = utilization switch
                {
                    > 80 => Severity.Warning,
                    _ => Severity.Ok
                };

                results.Add(new AnalysisResult("Network", sample1[i].Name,
                    $"{sample1[i].Name}: {mbps:F1} Mbps ({ConsoleHelper.FormatBytes(totalBytesPerSec)}/s) | " +
                    $"Link speed: {linkSpeedMbps:F0} Mbps",
                    severity,
                    severity != Severity.Ok
                        ? "High network usage may affect performance"
                        : null));
            }
        }
        catch
        {
            ConsoleHelper.ClearProgress();
        }
    }

    private static void AnalyzeActiveConnections(List<AnalysisResult> results)
    {
        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnections = properties.GetActiveTcpConnections();
            int established = tcpConnections.Count(c => c.State == TcpState.Established);

            var severity = established switch
            {
                > 500 => Severity.Warning,
                _ => Severity.Ok
            };

            results.Add(new AnalysisResult("Network", "TCP connections",
                $"Active TCP connections: {established} established of {tcpConnections.Length} total",
                severity,
                severity != Severity.Ok ? "Unusually many network connections" : null));
        }
        catch { }
    }

    private static void CheckBackgroundNetworkProcesses(List<AnalysisResult> results)
    {
        string[] knownNetworkHeavy = [
            "OneDrive", "Dropbox", "GoogleDriveSync", "iCloudDrive",
            "Teams", "Slack", "Discord",
            "steam", "EpicGamesLauncher",
            "MicrosoftEdgeUpdate", "GoogleUpdate", "spotify"
        ];

        var running = new List<string>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (knownNetworkHeavy.Any(name =>
                    proc.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase)))
                {
                    running.Add(proc.ProcessName);
                }
            }
            catch { }
            finally { proc.Dispose(); }
        }

        var unique = running.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (unique.Count > 0)
        {
            results.Add(new AnalysisResult("Network", "Background sync",
                $"Network-heavy background processes: {string.Join(", ", unique)}",
                unique.Count > 3 ? Severity.Warning : Severity.Ok,
                unique.Count > 3
                    ? "Many background processes may be using the network and disk simultaneously"
                    : null));
        }
    }
}
