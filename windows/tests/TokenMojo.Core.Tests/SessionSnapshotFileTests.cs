using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class SessionSnapshotFileTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private const string Id = "11111111-2222-4333-8444-555555555555";
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Build_OneSession_WritesEveryFieldInOrder()
    {
        // Arrange
        SessionSnapshot session = Session(
            """
            {"session_name":"Fix the widget's label",
             "workspace":{"current_dir":"/home/alice/widgets/.claude/worktrees/pr-12/src","project_dir":"/home/alice/widgets/.claude/worktrees/pr-12",
                          "repo":{"host":"github.com","owner":"octo","name":"widgets"}},
             "worktree":{"name":"pr-12"}}
            """) with
        { Status = SessionStatus.Ready, StatusSince = new DateTimeOffset(2026, 10, 22, 6, 30, 5, TimeSpan.FromHours(-6)), Distro = "Ubuntu" };
        IReadOnlyList<SnapshotSession> entries = SessionSnapshotFile.Entries([session], Now);

        // Act
        string json = SessionSnapshotFile.Build(entries, Now.AddSeconds(3));

        // Assert
        Assert.Equal("""
            {
              "version": 1,
              "written": "2026-10-22T12:00:03Z",
              "sessions": [
                {
                  "id": "11111111-2222-4333-8444-555555555555",
                  "title": "Fix the widget's label",
                  "status": "ready",
                  "statusSince": "2026-10-22T12:30:05Z",
                  "open": true,
                  "distro": "Ubuntu",
                  "folder": "/home/alice/widgets/.claude/worktrees/pr-12",
                  "repository": "octo/widgets",
                  "worktree": "pr-12",
                  "pullRequest": {
                    "owner": "octo",
                    "repo": "widgets",
                    "number": 12
                  }
                }
              ]
            }
            """.ReplaceLineEndings("\n"), json);
    }

    [Fact]
    public void Build_SessionWithoutOptionalFields_WritesThemAsNull()
    {
        // Arrange
        SessionSnapshot session = Session("""{"workspace":{"current_dir":"C:\\Users\\alice\\widgets"}}""");
        IReadOnlyList<SnapshotSession> entries = SessionSnapshotFile.Entries([session], Now);

        // Act
        string json = SessionSnapshotFile.Build(entries, Now);

        // Assert
        Assert.Equal("""
            {
              "version": 1,
              "written": "2026-10-22T12:00:00Z",
              "sessions": [
                {
                  "id": "11111111-2222-4333-8444-555555555555",
                  "title": "widgets",
                  "status": "none",
                  "statusSince": null,
                  "open": true,
                  "distro": null,
                  "folder": "C:\\Users\\alice\\widgets",
                  "repository": null,
                  "worktree": null,
                  "pullRequest": null
                }
              ]
            }
            """.ReplaceLineEndings("\n"), json);
    }

    [Fact]
    public void Build_NoSessions_WritesAnEmptyList()
    {
        // Act
        string json = SessionSnapshotFile.Build([], Now);

        // Assert
        Assert.Equal("""
            {
              "version": 1,
              "written": "2026-10-22T12:00:00Z",
              "sessions": []
            }
            """.ReplaceLineEndings("\n"), json);
    }

    [Theory]
    [InlineData(SessionStatus.NeedsInput, "\"status\": \"needs_input\"")]
    [InlineData(SessionStatus.Ready, "\"status\": \"ready\"")]
    [InlineData(SessionStatus.Working, "\"status\": \"working\"")]
    [InlineData(SessionStatus.None, "\"status\": \"none\"")]
    [InlineData(SessionStatus.Ended, "\"status\": \"ended\"")]
    public void Build_EachStatus_WritesItsName(SessionStatus status, string expected)
    {
        // Arrange
        IReadOnlyList<SnapshotSession> entries = SessionSnapshotFile.Entries([Session("{}") with { Status = status }], Now);

        // Act
        string json = SessionSnapshotFile.Build(entries, Now);

        // Assert
        Assert.Contains(expected, json);
    }

    [Fact]
    public void Entries_EndedSession_IsNotOpen()
    {
        // Arrange
        SessionSnapshot session = Session("{}") with { Status = SessionStatus.Ended };

        // Act
        SnapshotSession entry = SessionSnapshotFile.Entries([session], Now).Single();

        // Assert
        Assert.False(entry.Open);
    }

    [Fact]
    public void Entries_IdleSessionWithoutHooks_IsNotOpen()
    {
        // Arrange
        SessionSnapshot session = Session("{}", updated: Now.AddMinutes(-30));

        // Act
        SnapshotSession entry = SessionSnapshotFile.Entries([session], Now).Single();

        // Assert
        Assert.False(entry.Open);
    }

    [Fact]
    public void Entries_WorkingSessionWithAnOldBridgeWrite_IsOpen()
    {
        // Arrange
        SessionSnapshot session = Session("{}", updated: Now.AddHours(-2)) with { Status = SessionStatus.Working };

        // Act
        SnapshotSession entry = SessionSnapshotFile.Entries([session], Now).Single();

        // Assert
        Assert.True(entry.Open);
    }

    [Fact]
    public void Entries_WindowsSession_HasNoDistro()
    {
        // Arrange
        SessionSnapshot session = Session("{}") with { Distro = null };

        // Act
        SnapshotSession entry = SessionSnapshotFile.Entries([session], Now).Single();

        // Assert
        Assert.Null(entry.Distro);
    }

    [Fact]
    public void Entries_FolderWithoutProjectDir_IsTheCurrentDir()
    {
        // Arrange
        SessionSnapshot session = Session("""{"workspace":{"current_dir":"/home/alice/widgets"}}""");

        // Act
        SnapshotSession entry = SessionSnapshotFile.Entries([session], Now).Single();

        // Assert
        Assert.Equal("/home/alice/widgets", entry.Folder);
    }

    [Fact]
    public void Entries_PullRequestWorktreeOfAGitHubRepository_NamesThePullRequest()
    {
        // Arrange
        SessionSnapshot session = Session(Worktree("github.com", "pr-12"));

        // Act
        SnapshotSession entry = SessionSnapshotFile.Entries([session], Now).Single();

        // Assert
        Assert.Equal("octo/widgets", entry.Repository);
        Assert.Equal("pr-12", entry.Worktree);
        Assert.Equal(new PullRequestRef("octo", "widgets", 12), entry.PullRequest);
    }

    [Fact]
    public void Entries_GitHubHostInCapitals_StillNamesTheRepository()
    {
        // Arrange
        SessionSnapshot session = Session(Worktree("GitHub.com", "pr-12"));

        // Act
        SnapshotSession entry = SessionSnapshotFile.Entries([session], Now).Single();

        // Assert
        Assert.Equal("octo/widgets", entry.Repository);
    }

    [Fact]
    public void Entries_RepositoryOnAnotherHost_HasNoRepositoryOrPullRequest()
    {
        // Arrange
        SessionSnapshot session = Session(Worktree("git.example.com", "pr-12"));

        // Act
        SnapshotSession entry = SessionSnapshotFile.Entries([session], Now).Single();

        // Assert
        Assert.Null(entry.Repository);
        Assert.Null(entry.PullRequest);
    }

    [Fact]
    public void Entries_WorktreeNameWithTextAfterTheNumber_HasNoPullRequest()
    {
        // Arrange
        SessionSnapshot session = Session(Worktree("github.com", "pr-12x"));

        // Act
        SnapshotSession entry = SessionSnapshotFile.Entries([session], Now).Single();

        // Assert
        Assert.Equal("pr-12x", entry.Worktree);
        Assert.Null(entry.PullRequest);
    }

    [Fact]
    public void Entries_WorktreeNotNamedForAPullRequest_HasNoPullRequest()
    {
        // Arrange
        SessionSnapshot session = Session(Worktree("github.com", "feature"));

        // Act
        SnapshotSession entry = SessionSnapshotFile.Entries([session], Now).Single();

        // Assert
        Assert.Null(entry.PullRequest);
    }

    [Fact]
    public void Entries_PullRequestWorktreeWithoutARepository_HasNoPullRequest()
    {
        // Arrange
        SessionSnapshot session = Session("""{"workspace":{"current_dir":"/w"},"worktree":{"name":"pr-12"}}""");

        // Act
        SnapshotSession entry = SessionSnapshotFile.Entries([session], Now).Single();

        // Assert
        Assert.Null(entry.Repository);
        Assert.Null(entry.PullRequest);
    }

    [Fact]
    public void Entries_OpenAndClosedSessions_ListsThemInTheDashboardsOrder()
    {
        // Arrange
        SessionSnapshot[] sessions =
        [
            Session("{}", "ended", Now.AddMinutes(-1)) with { Status = SessionStatus.Ended },
            Session("{}", "working", Now.AddMinutes(-1)) with { Status = SessionStatus.Working },
            Session("{}", "idle", Now.AddMinutes(-30)),
            Session("{}", "needs-input", Now.AddMinutes(-20)) with { Status = SessionStatus.NeedsInput },
        ];

        // Act
        IReadOnlyList<SnapshotSession> entries = SessionSnapshotFile.Entries(sessions, Now);

        // Assert
        Assert.Equal(["needs-input", "working", "ended", "idle"], entries.Select(entry => entry.Id));
    }

    [Fact]
    public void ShouldWrite_NothingWrittenYet_Writes()
    {
        // Arrange
        IReadOnlyList<SnapshotSession> current = SessionSnapshotFile.Entries([Session("{}")], Now);

        // Act
        bool write = SessionSnapshotFile.ShouldWrite(null, current, null, Now);

        // Assert
        Assert.True(write);
    }

    [Fact]
    public void ShouldWrite_SessionsChangedSinceARecentWrite_Writes()
    {
        // Arrange
        IReadOnlyList<SnapshotSession> previous = SessionSnapshotFile.Entries([Session("{}")], Now);
        IReadOnlyList<SnapshotSession> current = SessionSnapshotFile.Entries([Session("{}") with { Status = SessionStatus.Working }], Now);

        // Act
        bool write = SessionSnapshotFile.ShouldWrite(previous, current, Now.AddSeconds(-5), Now);

        // Assert
        Assert.True(write);
    }

    [Fact]
    public void ShouldWrite_SameSessionsWrittenUnderAMinuteAgo_DoesNotWrite()
    {
        // Arrange
        IReadOnlyList<SnapshotSession> previous = SessionSnapshotFile.Entries([Session(Worktree("github.com", "pr-12"))], Now);
        IReadOnlyList<SnapshotSession> current = SessionSnapshotFile.Entries([Session(Worktree("github.com", "pr-12"))], Now);

        // Act
        bool write = SessionSnapshotFile.ShouldWrite(previous, current, Now.AddSeconds(-59), Now);

        // Assert
        Assert.False(write);
    }

    [Fact]
    public void ShouldWrite_SameSessionsWrittenAMinuteAgo_Writes()
    {
        // Arrange
        IReadOnlyList<SnapshotSession> previous = SessionSnapshotFile.Entries([Session("{}")], Now);
        IReadOnlyList<SnapshotSession> current = SessionSnapshotFile.Entries([Session("{}")], Now);

        // Act
        bool write = SessionSnapshotFile.ShouldWrite(previous, current, Now.AddSeconds(-60), Now);

        // Assert
        Assert.True(write);
    }

    [Fact]
    public void Write_MissingFolder_CreatesItAndWritesTheFile()
    {
        // Arrange
        string path = Path.Combine(_temp.Path, "TokenMojo", "sessions.json");

        // Act
        SessionSnapshotFile.Write(path, "{}");

        // Assert
        Assert.Equal("{}", File.ReadAllText(path));
    }

    [Fact]
    public void Write_ExistingFile_ReplacesItAndLeavesNoTempFile()
    {
        // Arrange
        string path = _temp.Write("sessions.json", "old", Now);

        // Act
        SessionSnapshotFile.Write(path, "new");

        // Assert
        Assert.Equal("new", File.ReadAllText(path));
        Assert.Equal(["sessions.json"], Directory.GetFiles(_temp.Path).Select(Path.GetFileName));
    }

    private static string Worktree(string host, string name) =>
        $$$"""{"workspace":{"current_dir":"/w","repo":{"host":"{{{host}}}","owner":"octo","name":"widgets"}},"worktree":{"name":"{{{name}}}"}}""";

    private static SessionSnapshot Session(string json, string id = Id, DateTimeOffset? updated = null) =>
        new(id, BridgeState.Parse(json), updated ?? Now.AddMinutes(-1));
}
