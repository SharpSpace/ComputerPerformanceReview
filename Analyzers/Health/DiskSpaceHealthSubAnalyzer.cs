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
                        $"CRITICALLY low disk space on {disk.DriveLetter}: only {disk.FreePercent:F1}% free ({ConsoleHelper.FormatBytes(disk.FreeBytes)} of {ConsoleHelper.FormatBytes(disk.TotalBytes)})",
                        "Critical",
                        $"Disk space on {disk.DriveLetter} is critically low. ACTIONS: " +
                        $"1) Clean %TEMP% files: Open 'Disk Cleanup' (cleanmgr.exe) → Select {disk.DriveLetter} → Check 'Temporary files'. " +
                        $"2) Uninstall unused programs: Settings → Apps → Installed apps. " +
                        $"3) Move large files to another disk or external storage. " +
                        $"4) Empty the Recycle Bin. " +
                        $"5) Clear browser cache: In Chrome/Edge → Settings → Privacy → Clear browsing data."));
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
                        $"Low disk space on {disk.DriveLetter}: {disk.FreePercent:F1}% free ({ConsoleHelper.FormatBytes(disk.FreeBytes)} of {ConsoleHelper.FormatBytes(disk.TotalBytes)})",
                        "Warning",
                        $"Disk space on {disk.DriveLetter} is getting low. TIPS: " +
                        $"1) Run 'Disk Cleanup' (cleanmgr.exe) to remove temporary files. " +
                        $"2) Check large files: Open Settings → System → Storage → View usage by category. " +
                        $"3) Empty the Recycle Bin and Downloads folder. " +
                        $"4) Clean old Windows.old folders via 'Disk Cleanup' → 'Clean up system files'."));
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
            ? "Disk space low on one or more volumes"
            : null;

        return new HealthAssessment(
            new HealthScore(Domain, healthScore, confidence, hint),
            events);
    }
}
