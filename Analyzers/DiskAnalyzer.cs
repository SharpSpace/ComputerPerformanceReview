namespace ComputerPerformanceReview.Analyzers;

public sealed class DiskAnalyzer : IAnalyzer
{
    private const int SnapshotScanTimeoutMs = 500;
    private const int SnapshotScanMaxDepth = 2;

    public string Name => "Disk Analysis";

    public Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        bool hasHdd = HasHddDetected();

        AnalyzeDriveSpace(results, hasHdd);
        AnalyzeTempFiles(results);
        AnalyzeAppDataLocal(results);
        AnalyzeDiskLatency(results);
        AnalyzeDiskBusyPercentage(results);
        AnalyzeTopIoProcesses(results);
        AnalyzeSmartStatus(results);
        DetectDiskType(results);

        return Task.FromResult(new AnalysisReport("DISK ANALYSIS", results));
    }

    private static void AnalyzeDriveSpace(List<AnalysisResult> results, bool hasHdd)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;

                double usedPercent = (double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100;
                double freePercent = 100 - usedPercent;

                var severity = freePercent switch
                {
                    < 10 => Severity.Critical,
                    < 20 => Severity.Warning,
                    _ => Severity.Ok
                };

                if (hasHdd && usedPercent > 85 && severity == Severity.Ok)
                    severity = Severity.Warning;

                string? recommendation = severity != Severity.Ok
                    ? $"Free up space on {drive.Name.TrimEnd('\\')}"
                    : null;

                if (hasHdd && usedPercent > 85)
                {
                    recommendation = "HDD with high fill rate can cause fragmentation and micro-freezes. Free up space or move data.";
                }

                List<ActionStep>? actionSteps = null;
                if (severity != Severity.Ok)
                {
                    char driveLetter = drive.Name[0];
                    actionSteps =
                    [
                        new ActionStep(
                            $"Open Disk Cleanup for drive {driveLetter}:",
                            $"cleanmgr.exe /d {driveLetter}",
                            "Easy"),
                        new ActionStep(
                            "Open Storage settings (manage large apps/files)",
                            "start ms-settings:storagesense",
                            "Easy"),
                    ];
                }

                results.Add(new AnalysisResult("Disk", $"Drive {drive.Name}",
                    $"{drive.Name.TrimEnd('\\')} {ConsoleHelper.FormatBytes(drive.AvailableFreeSpace)} free " +
                    $"of {ConsoleHelper.FormatBytes(drive.TotalSize)} ({usedPercent:F1}% used)",
                    severity,
                    recommendation,
                    actionSteps));
            }
            catch { }
        }
    }

    private static void AnalyzeTempFiles(List<AnalysisResult> results)
    {
        try
        {
            string tempPath = Path.GetTempPath();
            long totalSize = 0;
            int fileCount = 0;

            try
            {
                (totalSize, fileCount) = GetDirectorySizeFast(tempPath, SnapshotScanMaxDepth, SnapshotScanTimeoutMs);
            }
            catch { }

            var severity = totalSize switch
            {
                > 10L * 1024 * 1024 * 1024 => Severity.Critical,
                > 5L * 1024 * 1024 * 1024 => Severity.Warning,
                _ => Severity.Ok
            };

            results.Add(new AnalysisResult("Disk", "Temporary files",
                $"Temporary files: {ConsoleHelper.FormatBytes(totalSize)} ({fileCount} files) in {tempPath}",
                severity,
                severity != Severity.Ok ? "Clean temporary files via Disk Cleanup or delete %TEMP%" : null,
                severity != Severity.Ok
                    ?
                    [
                        new ActionStep(
                            "Open Disk Cleanup (includes temp files)",
                            "cleanmgr.exe",
                            "Easy"),
                        new ActionStep(
                            "Open the Temp folder to delete manually",
                            @"explorer.exe %TEMP%",
                            "Easy"),
                    ]
                    : null));
        }
        catch { }
    }

    private static void AnalyzeAppDataLocal(List<AnalysisResult> results)
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!Directory.Exists(localAppData)) return;

            ConsoleHelper.WriteProgress("Scanning AppData\\Local...");

            // Measure top-level folders
            var folderSizes = new List<(string Name, string Path, long Size)>();

            foreach (var dir in Directory.GetDirectories(localAppData))
            {
                try
                {
                    long size = GetDirectorySizeFast(dir, SnapshotScanMaxDepth, SnapshotScanTimeoutMs).Size;
                    if (size > 100 * 1024 * 1024) // Only show folders > 100 MB
                    {
                        folderSizes.Add((Path.GetFileName(dir), dir, size));
                    }
                }
                catch { }
            }

            ConsoleHelper.ClearProgress();

            long totalSize = folderSizes.Sum(f => f.Size);

            var totalSeverity = totalSize switch
            {
                > 50L * 1024 * 1024 * 1024 => Severity.Critical,
                > 20L * 1024 * 1024 * 1024 => Severity.Warning,
                _ => Severity.Ok
            };

            results.Add(new AnalysisResult("Disk", "AppData\\Local total",
                $"AppData\\Local: {ConsoleHelper.FormatBytes(totalSize)} in folders > 100 MB",
                totalSeverity,
                totalSeverity != Severity.Ok ? "Clean large folders in AppData\\Local (see below)" : null));

            // Sort biggest first, show top entries
            var topFolders = folderSizes
                .OrderByDescending(f => f.Size)
                .ToList();

            // Known cleanup candidates with recommendations
            var cleanupHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // === Temp & crash ===
                ["Temp"] = "Can be cleaned: delete the folder contents",
                ["CrashDumps"] = "Can be cleaned: old crash dumps are rarely needed",
                ["CrashReportClient"] = "Can be cleaned: old crash reports",
                ["CrashPad"] = "Can be cleaned: crash data from Chromium-based apps",
                ["Diagnostics"] = "Can be cleaned: diagnostic logs from Windows",
                ["ElevatedDiagnostics"] = "Can be cleaned: diagnostic data collected with admin rights",
                ["debuggee.mdmp"] = "Can be cleaned: debugger memory dump file",

                // === GPU & graphics cache ===
                ["D3DSCache"] = "Can be cleaned: DirectX shader cache is recreated automatically",
                ["NVIDIA"] = "Can be cleaned: NVIDIA shader cache (DXCache), recreated automatically",
                ["NVIDIA Corporation"] = "Can partially clean: the DXCache folder inside can be removed",
                ["IconCache.db"] = "Can be cleaned: icon cache is recreated automatically",

                // === Package managers & dev tools ===
                ["pip"] = "Can be cleaned: Python pip cache, run 'pip cache purge'",
                ["npm-cache"] = "Can be cleaned: npm cache, run 'npm cache clean --force'",
                ["yarn"] = "Can be cleaned: Yarn cache, run 'yarn cache clean'",
                ["NuGet"] = "Can partially clean: NuGet cache, run 'dotnet nuget locals all --clear'",
                ["conda"] = "Can be cleaned: Conda cache, run 'conda clean --all'",
                ["Docker"] = "Can be cleaned: Docker data, run 'docker system prune'",
                ["Docker Desktop Installer"] = "Can be cleaned: old Docker installer",
                ["Package Cache"] = "Installer cache (VS etc) - clean carefully, may be needed for repair",
                ["assembly"] = ".NET GAC assembly - do not remove without reason",
                ["CMakeTools"] = "Can be cleaned: CMake cache, recreated at build",
                ["ServiceHub"] = "VS ServiceHub cache, can be cleaned if VS is not running",
                ["Symbols"] = "Debug symbols cache - can be cleaned, re-downloaded as needed",
                ["SymbolSourceSymbols"] = "Can be cleaned: debug symbol cache, re-downloaded",
                ["RefSrcSymbols"] = "Can be cleaned: reference source symbols, re-downloaded",
                ["SourceServer"] = "Can be cleaned: source code symbol cache",
                ["flutter_webview_windows"] = "Can be cleaned: Flutter webview cache",
                ["Arduino15"] = "Arduino data: remove unused board packages via Arduino IDE",
                ["arduino-ide-updater"] = "Can be cleaned: update cache for Arduino IDE",
                ["azure-functions-core-tools"] = "Can be cleaned: Azure Functions tools cache",
                ["AzureFunctionsTools"] = "Can be cleaned: Azure Functions tools cache",
                ["AzureStorageEmulator"] = "Can be cleaned if you don't use Azure Storage Emulator",
                ["ms-playwright"] = "Can be cleaned: Playwright browsers, run 'npx playwright install' again if needed",
                ["XamarinBuildDownloadCache"] = "Can be cleaned: Xamarin build cache, re-downloaded",
                ["Xamarin"] = "Can be cleaned: Xamarin cache, re-downloaded at build",

                // === IDE & editors ===
                ["JetBrains"] = "JetBrains IDE cache: clean via IDE Settings → Invalidate Caches",
                ["CodeMaid"] = "Can be cleaned: CodeMaid cache",
                ["LINQPad"] = "LINQPad data: snippets/cache, clean snippets cache if large",
                ["GitHub"] = "GitHub Desktop data: cache can be cleaned",
                ["GitHubVisualStudio"] = "Can be cleaned: VS GitHub extension cache",
                ["GitKrakenCLI"] = "Can be cleaned: GitKraken CLI cache",
                ["github-copilot"] = "Can be cleaned: GitHub Copilot cache",

                // === Browsers ===
                ["Google"] = "Chrome data: clear cache via Chrome → Settings → Clear browsing data",
                ["BraveSoftware"] = "Brave cache: clear via Settings → Clear browsing data",
                ["Mozilla"] = "Firefox cache: clear via Settings → Privacy → Clear data",
                ["Opera Software"] = "Opera cache: clear via browser settings",
                ["MicrosoftEdge"] = "Edge cache: clear via Edge → Settings → Clear browsing data",
                ["CEF"] = "Chromium Embedded Framework cache, cleared by the app that uses it",

                // === Communication ===
                ["Discord"] = "Discord cache: the Cache/Code Cache folders inside can be cleaned",
                ["Slack"] = "Slack cache: the Cache folders inside can be cleaned",
                ["Teams"] = "Teams cache: can be cleaned via Teams settings",
                ["Comms"] = "Can be cleaned: Microsoft Teams/communication cache",

                // === Games & launchers ===
                ["Steam"] = "Steam data: move games to another disk via Steam → Settings → Downloads",
                ["EpicGamesLauncher"] = "Epic Games cache: clean the Saved/webcache folder",
                ["Battle.net"] = "Battle.net cache: clean the cache folder inside",
                ["Blizzard Entertainment"] = "Blizzard cache: can be cleaned via Battle.net settings",
                ["FortniteGame"] = "Fortnite cache: can grow large, clean via Epic Launcher verify",
                ["Origin"] = "Origin/EA cache: clean via app settings",
                ["Razer"] = "Razer data: cache can be cleaned if Synapse is not running",

                // === 3D/CAD/creative tools ===
                ["Autodesk"] = "Autodesk cache: clean temp/cache folders, keep licenses",
                ["Fusion360"] = "Fusion 360 cache: can grow large, clean offline cache in the app",
                ["BambuStudio"] = "Bambu Studio cache: clean via app settings",
                ["Creality Slicer"] = "Creality Slicer cache: config/cache can be cleaned",
                ["cura"] = "Cura slicer cache: clean the cache folder inside",
                ["UnrealEngine"] = "Unreal Engine: DerivedDataCache can be cleaned, recreated at build",
                ["UnrealEngineLauncher"] = "Can be cleaned: Unreal Launcher cache",
                ["Edraw"] = "Edraw data: cache can be cleaned",
                ["SketchUp"] = "SketchUp cache: Components/cache can be cleaned",

                // === Media & streaming ===
                ["Spotify"] = "Spotify cache: can grow large, clean via Settings in the app",
                ["Plex Media Server"] = "Plex data: clean cache/transcoding folder, can be very large",
                ["audacity"] = "Audacity temp data: can be cleaned if no projects are open",

                // === Cloud sync & backup ===
                ["OneDrive"] = "OneDrive cache: clean via OneDrive → Settings",
                ["Backup"] = "Windows backup data: remove old backups if not needed",

                // === Miscellaneous ===
                ["Packages"] = "Windows Store apps: uninstall unused apps via Settings → Apps",
                ["Microsoft"] = "Various Microsoft app cache - clean carefully",
                ["Ollama"] = "Ollama AI models: remove unused models with 'ollama rm'",
                ["Postman"] = "Postman data: clean cache/history via app settings",
                ["qBittorrent"] = "qBittorrent data: clean completed torrents",
                ["Bluestacks"] = "BlueStacks Android emulator: remove unused instances",
                ["BlueStacksSetup"] = "Can be cleaned: old BlueStacks installer",
                ["TeamViewer"] = "TeamViewer logs: can be cleaned if not needed",
                ["FileZilla"] = "FileZilla data: clean site manager cache",
                ["Greenshot"] = "Greenshot data: old screenshot cache",
                ["HueSync"] = "Philips Hue Sync cache",
                ["LogiBolt"] = "Logitech Bolt data: cache can be cleaned",
                ["LogiOptionsPlus"] = "Logitech Options+ cache: can be cleaned if issues occur",
                ["Logitech"] = "Logitech cache/logs: can be cleaned",
                ["paint.net"] = "paint.net cache: can be cleaned",
                ["certify"] = "Certify SSL cache: clean old certificate logs",
                ["SquirrelTemp"] = "Can be cleaned: update temp from Squirrel-based apps",
                ["netron-updater"] = "Can be cleaned: Netron update cache",
                ["sidequest-updater"] = "Can be cleaned: SideQuest VR update cache",
                ["vott-updater"] = "Can be cleaned: VoTT update cache",
                ["EVGA"] = "EVGA Precision data: cache/logs can be cleaned",
                ["fontconfig"] = "Can be cleaned: font cache, recreated automatically",
                ["gtk-3.0"] = "Can be cleaned: GTK cache, recreated automatically",
                ["recently-used.xbel"] = "Can be cleaned: recent files history",
                ["dftmp"] = "Can be cleaned: temporary data",
                ["cache"] = "Can be cleaned: general cache folder",
                ["py-yfinance"] = "Can be cleaned: Python yfinance data cache",
                ["nomic.ai"] = "Nomic AI model data: remove unused models",
                ["AnthropicClaude"] = "Claude Desktop data: cache can be cleaned",
                ["claude-cli-nodejs"] = "Can be cleaned: Claude CLI cache",
                ["AwesomeCopilot"] = "Can be cleaned: Copilot extension cache",
            };

            // Known CLI commands for direct cleanup (checked separately from the text hints)
            var commandHints = new Dictionary<string, (string Command, string Title, string Difficulty)>(StringComparer.OrdinalIgnoreCase)
            {
                // === Package managers ===
                ["pip"]            = ("pip cache purge",                    "Clean pip cache",             "Easy"),
                ["npm-cache"]      = ("npm cache clean --force",            "Clean npm cache",             "Easy"),
                ["yarn"]           = ("yarn cache clean",                   "Clean Yarn cache",            "Easy"),
                ["NuGet"]          = ("dotnet nuget locals all --clear",    "Clear NuGet local caches",    "Easy"),
                ["conda"]          = ("conda clean --all",                  "Clean Conda cache",           "Easy"),
                // === Containers ===
                ["Docker Desktop Installer"] = ("docker system prune -f",  "Prune Docker system (force)", "Medium"),
                ["Docker"]         = ("docker system prune",                "Prune Docker system",         "Medium"),
                // === AI models ===
                ["Ollama"]         = ("ollama list",                        "List installed Ollama models (run 'ollama rm <name>' to delete)", "Easy"),
            };

            foreach (var folder in topFolders)
            {
                // Find matching text hint
                string? hint = null;
                foreach (var kvp in cleanupHints)
                {
                    if (folder.Name.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        hint = kvp.Value;
                        break;
                    }
                }

                hint ??= folder.Size > 1L * 1024 * 1024 * 1024
                    ? "Investigate if cache/temporary data can be cleaned"
                    : null;

                // Find matching command hint → ActionStep
                List<ActionStep>? actionSteps = null;
                foreach (var kvp in commandHints)
                {
                    if (folder.Name.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        actionSteps = [new ActionStep(kvp.Value.Title, kvp.Value.Command, kvp.Value.Difficulty)];
                        break;
                    }
                }

                // Individual folders are informational only (Ok) - the total above carries the severity
                results.Add(new AnalysisResult("Disk", $"AppData\\Local\\{folder.Name}",
                    $"  {folder.Name,-30} {ConsoleHelper.FormatBytes(folder.Size),10}",
                    Severity.Ok,
                    hint,
                    actionSteps));
            }
        }
        catch
        {
            ConsoleHelper.ClearProgress();
        }
    }

    private static (long Size, int FileCount) GetDirectorySizeFast(string path, int maxDepth, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        return GetDirectorySizeInternal(path, 0, maxDepth, sw, timeoutMs);
    }

    private static (long Size, int FileCount) GetDirectorySizeInternal(
        string path,
        int depth,
        int maxDepth,
        Stopwatch sw,
        int timeoutMs)
    {
        if (depth > maxDepth || sw.ElapsedMilliseconds > timeoutMs)
            return (0, 0);

        long size = 0;
        int fileCount = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path))
            {
                if (sw.ElapsedMilliseconds > timeoutMs)
                    break;

                try
                {
                    size += new FileInfo(file).Length;
                    fileCount++;
                }
                catch { }
            }

            if (depth < maxDepth && sw.ElapsedMilliseconds <= timeoutMs)
            {
                foreach (var dir in Directory.EnumerateDirectories(path))
                {
                    if (sw.ElapsedMilliseconds > timeoutMs)
                        break;

                    var child = GetDirectorySizeInternal(dir, depth + 1, maxDepth, sw, timeoutMs);
                    size += child.Size;
                    fileCount += child.FileCount;
                }
            }
        }
        catch { }

        return (size, fileCount);
    }

    private static void AnalyzeDiskLatency(List<AnalysisResult> results)
    {
        try
        {
            using var read = new PerformanceCounter("PhysicalDisk", "Avg. Disk sec/Read", "_Total");
            using var write = new PerformanceCounter("PhysicalDisk", "Avg. Disk sec/Write", "_Total");

            read.NextValue();
            write.NextValue();
            Thread.Sleep(100);

            double readLatency = read.NextValue();
            double writeLatency = write.NextValue();
            double worst = Math.Max(readLatency, writeLatency);

            var severity = worst switch
            {
                > 0.15 => Severity.Critical,
                > 0.05 => Severity.Warning,
                _ => Severity.Ok
            };

            results.Add(new AnalysisResult(
                "Disk",
                "Disk latency",
                $"Read: {readLatency * 1000:F1} ms, Write: {writeLatency * 1000:F1} ms",
                severity,
                severity != Severity.Ok ? "High disk latency can cause freezes despite low CPU" : null));
        }
        catch { }
    }

    private static void AnalyzeDiskBusyPercentage(List<AnalysisResult> results)
    {
        try
        {
            using var busy = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
            busy.NextValue();
            Thread.Sleep(100);
            var diskBusy = busy.NextValue();

            var severity = diskBusy switch
            {
                > 95 => Severity.Critical,
                > 80 => Severity.Warning,
                _ => Severity.Ok
            };

            results.Add(new AnalysisResult(
                "Disk",
                "Disk load",
                $"Disk busy: {diskBusy:F1}%",
                severity,
                severity != Severity.Ok ? "High disk activity can cause sluggish response even when CPU looks low" : null));
        }
        catch { }
    }

    private static void AnalyzeTopIoProcesses(List<AnalysisResult> results)
    {
        try
        {
            var top = Process.GetProcesses()
                .Select(proc =>
                {
                    try
                    {
                        if (!proc.TryGetIoCounters(out var io))
                            return null;

                        ulong total = io.ReadTransferCount + io.WriteTransferCount;
                        return new { proc.ProcessName, TotalIo = total };
                    }
                    catch
                    {
                        return null;
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                })
                .Where(x => x is not null)
                .OrderByDescending(x => x!.TotalIo)
                .Take(5)
                .ToList();

            foreach (var item in top)
            {
                results.Add(new AnalysisResult(
                    "Disk",
                    "I/O process",
                    $"{item!.ProcessName}: {ConsoleHelper.FormatBytes((long)item.TotalIo)} total I/O",
                    Severity.Ok));
            }
        }
        catch { }
    }

    private static void AnalyzeSmartStatus(List<AnalysisResult> results)
    {
        try
        {
            var smartData = WmiHelper.Query(
                "SELECT PredictFailure, InstanceName FROM MSStorageDriver_FailurePredictStatus",
                @"\\.\root\WMI");

            if (smartData.Count == 0)
                return;

            foreach (var disk in smartData)
            {
                bool predictFailure = WmiHelper.GetValue<bool>(disk, "PredictFailure");
                string instanceName = WmiHelper.GetValue<string>(disk, "InstanceName") ?? "Disk";

                results.Add(new AnalysisResult(
                    "Disk",
                    "SMART-status",
                    $"{instanceName}: {(predictFailure ? "FAILURE PREDICTED" : "OK")}",
                    predictFailure ? Severity.Critical : Severity.Ok,
                    predictFailure ? "Critical disk health: back up immediately and replace disk." : null));
            }
        }
        catch { }
    }

    private static bool HasHddDetected()
    {
        try
        {
            var diskData = WmiHelper.Query(
                "SELECT MediaType FROM MSFT_PhysicalDisk",
                @"\\.\root\Microsoft\Windows\Storage");

            return diskData.Any(disk => WmiHelper.GetValue<int>(disk, "MediaType") == 3);
        }
        catch
        {
            return false;
        }
    }

    private static void DetectDiskType(List<AnalysisResult> results)
    {
        try
        {
            var diskData = WmiHelper.Query(
                "SELECT MediaType, FriendlyName FROM MSFT_PhysicalDisk",
                @"\\.\root\Microsoft\Windows\Storage");

            foreach (var disk in diskData)
            {
                var mediaType = WmiHelper.GetValue<int>(disk, "MediaType");
                var name = WmiHelper.GetValue<string>(disk, "FriendlyName") ?? "Unknown disk";

                string typeStr = mediaType switch
                {
                    3 => "HDD",
                    4 => "SSD",
                    5 => "SCM",
                    _ => "Unknown"
                };

                var severity = mediaType == 3 ? Severity.Warning : Severity.Ok;

                results.Add(new AnalysisResult("Disk", "Disk type",
                    $"{name}: {typeStr}",
                    severity,
                    mediaType == 3 ? "An HDD can cause slowness - upgrade to SSD" : null));
            }
        }
        catch { }
    }
}
