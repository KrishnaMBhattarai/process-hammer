using System.Collections;

namespace ProcessBooster.App.Hardware;

/// <summary>
/// Collects the process's environment variables, split into User and System (Machine) scopes.
/// Every read is defensive: if a target is denied or throws (e.g. the Machine scope under a
/// restricted token), that section is simply omitted rather than failing the whole collection.
/// </summary>
public static class EnvironmentInfoService
{
    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection>();
        AddTarget(sections, "User variables", EnvironmentVariableTarget.User);
        AddTarget(sections, "System variables", EnvironmentVariableTarget.Machine);
        return sections;
    }

    private static void AddTarget(List<InfoSection> sections, string title, EnvironmentVariableTarget target)
    {
        try
        {
            var s = new InfoSection { Title = title };
            s.Items.AddRange(ToSortedRows(Environment.GetEnvironmentVariables(target)));
            sections.Add(s);
        }
        catch
        {
            // Target unreadable (e.g. Machine scope denied) — skip it, keep the rest.
        }
    }

    /// <summary>Converts a variable dictionary to rows, sorted by name (case-insensitive).</summary>
    private static List<InfoItem> ToSortedRows(IDictionary vars)
    {
        var rows = new List<InfoItem>();
        foreach (DictionaryEntry e in vars)
        {
            var key = e.Key?.ToString() ?? "";
            var value = e.Value?.ToString() ?? "";
            rows.Add(new(key, value));
        }
        rows.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        return rows;
    }
}
