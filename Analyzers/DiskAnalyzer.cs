namespace ComputerPerformanceReview.Analyzers;

public sealed class DiskAnalyzer : IAnalyzer
{
    public string Name => "Diskanalys";

    public Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        AnalyzeDriveSpace(results);
        AnalyzeTempFiles(results);
        AnalyzeAppDataLocal(results);
        DetectDiskType(results);

        return Task.FromResult(new AnalysisReport("DISKANALYS", results));
    }

    private static void AnalyzeDriveSpace(List<AnalysisResult> results)
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

                results.Add(new AnalysisResult("Disk", $"Enhet {drive.Name}",
                    $"{drive.Name.TrimEnd('\\')} {ConsoleHelper.FormatBytes(drive.AvailableFreeSpace)} ledigt " +
                    $"av {ConsoleHelper.FormatBytes(drive.TotalSize)} ({usedPercent:F1}% använt)",
                    severity,
                    severity != Severity.Ok ? $"Frigör utrymme på {drive.Name.TrimEnd('\\')}" : null));
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
                foreach (var file in Directory.EnumerateFiles(tempPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        totalSize += fi.Length;
                        fileCount++;
                    }
                    catch { }
                }
            }
            catch { }

            var severity = totalSize switch
            {
                > 10L * 1024 * 1024 * 1024 => Severity.Critical,
                > 5L * 1024 * 1024 * 1024 => Severity.Warning,
                _ => Severity.Ok
            };

            results.Add(new AnalysisResult("Disk", "Temporära filer",
                $"Temporära filer: {ConsoleHelper.FormatBytes(totalSize)} ({fileCount} filer) i {tempPath}",
                severity,
                severity != Severity.Ok ? "Rensa temporära filer via Diskrensning eller radera %TEMP%" : null));
        }
        catch { }
    }

    private static void AnalyzeAppDataLocal(List<AnalysisResult> results)
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!Directory.Exists(localAppData)) return;

            ConsoleHelper.WriteProgress("Skannar AppData\\Local...");

            // Measure top-level folders
            var folderSizes = new List<(string Name, string Path, long Size)>();

            foreach (var dir in Directory.GetDirectories(localAppData))
            {
                try
                {
                    long size = GetDirectorySize(dir);
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
                $"AppData\\Local: {ConsoleHelper.FormatBytes(totalSize)} i mappar > 100 MB",
                totalSeverity,
                totalSeverity != Severity.Ok ? "Rensa stora mappar i AppData\\Local (se nedan)" : null));

            // Sort biggest first, show top entries
            var topFolders = folderSizes
                .OrderByDescending(f => f.Size)
                .ToList();

            // Known cleanup candidates with recommendations
            var cleanupHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // === Temp & crash ===
                ["Temp"] = "Kan rensas: radera innehållet i mappen",
                ["CrashDumps"] = "Kan rensas: gamla kraschdumpar behövs sällan",
                ["CrashReportClient"] = "Kan rensas: gamla kraschrapporter",
                ["CrashPad"] = "Kan rensas: kraschdata från Chromium-baserade appar",
                ["Diagnostics"] = "Kan rensas: diagnostikloggar från Windows",
                ["ElevatedDiagnostics"] = "Kan rensas: diagnostikdata som samlats med admin-rättigheter",
                ["debuggee.mdmp"] = "Kan rensas: debugger memory dump-fil",

                // === GPU & grafik-cache ===
                ["D3DSCache"] = "Kan rensas: DirectX shader-cache återskapas automatiskt",
                ["NVIDIA"] = "Kan rensas: NVIDIA shader-cache (DXCache), återskapas automatiskt",
                ["NVIDIA Corporation"] = "Kan delvis rensas: DXCache-mappen inuti kan tas bort",
                ["IconCache.db"] = "Kan rensas: ikoncache återskapas automatiskt",

                // === Pakethanterare & dev-verktyg ===
                ["pip"] = "Kan rensas: Python pip-cache, kör 'pip cache purge'",
                ["npm-cache"] = "Kan rensas: npm-cache, kör 'npm cache clean --force'",
                ["yarn"] = "Kan rensas: Yarn-cache, kör 'yarn cache clean'",
                ["NuGet"] = "Kan delvis rensas: NuGet-cache, kör 'dotnet nuget locals all --clear'",
                ["conda"] = "Kan rensas: Conda-cache, kör 'conda clean --all'",
                ["Docker"] = "Kan rensas: Docker-data, kör 'docker system prune'",
                ["Docker Desktop Installer"] = "Kan rensas: gammal Docker-installer",
                ["Package Cache"] = "Installers-cache (VS etc) - rensa försiktigt, kan behövas vid reparation",
                ["assembly"] = ".NET GAC-assembly - rör ej utan vidare",
                ["CMakeTools"] = "Kan rensas: CMake-cache, återskapas vid build",
                ["ServiceHub"] = "VS ServiceHub-cache, kan rensas om VS inte körs",
                ["Symbols"] = "Debug-symboler cache - kan rensas, laddas ner igen vid behov",
                ["SymbolSourceSymbols"] = "Kan rensas: debug-symbolcache, laddas ned igen",
                ["RefSrcSymbols"] = "Kan rensas: referenskälla-symboler, laddas ned igen",
                ["SourceServer"] = "Kan rensas: källkods-symbolcache",
                ["flutter_webview_windows"] = "Kan rensas: Flutter webview-cache",
                ["Arduino15"] = "Arduino-data: ta bort oanvända boardpaket via Arduino IDE",
                ["arduino-ide-updater"] = "Kan rensas: uppdateringscache för Arduino IDE",
                ["azure-functions-core-tools"] = "Kan rensas: Azure Functions-verktyg cache",
                ["AzureFunctionsTools"] = "Kan rensas: Azure Functions-verktyg cache",
                ["AzureStorageEmulator"] = "Kan rensas om du inte använder Azure Storage Emulator",
                ["ms-playwright"] = "Kan rensas: Playwright browsers, kör 'npx playwright install' igen vid behov",
                ["XamarinBuildDownloadCache"] = "Kan rensas: Xamarin build-cache, laddas ned igen",
                ["Xamarin"] = "Kan rensas: Xamarin-cache, laddas ned igen vid build",

                // === IDE & editors ===
                ["JetBrains"] = "JetBrains IDE-cache: rensa via IDE Settings → Invalidate Caches",
                ["CodeMaid"] = "Kan rensas: CodeMaid-cache",
                ["LINQPad"] = "LINQPad-data: snippets/cache, rensa snippets-cache om stor",
                ["GitHub"] = "GitHub Desktop-data: cache kan rensas",
                ["GitHubVisualStudio"] = "Kan rensas: VS GitHub-tilläggscache",
                ["GitKrakenCLI"] = "Kan rensas: GitKraken CLI-cache",
                ["github-copilot"] = "Kan rensas: GitHub Copilot-cache",

                // === Webbläsare ===
                ["Google"] = "Chrome-data: rensa cache via Chrome → Inställningar → Rensa webbdata",
                ["BraveSoftware"] = "Brave-cache: rensa via Inställningar → Rensa webbdata",
                ["Mozilla"] = "Firefox-cache: rensa via Inställningar → Integritet → Rensa data",
                ["Opera Software"] = "Opera-cache: rensa via webbläsarinställningar",
                ["MicrosoftEdge"] = "Edge-cache: rensa via Edge → Inställningar → Rensa webbdata",
                ["CEF"] = "Chromium Embedded Framework-cache, rensas av appen som använder den",

                // === Kommunikation ===
                ["Discord"] = "Discord-cache: Cache/Code Cache-mapparna inuti kan rensas",
                ["Slack"] = "Slack-cache: Cache-mapparna inuti kan rensas",
                ["Teams"] = "Teams-cache: kan rensas via Teams-inställningar",
                ["Comms"] = "Kan rensas: Microsoft Teams/kommunikations-cache",

                // === Spel & launchers ===
                ["Steam"] = "Steam-data: flytta spel till annan disk via Steam → Inställningar → Nedladdningar",
                ["EpicGamesLauncher"] = "Epic Games-cache: rensa Saved/webcache mappen",
                ["Battle.net"] = "Battle.net-cache: rensa cache-mapp inuti",
                ["Blizzard Entertainment"] = "Blizzard-cache: kan rensas via Battle.net-inställningar",
                ["FortniteGame"] = "Fortnite-cache: kan bli stor, rensa via Epic Launcher verify",
                ["Origin"] = "Origin/EA-cache: rensa via appens inställningar",
                ["Razer"] = "Razer-data: cache kan rensas om Synapse inte körs",

                // === 3D/CAD/kreativa verktyg ===
                ["Autodesk"] = "Autodesk-cache: rensa temp/cache-mappar, behåll licenser",
                ["Fusion360"] = "Fusion 360-cache: kan bli stor, rensa offline-cache i appen",
                ["BambuStudio"] = "Bambu Studio-cache: rensa via appens inställningar",
                ["Creality Slicer"] = "Creality Slicer-cache: config/cache kan rensas",
                ["cura"] = "Cura slicer-cache: rensa cache-mappen inuti",
                ["UnrealEngine"] = "Unreal Engine: DerivedDataCache kan rensas, återskapas vid build",
                ["UnrealEngineLauncher"] = "Kan rensas: Unreal Launcher-cache",
                ["Edraw"] = "Edraw-data: cache kan rensas",
                ["SketchUp"] = "SketchUp-cache: Components/cache kan rensas",

                // === Media & streaming ===
                ["Spotify"] = "Spotify-cache: kan bli stor, rensa via Inställningar i appen",
                ["Plex Media Server"] = "Plex-data: rensa cache/transcoding-mapp, kan bli mycket stor",
                ["audacity"] = "Audacity temp-data: kan rensas om inga projekt är öppna",

                // === Molnsynk & backup ===
                ["OneDrive"] = "OneDrive-cache: rensa via OneDrive → Inställningar",
                ["Backup"] = "Windows backup-data: rensa gamla säkerhetskopior om ej behövs",

                // === Övrigt ===
                ["Packages"] = "Windows Store-appar: avinstallera oanvända via Inställningar → Appar",
                ["Microsoft"] = "Diverse Microsoft-appcache - rensa försiktigt",
                ["Ollama"] = "Ollama AI-modeller: ta bort oanvända modeller med 'ollama rm'",
                ["Postman"] = "Postman-data: rensa cache/historik via appens inställningar",
                ["qBittorrent"] = "qBittorrent-data: rensa avslutade torrents",
                ["Bluestacks"] = "BlueStacks Android-emulator: ta bort oanvända instanser",
                ["BlueStacksSetup"] = "Kan rensas: gammal BlueStacks-installer",
                ["TeamViewer"] = "TeamViewer-loggar: kan rensas om ej behövs",
                ["FileZilla"] = "FileZilla-data: rensa sitemanager-cache",
                ["Greenshot"] = "Greenshot-data: gammal screenshot-cache",
                ["HueSync"] = "Philips Hue Sync-cache",
                ["LogiBolt"] = "Logitech Bolt-data: cache kan rensas",
                ["LogiOptionsPlus"] = "Logitech Options+-cache: kan rensas vid problem",
                ["Logitech"] = "Logitech-cache/loggar: kan rensas",
                ["paint.net"] = "paint.net-cache: kan rensas",
                ["certify"] = "Certify SSL-cache: rensa gamla certifikat-loggar",
                ["SquirrelTemp"] = "Kan rensas: uppdateringstemp från Squirrel-baserade appar",
                ["netron-updater"] = "Kan rensas: Netron-uppdateringscache",
                ["sidequest-updater"] = "Kan rensas: SideQuest VR-uppdateringscache",
                ["vott-updater"] = "Kan rensas: VoTT-uppdateringscache",
                ["EVGA"] = "EVGA Precision-data: cache/loggar kan rensas",
                ["fontconfig"] = "Kan rensas: font-cache, återskapas automatiskt",
                ["gtk-3.0"] = "Kan rensas: GTK-cache, återskapas automatiskt",
                ["recently-used.xbel"] = "Kan rensas: senaste filer-historik",
                ["dftmp"] = "Kan rensas: temporärdata",
                ["cache"] = "Kan rensas: generell cache-mapp",
                ["py-yfinance"] = "Kan rensas: Python yfinance-datacache",
                ["nomic.ai"] = "Nomic AI-modelldata: ta bort oanvända modeller",
                ["AnthropicClaude"] = "Claude Desktop-data: cache kan rensas",
                ["claude-cli-nodejs"] = "Kan rensas: Claude CLI-cache",
                ["AwesomeCopilot"] = "Kan rensas: Copilot-tilläggscache",
            };

            foreach (var folder in topFolders)
            {
                // Find matching cleanup hint
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
                    ? "Undersök om cache/temporärdata kan rensas"
                    : null;

                // Individual folders are informational only (Ok) - the total above carries the severity
                results.Add(new AnalysisResult("Disk", $"AppData\\Local\\{folder.Name}",
                    $"  {folder.Name,-30} {ConsoleHelper.FormatBytes(folder.Size),10}",
                    Severity.Ok,
                    hint));
            }
        }
        catch
        {
            ConsoleHelper.ClearProgress();
        }
    }

    private static long GetDirectorySize(string path)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch { }
            }
        }
        catch { }
        return size;
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
                var name = WmiHelper.GetValue<string>(disk, "FriendlyName") ?? "Okänd disk";

                string typeStr = mediaType switch
                {
                    3 => "HDD",
                    4 => "SSD",
                    5 => "SCM",
                    _ => "Okänd"
                };

                var severity = mediaType == 3 ? Severity.Warning : Severity.Ok;

                results.Add(new AnalysisResult("Disk", "Disktyp",
                    $"{name}: {typeStr}",
                    severity,
                    mediaType == 3 ? "En HDD kan orsaka långsamhet - uppgradera till SSD" : null));
            }
        }
        catch { }
    }
}
