using System.Security.Principal;
using ProcessBooster.App;
using Xunit;

namespace ProcessBooster.Tests;

/// <summary>
/// Functional test of the start-with-Windows logon task. Needs elevation to create a "highest
/// privileges" task; when not elevated it no-ops. The machine's real setting is snapshotted and
/// restored so the test never changes the user's actual startup preference.
/// </summary>
public class StartupServiceTests
{
    private static bool IsAdmin() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    [Fact]
    public void EnableThenDisable_RoundTrips()
    {
        if (!IsAdmin()) return; // scheduled-task creation requires admin; skip otherwise

        var original = StartupService.IsEnabled();
        try
        {
            StartupService.Enable(Environment.ProcessPath!);
            Assert.True(StartupService.IsEnabled());

            StartupService.Disable();
            Assert.False(StartupService.IsEnabled());
        }
        finally
        {
            if (original) StartupService.Enable(Environment.ProcessPath!);
            else if (StartupService.IsEnabled()) StartupService.Disable();
        }
    }
}
