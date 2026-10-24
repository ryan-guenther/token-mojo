using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class WindowsFolderTests
{
    [Theory]
    [InlineData("~")]
    [InlineData(@"~\my app")]
    [InlineData("~/my-app")]
    [InlineData(@"C:\code\my app")]
    [InlineData(@"C:\")]
    [InlineData("D:/code")]
    [InlineData(@"\\server\share\code")]
    public void IsValid_ProfileRelativeOrFullPath_IsAccepted(string folder)
    {
        // Act / Assert
        Assert.True(WindowsFolder.IsValid(folder));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("code")]
    [InlineData(@"code\app")]
    [InlineData(@"\\server")]
    [InlineData(@"\\server\")]
    [InlineData("C:code")]
    [InlineData(@"\code")]
    [InlineData("/home/alice")]
    [InlineData("~alice")]
    [InlineData(@"C:\code\""quoted""")]
    [InlineData("C:\\code\nnext")]
    public void IsValid_RelativeLinuxOrUnsafe_IsRefused(string? folder)
    {
        // Act / Assert
        Assert.False(WindowsFolder.IsValid(folder));
    }

    [Theory]
    [InlineData(@"C:\code", true)]
    [InlineData(@"~\code", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData("~", false)]
    [InlineData("~/code", false)]
    [InlineData("/srv/code", false)]
    [InlineData("code", false)]
    public void IsWindowsOnly_IsAWindowsFolderThatCannotBeReadAsALinuxOne(string folder, bool expected)
    {
        // Act / Assert
        Assert.Equal(expected, WindowsFolder.IsWindowsOnly(folder));
    }

    [Theory]
    [InlineData("~", NativePaths.Home)]
    [InlineData(@"~\my app", NativePaths.Home + @"\my app")]
    [InlineData("~/a/b", NativePaths.Home + @"\a\b")]
    [InlineData(@"D:\code", @"D:\code")]
    public void Expand_TildeIsTheProfile(string folder, string expected)
    {
        // Act / Assert
        Assert.Equal(expected, WindowsFolder.Expand(folder, NativePaths.Home));
    }

    [Theory]
    [InlineData(NativePaths.Home, "~")]
    [InlineData(NativePaths.Home + @"\", "~")]
    [InlineData(NativePaths.Home + @"\my app", @"~\my app")]
    [InlineData(@"c:\users\alice smith\code", @"~\code")]
    [InlineData(NativePaths.Home + @"son\code", NativePaths.Home + @"son\code")]
    [InlineData(@"D:\code", @"D:\code")]
    public void Abbreviate_FolderInTheProfile_IsWrittenWithATilde(string folder, string expected)
    {
        // Act / Assert
        Assert.Equal(expected, WindowsFolder.Abbreviate(folder, NativePaths.Home));
    }

    [Theory]
    [InlineData(@"C:\code\app\.claude\worktrees\pr-12", @"C:\code\app")]
    [InlineData(@"C:\code\app\.claude\worktrees\pr-12\src", @"C:\code\app")]
    [InlineData("C:/code/app/.claude/worktrees/pr-12", "C:/code/app")]
    [InlineData(@"C:\code\app", @"C:\code\app")]
    public void RepositoryRoot_FolderInAWorktree_IsTheRepositoryItWasMadeIn(string folder, string expected)
    {
        // Act / Assert
        Assert.Equal(expected, WindowsFolder.RepositoryRoot(folder));
    }
}
