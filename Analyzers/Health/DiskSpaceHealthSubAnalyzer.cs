namespace ComputerPerformanceReview.Analyzers.Health;

/// <summary>
/// Analyzes disk space on all volumes and provides cleanup recommendations
/// </summary>
public sealed class DiskSpaceHealthSubAnalyzer : IHealthSubAnalyzer
{
    public string Domain => "DiskSpace";

    // Thresholds
    private const double LowSpaceWarningPercent = 15.0;
    private const double LowSpaceCriticalPercent = 8.0;
    private const int CooldownSeconds = 120; // Only warn every 2 minutes

    // State
    private DateTime _lastLowSpaceWarning = DateTime.MinValue;

    public void Collect(MonitorSampleBuilder builder)
    {
        var diskSpaces = new List<DiskSpaceInfo>();
        
        try
        {
            var drives = WmiHelper.Query(
                "SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = 3");
            
            foreach (var drive in drives)
            {
                var deviceId = WmiHelper.GetValue<string>(drive, "DeviceID");
                var size = WmiHelper.GetValue<long>(drive, "Size");
                var freeSpace = WmiHelper.GetValue<long>(drive, "FreeSpace");
                
                if (string.IsNullOrEmpty(deviceId) || size == 0)
                    continue;
                
                double freePercent = (double)freeSpace / size * 100;
                
                diskSpaces.Add(new DiskSpaceInfo(
                    deviceId,
                    size,
                    freeSpace,
                    freePercent
                ));
            }
            
            builder.DiskSpaces = diskSpaces.Count > 0 ? diskSpaces : null;
        }
        catch { }
    }

    public HealthAssessment Analyze(MonitorSample current, IReadOnlyList<MonitorSample> history)
    {
        var events = new List<MonitorEvent>();
        int healthScore = 100;

        if (current.DiskSpaces is null || current.DiskSpaces.Count == 0)
        {
            return new HealthAssessment(
                new HealthScore(Domain, healthScore, 0, null),
                events);
        }

        // Check each drive for low space
        foreach (var disk in current.DiskSpaces)
        {
            if (disk.FreePercent < LowSpaceCriticalPercent)
            {
                if ((DateTime.Now - _lastLowSpaceWarning).TotalSeconds > CooldownSeconds)
                {
                    _lastLowSpaceWarning = DateTime.Now;
                    healthScore -= 30;

                    events.Add(new MonitorEvent(
                        DateTime.Now,
                        "LowDiskSpace",
                        $"KRITISKT lågt diskutrymme på {disk.DriveLetter}: endast {disk.FreePercent:F1}% ledigt ({ConsoleHelper.FormatBytes(disk.FreeBytes)} av {ConsoleHelper.FormatBytes(disk.TotalBytes)})",
                        "Critical",
                        $"Diskutrymmet på {disk.DriveLetter} är kritiskt lågt. ÅTGÄRDER: " +
                        $"1) Rensa %TEMP%-filer: Öppna 'Rensa disk' (cleanmgr.exe) → Välj {disk.DriveLetter} → Kryssa i 'Temporära filer'. " +
                        $"2) Avinstallera oanvända program: Inställningar → Appar → Installerade appar. " +
                        $"3) Flytta stora filer till annan disk eller extern lagring. " +
                        $"4) Töm Papperskorgen. " +
                        $"5) Rensa webbläsarcache: I Chrome/Edge → Inställningar → Sekretess → Rensa webbläsardata."));
                }
            }
            else if (disk.FreePercent < LowSpaceWarningPercent)
            {
                if ((DateTime.Now - _lastLowSpaceWarning).TotalSeconds > CooldownSeconds)
                {
                    _lastLowSpaceWarning = DateTime.Now;
                    healthScore -= 15;

                    events.Add(new MonitorEvent(
                        DateTime.Now,
                        "LowDiskSpace",
                        $"Lågt diskutrymme på {disk.DriveLetter}: {disk.FreePercent:F1}% ledigt ({ConsoleHelper.FormatBytes(disk.FreeBytes)} av {ConsoleHelper.FormatBytes(disk.TotalBytes)})",
                        "Warning",
                        $"Diskutrymmet på {disk.DriveLetter} börjar bli lågt. TIPS: " +
                        $"1) Kör 'Rensa disk' (cleanmgr.exe) för att ta bort temporära filer. " +
                        $"2) Kontrollera stora filer: Öppna Inställningar → System → Lagring → Visa användning per kategori. " +
                        $"3) Töm Papperskorgen och nedladdningar-mappen. " +
                        $"4) Rensa gamla Windows.old-mappar via 'Rensa disk' → 'Rensa systemfiler'."));
                }
            }
        }

        // Special check for C: drive (system drive)
        var cDrive = current.DiskSpaces.FirstOrDefault(d => 
            d.DriveLetter.Equals("C:", StringComparison.OrdinalIgnoreCase));
        
        if (cDrive != null && cDrive.FreePercent < 20)
        {
            healthScore -= 5; // Additional penalty for low system drive space
        }

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = history.Count >= 2 ? 1.0 : history.Count / 2.0;

        string? hint = healthScore < 70
            ? "Diskutrymme lågt på en eller flera volymer"
            : null;

        return new HealthAssessment(
            new HealthScore(Domain, healthScore, confidence, hint),
            events);
    }
}
