using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using WpfWindow = System.Windows.Window;
using WindowState = System.Windows.WindowState;

namespace ProcessHammer.App;

/// <summary>
/// System-tray presence: keeps Process Hammer running (so rules stay enforced) when the window is
/// closed, and offers Show / "Start with Windows" / Exit. Uses the WinForms NotifyIcon, which the WPF
/// dispatcher pumps for us — no separate message loop needed.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notify;
    private readonly WpfWindow _window;
    private readonly Action _exit;
    private readonly ToolStripMenuItem _startupItem;

    public TrayIcon(WpfWindow window, Action exit)
    {
        _window = window;
        _exit = exit;

        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup())
        {
            Checked = SafeIsStartupEnabled(),
            CheckOnClick = false,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Show Process Hammer", null, (_, _) => ShowWindow()) { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => _exit()));

        _notify = new NotifyIcon
        {
            Text = "Process Hammer",
            Visible = true,
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
        };
        _notify.DoubleClick += (_, _) => ShowWindow();
    }

    /// <summary>Show a one-time hint the first time the window is hidden to the tray.</summary>
    public void ShowRunningHint() =>
        _notify.ShowBalloonTip(3000, "Process Hammer",
            "Still running in the tray — rules stay active. Right-click the icon to exit.", ToolTipIcon.Info);

    private void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
    }

    private void ToggleStartup()
    {
        try
        {
            if (StartupService.IsEnabled()) StartupService.Disable();
            else StartupService.Enable(Environment.ProcessPath!);
            _startupItem.Checked = StartupService.IsEnabled();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Couldn't change the startup setting:\n" + ex.Message,
                "Process Hammer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private static bool SafeIsStartupEnabled()
    {
        try { return StartupService.IsEnabled(); } catch { return false; }
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try { return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application; }
        catch { return System.Drawing.SystemIcons.Application; }
    }

    public void Dispose()
    {
        _notify.Visible = false;
        _notify.Dispose();
    }
}
