using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class ResumeCommandTests
{
    private const string SessionId = "3636af4c-8a5c-4e7f-a26c-739e5f93af06";
    private const string Share = @"\\wsl.localhost";

    [Fact]
    public void Build_FolderExists_OpensANewWindowInThatFolderResumingTheSession()
    {
        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", "alice", SessionId, "/home/alice/my app", _ => true);

        // Assert
        Assert.Equal("wt.exe", command.FileName);
        Assert.Equal(
            [
                "-w", "new", "wsl.exe", "-d", "Ubuntu", "-u", "alice", "--cd", "/home/alice/my app", "--", "bash", "-lc",
                $"~/.local/bin/tm --resume {SessionId}",
            ],
            command.Arguments);
    }

    [Fact]
    public void BuildNative_FolderExists_OpensANewWindowThereWithGitBashResumingTheSession()
    {
        // Act
        ResumeCommand command = ResumeCommand.BuildNative(NativePaths.Bash, SessionId, @"C:\code\my app", _ => true, NativePaths.Home);

        // Assert
        Assert.Equal("wt.exe", command.FileName);
        Assert.Equal(
            ["-w", "new", "-p", TerminalProfile.Name, "-d", @"C:\code\my app", NativePaths.Bash, "-c", $"exec ~/.local/bin/tm --resume {SessionId}"],
            command.Arguments);
    }

    [Fact]
    public void BuildNative_WorktreeWasRemoved_ResumesFromTheRepositoryRoot()
    {
        // Act
        ResumeCommand command = ResumeCommand.BuildNative(
            NativePaths.Bash, SessionId, @"C:\code\app\.claude\worktrees\pr-12", path => path == @"C:\code\app", NativePaths.Home);

        // Assert
        Assert.Equal(@"C:\code\app", command.Arguments[5]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\gone")]
    [InlineData("/home/alice/linux-path")]
    public void BuildNative_NoFolderLeft_OpensInTheProfile(string? cwd)
    {
        // Act
        ResumeCommand command = ResumeCommand.BuildNative(NativePaths.Bash, SessionId, cwd, path => path == "/home/alice/linux-path", NativePaths.Home);

        // Assert
        Assert.Equal(NativePaths.Home, command.Arguments[5]);
    }

    [Fact]
    public void BuildNative_NotASessionId_IsRefused()
    {
        // Act / Assert
        Assert.Throws<ArgumentException>(() => ResumeCommand.BuildNative(NativePaths.Bash, "abc; rm -rf ~", @"C:\code", _ => true, NativePaths.Home));
    }

    [Fact]
    public void Build_ChecksTheFolderThroughTheDistroShare()
    {
        // Arrange
        List<string> checkedPaths = new();

        // Act
        ResumeCommand.Build("Ubuntu", "alice", SessionId, "/home/alice/my-app", path =>
        {
            checkedPaths.Add(path);
            return true;
        });

        // Assert
        Assert.Equal([$@"{Share}\Ubuntu\home\alice\my-app"], checkedPaths);
    }

    [Fact]
    public void Build_WorktreeWasRemoved_ResumesFromTheRepositoryRoot()
    {
        // Arrange
        string worktree = "/home/alice/my-app/.claude/worktrees/fix-login";

        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", "alice", SessionId, worktree, path => !path.Contains("worktrees"));

        // Assert
        Assert.Equal("/home/alice/my-app", CdTarget(command));
    }

    [Fact]
    public void Build_WorktreeFolderExists_KeepsTheWorktree()
    {
        // Arrange
        string worktree = "/home/alice/my-app/.claude/worktrees/fix-login";

        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", "alice", SessionId, worktree, _ => true);

        // Assert
        Assert.Equal(worktree, CdTarget(command));
    }

    [Fact]
    public void Build_WorktreeSubfolderWasRemoved_ResumesFromTheRepositoryRoot()
    {
        // Arrange
        string inside = "/home/alice/my-app/.claude/worktrees/fix-login/src";

        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", "alice", SessionId, inside, path => !path.Contains("worktrees"));

        // Assert
        Assert.Equal("/home/alice/my-app", CdTarget(command));
    }

    [Theory]
    [InlineData("/home/alice/gone")]
    [InlineData(null)]
    [InlineData("")]
    public void Build_FolderIsMissingAndNotAWorktree_StartsInTheHomeFolder(string? cwd)
    {
        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", "alice", SessionId, cwd, _ => false);

        // Assert
        Assert.Equal("~", CdTarget(command));
    }

    [Fact]
    public void Build_RepositoryRootIsAlsoMissing_StartsInTheHomeFolder()
    {
        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", "alice", SessionId, "/home/alice/my-app/.claude/worktrees/x", _ => false);

        // Assert
        Assert.Equal("~", CdTarget(command));
    }

    [Fact]
    public void Build_FolderHasSemicolon_EscapesItSoWindowsTerminalKeepsOneCommand()
    {
        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", "alice", SessionId, "/home/alice/a;b", _ => true);

        // Assert
        Assert.Equal(@"/home/alice/a\;b", CdTarget(command));
        Assert.DoesNotContain(command.Arguments, argument => argument.Replace(@"\;", "").Contains(';'));
    }

    [Fact]
    public void Build_NoUser_LeavesOutTheUserArgument()
    {
        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", null, SessionId, "/home/alice", _ => true);

        // Assert
        Assert.DoesNotContain("-u", command.Arguments);
    }

    [Fact]
    public void Build_RootUser_RunsAsRoot()
    {
        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", "root", SessionId, "/root", _ => true);

        // Assert
        Assert.Equal("root", command.Arguments[command.Arguments.ToList().IndexOf("-u") + 1]);
    }

    [Fact]
    public void Build_CallsTmByHomeRelativePathWithoutQuotes_SoTheLoginShellNeedsNoPathAndWtNeedsNoEscaping()
    {
        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", "alice", SessionId, "/home/alice", _ => true);

        // Assert
        Assert.DoesNotContain(command.Arguments, argument => argument.Contains('"') || argument.Contains('\''));
        Assert.StartsWith("~/.local/bin/tm --resume ", command.Arguments[^1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc; rm -rf ~")]
    [InlineData("abc def")]
    [InlineData("$(id)")]
    [InlineData("abc-123")]
    [InlineData("-3636af4c-8a5c-4e7f-a26c-739e5f93af06")]
    [InlineData("3636af4c-8a5c-4e7f-a26c-739e5f93af06\n")]
    [InlineData("3636af4c-8a5c-4e7f-a26c-739e5f93af0g")]
    [InlineData("3636af4c8a5c4e7fa26c739e5f93af06")]
    public void Build_SessionIdIsNotAUuid_Throws(string sessionId)
    {
        // Act / Assert
        Assert.Throws<ArgumentException>(() => ResumeCommand.Build("Ubuntu", "alice", sessionId, "/home/alice", _ => true));
    }

    [Fact]
    public void Build_UppercaseUuid_IsAccepted()
    {
        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", "alice", SessionId.ToUpperInvariant(), "/home/alice", _ => true);

        // Assert
        Assert.EndsWith(SessionId.ToUpperInvariant(), command.Arguments[^1]);
    }

    private static string CdTarget(ResumeCommand command) => command.Arguments[command.Arguments.ToList().IndexOf("--cd") + 1];
}
