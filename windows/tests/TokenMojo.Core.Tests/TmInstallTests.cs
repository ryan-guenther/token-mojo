using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class TmInstallTests : IDisposable
{
    private const string WindowsLauncher = "#!/usr/bin/env bash\n# tm launcher\nexec bash '/home/alice/token-mojo/cctokmon-tm.sh' \"$@\"\n";
    private const string TmScript = "#!/usr/bin/env bash\n# cctokmon-tm.sh — start or resume a Claude Code session in this terminal tab\n\nset -euo pipefail\n";

    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

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
    public void Inspect_FileThere_IsClassifiedByItsContent()
    {
        // Arrange
        string ours = Path.Combine(_temp.Path, "ours");
        string foreign = Path.Combine(_temp.Path, "foreign");
        File.WriteAllText(ours, WindowsLauncher);
        File.WriteAllText(foreign, "#!/bin/sh\n");

        // Act / Assert
        Assert.Equal((TmState.Ours, TmState.Foreign), (TmInstall.Inspect(ours), TmInstall.Inspect(foreign)));
    }

    [Fact]
    public void Inspect_NoFile_IsMissing()
    {
        // Act / Assert
        Assert.Equal(TmState.Missing, TmInstall.Inspect(Path.Combine(_temp.Path, "tm")));
    }

    [Fact]
    public void Inspect_LargeFile_OnlyItsStartIsRead()
    {
        // Arrange
        string path = Path.Combine(_temp.Path, "tm");
        File.WriteAllText(path, "#!/usr/bin/env bash\n# tm launcher\n" + new string('x', 1_000_000));

        // Act / Assert
        Assert.Equal(TmState.Ours, TmInstall.Inspect(path));
    }

    [Fact]
    public void Inspect_PathIsADirectory_IsForeign()
    {
        // Act / Assert
        Assert.Equal(TmState.Foreign, TmInstall.Inspect(_temp.MakeDir("tm")));
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
