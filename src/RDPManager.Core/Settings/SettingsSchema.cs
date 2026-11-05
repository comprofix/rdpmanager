namespace RDPManager.Core.Settings;

/// <summary>
/// The first-version RDP settings (DESIGN.md, Settings and inheritance). Built-in defaults follow
/// the Windows Remote Desktop client (mstsc) where it has one. Labels drive the generated settings tab.
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

        /// <summary>Groups in display order.</summary>
        public static IReadOnlyList<string> All { get; } = [Basics, Display, Security, Gateway, Redirection, Session];
    }

    public static IReadOnlyList<SettingDefinition> All { get; } =
    [
        // Basics
        new() { Key = "description", Label = "Description", Group = Groups.Basics, Type = SettingType.Text, Default = "", Inheritable = false },
        new() { Key = "host", Label = "Host", Group = Groups.Basics, Type = SettingType.Text, Default = "", Inheritable = false, Scope = SettingScope.Connection },
        new() { Key = "port", Label = "Port", Group = Groups.Basics, Type = SettingType.Number, Default = 3389, Minimum = 1, Maximum = 65535 },
        new() { Key = "username", Label = "Username", Group = Groups.Basics, Type = SettingType.Text, Default = "" },
        new() { Key = "domain", Label = "Domain", Group = Groups.Basics, Type = SettingType.Text, Default = "" },
        new() { Key = "password", Label = "Password", Group = Groups.Basics, Type = SettingType.Secret, Default = false },

        // Display
        new()
        {
            Key = "display.resolution", Label = "Resolution", Group = Groups.Display, Type = SettingType.Choice, Default = "fit",
            Choices = ["fit", "fullScreen", "1024x768", "1280x720", "1280x1024", "1366x768", "1600x900", "1920x1080"],
            ChoiceLabels = new Dictionary<string, string>
            {
                ["fit"] = "Fit to window",
                ["fullScreen"] = "Full screen",
                ["1024x768"] = "1024 × 768",
                ["1280x720"] = "1280 × 720",
                ["1280x1024"] = "1280 × 1024",
                ["1366x768"] = "1366 × 768",
                ["1600x900"] = "1600 × 900",
                ["1920x1080"] = "1920 × 1080",
            },
        },
        new()
        {
            Key = "display.colorDepth", Label = "Colour depth", Group = Groups.Display, Type = SettingType.Choice, Default = "32",
            Choices = ["15", "16", "24", "32"],
            ChoiceLabels = new Dictionary<string, string> { ["15"] = "15-bit", ["16"] = "16-bit", ["24"] = "24-bit", ["32"] = "32-bit (highest quality)" },
        },

        // Security
        new() { Key = "security.nla", Label = "Network Level Authentication", Group = Groups.Security, Type = SettingType.Boolean, Default = true },
        new()
        {
            Key = "security.serverAuthentication", Label = "If server authentication fails", Group = Groups.Security, Type = SettingType.Choice, Default = "warn",
            Choices = ["none", "warn", "required"],
            ChoiceLabels = new Dictionary<string, string> { ["none"] = "Connect and don't warn me", ["warn"] = "Warn me", ["required"] = "Do not connect" },
        },

        // RD Gateway
        new() { Key = "gateway.host", Label = "Gateway host", Group = Groups.Gateway, Type = SettingType.Text, Default = "" },
        new()
        {
            Key = "gateway.usage", Label = "Use RD Gateway", Group = Groups.Gateway, Type = SettingType.Choice, Default = "never",
            Choices = ["never", "always", "bypassLocal"],
            ChoiceLabels = new Dictionary<string, string> { ["never"] = "Never", ["always"] = "Always", ["bypassLocal"] = "Always, except for local addresses" },
        },
        new() { Key = "gateway.sameCredentials", Label = "Use the same credentials as the server", Group = Groups.Gateway, Type = SettingType.Boolean, Default = true },
        new() { Key = "gateway.username", Label = "Gateway username", Group = Groups.Gateway, Type = SettingType.Text, Default = "" },
        new() { Key = "gateway.domain", Label = "Gateway domain", Group = Groups.Gateway, Type = SettingType.Text, Default = "" },
        new() { Key = "gateway.password", Label = "Gateway password", Group = Groups.Gateway, Type = SettingType.Secret, Default = false },

        // Redirection
        new() { Key = "redirect.clipboard", Label = "Clipboard", Group = Groups.Redirection, Type = SettingType.Boolean, Default = true },
        new() { Key = "redirect.drives", Label = "Drives", Group = Groups.Redirection, Type = SettingType.Boolean, Default = false },
        new() { Key = "redirect.printers", Label = "Printers", Group = Groups.Redirection, Type = SettingType.Boolean, Default = true },
        new()
        {
            Key = "redirect.sound", Label = "Sound", Group = Groups.Redirection, Type = SettingType.Choice, Default = "local",
            Choices = ["local", "remote", "none"],
            ChoiceLabels = new Dictionary<string, string> { ["local"] = "Play on this computer", ["remote"] = "Play on the remote computer", ["none"] = "Do not play" },
        },

        // Session
        new() { Key = "session.admin", Label = "Connect to the console (admin) session", Group = Groups.Session, Type = SettingType.Boolean, Default = false },
        new() { Key = "session.autoReconnect", Label = "Reconnect automatically", Group = Groups.Session, Type = SettingType.Boolean, Default = true },
        new()
        {
            Key = "performance.preset", Label = "Performance", Group = Groups.Session, Type = SettingType.Choice, Default = "auto",
            Choices = ["auto", "lan", "broadband", "modem"],
            ChoiceLabels = new Dictionary<string, string> { ["auto"] = "Detect automatically", ["lan"] = "LAN", ["broadband"] = "Broadband", ["modem"] = "Modem" },
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
