using Luotsi.Cli.Errors;

namespace Luotsi.Cli.Models;

internal static class ScreenElementSelectorValidator
{
    public static ScreenElementSelector Validate(
        ScreenElementSelector selector,
        string commandName,
        ScreenElementSelectorFieldNaming fieldNaming = ScreenElementSelectorFieldNaming.SnakeCase)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var fields = SelectorFieldNames.For(fieldNaming);

        if (!selector.HasCriteria)
        {
            throw new UsageException($"{commandName} requires at least one selector field: {fields.Text}, {fields.ContentDescription}, {fields.ResourceId}, {fields.ClassName}, or {fields.Region}.");
        }

        ValidateMatchMode(selector.TextMatch, fields.TextMatch);
        ValidateMatchMode(selector.ContentDescriptionMatch, fields.ContentDescriptionMatch);
        ValidateMatchMode(selector.ResourceIdMatch, fields.ResourceIdMatch);
        ValidateMatchMode(selector.ClassNameMatch, fields.ClassNameMatch);

        if (selector.Region is not null &&
            (selector.Region.Left < 0 ||
             selector.Region.Top < 0 ||
             selector.Region.Right <= selector.Region.Left ||
             selector.Region.Bottom <= selector.Region.Top))
        {
            throw new UsageException("Selector region must have non-negative left/top and right/bottom greater than left/top.");
        }

        return selector;
    }

    private sealed record SelectorFieldNames(
        string Text,
        string TextMatch,
        string ContentDescription,
        string ContentDescriptionMatch,
        string ResourceId,
        string ResourceIdMatch,
        string ClassName,
        string ClassNameMatch,
        string Region)
    {
        public static SelectorFieldNames For(ScreenElementSelectorFieldNaming fieldNaming) =>
            fieldNaming == ScreenElementSelectorFieldNaming.CamelCase
                ? new(
                    "text",
                    "textMatch",
                    "contentDescription",
                    "contentDescriptionMatch",
                    "resourceId",
                    "resourceIdMatch",
                    "className",
                    "classNameMatch",
                    "region")
                : new(
                    "text",
                    "text_match",
                    "content_description",
                    "content_description_match",
                    "resource_id",
                    "resource_id_match",
                    "class_name",
                    "class_name_match",
                    "region");
    }

    private static void ValidateMatchMode(string? value, string fieldName)
    {
        if (string.Equals(value, ScreenElementMatchModes.Exact, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, ScreenElementMatchModes.Contains, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new UsageException($"{fieldName} must be 'exact' or 'contains'.");
    }
}

internal enum ScreenElementSelectorFieldNaming
{
    SnakeCase,
    CamelCase
}
