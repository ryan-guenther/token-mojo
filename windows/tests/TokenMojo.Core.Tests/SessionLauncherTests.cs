using System.ComponentModel;
using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class SessionLauncherTests
{
    [Fact]
    public void Launch_Prompt_IsDepositedForTheLaunchUserBeforeTheTerminalStartsAndLeftForTmToDelete()
    {
        // Arrange
        LaunchRequest request = new("Ubuntu", "alice", "~/my-app", "eng-42", "fix the build; then test");
        FakeHandoff handoff = new();
        List<string> depositedWhenStarted = new();

        // Act
        SessionLauncher.Launch(request, handoff, command =>
        {
            depositedWhenStarted.AddRange(handoff.Deposited.Select(deposit => deposit.Prompt));
            Assert.Contains("--prompt-file ~/.claude/cctokmon-launch/", command.Arguments[^1]);
        });

        // Assert
        Assert.Equal(["fix the build; then test"], depositedWhenStarted);
        Assert.Equal(("Ubuntu", "alice"), (handoff.Deposited.Single().Distro, handoff.Deposited.Single().User));
        Assert.Empty(handoff.Discarded);
    }

    [Fact]
    public void Launch_PromptFileNameInTheCommand_IsTheFileThatWasDeposited()
    {
        // Arrange
        LaunchRequest request = new("Ubuntu", "alice", "~", null, "hello");
        FakeHandoff handoff = new();
        string? shellCommand = null;

        // Act
        SessionLauncher.Launch(request, handoff, command => shellCommand = command.Arguments[^1]);

        // Assert
        Assert.EndsWith(LaunchPrompt.LinuxPath(handoff.Deposited.Single().Id), shellCommand);
    }

    [Fact]
    public void Launch_PromptNeverAppearsInTheTerminalCommand()
    {
        // Arrange
        LaunchRequest request = new("Ubuntu", "alice", "~", null, "top secret words");
        TerminalCommand? started = null;

        // Act
        SessionLauncher.Launch(request, new FakeHandoff(), command => started = command);

        // Assert
        Assert.DoesNotContain(started!.Arguments, argument => argument.Contains("secret"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n\t ")]
    public void Launch_NoPrompt_DepositsNothingAndPassesNoPromptFile(string prompt)
    {
        // Arrange
        LaunchRequest request = new("Ubuntu", "alice", "~", null, prompt);
        FakeHandoff handoff = new();
        TerminalCommand? started = null;

        // Act
        SessionLauncher.Launch(request, handoff, command => started = command);

        // Assert
        Assert.Empty(handoff.Deposited);
        Assert.Equal("~/.local/bin/tm", started!.Arguments[^1]);
    }

    [Fact]
    public void Launch_DepositFails_TheTerminalIsNotStartedAndTheFailureReachesTheCaller()
    {
        // Arrange
        LaunchRequest request = new("Ubuntu", "alice", "~", null, "hello");
        FakeHandoff handoff = new() { DepositError = new IOException("wsl.exe did not answer") };
        bool started = false;

        // Act
        IOException error = Assert.Throws<IOException>(() => SessionLauncher.Launch(request, handoff, _ => started = true));

        // Assert
        Assert.Contains("did not answer", error.Message);
        Assert.False(started);
    }

    [Fact]
    public void Launch_TerminalFailsToStart_DiscardsTheDepositedPromptAndRethrows()
    {
        // Arrange
        LaunchRequest request = new("Ubuntu", "alice", "~", null, "hello");
        FakeHandoff handoff = new();

        // Act
        Win32Exception error = Assert.Throws<Win32Exception>(
            () => SessionLauncher.Launch(request, handoff, _ => throw new Win32Exception("wt.exe not found")));

        // Assert
        Assert.Contains("wt.exe", error.Message);
        Assert.Equal([(handoff.Deposited.Single().Id, "alice")], handoff.Discarded.Select(discard => (discard.Id, discard.User)));
    }

    [Fact]
    public void Launch_TerminalFailsAndTheDiscardFails_RethrowsTheTerminalFailure()
    {
        // Arrange
        LaunchRequest request = new("Ubuntu", "alice", "~", null, "hello");
        FakeHandoff handoff = new() { DiscardError = new IOException("wsl.exe did not answer") };

        // Act
        Win32Exception error = Assert.Throws<Win32Exception>(
            () => SessionLauncher.Launch(request, handoff, _ => throw new Win32Exception("wt.exe not found")));

        // Assert
        Assert.Contains("wt.exe", error.Message);
    }

    [Fact]
    public void Launch_InvalidWorktree_ThrowsBeforeDepositingAnything()
    {
        // Arrange
        LaunchRequest request = new("Ubuntu", "alice", "~", "bad name", "hello");
        FakeHandoff handoff = new();
        bool started = false;

        // Act
        Assert.Throws<ArgumentException>(() => SessionLauncher.Launch(request, handoff, _ => started = true));

        // Assert
        Assert.False(started);
        Assert.Empty(handoff.Deposited);
    }

    private sealed class FakeHandoff : IPromptHandoff
    {
        public List<(string Distro, string User, Guid Id, string Prompt)> Deposited { get; } = new();
        public List<(string Distro, string User, Guid Id)> Discarded { get; } = new();
        public IOException? DepositError { get; init; }
        public IOException? DiscardError { get; init; }

        public void Deposit(string distro, string user, Guid id, string prompt)
        {
            if (DepositError is not null) throw DepositError;
            Deposited.Add((distro, user, id, prompt));
        }

        public void Discard(string distro, string user, Guid id)
        {
            Discarded.Add((distro, user, id));
            if (DiscardError is not null) throw DiscardError;
        }
    }
}
