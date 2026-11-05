using System.Text.Json.Nodes;
using RDPManager.Core.Storage;

namespace RDPManager.Core.Tests;

public sealed class ProgramSettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RDPManager.Tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, ProgramSettingsStore.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private void WriteFile(string json)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, json);
    }

    [Fact]
    public async Task MissingFile_GivesDefaults()
    {
        var settings = await new ProgramSettingsStore(_folder).LoadAsync();

        Assert.Equal(ProgramSettings.Themes.System, settings.Theme);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public async Task Theme_RoundTripsAndKeepsUnknownProperties()
    {
        WriteFile("""{ "schemaVersion": 1, "theme": "light", "future": [1, 2] }""");
        var store = new ProgramSettingsStore(_folder);

        var settings = await store.LoadAsync();
        settings.Theme = ProgramSettings.Themes.Dark;
        await store.SaveAsync(settings);

        var saved = JsonNode.Parse(File.ReadAllText(FilePath))!;
        Assert.Equal("dark", saved["theme"]!.GetValue<string>());
        Assert.Equal(2, saved["future"]![1]!.GetValue<int>());
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public async Task UnknownTheme_FallsBackToSystem()
    {
        WriteFile("""{ "schemaVersion": 1, "theme": "purple" }""");

        Assert.Equal(ProgramSettings.Themes.System, (await new ProgramSettingsStore(_folder).LoadAsync()).Theme);
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("""{ "schemaVersion": 2, "theme": "dark" }""")]
    public async Task UnreadableOrNewerFile_IsRefusedAndLeftUnchanged(string json)
    {
        WriteFile(json);

        var ex = await Assert.ThrowsAsync<InvalidSettingsFileException>(() => new ProgramSettingsStore(_folder).LoadAsync());

        Assert.Equal(FilePath, ex.FilePath);
        Assert.Equal(json, File.ReadAllText(FilePath));
    }
}
