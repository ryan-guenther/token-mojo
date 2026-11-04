using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>
/// Follows a tokenmojo:// link. A launch link starts a session at once, with no window, through the launcher's own
/// <see cref="SessionStarter"/>; a focus link does what clicking the session's row does. Problems are handed to
/// <paramref name="showProblem"/>; success shows nothing, since the terminal window appears. Call on the UI thread.
/// </summary>
internal sealed class LinkHandler(string settingsPath, ClaudeDirLocator locator, IWslDistros wsl, Action<string> showProblem)
{
    private const string NoTargetText =
        "Claude Code was not found on Windows or in a running WSL distro, so the link cannot start a session. Run install.sh, or open the distro.";

    // Launches still starting, by action and pull request, so a second click on the same link is ignored. UI thread only.
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public async Task HandleAsync(string text)
    {
        string? problem;
        try
        {
            LinkParse parsed = TokenMojoLink.Parse(text);
            problem = parsed.Link switch
            {
                LaunchLink launch => await LaunchAsync(launch),
                FocusLink focus => await FocusAsync(focus),
                _ => parsed.Problem,
            };
        }
        // The link arrives in an event handler: nothing may escape it.
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or AggregateException)
        {
            Trace.TraceWarning("TokenMojo: cannot follow a link: {0}", e.Message);
            problem = $"Could not follow the link: {e.Message}";
        }
        if (problem is not null) showProblem(problem);
    }

    private async Task<string?> LaunchAsync(LaunchLink link)
    {
        string key = $"{link.Action} {link.PullRequest.Url}";
        if (!_inFlight.Add(key)) return null;
        try
        {
            return await Task.Run(() => Launch(link));
        }
        finally
        {
            _inFlight.Remove(key);
        }
    }

    // Off the UI thread: everything here reads the WSL share or runs wsl.exe.
    private string? Launch(LaunchLink link)
    {
        Settings settings;
        try
        {
            settings = Settings.Load(settingsPath);
        }
        catch (SettingsException e)
        {
            return e.Message;
        }
        (IReadOnlyList<string> targets, IReadOnlyList<ClaudeSource> sources) = SessionStarter.Locate(wsl, locator);
        if (LaunchTargets.ChooseDistro(settings.Distro, targets) is not { } target) return NoTargetText;
        if (LaunchTargets.IsWindows(target))
        {
            if (SessionControl.NativeCheck().Problem is { } nativeProblem) return nativeProblem;
            IReadOnlyList<RepoInfo> windowsRepos = RepoScanner.ScanWindows(SessionControl.WindowsHome, settings.WindowsRepoRoots);
            LinkLaunchResult native = LinkLaunch.Plan(link, settings, windowsRepos, onWindows: true);
            if (native.Plan is not { } nativePlan) return native.Problem;
            return SessionStarter.TryLaunchNative(nativePlan.Folder, nativePlan.Worktree, nativePlan.Prompt);
        }
        LaunchUser choice = SessionStarter.ChooseUser(sources, target);
        if (choice.Source is not { } source) return choice.Problem;
        IReadOnlyList<RepoInfo> repos = RepoScanner.Scan(target, source.Home, settings.RepoRoots);
        LinkLaunchResult result = LinkLaunch.Plan(link, settings, repos, onWindows: false);
        if (result.Plan is not { } plan) return result.Problem;
        return SessionStarter.TryLaunch(new LaunchRequest(target, source.User!, plan.Folder, plan.Worktree, plan.Prompt, source.Home));
    }

    private async Task<string?> FocusAsync(FocusLink link)
    {
        LinkFocusResult result = await Task.Run(() => FindSession(link));
        if (result.Session is not { } session) return result.Problem;
        // The row click shows its own failures.
        await SessionControl.ActivateAsync(SessionTarget.From(session));
        return null;
    }

    private LinkFocusResult FindSession(FocusLink link)
    {
        Settings settings;
        try
        {
            settings = Settings.Load(settingsPath);
        }
        catch (SettingsException e)
        {
            return new LinkFocusResult(null, e.Message);
        }
        DateTimeOffset now = DateTimeOffset.Now;
        IReadOnlyList<SessionSnapshot> sessions = SessionStore.LoadSessions(locator.Locate(), now, settings.SessionWindow);
        return LinkFocus.Find(link, sessions, now);
    }
}
