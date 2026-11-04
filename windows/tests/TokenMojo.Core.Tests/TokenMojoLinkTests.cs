using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class TokenMojoLinkTests
{
    private const string Session = "0f8e2a4c-1b3d-4e5f-8a9b-0c1d2e3f4a5b";
    private const string EncodedPullRequest = "https%3A%2F%2Fgithub.com%2Focto%2Fwidgets%2Fpull%2F12";

    [Theory]
    [InlineData("tokenmojo://launch?action=review&pr=" + EncodedPullRequest)]
    [InlineData("tokenmojo://launch/?action=review&pr=" + EncodedPullRequest)]
    [InlineData("TokenMojo://LAUNCH?action=review&pr=" + EncodedPullRequest)]
    [InlineData("tokenmojo://launch?pr=" + EncodedPullRequest + "&action=review")]
    [InlineData("tokenmojo://launch?action=review&pr=https://github.com/octo/widgets/pull/12")]
    public void Parse_LaunchLink_GivesTheActionAndPullRequest(string text)
    {
        // Act
        LinkParse result = TokenMojoLink.Parse(text);

        // Assert
        Assert.Null(result.Problem);
        Assert.Equal(new LaunchLink("review", new PullRequestRef("octo", "widgets", 12)), result.Link);
    }

    [Theory]
    [InlineData("tokenmojo://focus?session=" + Session)]
    [InlineData("tokenmojo://focus/?session=" + Session)]
    [InlineData("TOKENMOJO://Focus?session=0F8E2A4C-1B3D-4E5F-8A9B-0C1D2E3F4A5B")]
    public void Parse_FocusLink_GivesTheSessionIdInLowercase(string text)
    {
        // Act
        LinkParse result = TokenMojoLink.Parse(text);

        // Assert
        Assert.Null(result.Problem);
        Assert.Equal(new FocusLink(Session), result.Link);
    }

    [Fact]
    public void Parse_LongerThanTheLimit_IsRefused()
    {
        // Arrange
        string text = "tokenmojo://focus?session=" + Session + "&" + new string('x', 2048);

        // Act
        LinkParse result = TokenMojoLink.Parse(text);

        // Assert
        Assert.Null(result.Link);
        Assert.Equal("The link is longer than 2048 characters.", result.Problem);
    }

    [Theory]
    [InlineData("https://github.com/octo/widgets/pull/12")]
    [InlineData("tokenmojo:focus?session=" + Session)]
    [InlineData("")]
    public void Parse_OtherScheme_IsRefused(string text)
    {
        // Act
        LinkParse result = TokenMojoLink.Parse(text);

        // Assert
        Assert.Null(result.Link);
        Assert.Equal("The link does not start with tokenmojo://.", result.Problem);
    }

    [Theory]
    [InlineData("tokenmojo://open?session=" + Session)]
    [InlineData("tokenmojo://launch/x?action=review")]
    [InlineData("tokenmojo://?session=" + Session)]
    [InlineData("tokenmojo://launch//?action=review")]
    public void Parse_OtherHost_IsRefused(string text)
    {
        // Act
        LinkParse result = TokenMojoLink.Parse(text);

        // Assert
        Assert.Equal("The link must be tokenmojo://launch or tokenmojo://focus.", result.Problem);
    }

    [Theory]
    [InlineData("tokenmojo://focus?session=" + Session + "&")]
    [InlineData("tokenmojo://focus?session")]
    [InlineData("tokenmojo://focus?=" + Session)]
    public void Parse_MalformedParameter_IsRefused(string text)
    {
        // Act
        LinkParse result = TokenMojoLink.Parse(text);

        // Assert
        Assert.Equal("Each link parameter must be name=value.", result.Problem);
    }

    [Fact]
    public void Parse_RepeatedParameter_IsRefusedNamingIt()
    {
        // Act
        LinkParse result = TokenMojoLink.Parse("tokenmojo://launch?action=review&action=merge&pr=" + EncodedPullRequest);

        // Assert
        Assert.Equal("The link has more than one action parameter.", result.Problem);
    }

    [Theory]
    [InlineData("tokenmojo://launch?action=review&pr=" + EncodedPullRequest + "&prompt=hi", "prompt")]
    [InlineData("tokenmojo://focus?session=" + Session + "&action=review", "action")]
    public void Parse_UnknownParameter_IsRefusedNamingIt(string text, string name)
    {
        // Act
        LinkParse result = TokenMojoLink.Parse(text);

        // Assert
        Assert.Equal($"The link has an unknown parameter {name}.", result.Problem);
    }

    [Theory]
    [InlineData("tokenmojo://launch?pr=" + EncodedPullRequest, "action")]
    [InlineData("tokenmojo://launch?action=review", "pr")]
    [InlineData("tokenmojo://focus", "session")]
    public void Parse_MissingParameter_IsRefusedNamingIt(string text, string name)
    {
        // Act
        LinkParse result = TokenMojoLink.Parse(text);

        // Assert
        Assert.Equal($"The link has no {name} parameter.", result.Problem);
    }

    [Fact]
    public void Parse_EmptyParameter_IsRefusedNamingIt()
    {
        // Act
        LinkParse result = TokenMojoLink.Parse("tokenmojo://launch?action=&pr=" + EncodedPullRequest);

        // Assert
        Assert.Equal("The link's action parameter is empty.", result.Problem);
    }

    [Theory]
    [InlineData("Review")]
    [InlineData("-review")]
    [InlineData("re%20view")]
    [InlineData("a234567890123456789012345678901234")]
    public void Parse_InvalidActionName_IsRefused(string action)
    {
        // Act
        LinkParse result = TokenMojoLink.Parse($"tokenmojo://launch?action={action}&pr={EncodedPullRequest}");

        // Assert
        Assert.Equal(
            $"The action must be up to 32 lowercase letters, digits and hyphens, not '{Uri.UnescapeDataString(action)}'.", result.Problem);
    }

    [Fact]
    public void Parse_PullRequestThatIsNotOne_IsRefused()
    {
        // Act
        LinkParse result = TokenMojoLink.Parse("tokenmojo://launch?action=review&pr=https%3A%2F%2Fgithub.com%2Focto%2Fwidgets%2Fissues%2F12");

        // Assert
        Assert.Equal("The pr parameter must be a GitHub pull request URL such as https://github.com/octo/widgets/pull/12.", result.Problem);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("0f8e2a4c1b3d4e5f8a9b0c1d2e3f4a5b")]
    [InlineData("{0f8e2a4c-1b3d-4e5f-8a9b-0c1d2e3f4a5b}")]
    public void Parse_SessionThatIsNotAUuid_IsRefused(string session)
    {
        // Act
        LinkParse result = TokenMojoLink.Parse("tokenmojo://focus?session=" + Uri.EscapeDataString(session));

        // Assert
        Assert.Equal("The session parameter must be a session id (a UUID).", result.Problem);
    }

    [Theory]
    [InlineData("review")]
    [InlineData("merge-main")]
    [InlineData("a1")]
    [InlineData("a2345678901234567890123456789012")]
    public void IsActionName_LowercaseNameUpTo32Characters_IsTrue(string name)
    {
        // Act / Assert
        Assert.True(TokenMojoLink.IsActionName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-merge")]
    [InlineData("Merge")]
    [InlineData("merge_main")]
    [InlineData("a23456789012345678901234567890123")]
    public void IsActionName_OtherText_IsFalse(string name)
    {
        // Act / Assert
        Assert.False(TokenMojoLink.IsActionName(name));
    }
}
