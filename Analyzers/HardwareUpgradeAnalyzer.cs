namespace ComputerPerformanceReview.Analyzers;

public sealed class HardwareUpgradeAnalyzer : IAnalyzer
{
    public string Name => "Hardware Upgrades";

    public Task<AnalysisReport> AnalyzeAsync()
    {
        var results = new List<AnalysisResult>();

        AnalyzeRamUpgradePath(results);

        return Task.FromResult(new AnalysisReport("HARDWARE UPGRADES", results));
    }

    private static void AnalyzeRamUpgradePath(List<AnalysisResult> results)
    {
        try
        {
            // ── Query installed sticks ──
            var dimms = WmiHelper.Query(
                "SELECT Manufacturer, PartNumber, Capacity, Speed FROM Win32_PhysicalMemory");

            if (dimms.Count == 0) return;

            long totalCapacityBytes = 0;
            long ratedSpeedMhz = 0;
            string partNumber = "";
            string manufacturer = "";

            foreach (var dimm in dimms)
            {
                totalCapacityBytes += WmiHelper.GetValue<long>(dimm, "Capacity");
                long speed = WmiHelper.GetValue<long>(dimm, "Speed");
                if (speed > ratedSpeedMhz) ratedSpeedMhz = speed;

                // Use part number from first stick that has one
                if (string.IsNullOrWhiteSpace(partNumber))
                {
                    partNumber = (WmiHelper.GetValue<string>(dimm, "PartNumber") ?? "")
                        .Trim().TrimEnd('\0').Trim();
                    manufacturer = (WmiHelper.GetValue<string>(dimm, "Manufacturer") ?? "")
                        .Trim().TrimEnd('\0').Trim();
                }
            }

            int usedSlots = dimms.Count;
            long currentTotalGb = totalCapacityBytes / (1024L * 1024 * 1024);

            // ── Query motherboard slot information ──
            var memArrayData = WmiHelper.Query(
                "SELECT MemoryDevices, MaxCapacityEx, MaxCapacity FROM Win32_PhysicalMemoryArray");

            int totalSlots = 0;
            long maxTotalGb = 0;

            if (memArrayData.Count > 0)
            {
                totalSlots = WmiHelper.GetValue<int>(memArrayData[0], "MemoryDevices");

                // MaxCapacityEx is in KB (supports > 4 TB), MaxCapacity is also in KB but caps at 4 GB
                long maxCapacityExKb = WmiHelper.GetValue<long>(memArrayData[0], "MaxCapacityEx");
                if (maxCapacityExKb > 0)
                {
                    maxTotalGb = maxCapacityExKb / (1024L * 1024);
                }
                else
                {
                    long maxCapacityKb = WmiHelper.GetValue<long>(memArrayData[0], "MaxCapacity");
                    maxTotalGb = maxCapacityKb / (1024L * 1024);
                }
            }

            int freeSlots = totalSlots > 0 ? totalSlots - usedSlots : 0;

            // ── Query current RAM usage ──
            double ramUsedPct = 0;
            var osData = WmiHelper.Query(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            if (osData.Count > 0)
            {
                long totalKb = WmiHelper.GetValue<long>(osData[0], "TotalVisibleMemorySize");
                long freeKb = WmiHelper.GetValue<long>(osData[0], "FreePhysicalMemory");
                if (totalKb > 0)
                    ramUsedPct = (double)(totalKb - freeKb) / totalKb * 100;
            }

            // ── Always emit inventory result ──
            string speedLabel = ratedSpeedMhz > 0 ? $" DDR4-{ratedSpeedMhz} MHz" : "";
            string slotInfo = totalSlots > 0
                ? $" | {usedSlots} of {totalSlots} slots used"
                : $" | {usedSlots} stick(s) installed";
            string maxInfo = maxTotalGb > 0 ? $" | Max supported: {maxTotalGb} GB" : "";

            results.Add(new AnalysisResult(
                "Hardware",
                "RAM configuration",
                $"{currentTotalGb} GB{speedLabel}{slotInfo}{maxInfo}",
                Severity.Ok));

            // ── Upgrade recommendation ──
            string stickId = !string.IsNullOrWhiteSpace(partNumber)
                ? partNumber
                : $"{manufacturer} {currentTotalGb / usedSlots} GB {ratedSpeedMhz} MHz";

            if (freeSlots > 0 && ramUsedPct >= 70 && maxTotalGb > currentTotalGb)
            {
                var severity = ramUsedPct >= 85 ? Severity.Critical : Severity.Warning;
                long perStickGb = currentTotalGb / usedSlots;
                long potentialMaxGb = Math.Min(maxTotalGb, currentTotalGb + freeSlots * perStickGb);

                results.Add(new AnalysisResult(
                    "Hardware",
                    "RAM upgrade recommended",
                    $"RAM usage is {ramUsedPct:F0}%. {freeSlots} slot(s) are free — " +
                    $"capacity could be expanded from {currentTotalGb} GB to up to {potentialMaxGb} GB.",
                    severity,
                    "Adding more RAM of the same model ensures compatibility and optimal dual/quad-channel performance.",
                    [
                        new ActionStep(
                            $"Buy: {stickId} ({ratedSpeedMhz} MHz) — same model as currently installed",
                            null, "Medium"),
                        new ActionStep(
                            "Verify compatibility on your motherboard's QVL (Qualified Vendor List) at the manufacturer's website",
                            null, "Easy")
                    ]));
            }
            else if (freeSlots == 0 && totalSlots > 0 && ramUsedPct >= 85)
            {
                results.Add(new AnalysisResult(
                    "Hardware",
                    "RAM slots full — upgrade requires replacement",
                    $"All {totalSlots} RAM slots are occupied and RAM usage is {ramUsedPct:F0}%. " +
                    "To get more RAM you must replace the existing sticks with higher-capacity modules.",
                    Severity.Warning,
                    $"Consider replacing {usedSlots} × {currentTotalGb / usedSlots} GB sticks with larger capacity modules. " +
                    $"Your motherboard supports up to {maxTotalGb} GB total.",
                    [
                        new ActionStep(
                            $"Look for higher-capacity RAM that matches your speed ({ratedSpeedMhz} MHz) and check the motherboard QVL",
                            null, "Medium")
                    ]));
            }
        }
        catch { }
    }
}
