using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class TerminalCommandTests
{
    [Fact]
    public void ClaudeSessionVariables_AreClaudesOwnAndKeepTheUsersSettings()
    {
        // Act / Assert
        Assert.Contains("CLAUDE_CODE_CHILD_SESSION", TerminalCommand.ClaudeSessionVariables);
        Assert.DoesNotContain("CLAUDE_CODE_GIT_BASH_PATH", TerminalCommand.ClaudeSessionVariables);
    }
}
