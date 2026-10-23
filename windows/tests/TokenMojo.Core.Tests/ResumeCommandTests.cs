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
        ResumeCommand command = ResumeCommand.Build("Ubuntu", SessionId, "/home/alice/my app", _ => true);

        // Assert
        Assert.Equal("wt.exe", command.FileName);
        Assert.Equal(
            ["-w", "new", "wsl.exe", "-d", "Ubuntu", "--cd", "/home/alice/my app", "--", "bash", "-lc", $"tm --resume {SessionId}"],
            command.Arguments);
    }

    [Fact]
    public void Build_ChecksTheFolderThroughTheDistroShare()
    {
        // Arrange
        List<string> checkedPaths = new();

        // Act
        ResumeCommand.Build("Ubuntu", SessionId, "/home/alice/my-app", path =>
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
        ResumeCommand command = ResumeCommand.Build("Ubuntu", SessionId, worktree, path => !path.Contains("worktrees"));

        // Assert
        Assert.Equal("/home/alice/my-app", CdTarget(command));
    }

    [Fact]
    public void Build_WorktreeFolderExists_KeepsTheWorktree()
    {
        // Arrange
        string worktree = "/home/alice/my-app/.claude/worktrees/fix-login";

        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", SessionId, worktree, _ => true);

        // Assert
        Assert.Equal(worktree, CdTarget(command));
    }

    [Fact]
    public void Build_WorktreeSubfolderWasRemoved_ResumesFromTheRepositoryRoot()
    {
        // Arrange
        string inside = "/home/alice/my-app/.claude/worktrees/fix-login/src";

        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", SessionId, inside, path => !path.Contains("worktrees"));

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
        ResumeCommand command = ResumeCommand.Build("Ubuntu", SessionId, cwd, _ => false);

        // Assert
        Assert.Equal("~", CdTarget(command));
    }

    [Fact]
    public void Build_RepositoryRootIsAlsoMissing_StartsInTheHomeFolder()
    {
        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", SessionId, "/home/alice/my-app/.claude/worktrees/x", _ => false);

        // Assert
        Assert.Equal("~", CdTarget(command));
    }

    [Fact]
    public void Build_FolderHasSemicolon_EscapesItSoWindowsTerminalKeepsOneCommand()
    {
        // Act
        ResumeCommand command = ResumeCommand.Build("Ubuntu", SessionId, "/home/alice/a;b", _ => true);

        // Assert
        Assert.Equal(@"/home/alice/a\;b", CdTarget(command));
        Assert.DoesNotContain(command.Arguments, argument => argument.Replace(@"\;", "").Contains(';'));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc; rm -rf ~")]
    [InlineData("abc def")]
    [InlineData("$(id)")]
    public void Build_SessionIdHasCharactersAShellWouldInterpret_Throws(string sessionId)
    {
        // Act / Assert
        Assert.Throws<ArgumentException>(() => ResumeCommand.Build("Ubuntu", sessionId, "/home/alice", _ => true));
    }

    private static string CdTarget(ResumeCommand command) => command.Arguments[command.Arguments.ToList().IndexOf("--cd") + 1];
}
