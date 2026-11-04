using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class PullRequestRefTests
{
    [Theory]
    [InlineData("https://github.com/octo/widgets/pull/12")]
    [InlineData("https://github.com/octo/widgets/pull/12/")]
    [InlineData("https://github.com/octo/widgets/pull/12/files")]
    [InlineData("https://github.com/octo/widgets/pull/12/commits")]
    [InlineData("https://github.com/octo/widgets/pull/12/checks")]
    [InlineData("https://github.com/octo/widgets/pull/12#issuecomment-1")]
    [InlineData("https://github.com/octo/widgets/pull/12/files?w=1")]
    [InlineData("https://github.com/octo/widgets/pull/12?notification_referrer_id=x")]
    public void Parse_PullRequestUrl_GivesOwnerRepoAndNumber(string url)
    {
        // Act
        PullRequestRef? pullRequest = PullRequestRef.Parse(url);

        // Assert
        Assert.Equal(new PullRequestRef("octo", "widgets", 12), pullRequest);
    }

    [Fact]
    public void Parse_OwnerAndRepoWithAllowedPunctuation_KeepsThem()
    {
        // Act
        PullRequestRef? pullRequest = PullRequestRef.Parse("https://github.com/Octo-Cat/my.widgets_2/pull/7");

        // Assert
        Assert.Equal(new PullRequestRef("Octo-Cat", "my.widgets_2", 7), pullRequest);
    }

    [Theory]
    [InlineData("http://github.com/octo/widgets/pull/12")]
    [InlineData("https://github.com.example.com/octo/widgets/pull/12")]
    [InlineData("https://example.com/octo/widgets/pull/12")]
    [InlineData("https://github.com/octo/widgets/issues/12")]
    [InlineData("https://github.com/octo/widgets/pull/abc")]
    [InlineData("https://github.com/octo/widgets/pull/0")]
    [InlineData("https://github.com/octo/widgets/pull/1234567890")]
    [InlineData("https://github.com/octo/widgets/pull/12/reviews")]
    [InlineData("https://github.com/octo/widgets/pull/12/files/extra")]
    [InlineData("https://github.com/octo/widgets/pull/12x")]
    [InlineData("https://github.com/octo/widgets/pull/12#a b")]
    [InlineData("https://github.com/octo/widgets/pull/12?a=\n")]
    [InlineData("https://github.com/-octo/widgets/pull/12")]
    [InlineData("https://github.com/octo-/widgets/pull/12")]
    [InlineData("https://github.com/oc--to/widgets/pull/12")]
    [InlineData("https://github.com/octo_cat/widgets/pull/12")]
    [InlineData("https://github.com/octo/wid;gets/pull/12")]
    [InlineData("https://github.com/octo/../pull/12")]
    [InlineData("https://github.com/octo/./pull/12")]
    [InlineData(" https://github.com/octo/widgets/pull/12")]
    public void Parse_AnythingElse_IsNull(string url)
    {
        // Act / Assert
        Assert.Null(PullRequestRef.Parse(url));
    }

    [Fact]
    public void Url_IsTheCanonicalPullRequestUrl()
    {
        // Arrange
        PullRequestRef pullRequest = PullRequestRef.Parse("https://github.com/octo/widgets/pull/12/files?w=1#diff")!;

        // Act / Assert
        Assert.Equal("https://github.com/octo/widgets/pull/12", pullRequest.Url);
    }

    [Theory]
    [InlineData("octo")]
    [InlineData("Octo-Cat")]
    [InlineData("a")]
    [InlineData("a23456789012345678901234567890123456789")]
    public void IsOwner_GitHubOwnerName_IsTrue(string owner)
    {
        // Act / Assert
        Assert.True(PullRequestRef.IsOwner(owner));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-octo")]
    [InlineData("octo-")]
    [InlineData("oc--to")]
    [InlineData("octo_cat")]
    [InlineData("octo cat")]
    [InlineData("a234567890123456789012345678901234567890")]
    public void IsOwner_OtherText_IsFalse(string owner)
    {
        // Act / Assert
        Assert.False(PullRequestRef.IsOwner(owner));
    }
}
