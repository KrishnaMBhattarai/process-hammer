namespace ProcessHammer.App.Hardware;

/// <summary>
/// A simple columnar table for list-style tabs (Startup, Software, Services, Environment).
/// Each row is a string[] aligned to <see cref="Columns"/>; the UI binds cells by index.
/// </summary>
public sealed record DataTable(string Title, IReadOnlyList<string> Columns, IReadOnlyList<string[]> Rows);
