using Luotsi.Cli.Cli;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public void Parse_Allows_Global_Options_Before_Command()
    {
        var options = CliOptions.Parse(["--device", "abc", "devices"]);

        Assert.Equal("devices", options.Command);
        Assert.Equal("abc", options.Get("device"));
    }


    [Fact]
    public void Parse_Allows_Global_Options_Before_View_Command()
    {
        var options = CliOptions.Parse(["--device", "abc", "view"]);

        Assert.Equal("view", options.Command);
        Assert.Equal("abc", options.Get("device"));
    }

    [Fact]
    public void Parse_Captures_Adb_Family_Subcommand_Arguments()
    {
        var options = CliOptions.Parse(["--device", "abc", "adb", "mdns", "check"]);

        Assert.Equal("adb", options.Command);
        Assert.Equal("abc", options.Get("device"));
        Assert.Equal(["mdns", "check"], options.Arguments);
    }

    [Fact]
    public void Parse_Allows_Flags_Before_Command()
    {
        var options = CliOptions.Parse(["--defaults", "view"]);

        Assert.Equal("view", options.Command);
        Assert.True(options.HasFlag("defaults"));
    }

    [Fact]
    public void Parse_Skips_Known_Command_Words_When_They_Are_Option_Values()
    {
        var options = CliOptions.Parse(["--package", "devices", "preflight"]);

        Assert.Equal("preflight", options.Command);
        Assert.Equal("devices", options.Get("package"));
    }

    [Fact]
    public void Parse_Recognizes_Doctor_Command()
    {
        var options = CliOptions.Parse(["--device", "abc", "doctor", "--fix"]);

        Assert.Equal("doctor", options.Command);
        Assert.Equal("abc", options.Get("device"));
        Assert.True(options.HasFlag("fix"));
    }

    [Fact]
    public void Parse_Recognizes_Quickstart_Command()
    {
        var options = CliOptions.Parse(["--device", "abc", "quickstart", "--package", "dev.luotsi.demo"]);

        Assert.Equal("quickstart", options.Command);
        Assert.Equal("abc", options.Get("device"));
        Assert.Equal("dev.luotsi.demo", options.Get("package"));
    }

    [Fact]
    public void Parse_Normalizes_ViewSetup_Alias_Command_And_Removes_Alias_Argument()
    {
        var options = CliOptions.Parse(["view", "--device", "abc", "setup", "extra"]);

        Assert.Equal("view-setup", options.Command);
        Assert.Equal(["extra"], options.Arguments);
        Assert.Equal("abc", options.Get("device"));
    }

    [Fact]
    public void Parse_Captures_Replay_Subcommand_Arguments()
    {
        var options = CliOptions.Parse(["replay", "summarize", "--artifacts", "/tmp/replay-root"]);

        Assert.Equal("replay", options.Command);
        Assert.Equal(["summarize"], options.Arguments);
        Assert.Equal("/tmp/replay-root", options.Get("artifacts"));
    }

}
