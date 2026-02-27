using Microsoft.Win32;

namespace ComputerPerformanceReview.Analyzers;

public sealed class GpuAnalyzer : IAnalyzer
{
    public string Name => "Graphics Analysis";

    public Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        AnalyzeGpuHardware(results);
        CheckHardwareAcceleration(results);

        return Task.FromResult(new AnalysisReport("GRAPHICS ANALYSIS", results));
    }

    private static void AnalyzeGpuHardware(List<AnalysisResult> results)
    {
        try
        {
            var gpuData = WmiHelper.Query(
                "SELECT Name, AdapterRAM, DriverVersion, DriverDate, Status FROM Win32_VideoController");

            if (gpuData.Count == 0)
            {
                results.Add(new AnalysisResult("Graphics", "GPU",
                    "Could not read GPU information", Severity.Warning));
                return;
            }

            foreach (var gpu in gpuData)
            {
                var name = WmiHelper.GetValue<string>(gpu, "Name") ?? "Unknown graphics card";
                var adapterRam = WmiHelper.GetValue<long>(gpu, "AdapterRAM");
                var driverVersion = WmiHelper.GetValue<string>(gpu, "DriverVersion") ?? "Unknown";
                var driverDateStr = WmiHelper.GetValue<string>(gpu, "DriverDate");
                var status = WmiHelper.GetValue<string>(gpu, "Status") ?? "Unknown";

                string vramStr = adapterRam > 0
                    ? ConsoleHelper.FormatBytes(adapterRam)
                    : "Unknown";

                results.Add(new AnalysisResult("Graphics", "GPU",
                    $"{name} ({vramStr} VRAM) | Driver: {driverVersion} | Status: {status}",
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

                        results.Add(new AnalysisResult("Graphics", "Driver age",
                            $"Driver installed: {driverDate:yyyy-MM-dd} ({driverAge.Days} days ago)",
                            severity,
                            severity != Severity.Ok ? "Update the graphics card driver" : null));
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
                    results.Add(new AnalysisResult("Graphics", "Hardware acceleration",
                        "Hardware acceleration is DISABLED (WPF)",
                        Severity.Warning,
                        "Enable hardware acceleration for better UI performance"));
                    return;
                }
            }

            results.Add(new AnalysisResult("Graphics", "Hardware acceleration",
                "Hardware acceleration appears to be enabled", Severity.Ok));
        }
        catch { }
    }
}
