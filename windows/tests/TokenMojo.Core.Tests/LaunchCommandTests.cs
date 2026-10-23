using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class LaunchCommandTests
{
    private static readonly Guid PromptId = Guid.Parse("3636af4c-8a5c-4e7f-a26c-739e5f93af06");

    [Fact]
    public void Build_WorktreeAndPrompt_OpensANewWindowRunningTmWithBoth()
    {
        // Act
        LaunchCommand command = LaunchCommand.Build("Ubuntu", "alice", "~/my-app", "eng-42", PromptId);

        // Assert
        Assert.Equal("wt.exe", command.FileName);
        Assert.Equal(
            [
                "-w", "new", "wsl.exe", "-d", "Ubuntu", "-u", "alice", "--cd", "~/my-app", "--", "bash", "-lc",
                $"~/.local/bin/tm --wt eng-42 --prompt-file ~/.claude/cctokmon-launch/{PromptId}.txt",
            ],
            command.Arguments);
    }

    [Fact]
    public void Build_NoWorktreeAndNoPrompt_RunsPlainTm()
    {
        // Act
        LaunchCommand command = LaunchCommand.Build("Ubuntu", "alice", "~", null, null);

        // Assert
        Assert.Equal("~/.local/bin/tm", command.Arguments[^1]);
    }

    [Fact]
    public void Build_PromptOnly_LeavesOutTheWorktree()
    {
        // Act
        LaunchCommand command = LaunchCommand.Build("Ubuntu", "alice", "~", null, PromptId);

        // Assert
        Assert.Equal($"~/.local/bin/tm --prompt-file ~/.claude/cctokmon-launch/{PromptId}.txt", command.Arguments[^1]);
    }

    [Fact]
    public void Build_WorktreeOnly_LeavesOutThePromptFile()
    {
        // Act
        LaunchCommand command = LaunchCommand.Build("Ubuntu", "alice", "~", "pr-12", null);

        // Assert
        Assert.Equal("~/.local/bin/tm --wt pr-12", command.Arguments[^1]);
    }

    [Theory]
    [InlineData("~")]
    [InlineData("~/my-app")]
    [InlineData("~/my app/sub")]
    [InlineData("/srv/code")]
    public void Build_Folder_IsPassedToWslAsGiven(string folder)
    {
        // Act
        LaunchCommand command = LaunchCommand.Build("Ubuntu", "alice", folder, null, null);

        // Assert
        Assert.Equal(folder, command.Arguments[command.Arguments.ToList().IndexOf("--cd") + 1]);
    }

    [Fact]
    public void Build_FolderHasSemicolon_EscapesItSoWindowsTerminalKeepsOneCommand()
    {
        // Act
        LaunchCommand command = LaunchCommand.Build("Ubuntu", "alice", "~/a;b", "eng-1", PromptId);

        // Assert
        Assert.Contains(@"~/a\;b", command.Arguments);
        Assert.DoesNotContain(command.Arguments, argument => argument.Replace(@"\;", "").Contains(';'));
    }

    [Fact]
    public void Build_TheShellCommand_HasNoQuotesOrSemicolonsForWindowsTerminalToMisread()
    {
        // Act
        LaunchCommand command = LaunchCommand.Build("Ubuntu", "alice", "~", "eng-42", PromptId);

        // Assert
        Assert.DoesNotContain(command.Arguments[^1], character => character is '"' or '\'' or ';');
    }

    [Fact]
    public void Build_RootUserAndNoUser_AreHandledLikeResume()
    {
        // Act
        LaunchCommand root = LaunchCommand.Build("Ubuntu", "root", "~", null, null);
        LaunchCommand anyone = LaunchCommand.Build("Ubuntu", null, "~", null, null);

        // Assert
        Assert.Contains("root", root.Arguments);
        Assert.DoesNotContain("-u", anyone.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("my-app")]
    [InlineData("~other/x")]
    [InlineData(@"C:\work")]
    [InlineData("~/a\nb")]
    public void Build_FolderIsNotAHomeOrAbsolutePath_Throws(string folder)
    {
        // Act / Assert
        Assert.Throws<ArgumentException>(() => LaunchCommand.Build("Ubuntu", "alice", folder, null, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a;b")]
    [InlineData("$(id)")]
    [InlineData("a/b")]
    [InlineData("-w")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("eng-42\n")]
    [InlineData("naïve")]
    public void Build_WorktreeNameTmWouldRefuse_Throws(string worktree)
    {
        // Act / Assert
        Assert.Throws<ArgumentException>(() => LaunchCommand.Build("Ubuntu", "alice", "~", worktree, null));
    }

    [Theory]
    [InlineData("eng-42")]
    [InlineData("pr-12")]
    [InlineData("fix_login.v2")]
    [InlineData("A")]
    public void Build_WorktreeNameTmAccepts_IsAccepted(string worktree)
    {
        // Act
        LaunchCommand command = LaunchCommand.Build("Ubuntu", "alice", "~", worktree, null);

        // Assert
        Assert.Equal($"~/.local/bin/tm --wt {worktree}", command.Arguments[^1]);
    }
}
