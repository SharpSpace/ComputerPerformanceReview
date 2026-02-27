using System.Security.Principal;
using System.Windows;

namespace ComputerPerformanceReview;

public partial class App : Application
{
    public static bool IsAdmin { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check admin
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        IsAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);

        // Apply OS theme
        Helpers.ThemeHelper.ApplyTheme(this, Helpers.ThemeHelper.IsLightTheme());
        Helpers.ThemeHelper.StartListening(this);
    }

    public static void RestartAsAdmin()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);
            Current.Shutdown();
        }
        catch
        {
            // User declined UAC
        }
    }
}
