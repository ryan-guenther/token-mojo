using System.ComponentModel;
using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class SessionLauncherTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Launch_Prompt_IsWrittenBeforeTheTerminalStartsAndLeftForTmToDelete()
    {
        // Arrange
        LaunchRequest request = new("Ubuntu", "alice", "~/my-app", "eng-42", "fix the build; then test");
        List<string> promptWhenStarted = new();

        // Act
        SessionLauncher.Launch(request, _temp.Path, command =>
        {
            promptWhenStarted.AddRange(Directory.GetFiles(_temp.Path).Select(File.ReadAllText));
            Assert.Contains("--prompt-file ~/.claude/cctokmon-launch/", command.Arguments[^1]);
        });

        // Assert
        Assert.Equal(["fix the build; then test"], promptWhenStarted);
        Assert.Single(Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public void Launch_PromptFileNameInTheCommand_IsTheFileThatWasWritten()
    {
        // Arrange
        LaunchRequest request = new("Ubuntu", "alice", "~", null, "hello");
        string? shellCommand = null;

        // Act
        SessionLauncher.Launch(request, _temp.Path, command => shellCommand = command.Arguments[^1]);

        // Assert
        string written = Path.GetFileName(Directory.GetFiles(_temp.Path).Single());
        Assert.EndsWith($"/{written}", shellCommand);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n\t ")]
    public void Launch_NoPrompt_WritesNoFileAndPassesNoPromptFile(string prompt)
    {
        // Arrange
        LaunchRequest request = new("Ubuntu", "alice", "~", null, prompt);
        TerminalCommand? started = null;

        // Act
        SessionLauncher.Launch(request, _temp.Path, command => started = command);

        // Assert
        Assert.Empty(Directory.GetFiles(_temp.Path));
        Assert.Equal("~/.local/bin/tm", started!.Arguments[^1]);
    }

    [Fact]
    public void Launch_TerminalFailsToStart_DeletesThePromptFileAndRethrows()
    {
        // Arrange
        LaunchRequest request = new("Ubuntu", "alice", "~", null, "hello");

        // Act
        Win32Exception error = Assert.Throws<Win32Exception>(
            () => SessionLauncher.Launch(request, _temp.Path, _ => throw new Win32Exception("wt.exe not found")));

        // Assert
        Assert.Contains("wt.exe", error.Message);
        Assert.Empty(Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public void Launch_InvalidWorktree_ThrowsBeforeWritingAnything()
    {
        // Arrange
        LaunchRequest request = new("Ubuntu", "alice", "~", "bad name", "hello");
        bool started = false;

        // Act
        Assert.Throws<ArgumentException>(() => SessionLauncher.Launch(request, _temp.Path, _ => started = true));

        // Assert
        Assert.False(started);
        Assert.Empty(Directory.GetFiles(_temp.Path));
    }
}
