using System.Globalization;
using System.Text;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal interface IScenarioTemplateResolver
{
    ScenarioFile ResolveScenario(ScenarioFile scenario);
}

internal sealed class ScenarioTemplateResolver(TimeProvider timeProvider, IEnvironmentVariables environment) : IScenarioTemplateResolver
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public ScenarioFile ResolveScenario(ScenarioFile scenario)
    {
        var resolvedVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (scenario.Variables is not null)
        {
            foreach (var key in scenario.Variables.Keys)
            {
                ResolveVariable(key, scenario.Variables, resolvedVariables, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
        }

        return scenario with
        {
            Name = ResolveValue(scenario.Name, scenario.Variables, resolvedVariables) ?? scenario.Name,
            Tags = scenario.Tags?.Select(value => ResolveValue(value, scenario.Variables, resolvedVariables) ?? value).ToArray(),
            Setup = ResolveSteps(scenario.Setup, scenario.Variables, resolvedVariables),
            Steps = ResolveSteps(scenario.Steps, scenario.Variables, resolvedVariables)!,
            Teardown = ResolveSteps(scenario.Teardown, scenario.Variables, resolvedVariables)
        };
    }

    private ScenarioStep[]? ResolveSteps(
        IReadOnlyList<ScenarioStep>? steps,
        IReadOnlyDictionary<string, string>? variables,
        IDictionary<string, string> resolvedVariables) =>
        steps?.Select(step => ResolveStep(step, variables, resolvedVariables)).ToArray();

    private ScenarioStep ResolveStep(
        ScenarioStep step,
        IReadOnlyDictionary<string, string>? variables,
        IDictionary<string, string> resolvedVariables) =>
        step with
        {
            Name = ResolveValue(step.Name, variables, resolvedVariables),
            Action = ResolveValue(step.Action, variables, resolvedVariables) ?? step.Action,
            Text = ResolveValue(step.Text, variables, resolvedVariables),
            Code = ResolveValue(step.Code, variables, resolvedVariables),
            Step = ResolveValue(step.Step, variables, resolvedVariables),
            Label = ResolveValue(step.Label, variables, resolvedVariables),
            Event = ResolveValue(step.Event, variables, resolvedVariables),
            Contains = step.Contains?.Select(value => ResolveValue(value, variables, resolvedVariables) ?? value).ToArray(),
            DetailsPattern = ResolveValue(step.DetailsPattern, variables, resolvedVariables),
            Below = ResolveValue(step.Below, variables, resolvedVariables),
            With = ResolveValue(step.With, variables, resolvedVariables),
            Package = ResolveValue(step.Package, variables, resolvedVariables),
            Activity = ResolveValue(step.Activity, variables, resolvedVariables),
            Uri = ResolveValue(step.Uri, variables, resolvedVariables),
            Permission = ResolveValue(step.Permission, variables, resolvedVariables),
            IntentAction = ResolveValue(step.IntentAction, variables, resolvedVariables),
            ExpectedSha256 = ResolveValue(step.ExpectedSha256, variables, resolvedVariables),
            ExpectedSha256File = ResolveValue(step.ExpectedSha256File, variables, resolvedVariables),
            BaselineFile = ResolveValue(step.BaselineFile, variables, resolvedVariables),
            ExpectedRegionSha256 = ResolveValue(step.ExpectedRegionSha256, variables, resolvedVariables),
            ExpectedRegionSha256File = ResolveValue(step.ExpectedRegionSha256File, variables, resolvedVariables)
        };

    private string ResolveVariable(
        string name,
        IReadOnlyDictionary<string, string> variables,
        IDictionary<string, string> resolvedVariables,
        ISet<string> stack)
    {
        if (resolvedVariables.TryGetValue(name, out var resolved))
        {
            return resolved;
        }

        if (!variables.TryGetValue(name, out var template))
        {
            throw new UsageException($"Scenario variable '{name}' is not defined.");
        }

        if (!stack.Add(name))
        {
            throw new UsageException($"Scenario variable '{name}' is part of a cycle.");
        }

        resolved = ResolveValue(template, variables, resolvedVariables, stack) ?? string.Empty;
        stack.Remove(name);
        resolvedVariables[name] = resolved;
        return resolved;
    }

    private string? ResolveValue(
        string? value,
        IReadOnlyDictionary<string, string>? variables,
        IDictionary<string, string> resolvedVariables,
        ISet<string>? stack = null)
    {
        if (value is null)
        {
            return null;
        }

        var builder = new StringBuilder();

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '$' && index + 1 < value.Length && value[index + 1] == '{')
            {
                var endIndex = FindPlaceholderEnd(value, index + 2);
                if (endIndex < 0)
                {
                    throw new UsageException($"Scenario template '{value}' has an unterminated placeholder.");
                }

                var token = value[(index + 2)..endIndex];
                builder.Append(ResolvePlaceholder(token, variables, resolvedVariables, stack ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
                index = endIndex;
                continue;
            }

            builder.Append(value[index]);
        }

        return builder.ToString();
    }

    private string ResolvePlaceholder(
        string token,
        IReadOnlyDictionary<string, string>? variables,
        IDictionary<string, string> resolvedVariables,
        ISet<string> stack)
    {
        if (token.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            var expression = token[4..];
            var splitIndex = FindTopLevelSeparator(expression, '|');
            var envName = splitIndex >= 0 ? expression[..splitIndex] : expression;
            var fallback = splitIndex >= 0 ? expression[(splitIndex + 1)..] : null;
            var envValue = _environment.GetEnvironmentVariable(envName);

            if (!string.IsNullOrWhiteSpace(envValue))
            {
                return envValue;
            }

            if (fallback is not null)
            {
                return ResolveValue(fallback, variables, resolvedVariables, stack) ?? string.Empty;
            }

            throw new UsageException($"Scenario requires environment variable '{envName}'.");
        }

        if (token.StartsWith("now:", StringComparison.OrdinalIgnoreCase))
        {
            return _timeProvider.GetUtcNow().ToLocalTime().ToString(token[4..], CultureInfo.InvariantCulture);
        }

        if (token.StartsWith("var:", StringComparison.OrdinalIgnoreCase))
        {
            if (variables is null)
            {
                throw new UsageException($"Scenario variable placeholder '{token}' has no variables block.");
            }

            return ResolveVariable(token[4..], variables, resolvedVariables, stack);
        }

        throw new UsageException($"Unsupported scenario placeholder '${{{token}}}'.");
    }

    private static int FindPlaceholderEnd(string value, int startIndex)
    {
        var depth = 1;

        for (var index = startIndex; index < value.Length; index++)
        {
            if (value[index] == '$' && index + 1 < value.Length && value[index + 1] == '{')
            {
                depth++;
                index++;
                continue;
            }

            if (value[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static int FindTopLevelSeparator(string value, char separator)
    {
        var depth = 0;

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '$' && index + 1 < value.Length && value[index + 1] == '{')
            {
                depth++;
                index++;
                continue;
            }

            if (value[index] == '}' && depth > 0)
            {
                depth--;
                continue;
            }

            if (depth == 0 && value[index] == separator)
            {
                return index;
            }
        }

        return -1;
    }
}
