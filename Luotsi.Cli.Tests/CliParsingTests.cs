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

}
