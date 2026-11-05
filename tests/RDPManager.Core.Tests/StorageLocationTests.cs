using RDPManager.Core.Storage;

namespace RDPManager.Core.Tests;

public class StorageLocationTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "RDPManager.Tests");
    private static readonly string FromArgs = Path.Combine(Root, "args");
    private static readonly string FromEnv = Path.Combine(Root, "env");
    private static readonly string AppData = Path.Combine(Root, "appdata");

    private static string Resolve(string[] args, string? env) =>
        StorageLocation.Resolve(args, name => name == StorageLocation.EnvironmentVariable ? env : null, () => AppData);

    [Fact]
    public void CommandLine_WinsOverEnvironment()
    {
        Assert.Equal(FromArgs, Resolve(["--config", FromArgs], FromEnv));
    }

    [Fact]
    public void CommandLine_AcceptsEqualsForm()
    {
        Assert.Equal(FromArgs, Resolve([$"--config={FromArgs}"], null));
    }

    [Fact]
    public void Environment_WinsOverAppData()
    {
        Assert.Equal(FromEnv, Resolve([], FromEnv));
    }

    [Fact]
    public void Default_IsAppDataPlusProductName()
    {
        Assert.Equal(Path.Combine(AppData, "RDPManager"), Resolve([], null));
        Assert.Equal(Path.Combine(AppData, "RDPManager"), Resolve(["--other"], "   "));
    }

    [Theory]
    [InlineData("--config")]
    [InlineData("--config=")]
    public void CommandLine_WithoutFolder_Throws(string arg)
    {
        Assert.Throws<ArgumentException>(() => Resolve([arg], null));
        Assert.Throws<ArgumentException>(() => Resolve(["--config", "--verbose"], null));
    }

    [Fact]
    public void RelativeFolder_IsMadeAbsolute()
    {
        Assert.True(Path.IsPathFullyQualified(Resolve(["--config", "relative-folder"], null)));
    }
}
