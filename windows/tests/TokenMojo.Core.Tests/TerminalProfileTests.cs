using System.Text.Json;
using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class TerminalProfileTests
{
    [Fact]
    public void FragmentJson_IsOneHiddenProfileWithTheNameLaunchesUseAndTheIcon()
    {
        // Act
        using JsonDocument fragment = JsonDocument.Parse(TerminalProfile.FragmentJson(@"C:\Users\Ann Lee\claude.png"));

        // Assert
        JsonElement profile = Assert.Single(fragment.RootElement.GetProperty("profiles").EnumerateArray());
        Assert.Equal(TerminalProfile.Name, profile.GetProperty("name").GetString());
        Assert.Equal(@"C:\Users\Ann Lee\claude.png", profile.GetProperty("icon").GetString());
        Assert.True(profile.GetProperty("hidden").GetBoolean());
        Assert.Matches(@"\A\{[0-9a-f-]{36}\}\z", profile.GetProperty("guid").GetString());
    }

    [Fact]
    public void ClaudeSessionVariables_KeepTheUsersOwnClaudeCodeSettings()
    {
        // Act / Assert
        Assert.Contains("CLAUDE_CODE_CHILD_SESSION", TerminalProfile.ClaudeSessionVariables);
        Assert.DoesNotContain("CLAUDE_CODE_GIT_BASH_PATH", TerminalProfile.ClaudeSessionVariables);
    }
}
