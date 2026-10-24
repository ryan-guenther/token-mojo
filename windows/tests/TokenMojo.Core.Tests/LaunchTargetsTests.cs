using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class LaunchTargetsTests
{
    [Fact]
    public void ChooseDistro_NoneConfigured_IsTheFirstRunning()
    {
        // Act / Assert
        Assert.Equal("Ubuntu", LaunchTargets.ChooseDistro(null, ["Ubuntu", "Debian"]));
    }

    [Fact]
    public void ChooseDistro_ConfiguredOneIsRunning_IsThatOneAsWslSpellsIt()
    {
        // Act / Assert
        Assert.Equal("Debian", LaunchTargets.ChooseDistro("debian", ["Ubuntu", "Debian"]));
    }

    [Fact]
    public void ChooseDistro_ConfiguredOneIsNotRunning_FallsBackToTheFirstRunning()
    {
        // Act / Assert
        Assert.Equal("Ubuntu", LaunchTargets.ChooseDistro("Arch", ["Ubuntu", "Debian"]));
    }

    [Fact]
    public void ChooseDistro_NothingRunning_IsNull()
    {
        // Act / Assert
        Assert.Null(LaunchTargets.ChooseDistro("Ubuntu", []));
    }

    [Fact]
    public void ChooseDistro_WindowsConfigured_IsWindowsEvenWithADistroBeforeIt()
    {
        // Act / Assert
        Assert.Equal(LaunchTargets.Windows, LaunchTargets.ChooseDistro("windows", ["Ubuntu", LaunchTargets.Windows]));
    }

    [Fact]
    public void Targets_DistrosWithAClaudeFolderInRunningOrder_ThenWindows()
    {
        // Arrange
        ClaudeSource[] sources = [Source("alice", distro: "Debian"), Source("bob"), WindowsSource()];

        // Act
        IReadOnlyList<string> targets = LaunchTargets.Targets(["Ubuntu", "docker-desktop", "Debian"], sources, windowsHasClaude: true);

        // Assert
        Assert.Equal(["Ubuntu", "Debian", LaunchTargets.Windows], targets);
    }

    [Fact]
    public void Targets_OnlyADistroWithoutClaude_IsJustWindows()
    {
        // Act
        IReadOnlyList<string> targets = LaunchTargets.Targets(["docker-desktop"], [WindowsSource()], windowsHasClaude: true);

        // Assert
        Assert.Equal([LaunchTargets.Windows], targets);
    }

    [Fact]
    public void Targets_NoClaudeOnWindows_LeavesWindowsOut()
    {
        // Act
        IReadOnlyList<string> targets = LaunchTargets.Targets(["Ubuntu"], [Source("alice"), WindowsSource()], windowsHasClaude: false);

        // Assert
        Assert.Equal(["Ubuntu"], targets);
    }

    [Fact]
    public void NativeProblem_GitBashAndOurTm_IsNone()
    {
        // Act / Assert
        Assert.Null(LaunchTargets.NativeProblem(@"C:\Git\bin\bash.exe", TmState.Ours, @"C:\Users\ann"));
    }

    [Fact]
    public void NativeProblem_NoGitBash_SaysSoWhateverTheTm()
    {
        // Act
        string? problem = LaunchTargets.NativeProblem(null, TmState.Ours, @"C:\Users\ann");

        // Assert
        Assert.Contains("Git Bash", problem);
    }

    [Theory]
    [InlineData(TmState.Missing, "Could not find tm")]
    [InlineData(TmState.Unreadable, "could not be read")]
    [InlineData(TmState.Foreign, "is not cctokmon's")]
    public void NativeProblem_TmNotOurs_NamesItsPath(TmState state, string expected)
    {
        // Act
        string? problem = LaunchTargets.NativeProblem(@"C:\Git\bin\bash.exe", state, @"C:\Users\ann\");

        // Assert
        Assert.Contains(expected, problem);
        Assert.Contains(@"C:\Users\ann\.local\bin\tm", problem);
    }

    [Fact]
    public void ChooseUser_DefaultUserHasTm_IsTheDefaultUserEvenWhenAnotherComesFirst()
    {
        // Arrange
        ClaudeSource[] sources = [Source("alice"), Source("bob", isDefault: true)];

        // Act
        LaunchUser choice = LaunchTargets.ChooseUser(sources, "Ubuntu", _ => TmState.Ours);

        // Assert
        Assert.Equal(("bob", null, null), (choice.Source!.User, choice.Problem, choice.Note));
    }

    [Theory]
    [InlineData(TmState.Missing)]
    [InlineData(TmState.Foreign)]
    public void ChooseUser_DefaultUserHasNoTmOfOurs_IsTheFirstUserThatHasOneAndTheNoteSaysWhy(TmState defaultsTm)
    {
        // Arrange
        ClaudeSource[] sources = [Source("alice"), Source("bob", isDefault: true), Source("carol")];

        // Act
        LaunchUser choice = LaunchTargets.ChooseUser(sources, "Ubuntu", source => source.User == "bob" ? defaultsTm : TmState.Ours);

        // Assert
        Assert.Equal("alice", choice.Source!.User);
        Assert.Null(choice.Problem);
        Assert.Equal("Starting as alice: the distro's default user, bob, has no tm of cctokmon's to run.", choice.Note);
    }

    [Fact]
    public void ChooseUser_NoDefaultUserAmongTheSources_IsTheFirstUserWithTmAndHasNoNote()
    {
        // Arrange
        ClaudeSource[] sources = [Source("alice"), Source("bob")];

        // Act
        LaunchUser choice = LaunchTargets.ChooseUser(sources, "Ubuntu", source => source.User == "alice" ? TmState.Missing : TmState.Ours);

        // Assert
        Assert.Equal(("bob", null, null), (choice.Source!.User, choice.Problem, choice.Note));
    }

    [Fact]
    public void ChooseUser_OnlyOtherDistrosAndTheWindowsProfileHaveFolders_ReportsNoClaudeFolder()
    {
        // Arrange
        ClaudeSource[] sources = [new("Windows", "s", "d"), Source("bob", distro: "Debian")];

        // Act
        LaunchUser choice = LaunchTargets.ChooseUser(sources, "Ubuntu", _ => TmState.Ours);

        // Assert
        Assert.Null(choice.Source);
        Assert.Equal("Could not find a Claude Code folder in Ubuntu. Run install.sh in that distro, then reopen this window.", choice.Problem);
    }

    [Fact]
    public void ChooseUser_NobodyHasTmOfOurs_ReportsTheDefaultUsersProblem()
    {
        // Arrange
        ClaudeSource[] sources = [Source("alice"), Source("bob", isDefault: true)];

        // Act
        LaunchUser choice = LaunchTargets.ChooseUser(sources, "Ubuntu", source => source.User == "bob" ? TmState.Foreign : TmState.Missing);

        // Assert
        Assert.Null(choice.Source);
        Assert.StartsWith("The tm for bob in Ubuntu (/home/bob/.local/bin/tm) is not cctokmon's", choice.Problem);
    }

    [Fact]
    public void ChooseUser_NobodyHasTmAndThereIsNoDefaultUser_ReportsTheFirstUsersProblem()
    {
        // Arrange
        ClaudeSource[] sources = [Source("alice"), Source("bob")];

        // Act
        LaunchUser choice = LaunchTargets.ChooseUser(sources, "Ubuntu", _ => TmState.Missing);

        // Assert
        Assert.StartsWith("Could not find tm for alice in Ubuntu.", choice.Problem);
    }

    [Fact]
    public void ChooseUser_DefaultUsersTmCouldNotBeChecked_ReportsThatInsteadOfFallingBackToAnotherUser()
    {
        // Arrange
        ClaudeSource[] sources = [Source("alice"), Source("bob", isDefault: true)];

        // Act
        LaunchUser choice = LaunchTargets.ChooseUser(sources, "Ubuntu", source => source.User == "bob" ? TmState.Unchecked : TmState.Ours);

        // Assert
        Assert.Null(choice.Source);
        Assert.StartsWith("Could not check tm for bob in Ubuntu", choice.Problem);
    }

    [Fact]
    public void ChooseUser_DefaultUsersTmCouldNotBeCheckedButALaterUserIsOurs_StillStopsTheSearch()
    {
        // Arrange
        ClaudeSource[] sources = [Source("alice", isDefault: true), Source("bob")];
        List<string> asked = new();

        // Act
        LaunchUser choice = LaunchTargets.ChooseUser(sources, "Ubuntu", source =>
        {
            asked.Add(source.User!);
            return source.User == "alice" ? TmState.Unchecked : TmState.Ours;
        });

        // Assert
        Assert.Null(choice.Source);
        Assert.StartsWith("Could not check tm for alice in Ubuntu", choice.Problem);
        Assert.Equal(["alice"], asked);
    }

    [Fact]
    public void ChooseUser_CheckCouldNotRunForAUser_IsSkippedAndReportedWithItsExitCodeWhenNobodyElseQualifies()
    {
        // Act
        LaunchUser choice = LaunchTargets.ChooseUser([Source("alice")], "Ubuntu", _ => new TmCheck(TmState.CouldNotRun, 127));

        // Assert
        Assert.Null(choice.Source);
        Assert.Contains("exit code 127", choice.Problem);
    }

    [Fact]
    public void ChooseUser_NonDefaultCandidateCouldNotBeChecked_IsSkipped()
    {
        // Arrange
        ClaudeSource[] sources = [Source("alice", isDefault: true), Source("bob"), Source("carol")];

        // Act
        LaunchUser choice = LaunchTargets.ChooseUser(sources, "Ubuntu", source => source.User switch
        {
            "alice" => TmState.Missing,
            "bob" => TmState.Unchecked,
            _ => TmState.Ours,
        });

        // Assert
        Assert.Equal("carol", choice.Source!.User);
    }

    [Fact]
    public void ChooseUser_FirstCandidateOfAListWithoutADefaultCouldNotBeChecked_IsSkipped()
    {
        // Arrange
        ClaudeSource[] sources = [Source("alice"), Source("bob")];

        // Act
        LaunchUser choice = LaunchTargets.ChooseUser(sources, "Ubuntu", source => source.User == "alice" ? TmState.Unchecked : TmState.Ours);

        // Assert
        Assert.Equal(("bob", null), (choice.Source!.User, choice.Problem));
    }

    [Fact]
    public void ChooseUser_NobodyQualifiesAndOneCouldNotBeChecked_ReportsNotChecked()
    {
        // Arrange
        ClaudeSource[] sources = [Source("alice", isDefault: true), Source("bob"), Source("carol")];

        // Act
        LaunchUser choice = LaunchTargets.ChooseUser(sources, "Ubuntu", source => source.User switch
        {
            "alice" => TmState.Foreign,
            "bob" => TmState.Unchecked,
            _ => TmState.Missing,
        });

        // Assert
        Assert.Null(choice.Source);
        Assert.StartsWith("Could not check tm for bob in Ubuntu", choice.Problem);
    }

    [Fact]
    public void ChooseUser_UnreadableTmIsSkippedLikeAForeignOne()
    {
        // Arrange
        ClaudeSource[] sources = [Source("alice", isDefault: true), Source("bob")];

        // Act
        LaunchUser choice = LaunchTargets.ChooseUser(sources, "Ubuntu", source => source.User == "alice" ? TmState.Unreadable : TmState.Ours);

        // Assert
        Assert.Equal("bob", choice.Source!.User);
        Assert.NotNull(choice.Note);
    }

    [Fact]
    public void ChooseUser_OnlyAnUnreadableTm_ReportsItCouldNotBeRead()
    {
        // Act
        LaunchUser choice = LaunchTargets.ChooseUser([Source("alice")], "Ubuntu", _ => TmState.Unreadable);

        // Assert
        Assert.Null(choice.Source);
        Assert.Contains("could not be read", choice.Problem);
    }

    [Fact]
    public void ChooseUser_DistroNameCaseDiffersFromWsl_StillMatches()
    {
        // Act
        LaunchUser choice = LaunchTargets.ChooseUser([Source("alice")], "ubuntu", _ => TmState.Ours);

        // Assert
        Assert.Equal("alice", choice.Source!.User);
    }

    private static ClaudeSource Source(string user, bool isDefault = false, string distro = "Ubuntu") =>
        new($"{distro}:{user}", "s", "d", distro, user, $"/home/{user}", isDefault);

    private static ClaudeSource WindowsSource() => new("Windows", "s", "d");
}
