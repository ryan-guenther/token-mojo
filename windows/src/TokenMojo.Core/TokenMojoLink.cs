using System.Text.RegularExpressions;

namespace TokenMojo.Core;

/// <summary>
/// A tokenmojo:// link: tokenmojo://launch?action=NAME&amp;pr=URL starts a session for a pull request with one of the
/// prompts in settings.json, tokenmojo://focus?session=ID brings a session forward. A link never carries prompt text.
/// </summary>
public abstract partial record TokenMojoLink
{
    public const int MaxLength = 2048;

    private const string Scheme = "tokenmojo://";
    private const string LaunchHost = "launch";
    private const string FocusHost = "focus";

    /// <summary>Whether the text can name an action: up to 32 lowercase letters, digits and hyphens, starting with a letter or digit.</summary>
    public static bool IsActionName(string name) => ActionName().IsMatch(name);

    /// <summary>The link the text holds, or why it is not one. Scheme and host are matched ignoring case; every parameter must be there once and no others.</summary>
    public static LinkParse Parse(string text)
    {
        if (text.Length > MaxLength) return Refused($"The link is longer than {MaxLength} characters.");
        if (!text.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return Refused("The link does not start with tokenmojo://.");
        string rest = text[Scheme.Length..];
        int queryStart = rest.IndexOf('?');
        string host = queryStart < 0 ? rest : rest[..queryStart];
        // Some browsers add a slash after the host.
        if (host.EndsWith('/')) host = host[..^1];
        string[] expected = host.ToLowerInvariant() switch
        {
            LaunchHost => ["action", "pr"],
            FocusHost => ["session"],
            _ => [],
        };
        if (expected.Length == 0) return Refused("The link must be tokenmojo://launch or tokenmojo://focus.");
        Dictionary<string, string> parameters = new(StringComparer.Ordinal);
        string query = queryStart < 0 ? "" : rest[(queryStart + 1)..];
        foreach (string pair in query.Length == 0 ? [] : query.Split('&'))
        {
            int separator = pair.IndexOf('=');
            if (separator <= 0) return Refused("Each link parameter must be name=value.");
            string name = Uri.UnescapeDataString(pair[..separator]);
            if (!expected.Contains(name)) return Refused($"The link has an unknown parameter {name}.");
            if (!parameters.TryAdd(name, Uri.UnescapeDataString(pair[(separator + 1)..])))
                return Refused($"The link has more than one {name} parameter.");
        }
        foreach (string name in expected)
        {
            if (!parameters.TryGetValue(name, out string? value)) return Refused($"The link has no {name} parameter.");
            if (value.Length == 0) return Refused($"The link's {name} parameter is empty.");
        }
        return expected.Length == 1 ? ParseFocus(parameters["session"]) : ParseLaunch(parameters["action"], parameters["pr"]);
    }

    private static LinkParse ParseLaunch(string action, string pullRequestUrl)
    {
        if (!IsActionName(action))
            return Refused($"The action must be up to 32 lowercase letters, digits and hyphens, not '{action}'.");
        if (PullRequestRef.Parse(pullRequestUrl) is not { } pullRequest)
            return Refused("The pr parameter must be a GitHub pull request URL such as https://github.com/octo/widgets/pull/12.");
        return new LinkParse(new LaunchLink(action, pullRequest), null);
    }

    // Claude Code's session ids are lowercase UUIDs with hyphens.
    private static LinkParse ParseFocus(string session) =>
        Guid.TryParseExact(session, "D", out Guid id)
            ? new LinkParse(new FocusLink(id.ToString("D")), null)
            : Refused("The session parameter must be a session id (a UUID).");

    private static LinkParse Refused(string problem) => new(null, problem);

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9-]{0,31}\z")]
    private static partial Regex ActionName();
}

/// <summary>Starts a session for <paramref name="PullRequest"/> with the prompt settings.json gives <paramref name="Action"/>.</summary>
public sealed record LaunchLink(string Action, PullRequestRef PullRequest) : TokenMojoLink;

/// <summary>Brings the session forward, or resumes it, as clicking its row does.</summary>
/// <param name="SessionId">A lowercase UUID.</param>
public sealed record FocusLink(string SessionId) : TokenMojoLink;

/// <param name="Link">The link; null when <paramref name="Problem"/> is set.</param>
/// <param name="Problem">Why the text is not a link TokenMojo follows.</param>
public sealed record LinkParse(TokenMojoLink? Link, string? Problem);
