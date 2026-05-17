using System.Globalization;
using System.Text;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioTemplateResolver(TimeProvider timeProvider, IEnvironmentVariables environment)
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
            Steps = scenario.Steps.Select(step => step with
            {
                Name = ResolveValue(step.Name, scenario.Variables, resolvedVariables),
                Action = ResolveValue(step.Action, scenario.Variables, resolvedVariables) ?? step.Action,
                Text = ResolveValue(step.Text, scenario.Variables, resolvedVariables),
                Code = ResolveValue(step.Code, scenario.Variables, resolvedVariables),
                Step = ResolveValue(step.Step, scenario.Variables, resolvedVariables),
                Label = ResolveValue(step.Label, scenario.Variables, resolvedVariables),
                Event = ResolveValue(step.Event, scenario.Variables, resolvedVariables),
                Contains = step.Contains?.Select(value => ResolveValue(value, scenario.Variables, resolvedVariables) ?? value).ToArray(),
                DetailsPattern = ResolveValue(step.DetailsPattern, scenario.Variables, resolvedVariables),
                Below = ResolveValue(step.Below, scenario.Variables, resolvedVariables),
                With = ResolveValue(step.With, scenario.Variables, resolvedVariables),
                Package = ResolveValue(step.Package, scenario.Variables, resolvedVariables)
            }).ToArray()
        };
    }

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