namespace RDPManager.Core.Settings;

/// <summary>
/// The first-version RDP settings (DESIGN.md, Settings and inheritance). Built-in defaults follow
/// the Windows Remote Desktop client (mstsc) where it has one.
/// </summary>
public static class SettingsSchema
{
    public static class Groups
    {
        public const string Basics = "Basics";
        public const string Display = "Display";
        public const string Security = "Security";
        public const string Gateway = "RD Gateway";
        public const string Redirection = "Redirection";
        public const string Session = "Session";
    }

    public static IReadOnlyList<SettingDefinition> All { get; } =
    [
        // Basics
        new() { Key = "description", Group = Groups.Basics, Type = SettingType.Text, Default = "", Inheritable = false },
        new() { Key = "host", Group = Groups.Basics, Type = SettingType.Text, Default = "", Inheritable = false, Scope = SettingScope.Connection },
        new() { Key = "port", Group = Groups.Basics, Type = SettingType.Number, Default = 3389 },
        new() { Key = "username", Group = Groups.Basics, Type = SettingType.Text, Default = "" },
        new() { Key = "domain", Group = Groups.Basics, Type = SettingType.Text, Default = "" },
        new() { Key = "password", Group = Groups.Basics, Type = SettingType.Secret, Default = false },

        // Display
        new()
        {
            Key = "display.resolution", Group = Groups.Display, Type = SettingType.Choice, Default = "fit",
            Choices = ["fit", "fullScreen", "1024x768", "1280x720", "1280x1024", "1366x768", "1600x900", "1920x1080"],
        },
        new()
        {
            Key = "display.colorDepth", Group = Groups.Display, Type = SettingType.Choice, Default = "32",
            Choices = ["15", "16", "24", "32"],
        },

        // Security
        new() { Key = "security.nla", Group = Groups.Security, Type = SettingType.Boolean, Default = true },
        new()
        {
            Key = "security.serverAuthentication", Group = Groups.Security, Type = SettingType.Choice, Default = "warn",
            Choices = ["none", "warn", "required"],
        },

        // RD Gateway
        new() { Key = "gateway.host", Group = Groups.Gateway, Type = SettingType.Text, Default = "" },
        new()
        {
            Key = "gateway.usage", Group = Groups.Gateway, Type = SettingType.Choice, Default = "never",
            Choices = ["never", "always", "bypassLocal"],
        },
        new() { Key = "gateway.sameCredentials", Group = Groups.Gateway, Type = SettingType.Boolean, Default = true },
        new() { Key = "gateway.username", Group = Groups.Gateway, Type = SettingType.Text, Default = "" },
        new() { Key = "gateway.domain", Group = Groups.Gateway, Type = SettingType.Text, Default = "" },
        new() { Key = "gateway.password", Group = Groups.Gateway, Type = SettingType.Secret, Default = false },

        // Redirection
        new() { Key = "redirect.clipboard", Group = Groups.Redirection, Type = SettingType.Boolean, Default = true },
        new() { Key = "redirect.drives", Group = Groups.Redirection, Type = SettingType.Boolean, Default = false },
        new() { Key = "redirect.printers", Group = Groups.Redirection, Type = SettingType.Boolean, Default = true },
        new()
        {
            Key = "redirect.sound", Group = Groups.Redirection, Type = SettingType.Choice, Default = "local",
            Choices = ["local", "remote", "none"],
        },

        // Session
        new() { Key = "session.admin", Group = Groups.Session, Type = SettingType.Boolean, Default = false },
        new() { Key = "session.autoReconnect", Group = Groups.Session, Type = SettingType.Boolean, Default = true },
        new()
        {
            Key = "performance.preset", Group = Groups.Session, Type = SettingType.Choice, Default = "auto",
            Choices = ["auto", "lan", "broadband", "modem"],
        },
    ];

    private static readonly Dictionary<string, SettingDefinition> ByKey =
        All.ToDictionary(d => d.Key, StringComparer.Ordinal);

    /// <summary>Finds a setting by key, or null if it is not in the schema.</summary>
    public static SettingDefinition? Find(string key) => ByKey.GetValueOrDefault(key);

    /// <summary>Gets a setting by key; throws if it is not in the schema.</summary>
    public static SettingDefinition Get(string key) =>
        Find(key) ?? throw new ArgumentException($"Unknown setting '{key}'.", nameof(key));
}
