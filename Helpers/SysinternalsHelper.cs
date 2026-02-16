namespace ComputerPerformanceReview.Helpers;

/// <summary>
/// Helper class for managing and executing Sysinternals tools.
/// Downloads tools from Microsoft on demand and caches them locally.
/// </summary>
public static class SysinternalsHelper
{
    private static readonly string ToolsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ComputerPerformanceReview", "Sysinternals");

    private static readonly Dictionary<string, SysinternalsToolInfo> AvailableTools = new()
    {
        ["handle"] = new SysinternalsToolInfo(
            "handle.exe",
            "Handle",
            "https://download.sysinternals.com/files/Handle.zip",
            "Lists open handles for processes"
        ),
        ["rammap"] = new SysinternalsToolInfo(
            "RAMMap.exe",
            "RAMMap",
            "https://download.sysinternals.com/files/RAMMap.zip",
            "Shows detailed memory usage breakdown"
        ),
        ["procdump"] = new SysinternalsToolInfo(
            "procdump.exe",
            "ProcDump",
            "https://download.sysinternals.com/files/Procdump.zip",
            "Creates process memory dumps"
        ),
        ["poolmon"] = new SysinternalsToolInfo(
            "poolmon.exe",
            "PoolMon",
            "https://download.sysinternals.com/files/PoolMon.zip",
            "Monitors kernel pool allocations"
        ),
        ["diskext"] = new SysinternalsToolInfo(
            "diskext.exe",
            "DiskExt",
            "https://download.sysinternals.com/files/DiskExt.zip",
            "Shows volume disk mapping"
        )
    };

    /// <summary>
    /// Ensures the tools directory exists
    /// </summary>
    private static void EnsureToolsDirectory()
    {
        if (!Directory.Exists(ToolsDirectory))
            Directory.CreateDirectory(ToolsDirectory);
    }

    /// <summary>
    /// Gets the full path to a Sysinternals tool, downloading if necessary
    /// </summary>
    public static async Task<string?> GetToolPathAsync(string toolKey)
    {
        if (!AvailableTools.TryGetValue(toolKey, out var toolInfo))
            return null;

        EnsureToolsDirectory();
        var toolPath = Path.Combine(ToolsDirectory, toolInfo.ExeName);

        if (File.Exists(toolPath))
            return toolPath;

        // Tool not found, try to download
        try
        {
            await DownloadToolAsync(toolInfo);
            return File.Exists(toolPath) ? toolPath : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Executes DiskExt to show volume to disk mapping
    /// </summary>
    public static async Task<string?> RunDiskExtAsync()
    {
        var toolPath = await GetToolPathAsync("diskext");
        if (toolPath == null)
            return null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                Arguments = "-accepteula",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                return null;

            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is UnauthorizedAccessException || ex is InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads and extracts a Sysinternals tool
    /// </summary>
    private static async Task DownloadToolAsync(SysinternalsToolInfo toolInfo)
    {
        var zipPath = Path.Combine(ToolsDirectory, $"{toolInfo.Name}.zip");

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            var response = await httpClient.GetAsync(toolInfo.DownloadUrl);
            response.EnsureSuccessStatusCode();
            
            await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs);
            await fs.FlushAsync();

            // Extract the zip file
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, ToolsDirectory, overwriteFiles: true);
        }
        finally
        {
            // Clean up zip file
            if (File.Exists(zipPath))
            {
                try { File.Delete(zipPath); } catch { }
            }
        }
    }

