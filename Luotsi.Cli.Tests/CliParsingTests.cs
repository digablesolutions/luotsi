using System.Text.Json;
using Luotsi.Cli;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;
using Luotsi.Cli.Telemetry;
using Luotsi.Cli.View;
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


}
