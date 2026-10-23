using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Load_NoFileYet_WritesDefaultsAndReturnsThem()
    {
        // Arrange
        string path = Path.Combine(_temp.Path, "TokenMojo", "settings.json");

        // Act
        Settings settings = Settings.Load(path);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), settings.LongTurn);
        Assert.Contains("\"longTurnSeconds\": 30", File.ReadAllText(path));
    }

    [Fact]
    public void Load_FileWithLongTurn_UsesIt()
    {
        // Arrange
        string path = _temp.Write("settings.json", """{"longTurnSeconds": 90}""", DateTimeOffset.UtcNow);

        // Act
        Settings settings = Settings.Load(path);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(90), settings.LongTurn);
    }

    [Fact]
    public void Load_FileWithoutLongTurnOrWithUnknownFields_FillsDefaults()
    {
        // Arrange
        string path = _temp.Write("settings.json", """{"somethingFromALaterVersion": true}""", DateTimeOffset.UtcNow);

        // Act
        Settings settings = Settings.Load(path);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), settings.LongTurn);
    }

    [Fact]
    public void Load_DefaultFileCannotBeCreated_ThrowsNamingTheFile()
    {
        // Arrange
        string blocker = _temp.Write("TokenMojo", "a file where the folder should be", DateTimeOffset.UtcNow);
        string path = Path.Combine(blocker, "settings.json");

        // Act
        SettingsException error = Assert.Throws<SettingsException>(() => Settings.Load(path));

        // Assert
        Assert.Contains(path, error.Message);
    }

    [Fact]
    public void Load_UnreadableFile_ThrowsNamingTheFile()
    {
        // Arrange
        string path = _temp.Write("settings.json", """{"longTurnSeconds": 90}""", DateTimeOffset.UtcNow);
        using IDisposable denied = TempDir.DenyRead(path);

        // Act
        SettingsException error = Assert.Throws<SettingsException>(() => Settings.Load(path));

        // Assert
        Assert.Contains(path, error.Message);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{"longTurnSeconds": -5}""")]
    [InlineData("""{"longTurnSeconds": "soon"}""")]
    public void Load_InvalidFile_ThrowsNamingTheFileAndLeavesItUntouched(string contents)
    {
        // Arrange
        string path = _temp.Write("settings.json", contents, DateTimeOffset.UtcNow);

        // Act
        SettingsException error = Assert.Throws<SettingsException>(() => Settings.Load(path));

        // Assert
        Assert.Contains(path, error.Message);
        Assert.Equal(contents, File.ReadAllText(path));
    }
}
