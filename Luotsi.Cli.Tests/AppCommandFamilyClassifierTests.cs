using Luotsi.Cli.Cli;
using Luotsi.Cli.Cli.Routing;
using Luotsi.Cli.Cli.View;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class AppCommandFamilyClassifierTests
{
    [Fact]
    public void Classify_ProfileList_Returns_ProfileList_Family()
    {
        var classification = AppCommandFamilyClassifier.Classify(CliOptions.Parse(["profile-list"]));

        Assert.Equal(AppCommandFamily.ProfileList, classification.Family);
        Assert.Null(classification.ViewDiagnostic);
    }

    [Fact]
    public void Classify_Inspect_Returns_Inspect_Family()
    {
        var classification = AppCommandFamilyClassifier.Classify(CliOptions.Parse(["inspect"]));

        Assert.Equal(AppCommandFamily.Inspect, classification.Family);
        Assert.Null(classification.ViewDiagnostic);
    }

    [Fact]
    public void Classify_Doctor_Returns_Doctor_Family()
    {
        var classification = AppCommandFamilyClassifier.Classify(CliOptions.Parse(["doctor", "--device", "abc"]));

        Assert.Equal(AppCommandFamily.Doctor, classification.Family);
        Assert.Null(classification.ViewDiagnostic);
    }

    [Fact]
    public void Classify_ViewSetup_Alias_Returns_ViewDiagnostics_Setup_Invocation()
    {
        var classification = AppCommandFamilyClassifier.Classify(CliOptions.Parse(["view", "setup", "--device", "abc"]));

        Assert.Equal(AppCommandFamily.ViewDiagnostics, classification.Family);
        var invocation = Assert.IsType<ViewDiagnosticInvocation>(classification.ViewDiagnostic);
        Assert.Equal(ViewDiagnosticAction.Setup, invocation.Action);
        Assert.Equal("view-setup", invocation.EnvelopeCommand);
        Assert.True(invocation.Fix);
    }

    [Fact]
    public void Classify_ViewDoctorFix_Returns_ViewDiagnostics_Setup_Invocation()
    {
        var classification = AppCommandFamilyClassifier.Classify(CliOptions.Parse(["view-doctor", "--device", "abc", "--fix"]));

        Assert.Equal(AppCommandFamily.ViewDiagnostics, classification.Family);
        var invocation = Assert.IsType<ViewDiagnosticInvocation>(classification.ViewDiagnostic);
        Assert.Equal(ViewDiagnosticAction.Setup, invocation.Action);
        Assert.Equal("view-doctor", invocation.EnvelopeCommand);
        Assert.True(invocation.Fix);
    }

    [Fact]
    public void Classify_Reconnect_Returns_ViewSession_Family()
    {
        var classification = AppCommandFamilyClassifier.Classify(CliOptions.Parse(["reconnect"]));

        Assert.Equal(AppCommandFamily.ViewSession, classification.Family);
        Assert.Null(classification.ViewDiagnostic);
    }

    [Fact]
    public void Classify_Devices_Returns_HostedCommand_Family()
    {
        var classification = AppCommandFamilyClassifier.Classify(CliOptions.Parse(["devices"]));

        Assert.Equal(AppCommandFamily.HostedCommand, classification.Family);
        Assert.Null(classification.ViewDiagnostic);
    }
}