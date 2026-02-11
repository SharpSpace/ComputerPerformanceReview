using Microsoft.Win32;

namespace ComputerPerformanceReview.Analyzers;

public sealed class GpuAnalyzer : IAnalyzer
{
    public string Name => "Grafikanalys";

    public Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        AnalyzeGpuHardware(results);
        CheckHardwareAcceleration(results);

        return Task.FromResult(new AnalysisReport("GRAFIKANALYS", results));
    }

    private static void AnalyzeGpuHardware(List<AnalysisResult> results)
    {
        try
        {
            var gpuData = WmiHelper.Query(
                "SELECT Name, AdapterRAM, DriverVersion, DriverDate, Status FROM Win32_VideoController");

            if (gpuData.Count == 0)
            {
                results.Add(new AnalysisResult("Grafik", "GPU",
                    "Kunde inte läsa GPU-information", Severity.Warning));
                return;
            }

            foreach (var gpu in gpuData)
            {
                var name = WmiHelper.GetValue<string>(gpu, "Name") ?? "Okänt grafikkort";
                var adapterRam = WmiHelper.GetValue<long>(gpu, "AdapterRAM");
                var driverVersion = WmiHelper.GetValue<string>(gpu, "DriverVersion") ?? "Okänd";
                var driverDateStr = WmiHelper.GetValue<string>(gpu, "DriverDate");
                var status = WmiHelper.GetValue<string>(gpu, "Status") ?? "Okänd";

                string vramStr = adapterRam > 0
                    ? ConsoleHelper.FormatBytes(adapterRam)
                    : "Okänt";

                results.Add(new AnalysisResult("Grafik", "GPU",
                    $"{name} ({vramStr} VRAM) | Drivrutin: {driverVersion} | Status: {status}",
                    status == "OK" ? Severity.Ok : Severity.Warning));

                // Check driver age
                if (driverDateStr is not null)
                {
                    try
                    {
                        var driverDate = ManagementDateTimeConverter.ToDateTime(driverDateStr);
                        var driverAge = DateTime.Now - driverDate;

                        var severity = driverAge.TotalDays switch
                        {
                            > 365 => Severity.Warning,
                            _ => Severity.Ok
                        };

                        results.Add(new AnalysisResult("Grafik", "Drivrutinsålder",
                            $"Drivrutin installerad: {driverDate:yyyy-MM-dd} ({driverAge.Days} dagar sedan)",
                            severity,
                            severity != Severity.Ok ? "Uppdatera grafikkortets drivrutin" : null));
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private static void CheckHardwareAcceleration(List<AnalysisResult> results)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Avalon.Graphics");
            if (key is not null)
            {
                var value = key.GetValue("DisableHWAcceleration");
                if (value is int intVal && intVal == 1)
                {
                    results.Add(new AnalysisResult("Grafik", "Hårdvaruacceleration",
                        "Hårdvaruacceleration är INAKTIVERAD (WPF)",
                        Severity.Warning,
                        "Aktivera hårdvaruacceleration för bättre gränssnittsprestanda"));
                    return;
                }
            }

            results.Add(new AnalysisResult("Grafik", "Hårdvaruacceleration",
                "Hårdvaruacceleration verkar vara aktiverad", Severity.Ok));
        }
        catch { }
    }
}
