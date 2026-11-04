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

    [Fact]
    public void Save_WritesOnlyTheValuesThatDifferFromTheDefaults()
    {
        // Arrange
        string path = Path.Combine(_temp.Path, "TokenMojo", "settings.json");
        Settings settings = new() { SessionHours = 24, OpenPopOutAtStart = true, LinearTeams = new Dictionary<string, string> { ["ENG"] = "~/my-app" } };

        // Act
        settings.Save(path);

        // Assert
        Assert.Equal(
            """
            {
              "openPopOutAtStart": true,
              "sessionHours": 24,
              "linearTeams": {
                "ENG": "~/my-app"
              }
            }
            """.ReplaceLineEndings("\n"),
            File.ReadAllText(path).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Save_ThenLoad_ReturnsTheSameValues()
    {
        // Arrange
        string path = Path.Combine(_temp.Path, "settings.json");
        Settings settings = new() { SessionRows = 20, Distro = "Ubuntu", RepoRoots = ["~", "~/code"] };

        // Act
        settings.Save(path);
        Settings loaded = Settings.Load(path);

        // Assert
        Assert.Equal(20, loaded.SessionRows);
        Assert.Equal("Ubuntu", loaded.Distro);
        Assert.Equal(["~", "~/code"], loaded.RepoRoots);
    }

    [Fact]
    public void Save_InvalidSettings_RefusesAndLeavesTheFileAlone()
    {
        // Arrange
        string path = _temp.Write("settings.json", """{"sessionRows": 20}""", DateTimeOffset.UtcNow);
        Settings settings = new() { SessionHours = 0 };

        // Act
        SettingsException error = Assert.Throws<SettingsException>(() => settings.Save(path));

        // Assert
        Assert.Contains("sessionHours", error.Message);
        Assert.Equal("""{"sessionRows": 20}""", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Load_NoFileYet_LeavesOutTheDerivedTimes()
    {
        // Arrange
        string path = Path.Combine(_temp.Path, "settings.json");

        // Act
        Settings.Load(path);

        // Assert
        string written = File.ReadAllText(path);
        Assert.DoesNotContain("longTurn\"", written);
        Assert.DoesNotContain("sessionWindow", written);
    }

    [Fact]
    public void Load_NoFileYet_HasNoActionsAndNoAllowedOwners()
    {
        // Arrange
        string path = Path.Combine(_temp.Path, "settings.json");

        // Act
        Settings settings = Settings.Load(path);

        // Assert
        Assert.Empty(settings.Actions);
        Assert.Empty(settings.AllowedOwners);
    }

    [Fact]
    public void Load_FileWithActionsAndAllowedOwners_UsesThem()
    {
        // Arrange
        string path = _temp.Write("settings.json", """
            {"actions": {"review": "Review {url}", "merge-main": "Merge main into {url}"}, "allowedOwners": ["octo", "Octo-Cat"]}
            """, DateTimeOffset.UtcNow);

        // Act
        Settings settings = Settings.Load(path);

        // Assert
        Assert.Equal(new Dictionary<string, string> { ["review"] = "Review {url}", ["merge-main"] = "Merge main into {url}" }, settings.Actions);
        Assert.Equal(["octo", "Octo-Cat"], settings.AllowedOwners);
    }

    [Theory]
    [InlineData("""{"actions": null}""")]
    [InlineData("""{"allowedOwners": null}""")]
    public void Load_NullActionsOrAllowedOwners_ThrowsNamingTheFile(string contents)
    {
        // Arrange
        string path = _temp.Write("settings.json", contents, DateTimeOffset.UtcNow);

        // Act
        SettingsException error = Assert.Throws<SettingsException>(() => Settings.Load(path));

        // Assert
        Assert.Contains(path, error.Message);
    }

    [Theory]
    [InlineData("Review", "actions key 'Review' must be up to 32 lowercase letters, digits and hyphens, such as review")]
    [InlineData("-review", "actions key '-review' must be up to 32 lowercase letters, digits and hyphens, such as review")]
    [InlineData("", "actions key '' must be up to 32 lowercase letters, digits and hyphens, such as review")]
    public void Problem_BadActionName_NamesIt(string name, string expected)
    {
        // Arrange
        Settings settings = new() { Actions = new Dictionary<string, string> { [name] = "Review {url}" } };

        // Act / Assert
        Assert.Equal(expected, settings.Problem());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Review the pull request")]
    [InlineData("Review {URL}")]
    public void Problem_TemplateWithoutUrlPlaceholder_NamesTheAction(string template)
    {
        // Arrange
        Settings settings = new() { Actions = new Dictionary<string, string> { ["review"] = template } };

        // Act / Assert
        Assert.Equal("actions.review must be a prompt containing {url}", settings.Problem());
    }

    [Theory]
    [InlineData("Review {url}\nthen report")]
    [InlineData("Review\t{url}")]
    public void Problem_TemplateWithControlCharacters_NamesTheAction(string template)
    {
        // Arrange
        Settings settings = new() { Actions = new Dictionary<string, string> { ["review"] = template } };

        // Act / Assert
        Assert.Equal("actions.review must be one line, without tabs or other control characters", settings.Problem());
    }

    [Theory]
    [InlineData("octo_cat")]
    [InlineData("-octo")]
    [InlineData("")]
    public void Problem_BadAllowedOwner_NamesIt(string owner)
    {
        // Arrange
        Settings settings = new() { AllowedOwners = [owner] };

        // Act / Assert
        Assert.Equal($"allowedOwners entry '{owner}' must be a GitHub user or organization name", settings.Problem());
    }

    [Fact]
    public void Save_ActionsAndAllowedOwners_WritesThemAndThenLoadsThem()
    {
        // Arrange
        string path = Path.Combine(_temp.Path, "settings.json");
        Settings settings = new() { Actions = new Dictionary<string, string> { ["review"] = "Review {url}" }, AllowedOwners = ["octo"] };

        // Act
        settings.Save(path);
        Settings loaded = Settings.Load(path);

        // Assert
        Assert.Equal(new Dictionary<string, string> { ["review"] = "Review {url}" }, loaded.Actions);
        Assert.Equal(["octo"], loaded.AllowedOwners);
    }

    [Fact]
    public void Save_DefaultActionsAndAllowedOwners_LeavesThemOut()
    {
        // Arrange
        string path = Path.Combine(_temp.Path, "settings.json");

        // Act
        new Settings().Save(path);

        // Assert
        Assert.Equal("{}", File.ReadAllText(path));
    }
}
