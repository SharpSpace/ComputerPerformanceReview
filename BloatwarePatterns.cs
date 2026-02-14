namespace ComputerPerformanceReview;

/// <summary>
/// Shared constants for bloatware and startup program patterns
/// </summary>
public static class BloatwarePatterns
{
    /// <summary>
    /// Common programs that are often safe to disable from autostart.
    /// These programs can be started manually when needed.
    /// </summary>
    public static readonly string[] CommonStartupBloatware = 
    {
        "Adobe", "Java", "QuickTime", "iTunes", "Spotify", 
        "Discord", "Steam", "Origin", "Epic", "Battle.net",
        "OneDrive", "Dropbox", "Google Drive", "Backup",
        "CCleaner", "WinZip", "WinRAR", "Skype",
        "Teams", "Zoom", "McAfee", "Norton", "Avast", "AVG"
    };
}
