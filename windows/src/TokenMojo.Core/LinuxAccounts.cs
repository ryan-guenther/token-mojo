namespace TokenMojo.Core;

/// <summary>Asks a running distro who its users are.</summary>
public interface ILinuxAccounts
{
    /// <summary>Null when the distro cannot be asked.</summary>
    LinuxAccounts? Query(string distro);
}

/// <param name="DefaultUser">The user wsl.exe runs commands as when it is given no -u, which is also who the distro share writes as.</param>
/// <param name="UserByHome">The account that owns each home folder, keyed by the folder's Linux path.</param>
public sealed record LinuxAccounts(string DefaultUser, IReadOnlyDictionary<string, string> UserByHome)
{
    /// <summary>Run by `sh -c` in the distro without -u: the default user's name, then every passwd entry.</summary>
    public const string QueryCommand = "id -un; getent passwd";

    /// <summary>Reads the output of <see cref="QueryCommand"/>; null when it holds no user name.</summary>
    public static LinuxAccounts? Parse(string output)
    {
        string[] lines = output.Split('\n', StringSplitOptions.TrimEntries);
        if (lines[0].Length == 0) return null;
        Dictionary<string, string> userByHome = new(StringComparer.Ordinal);
        foreach (string line in lines.Skip(1))
        {
            // name:password:uid:gid:gecos:home:shell
            string[] fields = line.Split(':');
            if (fields.Length < 7 || fields[0].Length == 0 || fields[5].Length == 0) continue;
            string home = fields[5].TrimEnd('/');
            // A home shared by two accounts goes to the default user, else to the first.
            if (!userByHome.TryAdd(home, fields[0]) && fields[0] == lines[0]) userByHome[home] = fields[0];
        }
        return new LinuxAccounts(lines[0], userByHome);
    }
}
