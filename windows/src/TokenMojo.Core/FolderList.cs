namespace TokenMojo.Core;

/// <summary>The folders the launcher offers: the default, then where recent sessions ran, then repositories found on disk.</summary>
public static class FolderList
{
    /// <param name="user">Whose home folder is written with a tilde; null leaves every path absolute.</param>
    public static IReadOnlyList<string> Build(
        string defaultFolder, string distro, string? user, IEnumerable<SessionSnapshot> sessions, IEnumerable<RepoInfo> repos)
    {
        IEnumerable<string> recent = sessions
            .Where(session => string.Equals(session.Distro, distro, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(session => session.Updated)
            .Select(session => session.ResumeFolder)
            .OfType<string>()
            .Select(LinuxFolder.RepositoryRoot);
        IEnumerable<string> found = repos.Select(repo => repo.Path).Order(StringComparer.Ordinal);
        return new[] { defaultFolder }
            .Concat(recent.Concat(found).Select(folder => LinuxFolder.Abbreviate(folder, user)))
            .Where(LinuxFolder.IsValid)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
