using System.Security.Cryptography;
using System.Text;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Inspect;

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
        var previousGroups = CreateElementGroups(previous.Elements);
        var currentGroups = CreateElementGroups(current.Elements);

        var added = new List<ScreenElement>();
        var removed = new List<string>();
        var changed = new List<InspectScreenElementChange>();

        foreach (var baseKey in OrderedBaseKeys(currentGroups, previousGroups))
        {
            var previousElements = previousGroups.Groups.TryGetValue(baseKey, out var previousGroup)
                ? new List<IndexedScreenElement>(previousGroup)
                : new List<IndexedScreenElement>();
            var currentElements = currentGroups.Groups.TryGetValue(baseKey, out var currentGroup)
                ? new List<IndexedScreenElement>(currentGroup)
                : new List<IndexedScreenElement>();

            RemoveExactMatches(previousElements, currentElements);
            MatchChangedElements(previousElements, currentElements, changed);

            added.AddRange(currentElements.Select(static element => element.Element));
            removed.AddRange(previousElements.Select(static element => element.Key));
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
        foreach (var element in state.Elements
            .OrderBy(GetElementKey, StringComparer.Ordinal)
            .ThenBy(GetElementHashKey, StringComparer.Ordinal))
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

    private static string GetElementHashKey(ScreenElement element) =>
        string.Join('|',
            element.Text,
            element.ContentDescription,
            element.ResourceId,
            element.ClassName,
            element.Enabled,
            element.Clickable,
            element.Left,
            element.Top,
            element.Right,
            element.Bottom);

    private static ElementGroupMap CreateElementGroups(IReadOnlyList<ScreenElement> elements)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = new Dictionary<string, List<IndexedScreenElement>>(StringComparer.Ordinal);
        var orderedBaseKeys = new List<string>();

        foreach (var element in elements)
        {
            var baseKey = GetElementKey(element);
            if (!groups.TryGetValue(baseKey, out var group))
            {
                group = [];
                groups[baseKey] = group;
                orderedBaseKeys.Add(baseKey);
            }

            counts.TryGetValue(baseKey, out var count);
            var nextCount = count + 1;
            counts[baseKey] = nextCount;

            var key = nextCount == 1
                ? baseKey
                : $"{baseKey}#{nextCount}";
            group.Add(new IndexedScreenElement(key, element));
        }

        return new ElementGroupMap(groups, orderedBaseKeys);
    }

    private static IEnumerable<string> OrderedBaseKeys(ElementGroupMap currentGroups, ElementGroupMap previousGroups)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in currentGroups.OrderedBaseKeys)
        {
            seen.Add(key);
            yield return key;
        }

        foreach (var key in previousGroups.OrderedBaseKeys)
        {
            if (seen.Add(key))
            {
                yield return key;
            }
        }
    }

    private static void RemoveExactMatches(List<IndexedScreenElement> previousElements, List<IndexedScreenElement> currentElements)
    {
        for (var currentIndex = 0; currentIndex < currentElements.Count;)
        {
            var matchIndex = previousElements.FindIndex(previous => Equals(previous.Element, currentElements[currentIndex].Element));
            if (matchIndex < 0)
            {
                currentIndex++;
                continue;
            }

            previousElements.RemoveAt(matchIndex);
            currentElements.RemoveAt(currentIndex);
        }
    }

    private static void MatchChangedElements(
        List<IndexedScreenElement> previousElements,
        List<IndexedScreenElement> currentElements,
        List<InspectScreenElementChange> changed)
    {
        while (previousElements.Count > 0 && currentElements.Count > 0)
        {
            var current = currentElements[0];
            var previousIndex = FindClosestElement(previousElements, current.Element);
            var previous = previousElements[previousIndex];

            changed.Add(new InspectScreenElementChange(previous.Key, previous.Element, current.Element));
            previousElements.RemoveAt(previousIndex);
            currentElements.RemoveAt(0);
        }
    }

    private static int FindClosestElement(IReadOnlyList<IndexedScreenElement> elements, ScreenElement target)
    {
        var closestIndex = 0;
        var closestDistance = ElementDistance(elements[0].Element, target);

        for (var index = 1; index < elements.Count; index++)
        {
            var distance = ElementDistance(elements[index].Element, target);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = index;
            }
        }

        return closestIndex;
    }

    private static long ElementDistance(ScreenElement previous, ScreenElement current) =>
        Math.Abs(previous.CenterX - current.CenterX)
        + Math.Abs(previous.CenterY - current.CenterY)
        + Math.Abs(previous.Left - current.Left)
        + Math.Abs(previous.Top - current.Top)
        + Math.Abs(previous.Right - current.Right)
        + Math.Abs(previous.Bottom - current.Bottom);

    private sealed record ElementGroupMap(
        IReadOnlyDictionary<string, List<IndexedScreenElement>> Groups,
        IReadOnlyList<string> OrderedBaseKeys);

    private sealed record IndexedScreenElement(string Key, ScreenElement Element);
}

internal sealed record InspectScreenElementChange(string StableId, ScreenElement Previous, ScreenElement Current);
