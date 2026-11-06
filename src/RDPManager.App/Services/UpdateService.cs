using Velopack;
using Velopack.Sources;

namespace RDPManager.App.Services;

/// <summary>
/// Updates from GitHub Releases (DESIGN: Packaging and releases). Nothing here runs unless the user asks
/// (Help › Check for updates) or turned on "Check for updates when RDPManager starts".
/// </summary>
public interface IUpdateService
{
    /// <summary>False when RDPManager was not installed by its installer or portable zip (for example a development build).</summary>
    bool CanUpdate { get; }

    /// <summary>Returns the newer version, or null when this is the latest.</summary>
    Task<string?> CheckAsync();

    /// <summary>Downloads the version found by <see cref="CheckAsync"/>, then exits, updates and restarts RDPManager.</summary>
    Task DownloadAndRestartAsync();
}

/// <summary>Velopack with GitHub Releases on comprofix/rdpmanager (published releases only).</summary>
public sealed class VelopackUpdateService : IUpdateService
{
    public const string RepositoryUrl = "https://github.com/comprofix/rdpmanager";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _found;

    public bool CanUpdate => _manager.IsInstalled;

    /// <summary>
    /// For the portable zip: its <c>Data</c> folder (next to <c>Update.exe</c>, outside the folder that updates
    /// replace), so connections, settings and logs travel with it. Null for installed and development copies.
    /// </summary>
    public static string? PortableDataFolder()
    {
        try
        {
            return Velopack.Locators.VelopackLocator.IsCurrentSet
                && Velopack.Locators.VelopackLocator.Current is { IsPortable: true, RootAppDir: { Length: > 0 } root }
                ? Path.Combine(root, "Data")
                : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    public async Task<string?> CheckAsync()
    {
        _found = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _found?.TargetFullRelease.Version.ToString();
    }

    public async Task DownloadAndRestartAsync()
    {
        if (_found is null)
        {
            throw new InvalidOperationException("Check for updates first.");
        }

        await _manager.DownloadUpdatesAsync(_found).ConfigureAwait(false);
        _manager.ApplyUpdatesAndRestart(_found.TargetFullRelease);
    }
}
