using System.ComponentModel;
using System.Runtime.Versioning;
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
    public void Launch_TerminalFailsAndThePromptFileCannotBeDeleted_RethrowsTheTerminalFailure()
    {
        // Arrange
        LaunchRequest request = new("Ubuntu", "alice", "~", null, "hello");
        List<IDisposable> locks = new();

        // Act
        Win32Exception error = Assert.Throws<Win32Exception>(() => SessionLauncher.Launch(request, _temp.Path, _ =>
        {
            locks.Add(PreventDeletes(Directory.GetFiles(_temp.Path).Single()));
            throw new Win32Exception("wt.exe not found");
        }));
        foreach (IDisposable held in locks) held.Dispose();

        // Assert
        Assert.Contains("wt.exe", error.Message);
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

    // A held-open file cannot be deleted on Windows; elsewhere deleting needs write access to the folder.
    private IDisposable PreventDeletes(string file)
    {
        if (OperatingSystem.IsWindows())
            return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        return PreventDeletesInFolder();
    }

    [UnsupportedOSPlatform("windows")]
    private IDisposable PreventDeletesInFolder()
    {
        UnixFileMode previous = File.GetUnixFileMode(_temp.Path);
        File.SetUnixFileMode(_temp.Path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return new Restore(() => File.SetUnixFileMode(_temp.Path, previous));
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
