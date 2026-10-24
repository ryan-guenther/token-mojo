using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class GitBashTests
{
    private static readonly Dictionary<string, string> StandardEnvironment = new()
    {
        ["ProgramW6432"] = @"C:\Program Files",
        ["ProgramFiles"] = @"C:\Program Files",
        ["ProgramFiles(x86)"] = @"C:\Program Files (x86)",
        ["LOCALAPPDATA"] = NativePaths.Home + @"\AppData\Local",
    };

    [Fact]
    public void Find_StandardInstall_IsItsBinBash()
    {
        // Act
        string? bash = GitBash.Find(Env(StandardEnvironment), path => path == NativePaths.Bash);

        // Assert
        Assert.Equal(NativePaths.Bash, bash);
    }

    [Fact]
    public void Find_PerUserInstall_IsFoundUnderLocalAppData()
    {
        // Arrange
        const string perUser = NativePaths.Home + @"\AppData\Local\Programs\Git\bin\bash.exe";

        // Act
        string? bash = GitBash.Find(Env(StandardEnvironment), path => path == perUser);

        // Assert
        Assert.Equal(perUser, bash);
    }

    [Fact]
    public void Find_ClaudeCodeIsToldWhereBashIs_ThatOneWinsOverAStandardInstall()
    {
        // Arrange
        Dictionary<string, string> environment = new(StandardEnvironment) { ["CLAUDE_CODE_GIT_BASH_PATH"] = @"""D:\Tools\Git\bin\bash.exe""" };

        // Act
        string? bash = GitBash.Find(Env(environment), _ => true);

        // Assert
        Assert.Equal(@"D:\Tools\Git\bin\bash.exe", bash);
    }

    [Fact]
    public void Find_ConfiguredUsrBinBash_PrefersTheBinBashBesideIt()
    {
        // Arrange
        Dictionary<string, string> environment = new() { ["CLAUDE_CODE_GIT_BASH_PATH"] = @"D:\Tools\Git\usr\bin\bash.exe" };
        List<string> asked = new();

        // Act
        string? bash = GitBash.Find(Env(environment), path =>
        {
            asked.Add(path);
            return true;
        });

        // Assert
        Assert.Equal(@"D:\Tools\Git\bin\bash.exe", bash);
        Assert.Equal([@"D:\Tools\Git\bin\bash.exe"], asked);
    }

    [Fact]
    public void Find_ConfiguredUsrBinBashWithoutBinBash_IsUsedAsItIs()
    {
        // Arrange
        Dictionary<string, string> environment = new() { ["CLAUDE_CODE_GIT_BASH_PATH"] = @"D:\Tools\Git\usr\bin\bash.exe" };

        // Act
        string? bash = GitBash.Find(Env(environment), path => path == @"D:\Tools\Git\usr\bin\bash.exe");

        // Assert
        Assert.Equal(@"D:\Tools\Git\usr\bin\bash.exe", bash);
    }

    [Fact]
    public void Find_ConfiguredUsrBinBashWithForwardSlashes_PrefersTheBinBashBesideIt()
    {
        // Arrange
        Dictionary<string, string> environment = new() { ["CLAUDE_CODE_GIT_BASH_PATH"] = "D:/Tools/Git/usr/bin/bash.exe" };

        // Act
        string? bash = GitBash.Find(Env(environment), _ => true);

        // Assert
        Assert.Equal(@"D:\Tools\Git\bin\bash.exe", bash);
    }

    [Theory]
    [InlineData(@"H:\", @"H:\")]
    [InlineData(@"\\files\home\alice", @"\\files\home\alice")]
    [InlineData(null, NativePaths.Home)]
    [InlineData("", NativePaths.Home)]
    [InlineData("/c/Users/alice", NativePaths.Home)]
    [InlineData("~", NativePaths.Home)]
    public void Home_IsHomeWhenItIsAFullWindowsPathElseTheProfile(string? home, string expected)
    {
        // Arrange
        Dictionary<string, string> environment = home is null ? new() : new() { ["HOME"] = home };

        // Act / Assert
        Assert.Equal(expected, GitBash.Home(Env(environment), NativePaths.Home));
    }

    [Fact]
    public void Find_NothingInstalled_IsNull()
    {
        // Act / Assert
        Assert.Null(GitBash.Find(Env(StandardEnvironment), _ => false));
    }

    private static Func<string, string?> Env(Dictionary<string, string> values) =>
        name => values.TryGetValue(name, out string? value) ? value : null;
}
