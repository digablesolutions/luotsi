using Luotsi.Cli.Errors;

namespace Luotsi.Cli.Models;

internal static class ScreenElementSelectorValidator
{
    public static ScreenElementSelector Validate(ScreenElementSelector selector, string commandName)
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (!selector.HasCriteria)
        {
            throw new UsageException($"{commandName} requires at least one selector field: text, content_description, resource_id, class_name, or region.");
        }

        ValidateMatchMode(selector.TextMatch, "text_match");
        ValidateMatchMode(selector.ContentDescriptionMatch, "content_description_match");
        ValidateMatchMode(selector.ResourceIdMatch, "resource_id_match");
        ValidateMatchMode(selector.ClassNameMatch, "class_name_match");

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
