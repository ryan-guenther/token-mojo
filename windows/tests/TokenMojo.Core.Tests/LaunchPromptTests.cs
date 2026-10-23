using System.Text;
using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class LaunchPromptTests : IDisposable
{
    private static readonly Guid PromptId = Guid.Parse("3636af4c-8a5c-4e7f-a26c-739e5f93af06");

    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void LinuxPath_IsTheFileTmIsToldToRead()
    {
        // Act / Assert
        Assert.Equal($"~/.claude/cctokmon-launch/{PromptId}.txt", LaunchPrompt.LinuxPath(PromptId));
    }

    [Fact]
    public void DirectoryOnShare_IsInsideTheUsersClaudeFolder()
    {
        // Act
        string directory = LaunchPrompt.DirectoryOnShare("Ubuntu", "alice", @"\\wsl.localhost");

        // Assert
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\alice\.claude\cctokmon-launch", directory);
    }

    [Fact]
    public void Write_Prompt_IsWrittenAsUtf8WithoutABomAndWithLineFeeds()
    {
        // Arrange
        string directory = Path.Combine(_temp.Path, "cctokmon-launch");

        // Act
        string file = LaunchPrompt.Write(directory, PromptId, "first\r\nsecond\rthird\nfourth é");

        // Assert
        byte[] bytes = File.ReadAllBytes(file);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("first\nsecond\nthird\nfourth é", new UTF8Encoding(false).GetString(bytes));
        Assert.Equal(Path.Combine(directory, $"{PromptId}.txt"), file);
    }

    [Fact]
    public void Write_PromptWithShellCharacters_KeepsEveryByte()
    {
        // Arrange
        string prompt = "say \"hi\"; echo 'there' $HOME `id` $(id) \\n\n  indented\n\nlast";

        // Act
        string file = LaunchPrompt.Write(_temp.Path, PromptId, prompt);

        // Assert
        Assert.Equal(prompt, File.ReadAllText(file));
    }

    [Fact]
    public void Write_FileAlreadyThere_Throws()
    {
        // Arrange
        LaunchPrompt.Write(_temp.Path, PromptId, "first");

        // Act / Assert
        Assert.Throws<IOException>(() => LaunchPrompt.Write(_temp.Path, PromptId, "second"));
    }

    [Fact]
    public void Delete_FileThere_RemovesIt()
    {
        // Arrange
        string file = LaunchPrompt.Write(_temp.Path, PromptId, "text");

        // Act
        LaunchPrompt.Delete(file);

        // Assert
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Delete_FileAlreadyGone_DoesNothing()
    {
        // Act / Assert
        LaunchPrompt.Delete(Path.Combine(_temp.Path, "missing.txt"));
    }
}
