using System.Runtime.InteropServices;
using System.Text;

namespace ComputerPerformanceReview.Helpers;

/// <summary>
/// Helper class for creating and analyzing process minidumps.
/// Uses P/Invoke to leverage Windows MiniDumpWriteDump API.
/// </summary>
public static class MiniDumpHelper
{
    private const int MaxDumpFiles = 10;
    // MiniDump type flags
    [Flags]
    private enum MINIDUMP_TYPE : uint
    {
        MiniDumpNormal = 0x00000000,
        MiniDumpWithDataSegs = 0x00000001,
        MiniDumpWithFullMemory = 0x00000002,
        MiniDumpWithHandleData = 0x00000004,
        MiniDumpFilterMemory = 0x00000008,
        MiniDumpScanMemory = 0x00000010,
        MiniDumpWithUnloadedModules = 0x00000020,
        MiniDumpWithIndirectlyReferencedMemory = 0x00000040,
        MiniDumpFilterModulePaths = 0x00000080,
        MiniDumpWithProcessThreadData = 0x00000100,
        MiniDumpWithPrivateReadWriteMemory = 0x00000200,
        MiniDumpWithoutOptionalData = 0x00000400,
        MiniDumpWithFullMemoryInfo = 0x00000800,
        MiniDumpWithThreadInfo = 0x00001000,
        MiniDumpWithCodeSegs = 0x00002000
    }

    [DllImport("Dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint ProcessId,
        IntPtr hFile,
        MINIDUMP_TYPE DumpType,
        IntPtr ExceptionParam,
        IntPtr UserStreamParam,
        IntPtr CallbackParam);

    /// <summary>
    /// Creates a minidump file for the specified process.
    /// </summary>
    /// <param name="process">Process to dump</param>
    /// <returns>Path to created minidump file, or null if failed</returns>
    public static string? CreateMiniDump(Process process)
    {
        try
        {
            // Create dumps directory
            var dumpsDir = Path.Combine(LogHelper.GetLogDir(), "dumps");
            if (!Directory.Exists(dumpsDir))
                Directory.CreateDirectory(dumpsDir);

            // Generate filename
            var fileName = $"freeze_{process.ProcessName}_{process.Id}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.dmp";
            var filePath = Path.Combine(dumpsDir, fileName);

            // Create dump file
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            
            var dumpType = MINIDUMP_TYPE.MiniDumpWithDataSegs
                         | MINIDUMP_TYPE.MiniDumpWithHandleData
                         | MINIDUMP_TYPE.MiniDumpWithThreadInfo
                         | MINIDUMP_TYPE.MiniDumpWithProcessThreadData
                         | MINIDUMP_TYPE.MiniDumpWithUnloadedModules;

            bool success = MiniDumpWriteDump(
                process.Handle,
                (uint)process.Id,
                fileStream.SafeFileHandle.DangerousGetHandle(),
                dumpType,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (success)
            {
                PruneOldDumps(dumpsDir);
                return filePath;
            }
            else
            {
                // Clean up failed dump
                try { File.Delete(filePath); } catch { }
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create minidump: {ex.Message}");
            return null;
        }
    }

    private static void PruneOldDumps(string dumpsDir)
    {
        try
        {
            var dumpFiles = Directory.GetFiles(dumpsDir, "*.dmp")
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.CreationTimeUtc)
                .ToList();

            if (dumpFiles.Count <= MaxDumpFiles)
                return;

            foreach (var file in dumpFiles.Skip(MaxDumpFiles))
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Analyzes a minidump file and extracts diagnostic information.
    /// This is a simplified analysis - full analysis would require Windows Debugging Tools.
    /// </summary>
    /// <param name="dumpPath">Path to the dump file</param>
    /// <returns>Analysis results or null if failed</returns>
    public static MiniDumpAnalysis? AnalyzeMiniDump(string dumpPath)
    {
        try
        {
            // For a basic implementation, we just verify the file exists and return basic info
            // A full implementation would use dbghelp.dll or Windows Debugging API
            if (!File.Exists(dumpPath))
                return null;

            var fileInfo = new FileInfo(dumpPath);
            
            // Return basic analysis - in a real implementation, this would parse the dump
            return new MiniDumpAnalysis(
                FaultingModule: "Analysis requires Windows Debugging Tools",
                ExceptionCode: null,
                FaultingThreadId: null,
                LoadedModules: new List<string> { $"Dump file created: {fileInfo.Length} bytes" },
                StackTraces: new List<string> { "Use WinDbg or Visual Studio to analyze this dump file" },
                FlaggedModules: new List<string>()
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to analyze minidump: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the directory where minidumps are stored.
    /// </summary>
    public static string GetDumpsDirectory()
    {
        return Path.Combine(LogHelper.GetLogDir(), "dumps");
    }
}
