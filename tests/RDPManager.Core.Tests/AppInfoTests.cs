using RDPManager.Core;

namespace RDPManager.Core.Tests;

public class AppInfoTests
{
    [Fact]
    public void ProductName_IsRDPManager()
    {
        Assert.Equal("RDPManager", AppInfo.ProductName);
    }

    [Fact]
    public void Version_IsTheBuildVersionWithoutSourceDetails()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", AppInfo.Version);
    }
}
