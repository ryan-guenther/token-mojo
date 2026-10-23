using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class LaunchTargetsTests
{
    [Fact]
    public void ChooseDistro_NoneConfigured_IsTheFirstRunning()
    {
        // Act / Assert
        Assert.Equal("Ubuntu", LaunchTargets.ChooseDistro(null, ["Ubuntu", "Debian"]));
    }

    [Fact]
    public void ChooseDistro_ConfiguredOneIsRunning_IsThatOneAsWslSpellsIt()
    {
        // Act / Assert
        Assert.Equal("Debian", LaunchTargets.ChooseDistro("debian", ["Ubuntu", "Debian"]));
    }

    [Fact]
    public void ChooseDistro_ConfiguredOneIsNotRunning_FallsBackToTheFirstRunning()
    {
        // Act / Assert
        Assert.Equal("Ubuntu", LaunchTargets.ChooseDistro("Arch", ["Ubuntu", "Debian"]));
    }

    [Fact]
    public void ChooseDistro_NothingRunning_IsNull()
    {
        // Act / Assert
        Assert.Null(LaunchTargets.ChooseDistro("Ubuntu", []));
    }

    [Fact]
    public void UserFor_DistroWithClaudeFolders_IsTheFirstUserFoundThere()
    {
        // Arrange
        ClaudeSource[] sources =
        [
            new("Windows", "s", "d"),
            new("Debian:bob", "s", "d", "Debian", "bob"),
            new("Ubuntu:alice", "s", "d", "Ubuntu", "alice"),
            new("Ubuntu:root", "s", "d", "Ubuntu", "root"),
        ];

        // Act / Assert
        Assert.Equal("alice", LaunchTargets.UserFor(sources, "ubuntu"));
    }

    [Fact]
    public void UserFor_DistroWithoutClaudeFolders_IsNull()
    {
        // Act / Assert
        Assert.Null(LaunchTargets.UserFor([new ClaudeSource("Windows", "s", "d")], "Ubuntu"));
    }
}
