using System.IO;
using System.Windows;
using System.Windows.Threading;
using ProcessBooster.App.ViewModels;
using ProcessBooster.Core.Config;
using ProcessBooster.Core.Logging;
using ProcessBooster.Core.Models;
using ProcessBooster.Core.Services;

namespace ProcessBooster.App;

public partial class App : Application
{
    private static readonly string StartupLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProcessBooster", "startup.log");

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

        var engine = new RuleEngine(() => config, inspector, controller, log);
        var vm = new MainViewModel(config, store, inspector, controller, topology, engine, log);

        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();
        vm.Start();
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
