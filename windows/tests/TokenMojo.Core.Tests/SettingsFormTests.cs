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

    [Fact]
    public void FromThenApply_ActionsAndOwners_GiveBackTheSameSettings()
    {
        // Arrange
        Settings original = new()
        {
            Actions = new Dictionary<string, string> { ["review"] = "Review {url}", ["merge"] = "Merge main into {url} = done" },
            AllowedOwners = ["octo", "Octo-Cat"],
        };

        // Act
        SettingsResult result = SettingsForm.From(original).Apply(new Settings());

        // Assert
        Assert.Null(result.Problem);
        Assert.Equal(original.Actions, result.Settings!.Actions);
        Assert.Equal(["octo", "Octo-Cat"], result.Settings.AllowedOwners);
    }

    [Fact]
    public void From_Actions_WritesNameEqualsPromptLines()
    {
        // Arrange
        Settings settings = new() { Actions = new Dictionary<string, string> { ["review"] = "Review {url}", ["merge"] = "Merge {url}" } };

        // Act
        SettingsForm form = SettingsForm.From(settings);

        // Assert
        Assert.Equal($"review = Review {{url}}{Environment.NewLine}merge = Merge {{url}}", form.Actions);
    }

    [Fact]
    public void Apply_ActionAndOwnerLines_TrimsLowercasesNamesAndSkipsBlankLines()
    {
        // Arrange
        SettingsForm form = SettingsForm.From(new Settings()) with
        {
            Actions = " Review = Review {url} \r\n\r\nmerge=Merge {url}",
            AllowedOwners = " octo \r\n\r\nOcto-Cat\r\n",
        };

        // Act
        SettingsResult result = form.Apply(new Settings());

        // Assert
        Assert.Equal(new Dictionary<string, string> { ["review"] = "Review {url}", ["merge"] = "Merge {url}" }, result.Settings!.Actions);
        Assert.Equal(["octo", "Octo-Cat"], result.Settings.AllowedOwners);
    }

    [Theory]
    [InlineData("review Review {url}", "Each action line must be name = prompt, not 'review Review {url}'.")]
    [InlineData("review = Review {url}\nReview = Look at {url}", "The action review is listed twice.")]
    [InlineData("review = Review it", "actions.review must be a prompt containing {url}")]
    public void Apply_BadActionLine_Refuses(string actions, string expected)
    {
        // Arrange
        SettingsForm form = SettingsForm.From(new Settings()) with { Actions = actions };

        // Act
        SettingsResult result = form.Apply(new Settings());

        // Assert
        Assert.Null(result.Settings);
        Assert.Equal(expected, result.Problem);
    }

    [Fact]
    public void Apply_BadOwnerLine_Refuses()
    {
        // Arrange
        SettingsForm form = SettingsForm.From(new Settings()) with { AllowedOwners = "octo\nocto_cat" };

        // Act
        SettingsResult result = form.Apply(new Settings());

        // Assert
        Assert.Equal("allowedOwners entry 'octo_cat' must be a GitHub user or organization name", result.Problem);
    }
}
