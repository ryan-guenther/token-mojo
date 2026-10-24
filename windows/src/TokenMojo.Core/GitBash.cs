namespace TokenMojo.Core;

/// <summary>
/// Finds Git Bash, which Claude Code on Windows needs anyway and which runs `tm` for sessions on Windows itself.
/// Never C:\Windows\System32\bash.exe, which is WSL's.
/// </summary>
public static class GitBash
{
    private const string BinBash = @"bin\bash.exe";
    private const string UsrBinBash = @"usr\bin\bash.exe";

    /// <summary>
    /// Git's bin\bash.exe: the one Claude Code is told to use (CLAUDE_CODE_GIT_BASH_PATH), else a standard install.
    /// bin\bash.exe puts Git's tools on PATH itself, which usr\bin\bash.exe started directly does not, so it is preferred.
    /// </summary>
    public static string? Find(Func<string, string?> getEnv, Func<string, bool> fileExists)
    {
        return Candidates(getEnv).FirstOrDefault(fileExists);
    }

    private static IEnumerable<string> Candidates(Func<string, string?> getEnv)
    {
        if (getEnv("CLAUDE_CODE_GIT_BASH_PATH") is { Length: > 0 } configured)
        {
            string path = configured.Trim('"');
            if (path.EndsWith(UsrBinBash, StringComparison.OrdinalIgnoreCase))
                yield return path[..^UsrBinBash.Length] + BinBash;
            yield return path;
        }
        foreach (string variable in new[] { "ProgramW6432", "ProgramFiles", "ProgramFiles(x86)" })
        {
            if (getEnv(variable) is { Length: > 0 } programFiles) yield return Join(programFiles, @"Git\" + BinBash);
        }
        if (getEnv("LOCALAPPDATA") is { Length: > 0 } localAppData)
            yield return Join(localAppData, @"Programs\Git\" + BinBash);
    }

    // Windows paths, whatever platform the tests run on.
    private static string Join(string folder, string relative) => folder.TrimEnd('\\') + @"\" + relative;
}
