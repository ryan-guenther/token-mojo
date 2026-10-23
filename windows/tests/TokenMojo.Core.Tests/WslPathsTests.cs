using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class WslPathsTests
{
    [Fact]
    public void SharePath_LinuxPath_BecomesAFolderOnTheDistroShare()
    {
        // Act
        string path = WslPaths.SharePath("Ubuntu", "/home/alice/my app", @"\\wsl.localhost");

        // Assert
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\alice\my app", path);
    }

    [Fact]
    public void SharePath_RootFolder_IsTheDistroRoot()
    {
        // Act / Assert
        Assert.Equal(@"\\wsl.localhost\Ubuntu", WslPaths.SharePath("Ubuntu", "/", @"\\wsl.localhost"));
    }
}
