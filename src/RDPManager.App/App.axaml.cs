using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RDPManager.App.Services;
using RDPManager.App.ViewModels;
using RDPManager.Core.Storage;

namespace RDPManager.App;

public partial class App : Application
{
    private const string Title = "RDPManager";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

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

            window.DataContext = new MainWindowViewModel(tree, folder, () => desktop.Shutdown());
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
