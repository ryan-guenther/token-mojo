namespace TokenMojo.Core;

public static class WslPaths
{
    public const string DefaultShareRoot = @"\\wsl.localhost";

    /// <summary>The Windows path of a Linux folder, through the distro's network share.</summary>
    public static string SharePath(string distro, string linuxPath, string shareRoot = DefaultShareRoot) =>
        $@"{shareRoot}\{distro}\{linuxPath.Replace('/', '\\').TrimStart('\\')}".TrimEnd('\\');
}
