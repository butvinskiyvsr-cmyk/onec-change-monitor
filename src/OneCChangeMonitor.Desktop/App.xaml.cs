using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace OneCChangeMonitor.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "OneCChangeMonitor.Desktop.SingleInstance", out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            MessageBox.Show("OneC Change Monitor уже запущен.", "OneC Change Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += HandleDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrashLog(args.ExceptionObject as Exception, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog(args.Exception, "TaskScheduler");
            args.SetObserved();
        };
        WriteLog("Application started.");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WriteLog($"Application stopped with code {e.ApplicationExitCode}.");
        if (_ownsMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception, "Dispatcher");
        MessageBox.Show(
            $"Приложение завершилось с ошибкой. Диагностика сохранена в:\n{LogDirectory}",
            "OneC Change Monitor",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }

    private static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OneCChangeMonitor",
        "logs");

    private static string LogPath => Path.Combine(LogDirectory, $"application-{DateTime.Today:yyyy-MM-dd}.log");

    private static void WriteCrashLog(Exception? exception, string source) =>
        WriteLog($"Unhandled exception ({source}):{Environment.NewLine}{exception}");

    private static void WriteLog(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch
        {
            // Logging must never prevent the application from starting.
        }
    }
}
