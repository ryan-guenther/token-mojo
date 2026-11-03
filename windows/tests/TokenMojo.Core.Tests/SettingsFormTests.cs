using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class SettingsFormTests
{
    [Fact]
    public void FromThenApply_UnchangedFields_GiveBackTheSameSettings()
    {
        // Arrange
        Settings original = new()
        {
            SessionHours = 24,
            RepoRoots = ["~", "~/code"],
            LinearTeams = new Dictionary<string, string> { ["ENG"] = "~/my-app" },
        };

        // Act
        SettingsResult result = SettingsForm.From(original).Apply(original);

        // Assert
        Assert.Null(result.Problem);
        Assert.Equal(24, result.Settings!.SessionHours);
        Assert.Equal(["~", "~/code"], result.Settings.RepoRoots);
        Assert.Equal("~/my-app", result.Settings.LinearTeams["ENG"]);
    }

    [Fact]
    public void Apply_ListsAndTeams_TrimsLinesAndSkipsBlankOnes()
    {
        // Arrange
        SettingsForm form = SettingsForm.From(new Settings()) with
        {
            RepoRoots = " ~/code \r\n\r\n~/work\r\n",
            LinearTeams = "eng = ~/my-app\r\n\r\nOPS=~/ops",
        };

        // Act
        SettingsResult result = form.Apply(new Settings());

        // Assert
        Assert.Equal(["~/code", "~/work"], result.Settings!.RepoRoots);
        Assert.Equal(new Dictionary<string, string> { ["ENG"] = "~/my-app", ["OPS"] = "~/ops" }, result.Settings.LinearTeams);
    }

    [Theory]
    [InlineData("abc", "12", "10", "whole number of seconds")]
    [InlineData("30", "12.5", "10", "hours must be a whole number")]
    [InlineData("30", "12", "", "rows must be a whole number")]
    [InlineData("30", "0", "10", "sessionHours")]
    public void Apply_BadNumber_SaysWhichField(string longTurn, string hours, string rows, string expected)
    {
        // Arrange
        SettingsForm form = SettingsForm.From(new Settings()) with { LongTurnSeconds = longTurn, SessionHours = hours, SessionRows = rows };

        // Act
        SettingsResult result = form.Apply(new Settings());

        // Assert
        Assert.Null(result.Settings);
        Assert.Contains(expected, result.Problem);
    }

    [Theory]
    [InlineData("ENG ~/my-app", "must be TEAM = folder")]
    [InlineData("ENG = ~/a\nEng = ~/b", "listed twice")]
    [InlineData("ENG = my-app", "linearTeams.ENG")]
    public void Apply_BadTeamLine_Refuses(string teams, string expected)
    {
        // Arrange
        SettingsForm form = SettingsForm.From(new Settings()) with { LinearTeams = teams };

        // Act
        SettingsResult result = form.Apply(new Settings());

        // Assert
        Assert.Contains(expected, result.Problem);
    }

    [Fact]
    public void Apply_BadFolder_Refuses()
    {
        // Arrange
        SettingsForm form = SettingsForm.From(new Settings()) with { DefaultFolder = "code" };

        // Act
        SettingsResult result = form.Apply(new Settings());

        // Assert
        Assert.Contains("defaultFolder", result.Problem);
    }
}
