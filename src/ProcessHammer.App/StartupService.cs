using System.Diagnostics;

namespace ProcessHammer.App;

/// <summary>
/// Starts Process Hammer at logon via a Scheduled Task with "highest privileges" — because the app
/// requires admin, this launches it elevated on boot WITHOUT a UAC prompt (a plain Run key would nag
/// every login). Thin wrapper over schtasks.exe.
/// </summary>
public static class StartupService
{
    public const string TaskName = "ProcessHammerStartup";

    public static bool IsEnabled() => Run($"/query /tn \"{TaskName}\"") == 0;

    public static void Enable(string exePath)
    {
        // /tr needs the path wrapped in escaped quotes so a path with spaces launches correctly.
        var args = $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f";
        Run(args, throwOnError: true);
    }

    public static void Disable() => Run($"/delete /tn \"{TaskName}\" /f", throwOnError: true);

    private static int Run(string args, bool throwOnError = false)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (throwOnError && p.ExitCode != 0)
            throw new InvalidOperationException($"schtasks failed ({p.ExitCode}): {err.Trim()}");
        return p.ExitCode;
    }
}
