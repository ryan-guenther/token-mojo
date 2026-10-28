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
    public void Load_NoFileYet_ListsTenSessionsFromTheLastTwelveHours()
    {
        // Arrange
        string path = Path.Combine(_temp.Path, "TokenMojo", "settings.json");

        // Act
        Settings settings = Settings.Load(path);

        // Assert
        Assert.Equal(TimeSpan.FromHours(12), settings.SessionWindow);
        Assert.Equal(10, settings.SessionRows);
        Assert.Contains("\"sessionHours\": 12", File.ReadAllText(path));
        Assert.Contains("\"sessionRows\": 10", File.ReadAllText(path));
    }

    [Fact]
    public void Load_FileWithSessionHoursAndRows_UsesThem()
    {
        // Arrange
        string path = _temp.Write("settings.json", """{"sessionHours": 24, "sessionRows": 20}""", DateTimeOffset.UtcNow);

        // Act
        Settings settings = Settings.Load(path);

        // Assert
        Assert.Equal(TimeSpan.FromHours(24), settings.SessionWindow);
        Assert.Equal(20, settings.SessionRows);
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
    public void Load_DefaultFileCannotBeCreated_UsesDefaults()
    {
        // Arrange
        string blocker = _temp.Write("TokenMojo", "a file where the folder should be", DateTimeOffset.UtcNow);
        string path = Path.Combine(blocker, "settings.json");

        // Act
        Settings settings = Settings.Load(path);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), settings.LongTurn);
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
    [InlineData("""{"sessionHours": 0}""")]
    [InlineData("""{"sessionHours": 169}""")]
    [InlineData("""{"sessionRows": 0}""")]
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

    [Fact]
    public void Load_NoFileYet_LaunchSettingsDefaultToHomeAndNoDistro()
    {
        // Arrange
        string path = Path.Combine(_temp.Path, "TokenMojo", "settings.json");

        // Act
        Settings settings = Settings.Load(path);

        // Assert
        Assert.Null(settings.Distro);
        Assert.Equal("~", settings.DefaultFolder);
        Assert.Equal(["~"], settings.RepoRoots);
        Assert.Equal("~", settings.WindowsDefaultFolder);
        Assert.Equal(["~"], settings.WindowsRepoRoots);
        Assert.Empty(settings.LinearTeams);
    }

    [Fact]
    public void Load_FileWithLaunchSettings_UsesThem()
    {
        // Arrange
        string path = _temp.Write("settings.json", """
            {"distro": "Debian", "defaultFolder": "~/my-app", "repoRoots": ["~", "/srv/code"],
             "linearTeams": {"ENG": "~/my-app", "OPS2": "/srv/ops", "WEB": "C:\\code\\web", "DOC": "~\\docs"}}
            """, DateTimeOffset.UtcNow);

        // Act
        Settings settings = Settings.Load(path);

        // Assert
        Assert.Equal("Debian", settings.Distro);
        Assert.Equal("~/my-app", settings.DefaultFolder);
        Assert.Equal(["~", "/srv/code"], settings.RepoRoots);
        Assert.Equal("~/my-app", settings.LinearTeams["ENG"]);
        Assert.Equal("/srv/ops", settings.LinearTeams["OPS2"]);
        Assert.Equal(@"C:\code\web", settings.LinearTeams["WEB"]);
        Assert.Equal(@"~\docs", settings.LinearTeams["DOC"]);
    }

    [Fact]
    public void Load_FileWithWindowsLaunchSettings_UsesThem()
    {
        // Arrange
        string path = _temp.Write("settings.json", """
            {"distro": "Windows", "windowsDefaultFolder": "C:\\code\\my-app", "windowsRepoRoots": ["~", "D:\\src"]}
            """, DateTimeOffset.UtcNow);

        // Act
        Settings settings = Settings.Load(path);

        // Assert
        Assert.Equal("Windows", settings.Distro);
        Assert.Equal(@"C:\code\my-app", settings.WindowsDefaultFolder);
        Assert.Equal(["~", @"D:\src"], settings.WindowsRepoRoots);
    }

    [Theory]
    [InlineData("""{"windowsDefaultFolder": "/home/alice"}""")]
    [InlineData("""{"windowsDefaultFolder": "code"}""")]
    [InlineData("""{"windowsDefaultFolder": null}""")]
    [InlineData("""{"windowsRepoRoots": ["~", "relative"]}""")]
    [InlineData("""{"windowsRepoRoots": null}""")]
    public void Load_InvalidWindowsFolder_IsAnError(string json)
    {
        // Arrange
        string path = _temp.Write("settings.json", json, DateTimeOffset.UtcNow);

        // Act / Assert
        Assert.Throws<SettingsException>(() => Settings.Load(path));
    }

    [Fact]
    public void Load_FileWithOnlyOtherFields_KeepsLaunchDefaults()
    {
        // Arrange
        string path = _temp.Write("settings.json", """{"longTurnSeconds": 90}""", DateTimeOffset.UtcNow);

        // Act
        Settings settings = Settings.Load(path);

        // Assert
        Assert.Equal("~", settings.DefaultFolder);
        Assert.Equal(["~"], settings.RepoRoots);
    }

    [Theory]
    [InlineData("""{"defaultFolder": "my-app"}""")]
    [InlineData("""{"defaultFolder": ""}""")]
    [InlineData("""{"defaultFolder": "~other/x"}""")]
    [InlineData("""{"defaultFolder": null}""")]
    [InlineData("""{"repoRoots": ["~", "code"]}""")]
    [InlineData("""{"repoRoots": null}""")]
    [InlineData("""{"linearTeams": {"ENG": "my-app"}}""")]
    [InlineData("""{"linearTeams": {"eng": "~/my-app"}}""")]
    [InlineData("""{"linearTeams": {"1ENG": "~/my-app"}}""")]
    [InlineData("""{"linearTeams": {"E-NG": "~/my-app"}}""")]
    [InlineData("""{"linearTeams": {"": "~/my-app"}}""")]
    [InlineData("""{"linearTeams": null}""")]
    [InlineData("""{"distro": ""}""")]
    public void Load_InvalidLaunchSettings_ThrowsNamingTheFile(string contents)
    {
        // Arrange
        string path = _temp.Write("settings.json", contents, DateTimeOffset.UtcNow);

        // Act
        SettingsException error = Assert.Throws<SettingsException>(() => Settings.Load(path));

        // Assert
        Assert.Contains(path, error.Message);
    }
}
