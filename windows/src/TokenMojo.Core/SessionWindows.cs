namespace TokenMojo.Core;

/// <summary>Finds a session's terminal window by the tag `tm` puts in its title: "TM &lt;name&gt; &lt;first 8 characters of the session id&gt;".</summary>
public static class SessionWindows
{
    private const int TagLength = 8;

    /// <summary>The index of the first title that carries the session's tag, or null.</summary>
    public static int? Find(IReadOnlyList<string> titles, string sessionId)
    {
        for (int index = 0; index < titles.Count; index++)
        {
            if (Matches(titles[index], sessionId)) return index;
        }
        return null;
    }

    /// <summary>True when the tag is a whole token: a longer run of letters or digits around it is some other id.</summary>
    public static bool Matches(string title, string sessionId)
    {
        if (sessionId.Length < TagLength) return false;
        string tag = sessionId[..TagLength];
        int from = 0;
        while (title.IndexOf(tag, from, StringComparison.OrdinalIgnoreCase) is int at and >= 0)
        {
            bool startsToken = at == 0 || !char.IsLetterOrDigit(title[at - 1]);
            int end = at + TagLength;
            bool endsToken = end == title.Length || !char.IsLetterOrDigit(title[end]);
            if (startsToken && endsToken) return true;
            from = at + 1;
        }
        return false;
    }
}
