using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ProcessBooster.App.Hardware;
using ProcessBooster.App.ViewModels;
using ProcessBooster.Core.Config;
using ProcessBooster.Core.Logging;
using ProcessBooster.Core.Models;
using ProcessBooster.Core.Services;
using Wpf.Ui.Appearance;

namespace ProcessBooster.App;

public partial class App : Application
{
    private static readonly string StartupLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProcessBooster", "startup.log");

    private TrayIcon? _tray;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Never let an unexpected error kill the app silently — surface + log it.
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogStartup("AppDomain: " + args.ExceptionObject);

        try { StartCore(); }
        catch (Exception ex)
        {
            LogStartup("Startup failed: " + ex);
            MessageBox.Show(ex.ToString(), "Process Booster — startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void LogStartup(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StartupLog)!);
            File.AppendAllText(StartupLog, $"{DateTime.Now:o}  {message}{Environment.NewLine}");
        }
        catch { }
    }

    private void StartCore()
    {

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProcessBooster");
        Directory.CreateDirectory(dir);

        var log = new ActionLog(Path.Combine(dir, "activity.log"));
        var topology = new CpuTopology();
        var controller = new ProcessController(topology, new GpuPreferenceStore());
        var inspector = new ProcessInspector();
        var store = new ConfigStore(ConfigStore.DefaultPath());

        AppConfig config;
        try { config = store.Load(); }
        catch (Exception ex) { log.Error($"Config load failed; starting empty: {ex.Message}"); config = new AppConfig(); }

        var engine = new RuleEngine(() => config, inspector.Snapshot, controller.ApplyRule, log);
        var monitor = new LiveMonitor();
        var power = new PowerService();

        // Cool violet accent (looks great on the dark Mica surface) instead of the default grey.
        ApplicationAccentColorManager.Apply(Color.FromRgb(0x8B, 0x5C, 0xF6), ApplicationTheme.Dark);

        var vm = new MainViewModel(config, store, inspector, controller, topology, engine, log, monitor, power);

        var window = new MainWindow { DataContext = vm };
        MainWindow = window;

        // Live in the system tray: closing the window hides it (rules keep being enforced); the tray
        // menu offers Show / Start-with-Windows / Exit.
        _tray = new TrayIcon(window, RequestExit);
        var hintShown = false;
        window.Closing += (_, e) =>
        {
            if (_exiting) return;
            e.Cancel = true;
            window.Hide();
            if (!hintShown) { _tray.ShowRunningHint(); hintShown = true; }
        };

        window.Show();
        vm.Start();
    }

    /// <summary>Real quit (from the tray "Exit"): let the window actually close, then shut down.</summary>
    private void RequestExit()
    {
        _exiting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }

    private bool _errorShown;

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogStartup("Dispatcher: " + e.Exception);
        if (!_errorShown)
        {
            _errorShown = true;
            MessageBox.Show(e.Exception.Message, "Process Booster — unexpected error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true; // keep the app alive; subsequent errors are logged, not popped
    }
}
