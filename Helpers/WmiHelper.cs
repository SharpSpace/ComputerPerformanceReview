namespace ComputerPerformanceReview.Helpers;

public static class WmiHelper
{
    public static List<Dictionary<string, object?>> Query(string wmiQuery, string? scope = null)
    {
        var results = new List<Dictionary<string, object?>>();
        try
        {
            using ManagementObjectSearcher searcher = scope is not null
                ? new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wmiQuery))
                : new ManagementObjectSearcher(wmiQuery);

            if (scope is not null)
                searcher.Scope.Connect();

            foreach (ManagementObject obj in searcher.Get())
            {
                var dict = new Dictionary<string, object?>();
                foreach (var prop in obj.Properties)
                {
                    dict[prop.Name] = prop.Value;
                }
                results.Add(dict);
                obj.Dispose();
            }
        }
        catch (Exception ex)
        {
            ConsoleHelper.WriteWarning($"WMI query failed: {ex.Message}");
        }
        return results;
    }

    public static T? GetValue<T>(Dictionary<string, object?> dict, string key, T? defaultValue = default)
    {
        if (dict.TryGetValue(key, out var value) && value is not null)
        {
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }
}
