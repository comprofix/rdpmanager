using System.Text.Json;
using System.Text.Json.Serialization;

namespace RDPManager.Core.Storage;

/// <summary>Program settings for this PC (<c>settings.json</c>), separate from the connection tree.</summary>
public sealed class ProgramSettings
{
    public const int CurrentSchemaVersion = 1;

    public static class Themes
    {
        public const string Light = "light";
        public const string Dark = "dark";
        public const string System = "system";

        public static IReadOnlyList<string> All { get; } = [Light, Dark, System];
    }

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary><see cref="Themes.Light"/>, <see cref="Themes.Dark"/> or <see cref="Themes.System"/> (default).</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = Themes.System;

    /// <summary>Properties this version does not recognise; written back unchanged.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownProperties { get; set; }

    public ProgramSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Theme = Theme,
        UnknownProperties = UnknownProperties is null ? null : new(UnknownProperties),
    };
}

/// <summary>The settings file could not be read (invalid JSON, invalid values, or a newer format). It is never overwritten.</summary>
public sealed class InvalidSettingsFileException(string path, string message, Exception? innerException = null)
    : Exception($"The settings file '{path}' could not be read: {message}", innerException)
{
    public string FilePath { get; } = path;
}

/// <summary>
/// Reads and writes <c>settings.json</c> next to <c>connections.json</c>, with the same safe saving and
/// version check. A missing file gives the defaults.
/// </summary>
public sealed class ProgramSettingsStore
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ProgramSettingsStore(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        FilePath = Path.Combine(Path.GetFullPath(folder), FileName);
    }

    public string FilePath { get; }

    public async Task<ProgramSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return new ProgramSettings();
        }

        ProgramSettings? settings;
        try
        {
            await using var stream = File.OpenRead(FilePath);
            settings = await JsonSerializer.DeserializeAsync<ProgramSettings>(stream, SerializerOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidSettingsFileException(FilePath, $"it is not valid JSON ({ex.Message}).", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidSettingsFileException(FilePath, ex.Message, ex);
        }

        if (settings is null)
        {
            throw new InvalidSettingsFileException(FilePath, "the file is empty.");
        }

        if (settings.SchemaVersion > ProgramSettings.CurrentSchemaVersion)
        {
            throw new InvalidSettingsFileException(
                FilePath,
                $"it uses format version {settings.SchemaVersion}, but this version of RDPManager supports up to version {ProgramSettings.CurrentSchemaVersion}.");
        }

        if (!ProgramSettings.Themes.All.Contains(settings.Theme))
        {
            settings.Theme = ProgramSettings.Themes.System;
        }

        return settings;
    }

    public Task SaveAsync(ProgramSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.SchemaVersion = ProgramSettings.CurrentSchemaVersion;
        return SafeFile.WriteJsonAsync(FilePath, settings, SerializerOptions, cancellationToken);
    }
}
