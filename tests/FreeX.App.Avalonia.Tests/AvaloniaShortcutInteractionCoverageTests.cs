using Avalonia.Input;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.App.Presentation.InteractionValidation;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaShortcutInteractionCoverageTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void ApplicationAndAvaloniaLocalRegistries_ExactlyMatchStructuredScenarioChords()
    {
        var scenarioChords = InteractiveValidationInventory.KeyboardShortcuts
            .SelectMany(scenario => scenario.Interactions.Select(interaction => (scenario.Id, Interaction: interaction)))
            .Where(item => item.Interaction.Steps.Count == 1)
            .Select(item => (item.Id, item.Interaction.DisplayText, Chord: ToAvaloniaChord(item.Interaction.Steps[0])))
            .Where(item => item.Chord is not null)
            .Select(item => (item.Id, item.DisplayText, Chord: item.Chord!.Value))
            .ToArray();

        var applicationRules = WorkbookKeyboardShortcutCatalog.ApplicationCommandShortcuts
            .Select(shortcut => (
                Key: Enum.Parse<Key>(shortcut.Key.ToString()),
                Modifiers: ToAvaloniaModifiers(shortcut.Modifiers),
                shortcut.Command))
            .Concat(
            [
                (Key: Key.Add, Modifiers: KeyModifiers.Control | KeyModifiers.Alt, Command: KeyboardCommandShortcut.ZoomIn),
                (Key: Key.Subtract, Modifiers: KeyModifiers.Control | KeyModifiers.Alt, Command: KeyboardCommandShortcut.ZoomOut),
                (Key: Key.Decimal, Modifiers: KeyModifiers.Control, Command: KeyboardCommandShortcut.CycleSelectionCorner),
            ])
            .ToArray();
        applicationRules.Should().HaveCount(50);
        applicationRules.Select(rule => rule.Command).Distinct().Should().HaveCount(42);
        applicationRules
            .GroupBy(rule => $"{rule.Modifiers}:{rule.Key}", StringComparer.Ordinal)
            .Should().OnlyContain(group => group.Count() == 1);
        foreach (var rule in applicationRules)
        {
            scenarioChords
                .Where(item => item.Chord == (rule.Key, rule.Modifiers))
                .Select(item => $"{item.Id}:{item.DisplayText}")
                .Should().ContainSingle($"{rule.Modifiers}+{rule.Key} must have one scenario interaction");
            MainWindow.TryResolveApplicationShortcutForTest(rule.Key, rule.Modifiers, out var resolved)
                .Should().BeTrue();
            resolved.Should().Be(rule.Command);
        }

        MainWindow.AvaloniaLocalShortcutRules.Should().HaveCount(6);
        var representedApplicationChords = scenarioChords
            .Select(item => item.Chord)
            .Distinct()
            .Where(chord => MainWindow.TryResolveApplicationShortcutForTest(chord.Key, chord.Modifiers, out _))
            .ToArray();
        representedApplicationChords.Should().BeEquivalentTo(
            applicationRules.Select(rule => (rule.Key, rule.Modifiers)));
        foreach (var rule in MainWindow.AvaloniaLocalShortcutRules)
        {
            scenarioChords
                .Where(item => item.Chord == (rule.Key, rule.Modifiers))
                .Should().ContainSingle();
        }
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

    [Fact]
    public async Task ProductionDispatch_CreditsTopLevelAndNestedKeytips()
    {
        await Session.Dispatch(async () =>
        {
            var interactions = InteractiveValidationInventory.KeyboardShortcuts
                .Single(scenario => scenario.Id == "shortcut.ribbon.keytip-routing")
                .Interactions;
            var topLevel = interactions.Single(interaction => interaction.DisplayText == "Alt, then H");
            var nested = interactions.Single(interaction => interaction.DisplayText == "Alt,H,B,S,D");
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var topLevelResult = await window.ExerciseShortcutInteractionAsync(topLevel);
                var nestedResult = await window.ExerciseShortcutInteractionAsync(nested);

                topLevelResult.Passed.Should().BeTrue(topLevelResult.Note);
                nestedResult.Passed.Should().BeTrue(nestedResult.Note);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ProductionShortcutValidationCore_CompletesEntireCatalog()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var results = await window.RunShortcutInteractionValidationCoreForTestAsync();

                var expectedStatuses = InteractiveValidationInventory.KeyboardShortcuts
                    .SelectMany(scenario => scenario.Interactions.Select((interaction, index) => new
                    {
                        Id = $"{scenario.Id}:{index}",
                        Status = scenario.IsNative || scenario.IsExternal || interaction.Kind == ShortcutInteractionKind.MouseWheel
                            ? "skipped"
                            : "passed",
                    }))
                    .ToDictionary(row => row.Id, row => row.Status, StringComparer.Ordinal);
                expectedStatuses.Should().HaveCount(276);

                var scenarioResults = results
                    .Where(result => result.Category == "shortcut-scenario")
                    .ToArray();
                scenarioResults.Should().HaveCount(276);
                scenarioResults.Select(result => result.Id)
                    .Should().BeEquivalentTo(expectedStatuses.Keys);

                var mismatches = scenarioResults
                    .Where(result => !string.Equals(result.Status, expectedStatuses[result.Id], StringComparison.Ordinal))
                    .ToArray();
                mismatches.Should().BeEmpty(
                    "every managed shortcut must pass and only explicit native/external/wheel boundaries may skip; mismatches: {0}",
                    string.Join(Environment.NewLine, mismatches.Select(result =>
                        $"{result.Id}: expected={expectedStatuses[result.Id]}, actual={result.Status}, note={result.Note}")));
                window.OwnedWindows.Should().BeEmpty();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReplacedValidationSessions_AreDisposedWithoutRetiringSharedSiblingDocuments()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            var firstSession = window.Session;
            try
            {
                var interaction = InteractiveValidationInventory.KeyboardShortcuts
                    .Single(scenario => scenario.Id == "shortcut.navigation.row-start")
                    .Interactions.Single();

                const int replacementStressCount = 256;
                for (var index = 0; index < replacementStressCount; index++)
                {
                    var previousSession = window.Session;
                    var result = await window.ExerciseShortcutInteractionAsync(interaction);

                    result.Passed.Should().BeTrue(result.Note);
                    AssertSessionDisposed(
                        previousSession,
                        $"validation replacement {index + 1} must dispose its previous session immediately");
                }

                AssertSessionDisposed(firstSession, "the first validation session was replaced");
                window.OwnedWindows.Should().BeEmpty();

                window.AllowCloseWithoutDirtyPromptForParityCapture();
                var sibling = window.CreateSharedViewForTest();
                sibling.Show();
                var sharedRoot = window.Session;
                var siblingSession = sibling.Session;
                window.Close();

                AssertSessionDisposed(sharedRoot, "closing the root window disposes its view session");
                using (siblingSession.CreateSiblingView(viewportHeight: 120, viewportWidth: 160))
                {
                    // A live sibling keeps the shared document available after the root closes.
                }
                sibling.AllowCloseWithoutDirtyPromptForParityCapture();
                sibling.Close();
                AssertSessionDisposed(siblingSession, "closing the final sibling disposes its view session");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                if (window.IsVisible)
                    window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static void AssertSessionDisposed(WorkbookSession session, string because)
    {
        var createSibling = () => session.CreateSiblingView(viewportHeight: 120, viewportWidth: 160);
        createSibling.Should().Throw<ObjectDisposedException>(because);
    }

    [Theory]
    [InlineData("shortcut.file.save", 0, "persisted a non-empty workbook")]
    [InlineData("shortcut.file.save", 1, "persisted a non-empty workbook")]
    [InlineData("shortcut.data.filter-toggle-reapply", 2, "applied AutoFilter")]
    public async Task AuditedShortcutResiduals_RequireSemanticProductionOutcomes(
        string scenarioId,
        int interactionIndex,
        string expectedOutcome)
    {
        await Session.Dispatch(async () =>
        {
            var interaction = InteractiveValidationInventory.KeyboardShortcuts
                .Single(scenario => scenario.Id == scenarioId)
                .Interactions[interactionIndex];
            var window = new MainWindow([]);
            try
            {
                var result = await window.ExerciseShortcutInteractionAsync(
                    interaction,
                    interactionId: $"{scenarioId}:{interactionIndex}");

                result.Passed.Should().BeTrue(result.Note);
                result.Note.Should().Contain(expectedOutcome);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("ArrowUp", Key.Up)]
    [InlineData("ArrowDown", Key.Down)]
    [InlineData("Grave", Key.Oem3)]
    [InlineData("Plus", Key.OemPlus)]
    [InlineData("Menu", Key.Apps)]
    public void ValidationGestureAliases_MapToProductionAvaloniaKeys(string source, Key expected)
    {
        ShortcutInteractionValidationCatalog.TryMapAvaloniaGesture(
                new ShortcutGestureStep(source),
                out var key,
                out var modifiers)
            .Should().BeTrue();
        key.Should().Be(expected);
        modifiers.Should().Be(KeyModifiers.None);
    }

    private static bool Is(
        ShortcutInteractionDescriptor interaction,
        string key,
        ShortcutModifierKeys modifiers) =>
        interaction.Steps.Count == 1 &&
        interaction.Steps[0] == new ShortcutGestureStep(key, modifiers);

    private static KeyModifiers ToAvaloniaModifiers(WorkbookShortcutModifiers modifiers)
    {
        var result = KeyModifiers.None;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Control)) result |= KeyModifiers.Control;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Meta)) result |= KeyModifiers.Meta;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Alt)) result |= KeyModifiers.Alt;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Shift)) result |= KeyModifiers.Shift;
        return result;
    }

    private static (Key Key, KeyModifiers Modifiers)? ToAvaloniaChord(ShortcutGestureStep step)
    {
        var keyName = step.Key switch
        {
            "Backspace" => nameof(Key.Back),
            "ArrowUp" => nameof(Key.Up),
            "ArrowDown" => nameof(Key.Down),
            "Grave" => nameof(Key.Oem3),
            "Plus" => nameof(Key.OemPlus),
            "Menu" => nameof(Key.Apps),
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
