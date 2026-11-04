using System.Globalization;
using System.Text.RegularExpressions;

namespace TokenMojo.Core;

/// <summary>A GitHub pull request, named by its repository's owner and name and its number.</summary>
public sealed partial record PullRequestRef(string Owner, string Repo, int Number)
{
    /// <summary>The pull request's own page, built from the parts rather than kept from the text it was parsed from.</summary>
    public string Url => $"https://github.com/{Owner}/{Repo}/pull/{Number}";

    /// <summary>
    /// The pull request a URL names: exactly https://github.com/owner/repo/pull/number, optionally followed by a slash,
    /// one of the pull request's tabs, a query or a fragment. Null for anything else.
    /// </summary>
    public static PullRequestRef? Parse(string url)
    {
        Match match = PullRequestUrl().Match(url);
        if (!match.Success) return null;
        string repo = match.Groups["repo"].Value;
        if (repo is "." or "..") return null;
        return new PullRequestRef(match.Groups["owner"].Value, repo, int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>Whether the text is a GitHub user or organization name: letters, digits and single hyphens, not at either end, up to 39 characters.</summary>
    public static bool IsOwner(string owner) => OwnerName().IsMatch(owner);

    private const string OwnerPattern = "[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}";

    [GeneratedRegex(@"\A" + OwnerPattern + @"\z")]
    private static partial Regex OwnerName();

    // A query or fragment may hold anything but whitespace and control characters.
    [GeneratedRegex(
        @"\Ahttps://github\.com/(?<owner>" + OwnerPattern + @")/(?<repo>[A-Za-z0-9._-]{1,100})/pull/(?<number>[1-9][0-9]{0,8})"
        + @"(?:/(?:(?:files|commits|checks)/?)?)?(?:[?#][^\s\p{Cc}]*)?\z")]
    private static partial Regex PullRequestUrl();
}