    /// <summary>
    /// Executes Handle.exe to get detailed handle information for a process
    /// </summary>
    public static async Task<HandleInfo?> RunHandleAsync(int processId, string processName)
    {
        var toolPath = await GetToolPathAsync("handle");
        if (toolPath == null)
            return null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                Arguments = $"-p {processId} -nobanner -accepteula",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                return null;

            return ParseHandleOutput(output, processId, processName);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is UnauthorizedAccessException)
        {
            // Expected exceptions when tool isn't available or access is denied
            return null;
        }
    }

    /// <summary>
    /// Parses Handle.exe output to extract handle information
    /// </summary>
    private static HandleInfo ParseHandleOutput(string output, int processId, string processName)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var handleTypes = new Dictionary<string, int>();
        int totalHandles = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.Contains("No matching handles found"))
                continue;

            // Handle.exe output format: "  <handle>: <type> <details>"
            // Example: "  1C: File          C:\Windows\System32\en-US\kernel32.dll.mui"
            var parts = trimmed.Split(new[] { ':' }, 2);
            if (parts.Length < 2)
                continue;

            var typeAndDetails = parts[1].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (typeAndDetails.Length == 0)
                continue;

            var handleType = typeAndDetails[0];
            totalHandles++;

            if (handleTypes.ContainsKey(handleType))
                handleTypes[handleType]++;
            else
                handleTypes[handleType] = 1;
        }

        return new HandleInfo(processId, processName, totalHandles, handleTypes);
    }

    /// <summary>
    /// Executes ProcDump to create a memory dump of a hanging process
    /// </summary>
    public static async Task<string?> RunProcDumpAsync(int processId, string processName, string reason)
    {
        var toolPath = await GetToolPathAsync("procdump");
        if (toolPath == null)
            return null;

        try
        {
            var dumpsDir = Path.Combine(ToolsDirectory, "Dumps");
            if (!Directory.Exists(dumpsDir))
                Directory.CreateDirectory(dumpsDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var dumpFileName = $"{processName}_{processId}_{timestamp}.dmp";
            var dumpPath = Path.Combine(dumpsDir, dumpFileName);

            var startInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                Arguments = $"-ma {processId} \"{dumpPath}\" -accepteula",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            // Wait up to 60 seconds for the dump to complete
            var completed = await Task.Run(() => process.WaitForExit(60000));
            
            if (!completed)
            {
                try { process.Kill(); } catch { }
                return null;
            }

            return File.Exists(dumpPath) ? dumpPath : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is UnauthorizedAccessException || ex is InvalidOperationException)
        {
            // Expected exceptions when tool isn't available, access is denied, or process already exited
            return null;
        }
    }

    /// <summary>
    /// Executes RAMMap to get detailed memory analysis
    /// </summary>
    public static async Task<RamMapInfo?> RunRamMapAsync()
    {
        var toolPath = await GetToolPathAsync("rammap");
        if (toolPath == null)
            return null;

        try
        {
            // RAMMap doesn't have command-line output mode, so we can only verify it exists
            // For now, return basic info that it's available
            return new RamMapInfo(File.Exists(toolPath));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is UnauthorizedAccessException || ex is IOException)
        {
            // Expected exceptions when accessing file system
            return null;
        }
    }

    /// <summary>
    /// Gets a list of all available tools and their status
    /// </summary>
    public static async Task<List<ToolStatus>> GetToolStatusAsync()
    {
        var statuses = new List<ToolStatus>();

        foreach (var (key, info) in AvailableTools)
        {
            var toolPath = Path.Combine(ToolsDirectory, info.ExeName);
            var isInstalled = File.Exists(toolPath);
            statuses.Add(new ToolStatus(key, info.Name, info.Description, isInstalled, toolPath));
        }

        return statuses;
    }

    /// <summary>
    /// Executes PoolMon to analyze kernel pool usage
    /// Note: Requires administrative privileges
    /// </summary>
    public static async Task<PoolMonInfo?> RunPoolMonAsync()
    {
        var toolPath = await GetToolPathAsync("poolmon");
        if (toolPath == null)
            return null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                Arguments = "/c /b /n 1",  // Capture, batch mode, single iteration
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            
            // Wait up to 10 seconds
            var completed = await Task.Run(() => process.WaitForExit(10000));
            
            if (!completed)
            {
                try { process.Kill(); } catch { }
                return null;
            }

            if (process.ExitCode != 0)
                return null;

            return ParsePoolMonOutput(output);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is UnauthorizedAccessException || ex is InvalidOperationException)
        {
            // Expected exceptions when tool isn't available, requires admin privileges, or process issues
            return null;
        }
    }

    /// <summary>
    /// Parses PoolMon output to extract top pool allocators
    /// </summary>
    private static PoolMonInfo ParsePoolMonOutput(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var topAllocations = new List<PoolAllocation>();
        
        // PoolMon output format (skip header lines):
        // Tag  Type    Allocs     Frees    Diff   Bytes       Per Alloc
        bool inDataSection = false;
        
        foreach (var line in lines)
        {
            if (line.Contains("Tag") && line.Contains("Type") && line.Contains("Allocs"))
            {
                inDataSection = true;
                continue;
            }
            
            if (!inDataSection || string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 7)
                continue;

            try
            {
                var tag = parts[0];
                var type = parts[1];
                if (!long.TryParse(parts[5], out var bytes))
                    continue;

                topAllocations.Add(new PoolAllocation(tag, type, bytes));
            }
            catch { }
        }

        return new PoolMonInfo(
            topAllocations.OrderByDescending(a => a.Bytes).Take(10).ToList()
        );
    }
}

/// <summary>
/// Information about a Sysinternals tool
/// </summary>
public record SysinternalsToolInfo(
    string ExeName,
    string Name,
    string DownloadUrl,
    string Description
);

/// <summary>
/// Handle information from Handle.exe
/// </summary>
public record HandleInfo(
    int ProcessId,
    string ProcessName,
    int TotalHandles,
    Dictionary<string, int> HandleTypeBreakdown
);

/// <summary>
/// RAMMap information
/// </summary>
public record RamMapInfo(
    bool IsAvailable
);

/// <summary>
/// Tool installation status
/// </summary>
public record ToolStatus(
    string Key,
    string Name,
    string Description,
    bool IsInstalled,
    string Path
);

/// <summary>
/// PoolMon analysis results
/// </summary>
public record PoolMonInfo(
    List<PoolAllocation> TopAllocations
);

/// <summary>
/// Individual pool allocation entry
/// </summary>
public record PoolAllocation(
    string Tag,
    string Type,
    long Bytes
);
