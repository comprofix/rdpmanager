using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using RDPManager.App.Services;
using RDPManager.App.ViewModels;
using RDPManager.Core.Storage;

namespace RDPManager.App;

public partial class App : Application
{
    private const string Title = "RDPManager";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

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
            var dialogs = new WindowsDialogService(() => window.TryGetPlatformHandle()?.Handle ?? 0);

            string folder;
            try
            {
                folder = StorageLocation.Resolve(desktop.Args ?? []);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                dialogs.ShowError(Title, ex.Message);
                desktop.Shutdown(2);
                return;
            }

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
            var tree = new ConnectionTreeViewModel(store, dialogs, "Personal");
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

            window.DataContext = new MainWindowViewModel(tree, dialogs, folder, CreateProgramSettingsTab, () => desktop.Shutdown());
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
