using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class LinuxAccountsTests
{
    private const string Passwd =
        "root:x:0:0:root:/root:/bin/bash\n" +
        "daemon:x:1:1:daemon:/usr/sbin:/usr/sbin/nologin\n" +
        "alice:x:1000:1000:,,,:/home/alice.smith:/bin/bash\n" +
        "bob:x:1001:1001::/home/bob:/bin/zsh\n";

    [Fact]
    public void Parse_FirstLineIsTheDefaultUser()
    {
        // Act
        LinuxAccounts? accounts = LinuxAccounts.Parse("alice\n" + Passwd);

        // Assert
        Assert.Equal("alice", accounts!.DefaultUser);
    }

    [Fact]
    public void Parse_HomeFolderNamedDifferentlyFromTheAccount_MapsTheFolderToTheAccount()
    {
        // Act
        LinuxAccounts? accounts = LinuxAccounts.Parse("alice\n" + Passwd);

        // Assert
        Assert.Equal("alice", accounts!.UserByHome["/home/alice.smith"]);
        Assert.Equal("bob", accounts.UserByHome["/home/bob"]);
        Assert.Equal("root", accounts.UserByHome["/root"]);
    }

    [Fact]
    public void Parse_WindowsLineEndingsAndTrailingSlashes_AreTolerated()
    {
        // Act
        LinuxAccounts? accounts = LinuxAccounts.Parse("alice\r\nalice:x:1000:1000::/home/alice/:/bin/bash\r\n");

        // Assert
        Assert.Equal("alice", accounts!.UserByHome["/home/alice"]);
    }

    [Fact]
    public void Parse_TwoAccountsShareAHome_FirstOneWins()
    {
        // Act
        LinuxAccounts? accounts = LinuxAccounts.Parse("a\na:x:1:1::/srv/shared:/bin/sh\nb:x:2:2::/srv/shared:/bin/sh\n");

        // Assert
        Assert.Equal("a", accounts!.UserByHome["/srv/shared"]);
    }

    [Fact]
    public void Parse_MalformedPasswdLines_AreSkipped()
    {
        // Act
        LinuxAccounts? accounts = LinuxAccounts.Parse("alice\nnot a passwd line\n\nalice:x:1000:1000::/home/alice:/bin/bash\n");

        // Assert
        Assert.Equal(["/home/alice"], accounts!.UserByHome.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("  \r\n")]
    public void Parse_NoDefaultUser_IsNull(string output)
    {
        // Act / Assert
        Assert.Null(LinuxAccounts.Parse(output));
    }
}
