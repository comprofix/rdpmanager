using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using RDPManager.App.Services;
using RDPManager.App.ViewModels;
using RDPManager.Core;
using RDPManager.Core.Credentials;
using RDPManager.Core.Logging;
using RDPManager.Core.Storage;

namespace RDPManager.App;

public partial class App : Application
{
    private const string Title = "RDPManager";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>The application log, once the data folder is known.</summary>
    public static IAppLog Log { get; private set; } = NullLog.Instance;

    /// <summary>Opens a folder in File Explorer.</summary>
    public static void OpenFolder(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    /// <summary>Applies a program-settings theme value ("light", "dark" or "system").</summary>
    public static void ApplyTheme(string theme)
    {
        if (Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                ProgramSettings.Themes.Light => ThemeVariant.Light,
                ProgramSettings.Themes.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var nativeDialogs = new WindowsDialogService(() => window.TryGetPlatformHandle()?.Handle ?? 0);

            string folder;
            try
            {
                folder = StorageLocation.Resolve(desktop.Args ?? []);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                nativeDialogs.ShowError(Title, ex.Message);
                desktop.Shutdown(2);
                return;
            }

            // Log file and crash logging (DESIGN: Log file). Every error shown in a dialog is logged too.
            var log = new FileLog(folder);
            Log = log;
            log.Write(LogLevel.Info, $"{AppInfo.ProductName} {AppInfo.Version} started. Data folder: {folder}");
            var dialogs = new LoggingDialogService(nativeDialogs, log);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                log.Write(LogLevel.Error, $"Unhandled error (the app will close):\n{e.ExceptionObject}");
            TaskScheduler.UnobservedTaskException += (_, e) =>
                log.Write(LogLevel.Error, $"Unhandled background error:\n{e.Exception}");
            desktop.Exit += (_, _) => log.Write(LogLevel.Info, $"{AppInfo.ProductName} closed.");

            // Program settings: a bad settings.json is reported but does not stop the app; it is not overwritten.
            var settingsStore = new ProgramSettingsStore(folder);
            var settings = new ProgramSettings();
            var canSaveSettings = true;
            try
            {
                settings = Task.Run(() => settingsStore.LoadAsync()).GetAwaiter().GetResult();
            }
            catch (InvalidSettingsFileException ex)
            {
                canSaveSettings = false;
                dialogs.ShowError(Title, $"{ex.Message}\n\nDefault program settings are used, and the file is not changed.");
            }

            ApplyTheme(settings.Theme);

            var store = new JsonFileStore(folder);
            var tree = new ConnectionTreeViewModel(store, new CredentialService(new WindowsCredentialStore()), dialogs, "Personal");
            try
            {
                // Load off the UI thread so the store's awaits cannot deadlock on it.
                tree.SetTree(Task.Run(() => store.LoadAsync()).GetAwaiter().GetResult());
            }
            catch (InvalidConnectionFileException ex)
            {
                dialogs.ShowError(Title, $"{ex.Message}\n\nThe file has not been changed.");
                desktop.Shutdown(1);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                dialogs.ShowError(Title, $"The connection file in '{folder}' could not be opened.\n\n{ex.Message}");
                desktop.Shutdown(1);
                return;
            }

            ProgramSettingsTabViewModel CreateProgramSettingsTab()
            {
                var tab = new ProgramSettingsTabViewModel(settingsStore, settings, dialogs, ApplyTheme, canSaveSettings);
                tab.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(TabViewModel.IsModified) && !tab.IsModified)
                    {
                        settings = tab.Saved; // Reopening the tab shows what was last saved.
                    }
                };
                return tab;
            }

            static IDisposable StartTimer(TimeSpan interval, Action tick) =>
                Avalonia.Threading.DispatcherTimer.Run(() => { tick(); return true; }, interval);

            // Closing the main window (which asks first) ends the app, including undocked session windows.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var messages = new MessagesViewModel(log, log.Folder, action => Avalonia.Threading.Dispatcher.UIThread.Post(action), OpenFolder);
            window.DataContext = new MainWindowViewModel(tree, dialogs, folder, CreateProgramSettingsTab, StartTimer, window.Close, log, messages);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
