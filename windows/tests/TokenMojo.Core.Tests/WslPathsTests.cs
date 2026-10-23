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

    [Theory]
    [InlineData(@"\\wsl.localhost\Ubuntu\home\alice\my app", "/home/alice/my app")]
    [InlineData(@"\\wsl$\Ubuntu\home\alice\my-app", "/home/alice/my-app")]
    [InlineData(@"\\WSL.LOCALHOST\ubuntu\srv\code\", "/srv/code")]
    [InlineData(@"\\wsl.localhost\Ubuntu", "/")]
    [InlineData(@"\\wsl.localhost\Ubuntu\", "/")]
    public void ToLinuxPath_FolderOnTheDistroShare_IsItsLinuxPath(string windowsPath, string expected)
    {
        // Act / Assert
        Assert.Equal(expected, WslPaths.ToLinuxPath(windowsPath, "Ubuntu"));
    }

    [Theory]
    [InlineData(@"\\wsl.localhost\Debian\home\alice")]
    [InlineData(@"\\wsl.localhost\UbuntuX\home\alice")]
    [InlineData(@"C:\Users\alice\code")]
    [InlineData(@"\\server\share\Ubuntu\home")]
    [InlineData(@"\\wsl.localhost\")]
    [InlineData("/home/alice")]
    [InlineData("")]
    public void ToLinuxPath_AnythingElse_IsNull(string windowsPath)
    {
        // Act / Assert
        Assert.Null(WslPaths.ToLinuxPath(windowsPath, "Ubuntu"));
    }
}
