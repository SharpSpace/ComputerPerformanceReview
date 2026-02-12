# Deep Freeze Diagnostic Engine

## Overview

The Deep Freeze Diagnostic Engine enhances the system monitoring tool with in-depth analysis capabilities when a process becomes unresponsive for an extended period (freeze > 5 seconds). This feature provides detailed thread-level diagnostics and root cause analysis to help identify why applications freeze or hang.

## Components

### 1. FreezeInvestigator

The `FreezeInvestigator` class is the core diagnostic engine that collects detailed information when a freeze is detected.

**Key Features:**
- Collects thread state information for all threads in the frozen process
- Tracks ThreadState (Running, Wait, etc.)
- Captures WaitReason for waiting threads
- Counts threads by state and wait reason
- Identifies dominant wait reasons (>60% occurrence)
- Applies heuristic analysis to determine likely root cause
- Creates minidumps for freezes lasting >15 seconds

**Performance:**
- Analysis completes within 200ms
- Uses direct .NET Process API (no WMI)
- Only triggered when a freeze is detected (not continuous monitoring)

### 2. FreezeReport Model

The `FreezeReport` record contains comprehensive freeze analysis data:

```csharp
public sealed record FreezeReport(
    string ProcessName,              // Name of the frozen process
    int ProcessId,                   // Process ID
    TimeSpan FreezeDuration,         // How long the freeze has lasted
    int TotalThreads,                // Total thread count
    int RunningThreads,              // Threads in Running state
    Dictionary<string, int> WaitReasonCounts,  // Wait reasons and counts
    string? DominantWaitReason,      // Most common wait reason (>60%)
    string LikelyRootCause,          // Heuristically determined cause
    string? MiniDumpPath,            // Path to dump file (if created)
    MiniDumpAnalysis? MiniDumpAnalysis  // Dump analysis results
);
```

### 3. Root Cause Heuristics

The engine applies intelligent heuristics to determine the likely cause of freezes:

| Wait Reason/Pattern | Likely Root Cause |
|-------------------|------------------|
| Executive (>60%) | Lock contention or synchronization block |
| PageIn | Memory or disk paging pressure |
| All threads waiting + CPU <20% | Deadlock or kernel object wait |
| High context switches + low CPU | Scheduler thrashing |
| High DPC time | Driver or interrupt latency issue |
| UserRequest | Waiting for user input or I/O completion |
| FreePage | Memory allocation pressure |
| VirtualMemory | Virtual memory management delay |
| >100 threads, <2 running | Thread pool starvation or async deadlock |

### 4. MiniDump Support

For freezes lasting more than 15 seconds, the engine automatically creates a minidump using Windows' `MiniDumpWriteDump` API.

**MiniDump Features:**
- P/Invoke to Windows debugging API
- Captures:
  - Thread data
  - Handle information
  - Unloaded modules
  - Process thread data
- Stored in `{LogDir}/dumps/` directory
- Filename format: `freeze_{ProcessName}_{PID}_{Timestamp}.dmp`

**Analysis:**
The basic implementation creates dumps for later analysis with tools like WinDbg or Visual Studio. A full automated analysis implementation could be added in the future.

## Integration

The Deep Freeze Diagnostic Engine is integrated into the monitoring pipeline:

1. **ProcessHealthAnalyzer** detects hanging processes (not responding)
2. **SystemHealthEngine** calls `FreezeDetector.Classify()` for basic classification
3. **FreezeInvestigator.Investigate()** performs deep analysis for freezes >5 seconds
4. Results are added to `MonitorSample.DeepFreezeReport`
5. **MonitorDisplay** shows detailed analysis in the dashboard

## Console Output Example

When a freeze is detected and analyzed, the dashboard displays:

```
  Hängande: notepad (12.3s)
            → Trolig orsak: Intern blockering (deadlock/väntan)
              
            ┌─ DJUPANALYS (Deep Freeze Investigation) ─────────────┐
              Process: notepad (PID 1234)
              Frysduration: 12.3s
              Trådar: 18 totalt, 1 körande
              Vänteorsaker:
                Executive: 14 trådar
                UserRequest: 3 trådar
              Dominerande: Executive
              Trolig grundorsak: Lock contention or synchronization block
            └──────────────────────────────────────────────────────┘
```

For freezes >15 seconds with minidump:

```
              MiniDump: freeze_notepad_1234_2026-02-12_20-35-42.dmp
                - Dump file created: 256.5 KB
```

## Usage

The feature is automatically activated during monitoring mode when a freeze is detected. No manual intervention is required.

**To trigger investigation:**
1. Run the monitoring mode: `ComputerPerformanceReview.exe --monitor 10`
2. Open an application and make it unresponsive (e.g., long-running operation on UI thread)
3. Wait for >5 seconds
4. The deep freeze analysis will appear in the dashboard

## API Documentation

### FreezeInvestigator.Investigate()

```csharp
public static FreezeReport? Investigate(
    string processName,
    int processId,
    TimeSpan freezeDuration,
    MonitorSample sample
)
```

Investigates a frozen process and returns detailed analysis.

**Parameters:**
- `processName`: Name of the frozen process
- `processId`: Process ID (PID)
- `freezeDuration`: How long the process has been frozen
- `sample`: Current system monitoring sample for correlation

**Returns:**
- `FreezeReport` with detailed analysis, or `null` if investigation failed

### MiniDumpHelper.CreateMiniDump()

```csharp
public static string? CreateMiniDump(Process process)
```

Creates a minidump file for the specified process.

**Parameters:**
- `process`: The process to dump

**Returns:**
- Path to the created dump file, or `null` if creation failed

## Testing

Manual tests are provided in `Tests/FreezeInvestigatorTests.cs`:

```csharp
// Test thread state collection
FreezeInvestigatorTests.TestCurrentProcess();

// Test minidump creation
FreezeInvestigatorTests.TestMiniDumpCreation();
```

## Limitations

1. **Windows Only**: Uses Windows-specific APIs (ProcessThread, MiniDumpWriteDump)
2. **Permissions**: May require elevated privileges for some processes
3. **Analysis Depth**: Basic minidump analysis - full stack trace analysis requires external tools
4. **Performance**: Thread enumeration may be slow for processes with thousands of threads

## Future Enhancements

Possible improvements:
- Automated minidump analysis using Windows Debugging API
- Stack trace capture and analysis
- Module stability database (known problematic DLLs)
- Historical freeze pattern detection
- Cross-platform support (limited functionality on Linux/macOS)

## Architecture Notes

The implementation follows clean, modular design principles:
- Separation of concerns (investigation, reporting, display)
- Immutable data models (records)
- Fail-safe error handling (returns null on failure)
- Performance constraints enforced (200ms target)
- No WMI dependencies (direct .NET APIs only)
