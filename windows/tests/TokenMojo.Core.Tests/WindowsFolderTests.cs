using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class WindowsFolderTests
{
    private const string Home = @"C:\Users\Ann Lee";

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
    [InlineData("~", Home)]
    [InlineData(@"~\my app", @"C:\Users\Ann Lee\my app")]
    [InlineData("~/a/b", @"C:\Users\Ann Lee\a\b")]
    [InlineData(@"D:\code", @"D:\code")]
    public void Expand_TildeIsTheProfile(string folder, string expected)
    {
        // Act / Assert
        Assert.Equal(expected, WindowsFolder.Expand(folder, Home));
    }

    [Theory]
    [InlineData(Home, "~")]
    [InlineData(@"C:\Users\Ann Lee\", "~")]
    [InlineData(@"C:\Users\Ann Lee\my app", @"~\my app")]
    [InlineData(@"c:\users\ann lee\code", @"~\code")]
    [InlineData(@"C:\Users\Ann Leeds\code", @"C:\Users\Ann Leeds\code")]
    [InlineData(@"D:\code", @"D:\code")]
    public void Abbreviate_FolderInTheProfile_IsWrittenWithATilde(string folder, string expected)
    {
        // Act / Assert
        Assert.Equal(expected, WindowsFolder.Abbreviate(folder, Home));
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
