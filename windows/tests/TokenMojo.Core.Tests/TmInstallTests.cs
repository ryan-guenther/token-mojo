using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class TmInstallTests
{
    private const string WindowsLauncher = "#!/usr/bin/env bash\n# tm launcher\nexec bash '/home/alice/token-mojo/cctokmon-tm.sh' \"$@\"\n";
    private const string TmScript = "#!/usr/bin/env bash\n# cctokmon-tm.sh — start or resume a Claude Code session in this terminal tab\n\nset -euo pipefail\n";

    [Theory]
    [InlineData(WindowsLauncher)]
    [InlineData(TmScript)]
    [InlineData("#!/usr/bin/env bash\r\n# tm launcher\r\nexec bash x\r\n")]
    public void Classify_ContentOfTheInstallersTm_IsOurs(string content)
    {
        // Act / Assert
        Assert.Equal(TmState.Ours, TmInstall.Classify(content));
    }

    [Theory]
    [InlineData("#!/bin/sh\necho 'a different tm'\n")]
    [InlineData("")]
    [InlineData("#!/usr/bin/env bash\necho '# tm launcher is mentioned but not on a line of its own'\n")]
    [InlineData("#!/usr/bin/env bash\n# my tm launcher\n")]
    public void Classify_AnyOtherContent_IsForeign(string content)
    {
        // Act / Assert
        Assert.Equal(TmState.Foreign, TmInstall.Classify(content));
    }

    [Fact]
    public void FromScript_ExitedZero_IsClassifiedByWhatItPrinted()
    {
        // Act / Assert
        Assert.Equal((TmState.Ours, TmState.Foreign), (TmInstall.FromScript(0, TmScript), TmInstall.FromScript(0, "#!/bin/sh\n")));
    }

    [Fact]
    public void FromScript_NoTm_IsMissing()
    {
        // Act / Assert
        Assert.Equal(TmState.Missing, TmInstall.FromScript(3, ""));
    }

    [Fact]
    public void FromScript_TmIsNotARegularFile_IsForeign()
    {
        // Act / Assert
        Assert.Equal(TmState.Foreign, TmInstall.FromScript(4, ""));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(127)]
    public void FromScript_AnyOtherExit_IsUnreadableNotMissingOrUnchecked(int exitCode)
    {
        // Act / Assert
        Assert.Equal(TmState.Unreadable, TmInstall.FromScript(exitCode, ""));
    }

    [Fact]
    public void ProblemText_Unreadable_NamesTheFileAndSaysItCouldNotBeRead()
    {
        // Act
        string? text = TmInstall.ProblemText(TmState.Unreadable, "alice", "Ubuntu", "/home/alice");

        // Assert
        Assert.Equal(
            "The tm for alice in Ubuntu (/home/alice/.local/bin/tm) could not be read, so TokenMojo did not run it. Check its permissions.",
            text);
    }

    [Fact]
    public void InspectScript_FollowsTheLinkAndReadsOnlyTheStart()
    {
        // Act / Assert
        Assert.Equal(
            "f=\"$HOME/.local/bin/tm\"; [ -e \"$f\" ] || exit 3; [ -f \"$f\" ] || exit 4; head -c 4096 -- \"$f\"",
            TmInstall.InspectScript);
    }

    [Fact]
    public void ProblemText_OursIsNoProblem()
    {
        // Act / Assert
        Assert.Null(TmInstall.ProblemText(TmState.Ours, "alice", "Ubuntu", "/home/alice"));
    }

    [Fact]
    public void ProblemText_Missing_SaysToRunTheInstaller()
    {
        // Act
        string? text = TmInstall.ProblemText(TmState.Missing, "alice", "Ubuntu", "/home/alice");

        // Assert
        Assert.Equal("Could not find tm for alice in Ubuntu. Run install.sh in that distro, or try again if the distro was just starting.", text);
    }

    [Fact]
    public void ProblemText_Unchecked_SaysItCouldNotBeCheckedRatherThanMissing()
    {
        // Act
        string? text = TmInstall.ProblemText(TmState.Unchecked, "alice", "Ubuntu", "/home/alice");

        // Assert
        Assert.Equal("Could not check tm for alice in Ubuntu: the distro did not answer. Try again in a moment.", text);
    }

    [Fact]
    public void ProblemText_Foreign_NamesTheFileAndSaysNothingWasStarted()
    {
        // Act
        string? text = TmInstall.ProblemText(TmState.Foreign, "alice", "Ubuntu", "/home/alice");

        // Assert
        Assert.Equal(
            "The tm for alice in Ubuntu (/home/alice/.local/bin/tm) is not cctokmon's, so install.sh left it alone and TokenMojo did not run it. "
            + "Move it aside and run install.sh again.",
            text);
    }
}
