namespace RDPManager.Core.Storage;

/// <summary>
/// Where RDPManager keeps its files: <c>--config &lt;folder&gt;</c> (or <c>--config=&lt;folder&gt;</c>) on the
/// command line, otherwise the <c>RDPMANAGER_CONFIG</c> environment variable, otherwise the per-user
/// application data folder plus <c>RDPManager</c> (<c>%APPDATA%\RDPManager</c> on Windows). A portable copy
/// passes its own <c>Data</c> folder, which is used before the application data folder.
/// </summary>
public static class StorageLocation
{
    public const string CommandLineOption = "--config";
    public const string EnvironmentVariable = "RDPMANAGER_CONFIG";

    /// <summary>Resolves the folder for this process.</summary>
    public static string Resolve(IReadOnlyList<string> args, string? portableFolder = null) =>
        Resolve(
            args,
            Environment.GetEnvironmentVariable,
            () => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            portableFolder);

    /// <summary>Resolves the folder from explicit inputs (used by tests).</summary>
    public static string Resolve(
        IReadOnlyList<string> args,
        Func<string, string?> getEnvironmentVariable,
        Func<string> getAppDataFolder,
        string? portableFolder = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        var fromArgs = FromArguments(args);
        if (fromArgs is not null)
        {
            return Path.GetFullPath(fromArgs);
        }

        var fromEnvironment = getEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment.Trim());
        }

        if (!string.IsNullOrWhiteSpace(portableFolder))
        {
            return Path.GetFullPath(portableFolder);
        }

        var appData = getAppDataFolder();
        if (string.IsNullOrWhiteSpace(appData))
        {
            throw new InvalidOperationException(
                $"The user's application data folder could not be found. Use {CommandLineOption} <folder> or set {EnvironmentVariable}.");
        }

        return Path.GetFullPath(Path.Combine(appData, AppInfo.ProductName));
    }

    private static string? FromArguments(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.StartsWith(CommandLineOption + "=", StringComparison.OrdinalIgnoreCase))
            {
                return RequireValue(arg[(CommandLineOption.Length + 1)..]);
            }

            if (string.Equals(arg, CommandLineOption, StringComparison.OrdinalIgnoreCase))
            {
                return RequireValue(i + 1 < args.Count ? args[i + 1] : null);
            }
        }

        return null;
    }

    private static string RequireValue(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal)
            ? throw new ArgumentException($"{CommandLineOption} needs a folder, for example {CommandLineOption} C:\\Temp\\RDPManager-test.")
            : value.Trim();
}
