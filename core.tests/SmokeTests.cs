namespace OpenPolitics.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssemblyLoads()
    {
        Assert.Equal("OpenPolitics.Core", typeof(OpenPolitics.Core.CoreInfo).Assembly.GetName().Name);
    }
}
