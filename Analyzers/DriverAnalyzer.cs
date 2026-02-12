namespace ComputerPerformanceReview.Analyzers;

public sealed class DriverAnalyzer : IAnalyzer
{
    public string Name => "Drivrutinanalys";

    public Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        CheckDevicesWithProblems(results);
        ListInstalledDrivers(results);
        CheckDriverAge(results);
        CheckUnsignedDrivers(results);
        CreateDriverSummary(results);

        return Task.FromResult(new AnalysisReport("DRIVRUTINANALYS", results));
    }

    private static void CheckDevicesWithProblems(List<AnalysisResult> results)
    {
        try
        {
            var devices = WmiHelper.Query(
                "SELECT Name, DeviceID, ConfigManagerErrorCode, Status FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");

            if (devices.Count == 0)
            {
                results.Add(new AnalysisResult("Drivrutiner", "Enheter med problem",
                    "Inga enheter med problem hittades", Severity.Ok));
                return;
            }

            foreach (var device in devices)
            {
                var name = WmiHelper.GetValue<string>(device, "Name") ?? "Okänd enhet";
                var errorCode = WmiHelper.GetValue<uint>(device, "ConfigManagerErrorCode");

                results.Add(new AnalysisResult("Drivrutiner", "Enhet med problem",
                    $"{name} (Felkod: {errorCode})",
                    Severity.Critical,
                    "Uppdatera eller installera om drivrutinen för denna enhet"));
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
                results.Add(new AnalysisResult("Drivrutiner", "Installerade drivrutiner",
                    "Kunde inte läsa drivrutinsinformation", Severity.Warning));
                return;
            }

            // Sort by date and get oldest 10
            var driverList = new List<(string Name, string Version, DateTime? Date)>();

            foreach (var driver in drivers)
            {
                var deviceName = WmiHelper.GetValue<string>(driver, "DeviceName") ?? 
                                 WmiHelper.GetValue<string>(driver, "DriverName") ?? "Okänd";
                var version = WmiHelper.GetValue<string>(driver, "DriverVersion") ?? "Okänd";
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
                    driverNames += $" och {oldestDrivers.Count - 3} till";

                results.Add(new AnalysisResult("Drivrutiner", "Äldsta drivrutiner",
                    $"Äldsta drivrutiner: {driverNames}",
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
                                         WmiHelper.GetValue<string>(driver, "DriverName") ?? "Okänd";
                        var version = WmiHelper.GetValue<string>(driver, "DriverVersion") ?? "Okänd";
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
                results.Add(new AnalysisResult("Drivrutiner", "Föråldrad drivrutin",
                    $"{driver.Name} (v{driver.Version}, {driver.Date:yyyy-MM-dd}, {driver.Years:F0} år gammal)",
                    Severity.Critical,
                    "Mycket gammal drivrutin - uppdatera för bättre prestanda och säkerhet"));
            }

            foreach (var driver in warningDrivers.Take(5))
            {
                results.Add(new AnalysisResult("Drivrutiner", "Föråldrad drivrutin",
                    $"{driver.Name} (v{driver.Version}, {driver.Date:yyyy-MM-dd}, {driver.Years:F0} år gammal)",
                    Severity.Warning,
                    "Gammal drivrutin - överväg att uppdatera"));
            }

            if (oldDrivers.Count == 0)
            {
                results.Add(new AnalysisResult("Drivrutiner", "Drivrutinsålder",
                    "Alla drivrutiner är relativt uppdaterade (mindre än 2 år gamla)",
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
                results.Add(new AnalysisResult("Drivrutiner", "Osignerade drivrutiner",
                    "Inga osignerade drivrutiner hittades", Severity.Ok));
                return;
            }

            foreach (var driver in drivers.Take(10))
            {
                var name = WmiHelper.GetValue<string>(driver, "DeviceName") ?? 
                           WmiHelper.GetValue<string>(driver, "DriverName") ?? "Okänd";

                results.Add(new AnalysisResult("Drivrutiner", "Osignerad drivrutin",
                    $"{name}",
                    Severity.Warning,
                    "Osignerade drivrutiner kan vara en säkerhetsrisk"));
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
                results.Add(new AnalysisResult("Drivrutiner", "Sammanfattning",
                    "Kunde inte läsa drivrutinsinformation", Severity.Warning));
                return;
            }

            int totalDrivers = drivers.Count;
            int unsignedDrivers = drivers.Count(d => !WmiHelper.GetValue<bool>(d, "IsSigned", true));

            // Check for devices with problems separately
            var problemDevices = WmiHelper.Query(
                "SELECT Name FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
            int problemDrivers = problemDevices.Count;

            var severity = problemDrivers > 0 || unsignedDrivers > 0 ? Severity.Warning : Severity.Ok;

            results.Add(new AnalysisResult("Drivrutiner", "Drivrutinsstatus",
                $"{totalDrivers} drivrutiner installerade ({problemDrivers} med problem, {unsignedDrivers} osignerade)",
                severity));
        }
        catch { }
    }
}
