using Microsoft.Win32;
using ProcessHammer.Core.Models;

namespace ProcessHammer.Core.Services;

/// <summary>
/// Reads/writes the per-application GPU preference at
/// HKCU\Software\Microsoft\DirectX\UserGpuPreferences. The value name is the full exe path and the
/// data is a "GpuPreference=N;" string. Windows applies it when the app next launches.
/// </summary>
public sealed class GpuPreferenceStore
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public void Set(string exePath, GpuPreference preference)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open UserGpuPreferences key.");

        if (preference == GpuPreference.SystemDefault)
        {
            // "System default" = remove any override for this exe.
            if (key.GetValue(exePath) is not null) key.DeleteValue(exePath, throwOnMissingValue: false);
            return;
        }

        // Preserve other tokens (e.g. AutoHDREnable) already present in the value.
        var tokens = ParseTokens(key.GetValue(exePath) as string);
        tokens["GpuPreference"] = ((int)preference).ToString();
        key.SetValue(exePath, Compose(tokens), RegistryValueKind.String);
    }

    public GpuPreference Get(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        var tokens = ParseTokens(key?.GetValue(exePath) as string);
        return tokens.TryGetValue("GpuPreference", out var v) && int.TryParse(v, out var n) && Enum.IsDefined(typeof(GpuPreference), n)
            ? (GpuPreference)n
            : GpuPreference.SystemDefault;
    }

    // Value format: "Key1=Val1;Key2=Val2;" — order-preserving parse/compose.
    private static Dictionary<string, string> ParseTokens(string? value)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value)) return dict;
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) dict[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }
        return dict;
    }

    private static string Compose(Dictionary<string, string> tokens) =>
        string.Concat(tokens.Select(kv => $"{kv.Key}={kv.Value};"));
}
