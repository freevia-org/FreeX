using System.Windows.Controls;

namespace Freexcel.App.Host;

public static class MenuKeyTipAssigner
{
    public static void AssignUniqueKeyTips(IEnumerable<MenuItem> menuItems)
    {
        var items = menuItems.ToList();
        var used = new List<string>();

        foreach (var item in items)
        {
            var existing = NormalizeKeyTip(RibbonTooltip.GetKeyTip(item));
            if (string.IsNullOrWhiteSpace(existing))
                continue;

            if (IsTypeableKeyTip(existing) && IsAvailable(existing, used))
            {
                RibbonTooltip.SetKeyTip(item, existing);
                used.Add(existing);
                continue;
            }

            RibbonTooltip.SetKeyTip(item, "");
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

    private static string CreateKeyTip(string? header, IReadOnlyCollection<string> used)
    {
        foreach (var character in EnumerateCandidateCharacters(header))
        {
            var candidate = NormalizeKeyTip(character.ToString());
            if (IsAvailable(candidate, used))
                return candidate;
        }

        for (var index = 1; index <= 99; index++)
        {
            var candidate = index.ToString();
            if (IsAvailable(candidate, used))
                return candidate;
        }

        foreach (var candidate in EnumerateFallbackKeyTips())
        {
            if (IsAvailable(candidate, used))
                return candidate;
        }

        throw new InvalidOperationException("Unable to assign a unique menu keytip.");
    }

    private static string NormalizeKeyTip(string? keyTip) =>
        keyTip?.Trim().ToUpperInvariant() ?? "";

    private static bool IsAvailable(string candidate, IEnumerable<string> used) =>
        used.All(existing =>
            !string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase) &&
            !existing.StartsWith(candidate, StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith(existing, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<char> EnumerateCandidateCharacters(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            yield break;

        foreach (var character in header)
        {
            if (IsTypeableKeyTipCharacter(character))
                yield return character;
        }
    }

    private static bool IsTypeableKeyTipCharacter(char character) =>
        character is >= '0' and <= '9' or
            >= 'A' and <= 'Z' or
            >= 'a' and <= 'z';

    private static bool IsTypeableKeyTip(string keyTip) =>
        keyTip.All(IsTypeableKeyTipCharacter);

    private static IEnumerable<string> EnumerateFallbackKeyTips()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        foreach (var first in alphabet)
        {
            foreach (var second in alphabet)
                yield return $"{first}{second}";
        }

        foreach (var first in alphabet)
        {
            foreach (var second in alphabet)
            {
                foreach (var third in alphabet)
                    yield return $"{first}{second}{third}";
            }
        }
    }
}
