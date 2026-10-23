namespace TokenMojo.Core;

public static class WslPaths
{
    public const string DefaultShareRoot = @"\\wsl.localhost";
    private static readonly string[] ShareRoots = [DefaultShareRoot, @"\\wsl$"];

    /// <summary>Where `tm` is installed for a user, on the distro share; null when the user is not known.</summary>
    public static string? TmInstallPath(string distro, string? user, string shareRoot = DefaultShareRoot)
    {
        if (user is null) return null;
        return SharePath(distro, $"{HomeFolder(user)}/.local/bin/tm", shareRoot);
    }

    public static string HomeFolder(string user) => user == "root" ? "/root" : $"/home/{user}";

    /// <summary>The Linux path of a folder picked on the distro's share, in either share spelling; null for any other path.</summary>
    public static string? ToLinuxPath(string windowsPath, string distro)
    {
        foreach (string root in ShareRoots)
        {
            string prefix = $@"{root}\{distro}";
            if (!windowsPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = windowsPath[prefix.Length..];
            // Another distro whose name starts with this one's.
            if (rest.Length > 0 && rest[0] != '\\') continue;
            return "/" + rest.Replace('\\', '/').Trim('/');
        }
        return null;
    }

    /// <summary>The Windows path of a Linux folder, through the distro's network share.</summary>
    public static string SharePath(string distro, string linuxPath, string shareRoot = DefaultShareRoot) =>
        $@"{shareRoot}\{distro}\{linuxPath.Replace('/', '\\').TrimStart('\\')}".TrimEnd('\\');
}
