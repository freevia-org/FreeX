using System.Windows.Controls;

namespace Freexcel.App.Host;

public static class MenuKeyTipAssigner
{
    public static void AssignUniqueKeyTips(IEnumerable<MenuItem> menuItems)
    {
        var items = menuItems.ToList();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var existing = RibbonTooltip.GetKeyTip(item);
            if (!string.IsNullOrWhiteSpace(existing))
                used.Add(existing);
        }

        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(item)))
                continue;

            var keyTip = CreateKeyTip(item.Header?.ToString(), used);
            RibbonTooltip.SetKeyTip(item, keyTip);
            used.Add(keyTip);
        }
    }

    private static string CreateKeyTip(string? header, HashSet<string> used)
    {
        foreach (var character in EnumerateCandidateCharacters(header))
        {
            var candidate = character.ToString().ToUpperInvariant();
            if (used.Add(candidate))
            {
                used.Remove(candidate);
                return candidate;
            }
        }

        for (var index = 1; index <= 99; index++)
        {
            var candidate = index.ToString();
            if (!used.Contains(candidate))
                return candidate;
        }

        return Guid.NewGuid().ToString("N")[..2].ToUpperInvariant();
    }

    private static IEnumerable<char> EnumerateCandidateCharacters(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            yield break;

        foreach (var character in header)
        {
            if (char.IsLetterOrDigit(character))
                yield return character;
        }
    }
}
