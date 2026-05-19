using System.Security.Cryptography;
using System.Text;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli;

internal sealed record InspectScreenStateDelta(
    string PreviousHash,
    string CurrentHash,
    int AddedCount,
    int RemovedCount,
    int ChangedCount,
    IReadOnlyList<ScreenElement> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<InspectScreenElementChange> Changed)
{
    public static InspectScreenStateDelta Create(ScreenState previous, ScreenState current)
    {
        var previousMap = previous.Elements.ToDictionary(GetElementKey, static element => element, StringComparer.Ordinal);
        var currentMap = current.Elements.ToDictionary(GetElementKey, static element => element, StringComparer.Ordinal);

        var added = new List<ScreenElement>();
        var removed = new List<string>();
        var changed = new List<InspectScreenElementChange>();

        foreach (var pair in currentMap)
        {
            if (!previousMap.TryGetValue(pair.Key, out var previousElement))
            {
                added.Add(pair.Value);
                continue;
            }

            if (!Equals(previousElement, pair.Value))
            {
                changed.Add(new InspectScreenElementChange(pair.Key, previousElement, pair.Value));
            }
        }

        foreach (var key in previousMap.Keys.Where(key => !currentMap.ContainsKey(key)))
        {
            removed.Add(key);
        }

        return new InspectScreenStateDelta(
            CreateHash(previous),
            CreateHash(current),
            added.Count,
            removed.Count,
            changed.Count,
            added,
            removed,
            changed);
    }

    public static string CreateHash(ScreenState state)
    {
        var builder = new StringBuilder();
        foreach (var element in state.Elements.OrderBy(GetElementKey, StringComparer.Ordinal))
        {
            builder.Append(GetElementKey(element))
                .Append('|')
                .Append(element.Text)
                .Append('|')
                .Append(element.ContentDescription)
                .Append('|')
                .Append(element.ResourceId)
                .Append('|')
                .Append(element.ClassName)
                .Append('|')
                .Append(element.Enabled)
                .Append('|')
                .Append(element.Clickable)
                .Append('|')
                .Append(element.Left)
                .Append(',')
                .Append(element.Top)
                .Append(',')
                .Append(element.Right)
                .Append(',')
                .Append(element.Bottom)
                .AppendLine();
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GetElementKey(ScreenElement element) =>
        !string.IsNullOrWhiteSpace(element.StableId)
            ? element.StableId
            : string.Join('|', element.ClassName, element.Left, element.Top, element.Right, element.Bottom, element.Text, element.ContentDescription);
}

internal sealed record InspectScreenElementChange(string StableId, ScreenElement Previous, ScreenElement Current);
