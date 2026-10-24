using System.Text.Json;
using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class TerminalProfileTests
{
    [Fact]
    public void FragmentJson_IsOneHiddenProfileWithTheNameLaunchesUseAndTheIcon()
    {
        // Arrange
        const string icon = NativePaths.Home + @"\claude.png";

        // Act
        using JsonDocument fragment = JsonDocument.Parse(TerminalProfile.FragmentJson(icon));

        // Assert
        JsonElement profile = Assert.Single(fragment.RootElement.GetProperty("profiles").EnumerateArray());
        Assert.Equal(TerminalProfile.Name, profile.GetProperty("name").GetString());
        Assert.Equal(icon, profile.GetProperty("icon").GetString());
        Assert.True(profile.GetProperty("hidden").GetBoolean());
        Assert.Matches(@"\A\{[0-9a-f-]{36}\}\z", profile.GetProperty("guid").GetString());
    }
}
