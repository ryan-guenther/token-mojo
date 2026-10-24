using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class LocalPromptHandoffTests : IDisposable
{
    private static readonly Guid Id = Guid.Parse("3636af4c-8a5c-4e7f-a26c-739e5f93af06");
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Deposit_WritesThePromptWhereTmLooksForIt()
    {
        // Arrange
        LocalPromptHandoff handoff = new(_temp.Path);

        // Act
        handoff.Deposit(LaunchTargets.Windows, "", Id, "fix \"it\"; then $HOME\r\nünïcødé");

        // Assert
        string path = Path.Combine(_temp.Path, ".claude", "cctokmon-launch", $"{Id}.txt");
        Assert.Equal(path, handoff.PathOf(Id));
        Assert.Equal(LaunchPrompt.Encode("fix \"it\"; then $HOME\r\nünïcødé"), File.ReadAllBytes(path));
    }

    [Fact]
    public void Deposit_FileAlreadyThere_IsAnErrorAndLeavesItAlone()
    {
        // Arrange
        LocalPromptHandoff handoff = new(_temp.Path);
        handoff.Deposit(LaunchTargets.Windows, "", Id, "first");

        // Act / Assert
        Assert.Throws<IOException>(() => handoff.Deposit(LaunchTargets.Windows, "", Id, "second"));
        Assert.Equal("first", File.ReadAllText(handoff.PathOf(Id)));
    }

    [Fact]
    public void Discard_RemovesTheFile()
    {
        // Arrange
        LocalPromptHandoff handoff = new(_temp.Path);
        handoff.Deposit(LaunchTargets.Windows, "", Id, "hello");

        // Act
        handoff.Discard(LaunchTargets.Windows, "", Id);

        // Assert
        Assert.False(File.Exists(handoff.PathOf(Id)));
    }
}
