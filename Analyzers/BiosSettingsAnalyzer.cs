using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ComputerPerformanceReview.Analyzers;

public sealed class BiosSettingsAnalyzer : IAnalyzer
{
    public string Name => "BIOS Settings";

    public Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        CheckXmpDocp(results);
        CheckBiosAge(results);
        CheckResizableBar(results);
        CheckCpuBiosSettings(results);

        return Task.FromResult(new AnalysisReport("BIOS SETTINGS", results));
    }

    // ────────────────────────────────────────────────────────────────
    // XMP / DOCP
    // ────────────────────────────────────────────────────────────────

    private static void CheckXmpDocp(List<AnalysisResult> results)
    {
        try
        {
            var dimms = WmiHelper.Query(
                "SELECT PartNumber, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");

            if (dimms.Count == 0) return;

            // Prefer ConfiguredClockSpeed (more accurate); fall back to Speed
            long configuredSpeed = 0;
            string partNumber = "";

            foreach (var dimm in dimms)
            {
                if (configuredSpeed == 0)
                {
                    long cfg = WmiHelper.GetValue<long>(dimm, "ConfiguredClockSpeed");
                    long spd = WmiHelper.GetValue<long>(dimm, "Speed");
                    configuredSpeed = cfg > 0 ? cfg : spd;
                }

                if (string.IsNullOrWhiteSpace(partNumber))
                {
                    partNumber = (WmiHelper.GetValue<string>(dimm, "PartNumber") ?? "")
                        .Trim().TrimEnd('\0').Trim();
                }
            }

            if (configuredSpeed == 0)
            {
                results.Add(new AnalysisResult("BIOS", "XMP / DOCP",
                    "Could not read RAM clock speed from WMI on this system",
                    Severity.Ok));
                return;
            }

            // Try to extract the module's rated speed from the part number
            // (WMI Speed/ConfiguredClockSpeed both reflect BIOS-configured speed, not module max)
            long ratedSpeed = ExtractSpeedFromPartNumber(partNumber);

            string currentLabel = $"Current: {configuredSpeed} MHz";
            string partLabel = !string.IsNullOrEmpty(partNumber) ? $" | Part: {partNumber}" : "";

            if (ratedSpeed > 0 && ratedSpeed > configuredSpeed + 200)
            {
                // Part number reveals the module is rated higher than currently configured
                results.Add(new AnalysisResult(
                    "BIOS",
                    "XMP / DOCP not enabled",
                    $"{currentLabel} | Rated: {ratedSpeed} MHz{partLabel} — RAM is running below its rated speed.",
                    Severity.Warning,
                    $"Enable XMP (Intel) or DOCP (AMD) in BIOS. Your modules are rated for {ratedSpeed} MHz " +
                    $"but the BIOS is only running them at {configuredSpeed} MHz (JEDEC default). " +
                    "This is free performance — no hardware changes needed.",
                    [new ActionStep(
                        "Restart → enter BIOS (Del or F2) → find 'XMP' or 'DOCP' or 'DRAM Profile' → select XMP profile → save & exit",
                        null, "Medium")]));
            }
            else if (configuredSpeed <= 2133)
            {
                // At DDR4 JEDEC base speed — XMP is very likely not enabled
                string partHint = !string.IsNullOrEmpty(partNumber)
                    ? $" ({partNumber})"
                    : "";
                results.Add(new AnalysisResult(
                    "BIOS",
                    "XMP / DOCP — verify in BIOS",
                    $"{currentLabel} (DDR4 JEDEC default){partHint} — " +
                    "if your modules are rated above 2133 MHz, XMP/DOCP may not be enabled.",
                    Severity.Warning,
                    "DDR4 runs at 2133 MHz by default without XMP. Check your RAM box or part number for the rated speed. " +
                    "If it's rated above 2133 MHz, enable XMP (Intel) or DOCP (AMD) in BIOS.",
                    [new ActionStep(
                        "Restart → enter BIOS → find 'XMP', 'DOCP', or 'DRAM Profile' → enable the XMP profile → save & exit",
                        null, "Medium")]));
            }
            else
            {
                results.Add(new AnalysisResult("BIOS", "XMP / DOCP",
                    $"{currentLabel} — above DDR4 JEDEC default, XMP/DOCP appears active{partLabel}",
                    Severity.Ok));
            }
        }
        catch { }
    }

    /// <summary>
    /// Extracts a plausible RAM speed (MHz) from a part number string.
    /// E.g. "CMK32GX4M2A2666C16" → 2666, "F4-3600C18D-32GVK" → 3600
    /// </summary>
    private static long ExtractSpeedFromPartNumber(string part)
    {
        if (string.IsNullOrEmpty(part)) return 0;

        foreach (Match m in Regex.Matches(part, @"\b(\d{4})\b"))
        {
            if (long.TryParse(m.Groups[1].Value, out long val) && val >= 1600 && val <= 8000)
                return val;
        }
        return 0;
    }

    // ────────────────────────────────────────────────────────────────
    // BIOS firmware age
    // ────────────────────────────────────────────────────────────────

    private static void CheckBiosAge(List<AnalysisResult> results)
    {
        try
        {
            var biosData = WmiHelper.Query(
                "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");

            if (biosData.Count == 0) return;

            var bios = biosData[0];
            var manufacturer = WmiHelper.GetValue<string>(bios, "Manufacturer") ?? "Unknown";
            var version = WmiHelper.GetValue<string>(bios, "SMBIOSBIOSVersion") ?? "Unknown";
            var releaseDateStr = WmiHelper.GetValue<string>(bios, "ReleaseDate");

            if (releaseDateStr is null) return;

            var releaseDate = ManagementDateTimeConverter.ToDateTime(releaseDateStr);
            int ageDays = (int)(DateTime.Now - releaseDate).TotalDays;

            // Build browser search URL using motherboard model
            string? searchCommandHint = BuildBiosSearchUrl();

            string desc = $"BIOS from {releaseDate:yyyy-MM-dd} ({ageDays} days old) — {manufacturer} {version}";

            if (ageDays > 730) // > 2 years
            {
                var steps = new List<ActionStep>();

                if (searchCommandHint is not null)
                    steps.Add(new ActionStep(
                        "Open browser to search for BIOS update for your motherboard",
                        searchCommandHint, "Easy"));
                else
                    steps.Add(new ActionStep(
                        "Search for your motherboard model + 'BIOS update' on the manufacturer's website (ASUS, MSI, Gigabyte, ASRock, etc.)",
                        null, "Easy"));

                steps.Add(new ActionStep(
                    "Download the BIOS file → copy to USB → restart → enter BIOS → Tools / EZ Flash → select file → update",
                    null, "Medium"));

                results.Add(new AnalysisResult(
                    "BIOS",
                    "BIOS firmware age",
                    desc,
                    Severity.Warning,
                    "A BIOS update may improve stability, fix hardware compatibility issues, and add support for newer RAM speeds. " +
                    "Check your motherboard manufacturer's website.",
                    steps));
            }
            else
            {
                results.Add(new AnalysisResult("BIOS", "BIOS firmware age",
                    desc, Severity.Ok));
            }
        }
        catch { }
    }

    private static string? BuildBiosSearchUrl()
    {
        try
        {
            var boardData = WmiHelper.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            if (boardData.Count == 0) return null;

            var board = boardData[0];
            string mfr = (WmiHelper.GetValue<string>(board, "Manufacturer") ?? "").Trim();
            string product = (WmiHelper.GetValue<string>(board, "Product") ?? "").Trim();

            if (string.IsNullOrWhiteSpace(mfr) || string.IsNullOrWhiteSpace(product))
                return null;

            string query = Uri.EscapeDataString($"{mfr} {product} BIOS update");
            return $"start https://www.google.com/search?q={query}";
        }
        catch { return null; }
    }

    // ────────────────────────────────────────────────────────────────
    // Resizable BAR (ReBAR)
    // ────────────────────────────────────────────────────────────────

    private static void CheckResizableBar(List<AnalysisResult> results)
    {
        try
        {
            var gpus = WmiHelper.Query("SELECT Name FROM Win32_VideoController");
            string? modernGpuName = null;

            foreach (var gpu in gpus)
            {
                var name = WmiHelper.GetValue<string>(gpu, "Name") ?? "";
                if (IsModernDiscreteGpu(name))
                {
                    modernGpuName = name;
                    break;
                }
            }

            if (modernGpuName is null) return;

            bool? rebarEnabled = TryDetectRebarFromRegistry();

            if (rebarEnabled == true)
            {
                results.Add(new AnalysisResult("BIOS", "Resizable BAR (ReBAR)",
                    $"Resizable BAR appears to be enabled — {modernGpuName} can access full VRAM over PCIe",
                    Severity.Ok));
            }
            else if (rebarEnabled == false)
            {
                results.Add(new AnalysisResult(
                    "BIOS",
                    "Resizable BAR not enabled",
                    $"{modernGpuName} supports Resizable BAR but it does not appear to be active. " +
                    "ReBAR can improve gaming performance by up to 15%.",
                    Severity.Warning,
                    "Enable 'Resizable BAR' (NVIDIA) or 'Smart Access Memory' (AMD) in BIOS under the PCIe/PCI Express settings. " +
                    "Both BIOS and Windows must support it (Windows 10 v2004+).",
                    [new ActionStep(
                        "Enter BIOS → Advanced / PCIe settings → enable 'Above 4G Decoding' → enable 'Resizable BAR' or 'Re-Size BAR Support' → save & exit",
                        null, "Medium")]));
            }
            else
            {
                results.Add(new AnalysisResult("BIOS", "Resizable BAR (ReBAR)",
                    $"{modernGpuName} supports Resizable BAR. Could not automatically verify if it is enabled. " +
                    "Check BIOS for 'Above 4G Decoding' and 'Resizable BAR' settings.",
                    Severity.Ok));
            }
        }
        catch { }
    }

    private static bool IsModernDiscreteGpu(string name)
    {
        if (name.Contains("RTX", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("GTX 16", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("RX 5", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("RX 6", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("RX 7", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("RX 9", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("Arc", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <returns>true = enabled, false = disabled, null = unknown</returns>
    private static bool? TryDetectRebarFromRegistry()
    {
        const string gpuClassGuid = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(gpuClassGuid);
            if (classKey is null) return null;

            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                if (!int.TryParse(subKeyName, out _)) continue;

                using var deviceKey = classKey.OpenSubKey(subKeyName);
                if (deviceKey is null) continue;

                // NVIDIA: KMD_EnableInternalLargePage = 2 means ReBAR active
                var nvReBar = deviceKey.GetValue("KMD_EnableInternalLargePage");
                if (nvReBar is int nvVal) return nvVal == 2;

                // AMD: RBARFeature
                var amdRebar = deviceKey.GetValue("RBARFeature");
                if (amdRebar is int amdVal) return amdVal != 0;
            }
        }
        catch { }
        return null;
    }

    // ────────────────────────────────────────────────────────────────
    // CPU BIOS settings
    // ────────────────────────────────────────────────────────────────

    private static void CheckCpuBiosSettings(List<AnalysisResult> results)
    {
        try
        {
            var cpuData = WmiHelper.Query(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, CurrentClockSpeed, MaxClockSpeed FROM Win32_Processor");

            if (cpuData.Count == 0) return;

            var cpu = cpuData[0];
            string cpuName = WmiHelper.GetValue<string>(cpu, "Name")?.Trim() ?? "Unknown CPU";
            int cores = WmiHelper.GetValue<int>(cpu, "NumberOfCores");
            int threads = WmiHelper.GetValue<int>(cpu, "NumberOfLogicalProcessors");
            long currentMhz = WmiHelper.GetValue<long>(cpu, "CurrentClockSpeed");
            long baseMhz = WmiHelper.GetValue<long>(cpu, "MaxClockSpeed");

            // ── Always emit CPU info ──
            string clockInfo = baseMhz > 0 ? $" | Base: {baseMhz} MHz | Current: {currentMhz} MHz" : "";
            string threadInfo = cores > 0 && threads > 0 ? $" | {cores} cores / {threads} threads" : "";
            results.Add(new AnalysisResult("BIOS", "CPU configuration",
                $"{cpuName}{threadInfo}{clockInfo}",
                Severity.Ok));

            // ── Hyper-Threading / SMT ──
            if (cores > 0 && threads > 0)
            {
                if (threads == cores)
                {
                    // Logical == Physical → HT/SMT is disabled
                    results.Add(new AnalysisResult(
                        "BIOS",
                        "Hyper-Threading / SMT appears disabled",
                        $"{cores} cores, {threads} logical processors — Hyper-Threading (Intel) or SMT (AMD) may be off in BIOS.",
                        Severity.Warning,
                        "Enabling Hyper-Threading gives the OS twice as many threads, which significantly improves " +
                        "performance in multi-threaded workloads like video encoding, compiling, and heavy multitasking.",
                        [new ActionStep(
                            "Enter BIOS → CPU configuration (or Advanced CPU settings) → enable Hyper-Threading (Intel) or SMT (AMD) → save & exit",
                            null, "Medium")]));
                }
                else
                {
                    results.Add(new AnalysisResult("BIOS", "Hyper-Threading / SMT",
                        $"Enabled — {cores} cores, {threads} threads",
                        Severity.Ok));
                }
            }

            // ── Turbo Boost / Precision Boost tip ──
            // We cannot reliably detect Turbo state from a snapshot (CPU is idle, SpeedStep lowers clock).
            // Show an informational tip for non-laptops.
            bool isLaptop = WmiHelper.Query("SELECT * FROM Win32_Battery").Count > 0;
            if (!isLaptop)
            {
                results.Add(new AnalysisResult(
                    "BIOS",
                    "Turbo Boost / Precision Boost",
                    "Cannot be verified from Windows — confirm in BIOS that Turbo Boost (Intel) or Precision Boost (AMD) is enabled.",
                    Severity.Ok,
                    "Turbo Boost allows the CPU to automatically exceed its base clock during demanding workloads. " +
                    "It should be enabled for maximum single-core and gaming performance. " +
                    "Some BIOS updates or power limit changes may inadvertently disable it."));
            }
        }
        catch { }
    }
}
