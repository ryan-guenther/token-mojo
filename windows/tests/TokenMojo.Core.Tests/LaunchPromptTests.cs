using System.Text;
using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class LaunchPromptTests
{
    private static readonly Guid PromptId = Guid.Parse("3636af4c-8a5c-4e7f-a26c-739e5f93af06");

    [Fact]
    public void LinuxPath_IsTheFileTmIsToldToRead()
    {
        // Act / Assert
        Assert.Equal($"~/.claude/cctokmon-launch/{PromptId}.txt", LaunchPrompt.LinuxPath(PromptId));
    }

    [Fact]
    public void Encode_Prompt_IsUtf8WithoutABomAndWithLineFeeds()
    {
        // Act
        byte[] bytes = LaunchPrompt.Encode("first\r\nsecond\rthird\nfourth é");

        // Assert
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("first\nsecond\nthird\nfourth é", new UTF8Encoding(false).GetString(bytes));
    }

    [Fact]
    public void Encode_PromptWithShellCharacters_KeepsEveryByte()
    {
        // Arrange
        string prompt = "say \"hi\"; echo 'there' $HOME `id` $(id) \\n\n  indented\n\nlast";

        // Act / Assert
        Assert.Equal(prompt, new UTF8Encoding(false).GetString(LaunchPrompt.Encode(prompt)));
    }

    [Fact]
    public void DepositScript_ReadsStandardInputIntoTheFileInTheLaunchUsersHome()
    {
        // Act
        string script = LaunchPrompt.DepositScript(PromptId);

        // Assert
        Assert.Equal(
            $"umask 077; mkdir -p \"$HOME/.claude/cctokmon-launch\" && set -C && cat > \"$HOME/.claude/cctokmon-launch/{PromptId}.txt\"",
            script);
    }

    [Fact]
    public void DiscardScript_RemovesTheFileIfItIsThere()
    {
        // Act / Assert
        Assert.Equal($"rm -f -- \"$HOME/.claude/cctokmon-launch/{PromptId}.txt\"", LaunchPrompt.DiscardScript(PromptId));
    }
}
