using RDPManager.Core;

namespace RDPManager.Core.Tests;

public class AppInfoTests
{
    [Fact]
    public void ProductName_IsRDPManager()
    {
        Assert.Equal("RDPManager", AppInfo.ProductName);
    }
}
