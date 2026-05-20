using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Provenance;

internal sealed class BuildProvenanceProvider(IEnvironmentVariables environment)
{
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public BuildProvenance Create()
    {
        var assembly = typeof(ResultSchemas).Assembly;
        return new BuildProvenance(
            "luotsi",
            GetVersion(assembly),
            FirstNonBlank("GITHUB_SHA", "BUILD_SOURCEVERSION", "CI_COMMIT_SHA"),
            FirstNonBlank("GITHUB_REF_NAME", "BUILD_SOURCEBRANCHNAME", "CI_COMMIT_REF_NAME"),
            FirstNonBlank("GITHUB_REPOSITORY", "BUILD_REPOSITORY_NAME", "CI_PROJECT_PATH"),
            GetCiProvider(),
            FirstNonBlank("GITHUB_RUN_ID", "BUILD_BUILDID", "CI_PIPELINE_ID"),
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            RuntimeInformation.FrameworkDescription);
    }

    private string GetVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";

    private string? GetCiProvider()
    {
        if (IsTruthy(_environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
        {
            return "github-actions";
        }

        if (IsTruthy(_environment.GetEnvironmentVariable("TF_BUILD")))
        {
            return "azure-pipelines";
        }

        if (IsTruthy(_environment.GetEnvironmentVariable("GITLAB_CI")))
        {
            return "gitlab";
        }

        if (IsTruthy(_environment.GetEnvironmentVariable("CI")))
        {
            return "ci";
        }

        return null;
    }

    private string? FirstNonBlank(params string[] names)
    {
        return names
            .Select(_environment.GetEnvironmentVariable)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
            ?.Trim();
    }

    private static bool IsTruthy(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase);
}
