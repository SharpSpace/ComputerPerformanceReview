namespace ComputerPerformanceReview.Tests;

/// <summary>
/// Manual test harness for validating SysinternalsHelper functionality.
/// These tests verify that the helper can download and manage Sysinternals tools.
/// </summary>
public static class SysinternalsHelperTests
{
    /// <summary>
    /// Tests the ability to check tool status
    /// </summary>
    public static async Task TestGetToolStatus()
    {
        Console.WriteLine("=== SysinternalsHelper.GetToolStatus Test ===\n");

        var statuses = await SysinternalsHelper.GetToolStatusAsync();
        
        if (statuses.Count == 0)
        {
            Console.WriteLine("❌ No tools found in registry");
            return;
        }

        Console.WriteLine($"✓ Found {statuses.Count} tools\n");

        foreach (var tool in statuses)
        {
            var status = tool.IsInstalled ? "✓ Installed" : "✗ Not installed";
            Console.WriteLine($"{status,-20} {tool.Name,-15} - {tool.Description}");
        }

        Console.WriteLine("\n=== Test Completed Successfully ===");
    }

    /// <summary>
    /// Tests downloading a tool (Handle.exe as an example)
    /// This test will actually download from Microsoft if not cached
    /// </summary>
    public static async Task TestDownloadTool()
    {
        Console.WriteLine("=== SysinternalsHelper.GetToolPath (Download) Test ===\n");
        Console.WriteLine("Attempting to get Handle.exe...");

        var toolPath = await SysinternalsHelper.GetToolPathAsync("handle");

        if (toolPath == null)
        {
            Console.WriteLine("❌ Failed to get tool path");
            return;
        }

        if (!File.Exists(toolPath))
        {
            Console.WriteLine($"❌ Tool path returned but file doesn't exist: {toolPath}");
            return;
        }

        Console.WriteLine($"✓ Tool available at: {toolPath}");
        Console.WriteLine($"✓ File size: {new FileInfo(toolPath).Length / 1024} KB");

        Console.WriteLine("\n=== Test Completed Successfully ===");
    }

    /// <summary>
    /// Tests running Handle.exe on the current process
    /// </summary>
    public static async Task TestRunHandle()
    {
        Console.WriteLine("=== SysinternalsHelper.RunHandle Test ===\n");

        var currentProcess = Process.GetCurrentProcess();
        Console.WriteLine($"Testing with current process: {currentProcess.ProcessName} (PID: {currentProcess.Id})");
        Console.WriteLine("This will download Handle.exe if not already cached...\n");

        var handleInfo = await SysinternalsHelper.RunHandleAsync(currentProcess.Id, currentProcess.ProcessName);

        if (handleInfo == null)
        {
            Console.WriteLine("❌ RunHandle returned null");
            Console.WriteLine("Note: This may be expected if the tool couldn't be downloaded or run.");
            return;
        }

        Console.WriteLine("✓ Handle.exe executed successfully\n");
        Console.WriteLine($"Process: {handleInfo.ProcessName} (PID: {handleInfo.ProcessId})");
        Console.WriteLine($"Total Handles: {handleInfo.TotalHandles}");

        if (handleInfo.HandleTypeBreakdown.Count > 0)
        {
            Console.WriteLine("\nHandle Type Breakdown:");
            foreach (var kvp in handleInfo.HandleTypeBreakdown.OrderByDescending(x => x.Value).Take(10))
            {
                Console.WriteLine($"  {kvp.Key,-20} {kvp.Value,6} handles");
            }
        }
        else
        {
            Console.WriteLine("\n⚠ No handle type breakdown available");
        }

        Console.WriteLine("\n=== Test Completed Successfully ===");
    }

    /// <summary>
    /// Tests the complete workflow
    /// </summary>
    public static async Task TestCompleteWorkflow()
    {
        Console.WriteLine("=== Complete Sysinternals Workflow Test ===\n");

        await TestGetToolStatus();
        Console.WriteLine("\n" + new string('─', 70) + "\n");

        await TestDownloadTool();
        Console.WriteLine("\n" + new string('─', 70) + "\n");

        await TestRunHandle();

        Console.WriteLine("\n=== All Tests Completed ===");
    }
}
