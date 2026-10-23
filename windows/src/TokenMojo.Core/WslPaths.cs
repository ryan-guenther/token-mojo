namespace TokenMojo.Core;

public static class WslPaths
{
    public const string DefaultShareRoot = @"\\wsl.localhost";

    /// <summary>Where `tm` is installed for a user, on the distro share; null when the user is not known.</summary>
    public static string? TmInstallPath(string distro, string? user, string shareRoot = DefaultShareRoot)
    {
        if (user is null) return null;
        string home = user == "root" ? "/root" : $"/home/{user}";
        return SharePath(distro, $"{home}/.local/bin/tm", shareRoot);
    }

    /// <summary>The Windows path of a Linux folder, through the distro's network share.</summary>
    public static string SharePath(string distro, string linuxPath, string shareRoot = DefaultShareRoot) =>
        $@"{shareRoot}\{distro}\{linuxPath.Replace('/', '\\').TrimStart('\\')}".TrimEnd('\\');
}
