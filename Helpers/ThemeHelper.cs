using System.Windows;
using Microsoft.Win32;

namespace ComputerPerformanceReview.Helpers;

public static class ThemeHelper
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryKey = "AppsUseLightTheme";

    public static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            var value = key?.GetValue(RegistryKey);
            return value is int i && i == 1;
        }
        catch
        {
            return false; // Default to dark
        }
    }

    public static void ApplyTheme(Application app, bool light)
    {
        var themePath = light
            ? "Themes/LightTheme.xaml"
            : "Themes/DarkTheme.xaml";

        var themeDict = new ResourceDictionary
        {
            Source = new Uri(themePath, UriKind.Relative)
        };

        // Theme dict is always at index 0
        if (app.Resources.MergedDictionaries.Count > 0)
            app.Resources.MergedDictionaries[0] = themeDict;
        else
            app.Resources.MergedDictionaries.Insert(0, themeDict);
    }

    public static void StartListening(Application app)
    {
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category == UserPreferenceCategory.General)
            {
                app.Dispatcher.Invoke(() => ApplyTheme(app, IsLightTheme()));
            }
        };
    }
}
