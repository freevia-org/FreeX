using Avalonia.Input;
using FluentAssertions;
using FreeX.App.Presentation.InteractionValidation;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaShortcutInteractionCoverageTests
{
    [Fact]
    public void AvaloniaHostRegistry_ExactlyMatchesStructuredScenarioChords()
    {
        var scenarioChords = InteractiveValidationInventory.KeyboardShortcuts
            .SelectMany(scenario => scenario.Interactions.Select(interaction => (scenario.Id, Interaction: interaction)))
            .Where(item => item.Interaction.Steps.Count == 1)
            .Select(item => (item.Id, item.Interaction.DisplayText, Chord: ToAvaloniaChord(item.Interaction.Steps[0])))
            .Where(item => item.Chord is not null)
            .Select(item => (item.Id, item.DisplayText, Chord: item.Chord!.Value))
            .ToArray();

        MainWindow.AvaloniaHostShortcutRules.Should().HaveCount(48);
        MainWindow.AvaloniaHostShortcutRules
            .GroupBy(rule => $"{rule.Modifiers}:{rule.Key}", StringComparer.Ordinal)
            .Should().OnlyContain(group => group.Count() == 1);
        foreach (var rule in MainWindow.AvaloniaHostShortcutRules)
        {
            scenarioChords
                .Where(item => item.Chord == (rule.Key, rule.Modifiers))
                .Select(item => $"{item.Id}:{item.DisplayText}")
                .Should().ContainSingle($"{rule.Modifiers}+{rule.Key} must have one scenario interaction");
            MainWindow.TryResolveAvaloniaHostShortcutForTest(rule.Key, rule.Modifiers, out var resolved)
                .Should().BeTrue();
            resolved.Should().Be(rule.Shortcut);
        }

        var representedHostChords = scenarioChords
            .Select(item => item.Chord)
            .Distinct()
            .Where(chord => MainWindow.TryResolveAvaloniaHostShortcutForTest(chord.Key, chord.Modifiers, out _))
            .ToArray();
        representedHostChords.Should().BeEquivalentTo(
            MainWindow.AvaloniaHostShortcutRules.Select(rule => (rule.Key, rule.Modifiers)));
    }

    [Fact]
    public void DocumentedRibbonKeytipSequences_AllResolveAgainstRuntimeDefinition()
    {
        var allInteractions = InteractiveValidationInventory.KeyboardShortcuts
            .Single(scenario => scenario.Id == "shortcut.ribbon.keytip-routing")
            .Interactions;
        var interactions = allInteractions
            .Where(interaction => interaction.Kind == ShortcutInteractionKind.RibbonKeytipSequence)
            .ToArray();

        allInteractions.Should().HaveCount(79);
        interactions.Should().HaveCount(66);
        foreach (var interaction in interactions)
        {
            ShortcutInteractionValidationCatalog.TryResolveRibbonKeytipInteraction(interaction, out var route)
                .Should().BeTrue(interaction.DisplayText);
            route.Should().NotBeNullOrWhiteSpace(interaction.DisplayText);
        }
    }

    [Fact]
    public void CorrectedF12AndPreviouslyMissingRuntimeChords_AreStructured()
    {
        var interactions = InteractiveValidationInventory.KeyboardShortcuts
            .SelectMany(scenario => scenario.Interactions.Select(interaction => (scenario.Id, Interaction: interaction)))
            .ToArray();

        interactions.Should().NotContain(item =>
            item.Interaction.Steps.Count == 1 &&
            item.Interaction.Steps[0] == new ShortcutGestureStep("F12"));
        interactions.Should().Contain(item => Is(item.Interaction, "E", ShortcutModifierKeys.Control));
        interactions.Should().Contain(item => Is(item.Interaction, "F12", ShortcutModifierKeys.Control));
        interactions.Should().Contain(item => Is(item.Interaction, "F12", ShortcutModifierKeys.Shift));
        interactions.Should().Contain(item => Is(item.Interaction, "F12", ShortcutModifierKeys.Control | ShortcutModifierKeys.Shift));
        interactions.Should().Contain(item => Is(item.Interaction, "Backspace", ShortcutModifierKeys.None));
        interactions.Should().Contain(item => Is(item.Interaction, "Backspace", ShortcutModifierKeys.Shift));
        interactions.Should().Contain(item =>
            item.Interaction.Steps.Any(step => step.Modifiers.HasFlag(ShortcutModifierKeys.Meta)));
    }

    private static bool Is(
        ShortcutInteractionDescriptor interaction,
        string key,
        ShortcutModifierKeys modifiers) =>
        interaction.Steps.Count == 1 &&
        interaction.Steps[0] == new ShortcutGestureStep(key, modifiers);

    private static (Key Key, KeyModifiers Modifiers)? ToAvaloniaChord(ShortcutGestureStep step)
    {
        var keyName = step.Key switch
        {
            "Backspace" => nameof(Key.Back),
            "Semicolon" => nameof(Key.OemSemicolon),
            "0" => nameof(Key.D0),
            "1" => nameof(Key.D1),
            "2" => nameof(Key.D2),
            "3" => nameof(Key.D3),
            "4" => nameof(Key.D4),
            "5" => nameof(Key.D5),
            "6" => nameof(Key.D6),
            "7" => nameof(Key.D7),
            "8" => nameof(Key.D8),
            "9" => nameof(Key.D9),
            "ArrowRight" => nameof(Key.Right),
            "ArrowLeft" => nameof(Key.Left),
            "Equals" => nameof(Key.OemPlus),
            "NumpadPlus" => nameof(Key.Add),
            "Minus" => nameof(Key.OemMinus),
            "NumpadMinus" => nameof(Key.Subtract),
            "Quote" => nameof(Key.OemQuotes),
            "Period" => nameof(Key.OemPeriod),
            "Decimal" => nameof(Key.Decimal),
            "OpenBracket" => nameof(Key.OemOpenBrackets),
            "CloseBracket" => nameof(Key.OemCloseBrackets),
            _ => step.Key,
        };
        if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key))
            return null;

        var modifiers = KeyModifiers.None;
        if (step.Modifiers.HasFlag(ShortcutModifierKeys.Control)) modifiers |= KeyModifiers.Control;
        if (step.Modifiers.HasFlag(ShortcutModifierKeys.Shift)) modifiers |= KeyModifiers.Shift;
        if (step.Modifiers.HasFlag(ShortcutModifierKeys.Alt)) modifiers |= KeyModifiers.Alt;
        if (step.Modifiers.HasFlag(ShortcutModifierKeys.Meta)) modifiers |= KeyModifiers.Meta;
        return (key, modifiers);
    }
}
