using System.Collections;

namespace ProcessBooster.App.Hardware;

/// <summary>
/// Collects the process's environment variables, split into User and System (Machine) scopes,
/// as a pair of <see cref="DataTable"/>s.
/// Every read is defensive: if a target is denied or throws (e.g. the Machine scope under a
/// restricted token), that table is simply omitted rather than failing the whole collection.
/// The entry point never throws.
/// </summary>
public static class EnvironmentInfoService
{
    public static List<DataTable> CollectTables()
    {
        var tables = new List<DataTable>();
        try
        {
            AddTarget(tables, "User variables", EnvironmentVariableTarget.User);
            AddTarget(tables, "System variables", EnvironmentVariableTarget.Machine);
        }
        catch
        {
            // Total failure — return whatever (if anything) succeeded, never throw.
        }
        return tables;
    }

    private static void AddTarget(List<DataTable> tables, string title, EnvironmentVariableTarget target)
    {
        try
        {
            var rows = ToSortedRows(Environment.GetEnvironmentVariables(target));
            tables.Add(new DataTable(title, new[] { "Variable", "Value" }, rows));
        }
        catch
        {
            // Target unreadable (e.g. Machine scope denied) — skip it, keep the rest.
        }
    }

    /// <summary>Converts a variable dictionary to rows, sorted by name (case-insensitive).</summary>
    private static List<string[]> ToSortedRows(IDictionary vars)
    {
        var rows = new List<string[]>();
        foreach (DictionaryEntry e in vars)
        {
            var key = e.Key?.ToString() ?? "";
            var value = e.Value?.ToString() ?? "";
            rows.Add(new[] { key, value });
        }
        rows.Sort((a, b) => string.Compare(a[0], b[0], StringComparison.OrdinalIgnoreCase));
        return rows;
    }
}
