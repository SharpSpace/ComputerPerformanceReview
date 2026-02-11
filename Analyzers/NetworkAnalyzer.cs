using System.Net.NetworkInformation;

namespace ComputerPerformanceReview.Analyzers;

public sealed class NetworkAnalyzer : IAnalyzer
{
    public string Name => "Nätverksanalys";

    public async Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        await AnalyzeNetworkThroughput(results);
        AnalyzeActiveConnections(results);
        CheckBackgroundNetworkProcesses(results);

        return new AnalysisReport("NÄTVERKSANALYS", results);
    }

    private static async Task AnalyzeNetworkThroughput(List<AnalysisResult> results)
    {
        try
        {
            ConsoleHelper.WriteProgress("Mäter nätverkstrafik (2 sekunder)...");

            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                             && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            if (interfaces.Count == 0)
            {
                results.Add(new AnalysisResult("Nätverk", "Nätverksgränssnitt",
                    "Inga aktiva nätverksgränssnitt hittades", Severity.Warning));
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

                results.Add(new AnalysisResult("Nätverk", sample1[i].Name,
                    $"{sample1[i].Name}: {mbps:F1} Mbps ({ConsoleHelper.FormatBytes(totalBytesPerSec)}/s) | " +
                    $"Länkhastighet: {linkSpeedMbps:F0} Mbps",
                    severity,
                    severity != Severity.Ok
                        ? "Hög nätverksanvändning kan påverka prestanda"
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

            results.Add(new AnalysisResult("Nätverk", "TCP-anslutningar",
                $"Aktiva TCP-anslutningar: {established} etablerade av {tcpConnections.Length} totalt",
                severity,
                severity != Severity.Ok ? "Ovanligt många nätverksanslutningar" : null));
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
            results.Add(new AnalysisResult("Nätverk", "Bakgrundssynk",
                $"Nätverkstunga bakgrundsprocesser: {string.Join(", ", unique)}",
                unique.Count > 3 ? Severity.Warning : Severity.Ok,
                unique.Count > 3
                    ? "Många bakgrundsprocesser kan använda nätverket och disk samtidigt"
                    : null));
        }
    }
}
