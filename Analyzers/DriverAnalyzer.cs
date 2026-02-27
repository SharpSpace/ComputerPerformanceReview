namespace ComputerPerformanceReview.Analyzers;

public sealed class DriverAnalyzer : IAnalyzer
{
    public string Name => "Driver Analysis";

    public Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        CheckDevicesWithProblems(results);
        ListInstalledDrivers(results);
        CheckDriverAge(results);
        CheckUnsignedDrivers(results);
        CreateDriverSummary(results);

        return Task.FromResult(new AnalysisReport("DRIVER ANALYSIS", results));
    }

    private static void CheckDevicesWithProblems(List<AnalysisResult> results)
    {
        try
        {
            var devices = WmiHelper.Query(
                "SELECT Name, DeviceID, ConfigManagerErrorCode, Status FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");

            if (devices.Count == 0)
            {
                results.Add(new AnalysisResult("Drivers", "Devices with problems",
                    "No devices with problems found", Severity.Ok));
                return;
            }

            foreach (var device in devices)
            {
                var name = WmiHelper.GetValue<string>(device, "Name") ?? "Unknown device";
                var errorCode = WmiHelper.GetValue<uint>(device, "ConfigManagerErrorCode");

                results.Add(new AnalysisResult("Drivers", "Device with problem",
                    $"{name} (Error code: {errorCode})",
                    Severity.Critical,
                    "Update or reinstall the driver for this device"));
            }
        }
        catch { }
    }

    private static void ListInstalledDrivers(List<AnalysisResult> results)
    {
        try
        {
            var drivers = WmiHelper.Query(
                "SELECT DeviceName, DriverName, DriverVersion, DriverDate, IsSigned FROM Win32_PnPSignedDriver");

            if (drivers.Count == 0)
            {
                results.Add(new AnalysisResult("Drivers", "Installed drivers",
                    "Could not read driver information", Severity.Warning));
                return;
            }

            // Sort by date and get oldest 10
            var driverList = new List<(string Name, string Version, DateTime? Date)>();

            foreach (var driver in drivers)
            {
                var deviceName = WmiHelper.GetValue<string>(driver, "DeviceName") ??
                                 WmiHelper.GetValue<string>(driver, "DriverName") ?? "Unknown";
                var version = WmiHelper.GetValue<string>(driver, "DriverVersion") ?? "Unknown";
                var dateStr = WmiHelper.GetValue<string>(driver, "DriverDate");

                DateTime? date = null;
                if (dateStr is not null)
                {
                    try
                    {
                        date = ManagementDateTimeConverter.ToDateTime(dateStr);
                    }
                    catch { }
                }

                driverList.Add((deviceName, version, date));
            }

            var oldestDrivers = driverList
                .Where(d => d.Date.HasValue)
                .OrderBy(d => d.Date)
                .Take(10)
                .ToList();

            if (oldestDrivers.Count > 0)
            {
                var driverNames = string.Join(", ", oldestDrivers
                    .Take(3)
                    .Select(d => $"{d.Name} ({d.Date:yyyy-MM-dd})"));

                if (oldestDrivers.Count > 3)
                    driverNames += $" and {oldestDrivers.Count - 3} more";

                results.Add(new AnalysisResult("Drivers", "Oldest drivers",
                    $"Oldest drivers: {driverNames}",
                    Severity.Ok));
            }
        }
        catch { }
    }

    private static void CheckDriverAge(List<AnalysisResult> results)
    {
        try
        {
            var drivers = WmiHelper.Query(
                "SELECT DeviceName, DriverName, DriverVersion, DriverDate FROM Win32_PnPSignedDriver");

            if (drivers.Count == 0)
                return;

            var now = DateTime.Now;
            var oldDrivers = new List<(string Name, string Version, DateTime Date, double Years)>();

            foreach (var driver in drivers)
            {
                var dateStr = WmiHelper.GetValue<string>(driver, "DriverDate");
                if (dateStr is null)
                    continue;

                try
                {
                    var driverDate = ManagementDateTimeConverter.ToDateTime(dateStr);
                    var age = now - driverDate;
                    var years = age.TotalDays / 365.25;

                    if (years > 2)
                    {
                        var deviceName = WmiHelper.GetValue<string>(driver, "DeviceName") ??
                                         WmiHelper.GetValue<string>(driver, "DriverName") ?? "Unknown";
                        var version = WmiHelper.GetValue<string>(driver, "DriverVersion") ?? "Unknown";
                        oldDrivers.Add((deviceName, version, driverDate, years));
                    }
                }
                catch { }
            }

            // Report critical (>5 years) first, then warnings (>2 years)
            var criticalDrivers = oldDrivers.Where(d => d.Years > 5).OrderByDescending(d => d.Years).ToList();
            var warningDrivers = oldDrivers.Where(d => d.Years <= 5 && d.Years > 2).OrderByDescending(d => d.Years).ToList();

            foreach (var driver in criticalDrivers.Take(5))
            {
                results.Add(new AnalysisResult("Drivers", "Outdated driver",
                    $"{driver.Name} (v{driver.Version}, {driver.Date:yyyy-MM-dd}, {driver.Years:F0} years old)",
                    Severity.Critical,
                    "Very old driver - update for better performance and security"));
            }

            foreach (var driver in warningDrivers.Take(5))
            {
                results.Add(new AnalysisResult("Drivers", "Outdated driver",
                    $"{driver.Name} (v{driver.Version}, {driver.Date:yyyy-MM-dd}, {driver.Years:F0} years old)",
                    Severity.Warning,
                    "Old driver - consider updating"));
            }

            if (oldDrivers.Count == 0)
            {
                results.Add(new AnalysisResult("Drivers", "Driver age",
                    "All drivers are relatively up to date (less than 2 years old)",
                    Severity.Ok));
            }
        }
        catch { }
    }

    private static void CheckUnsignedDrivers(List<AnalysisResult> results)
    {
        try
        {
            var drivers = WmiHelper.Query(
                "SELECT DeviceName, DriverName, IsSigned FROM Win32_PnPSignedDriver WHERE IsSigned = False");

            if (drivers.Count == 0)
            {
                results.Add(new AnalysisResult("Drivers", "Unsigned drivers",
                    "No unsigned drivers found", Severity.Ok));
                return;
            }

            foreach (var driver in drivers.Take(10))
            {
                var name = WmiHelper.GetValue<string>(driver, "DeviceName") ??
                           WmiHelper.GetValue<string>(driver, "DriverName") ?? "Unknown";

                results.Add(new AnalysisResult("Drivers", "Unsigned driver",
                    $"{name}",
                    Severity.Warning,
                    "Unsigned drivers can be a security risk"));
            }
        }
        catch { }
    }

    private static void CreateDriverSummary(List<AnalysisResult> results)
    {
        try
        {
            var drivers = WmiHelper.Query(
                "SELECT DeviceName, IsSigned FROM Win32_PnPSignedDriver");

            if (drivers.Count == 0)
            {
                results.Add(new AnalysisResult("Drivers", "Summary",
                    "Could not read driver information", Severity.Warning));
                return;
            }

            int totalDrivers = drivers.Count;
            int unsignedDrivers = drivers.Count(d => !WmiHelper.GetValue<bool>(d, "IsSigned", true));

            // Check for devices with problems separately
            var problemDevices = WmiHelper.Query(
                "SELECT Name FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
            int problemDrivers = problemDevices.Count;

            var severity = problemDrivers > 0 || unsignedDrivers > 0 ? Severity.Warning : Severity.Ok;

            results.Add(new AnalysisResult("Drivers", "Driver status",
                $"{totalDrivers} drivers installed ({problemDrivers} with problems, {unsignedDrivers} unsigned)",
                severity));
        }
        catch { }
    }
}
