using System.Linq;
using System.Reflection;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.App.Host.Tests;

public sealed partial class KeyboardShortcutMatcherTests
{
    private static readonly ModifierKeys[] ModifierCombinations =
    [
        ModifierKeys.None,
        ModifierKeys.Control,
        ModifierKeys.Shift,
        ModifierKeys.Alt,
        ModifierKeys.Control | ModifierKeys.Shift,
        ModifierKeys.Control | ModifierKeys.Alt,
        ModifierKeys.Shift | ModifierKeys.Alt,
        ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt
    ];

    [Fact]
    public void CommandShortcutRules_DoNotMatchTheSamePhysicalChord()
    {
        var field = typeof(KeyboardShortcutMatcher).GetField("CommandShortcutRules", BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();
        var rules = (Array)field!.GetValue(null)!;

        var collisions = EnumeratePhysicalChords()
            .Select(chord => new
            {
                Chord = chord,
                Matches = rules.Cast<object>()
                    .Where(rule => RuleMatches(rule, chord.Key, chord.Modifiers))
                    .Select(rule => RuleShortcut(rule).ToString())
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
            })
            .Where(result => result.Matches.Count > 1)
            .Select(result => $"{result.Chord.Modifiers}+{result.Chord.Key}: {string.Join(", ", result.Matches)}")
            .ToList();

        collisions.Should().BeEmpty("a workbook command shortcut chord must dispatch to only one command");
    }

    [Fact]
    public void CommandShortcutRules_CoverEveryCommandShortcut()
    {
        var field = typeof(KeyboardShortcutMatcher).GetField("CommandShortcutRules", BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();
        var rules = (Array)field!.GetValue(null)!;

        var coveredShortcuts = rules
            .Cast<object>()
            .Select(RuleShortcut)
            .Distinct()
            .ToArray();

        coveredShortcuts.Should().BeEquivalentTo(
            Enum.GetValues<KeyboardCommandShortcut>(),
            "every keyboard command enum value should remain reachable from at least one physical shortcut");
    }

    [Fact]
    public void ShortcutFamilies_DoNotReuseTheSamePhysicalChord()
    {
        var collisions = EnumeratePhysicalChords()
            .Select(chord => new
            {
                Chord = chord,
                Matches = GetShortcutFamilyMatches(chord.Key, Key.None, chord.Modifiers).ToList()
            })
            .Concat(EnumeratePhysicalChords()
                .Select(chord => new
                {
                    Chord = chord,
                    Matches = GetShortcutFamilyMatches(Key.None, chord.Key, chord.Modifiers).ToList()
                }))
            .Concat(EnumeratePhysicalChords()
                .Select(chord => new
                {
                    Chord = chord,
                    Matches = GetShortcutFamilyMatches(Key.System, chord.Key, chord.Modifiers).ToList()
                }))
            .Where(result => result.Matches.Count > 1)
            .Select(result => $"{result.Chord.Modifiers}+{result.Chord.Key}: {string.Join(", ", result.Matches)}")
            .ToList();

        collisions.Should().BeEmpty("a physical keyboard chord should have one active workbook/grid shortcut meaning in a given scope");
    }

    private static IEnumerable<(Key Key, ModifierKeys Modifiers)> EnumeratePhysicalChords()
    {
        foreach (var key in Enum.GetValues<Key>().Where(key => key != Key.None))
        foreach (var modifiers in ModifierCombinations)
            yield return (key, modifiers);
    }

    private static IEnumerable<string> GetShortcutFamilyMatches(Key key, Key systemKey, ModifierKeys modifiers)
    {
        var effectiveKey = key is Key.None or Key.System ? systemKey : key;

        if (KeyboardShortcutMatcher.IsCtrlPlus(key, systemKey, modifiers))
            yield return "Insert cells";
        if (KeyboardShortcutMatcher.IsCtrlMinus(key, systemKey, modifiers))
            yield return "Delete cells";
        if (KeyboardShortcutMatcher.IsPasteSpecialShortcut(key, systemKey, modifiers))
            yield return "Paste special";
        if (KeyboardShortcutMatcher.TryGetGridShortcut(effectiveKey, modifiers, out var gridShortcut))
            yield return $"Grid:{gridShortcut}";
        if (KeyboardShortcutMatcher.TryGetSelectionShortcut(effectiveKey, modifiers, out var selectionShortcut))
            yield return $"Selection:{selectionShortcut}";
        if (KeyboardShortcutMatcher.TryGetCommandShortcut(key, systemKey, modifiers, out var commandShortcut))
            yield return $"Command:{commandShortcut}";
        if (KeyboardShortcutMatcher.TryGetNumberFormatShortcut(effectiveKey, modifiers, out var numberFormatShortcut))
            yield return $"NumberFormat:{numberFormatShortcut}";
        if (KeyboardShortcutMatcher.TryGetFontToggleShortcut(effectiveKey, modifiers, out var fontToggleShortcut))
            yield return $"Font:{fontToggleShortcut}";
        if (KeyboardShortcutMatcher.TryGetBorderShortcut(effectiveKey, modifiers, out var borderShortcut))
            yield return $"Border:{borderShortcut}";
    }

    private static bool RuleMatches(object rule, Key key, ModifierKeys modifiers)
    {
        var matches = (Func<Key, ModifierKeys, bool>)rule.GetType().GetProperty("Matches")!.GetValue(rule)!;
        return matches(key, modifiers);
    }

    private static KeyboardCommandShortcut RuleShortcut(object rule) =>
        (KeyboardCommandShortcut)rule.GetType().GetProperty("Shortcut")!.GetValue(rule)!;
}
