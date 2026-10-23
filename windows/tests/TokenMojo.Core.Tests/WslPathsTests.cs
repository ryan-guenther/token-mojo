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
    public void SharePath_PathWithoutLeadingSlash_IsStillSeparatedFromTheDistroName()
    {
        // Act
        string path = WslPaths.SharePath("Ubuntu", "home/alice", @"\\wsl.localhost");

        // Assert
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\alice", path);
    }

    [Fact]
    public void TmInstallPath_OrdinaryUser_IsUnderTheirHomeFolder()
    {
        // Act
        string? path = WslPaths.TmInstallPath("Ubuntu", "alice", @"\\wsl.localhost");

        // Assert
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\alice\.local\bin\tm", path);
    }

    [Fact]
    public void TmInstallPath_RootUser_IsUnderRootsHome()
    {
        // Act
        string? path = WslPaths.TmInstallPath("Ubuntu", "root", @"\\wsl.localhost");

        // Assert
        Assert.Equal(@"\\wsl.localhost\Ubuntu\root\.local\bin\tm", path);
    }

    [Fact]
    public void TmInstallPath_UserNotKnown_IsNull()
    {
        // Act / Assert
        Assert.Null(WslPaths.TmInstallPath("Ubuntu", null, @"\\wsl.localhost"));
    }

    [Fact]
    public void SharePath_RootFolder_IsTheDistroRoot()
    {
        // Act / Assert
        Assert.Equal(@"\\wsl.localhost\Ubuntu", WslPaths.SharePath("Ubuntu", "/", @"\\wsl.localhost"));
    }
}
