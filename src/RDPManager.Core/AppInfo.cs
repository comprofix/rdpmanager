namespace RDPManager.Core;

/// <summary>
/// Product identity shared by the application and its platform-specific parts.
/// </summary>
public static class AppInfo
{
    /// <summary>The product name, used for the window title and the per-user data folder.</summary>
    public const string ProductName = "RDPManager";

    /// <summary>
    /// The version being run, for example "0.1.0" (from the build's <c>Version</c>; release builds set it from the
    /// tag). Source-control details after "+" are dropped.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        return informational?.Split('+')[0] ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
