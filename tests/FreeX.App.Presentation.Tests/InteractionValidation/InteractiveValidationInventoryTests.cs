using FluentAssertions;
using FreeX.App.Presentation.InteractionValidation;

namespace FreeX.App.Presentation.Tests.InteractionValidation;

public sealed class InteractiveValidationInventoryTests
{
    [Fact]
    public void KeyboardShortcuts_ContainTheDocumentedLogicalScenariosByArea()
    {
        InteractiveValidationInventory.KeyboardShortcuts.Should().HaveCount(93);

        AreaCounts(InteractiveValidationInventory.KeyboardShortcuts, scenario => scenario.Area)
            .Should().BeEquivalentTo(new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Analysis"] = 1,
                ["Clipboard"] = 4,
                ["Data"] = 3,
                ["Edit"] = 2,
                ["Editing"] = 12,
                ["File"] = 6,
                ["Find"] = 2,
                ["Formatting"] = 8,
                ["Formulas"] = 9,
                ["Help"] = 1,
                ["Insert"] = 5,
                ["Navigation"] = 13,
                ["Review"] = 2,
                ["Ribbon"] = 1,
                ["Row/Column"] = 2,
                ["Selection"] = 8,
                ["Sheet Tabs"] = 3,
                ["UI"] = 4,
                ["View"] = 3,
                ["Workbook"] = 4,
            });
    }

    [Fact]
    public void WorksheetRangeTargets_ContainTheWpfInventoryByArea()
    {
        InteractiveValidationInventory.WorksheetRangeTargets.Should().HaveCount(31);

        AreaCounts(InteractiveValidationInventory.WorksheetRangeTargets, target => target.Area)
            .Should().BeEquivalentTo(new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Advanced Filter"] = 3,
                ["Allow Edit Range"] = 1,
                ["Chart Data Source"] = 1,
                ["Conditional Format"] = 1,
                ["Consolidate"] = 2,
                ["Create Table"] = 1,
                ["Data Table"] = 2,
                ["Data Validation"] = 2,
                ["Function Argument"] = 1,
                ["Goal Seek"] = 2,
                ["Move Pivot"] = 1,
                ["Named Ranges"] = 2,
                ["Page Setup"] = 3,
                ["Pivot Create"] = 2,
                ["Pivot Data Source"] = 1,
                ["Resize Table"] = 1,
                ["Scenario Manager"] = 2,
                ["Sparklines"] = 2,
                ["Text to Columns"] = 1,
            });
    }

    [Fact]
    public void AllRecords_HaveUniqueIdsAndCompleteMachineReadableFields()
    {
        var shortcuts = InteractiveValidationInventory.KeyboardShortcuts;
        var rangeTargets = InteractiveValidationInventory.WorksheetRangeTargets;
        var allIds = shortcuts.Select(scenario => scenario.Id)
            .Concat(rangeTargets.Select(target => target.Id))
            .ToArray();

        allIds.Should().OnlyHaveUniqueItems();
        shortcuts.Should().OnlyContain(scenario =>
            HasText(scenario.Id) &&
            HasText(scenario.Area) &&
            HasText(scenario.Owner) &&
            HasText(scenario.DisplayChord) &&
            HasText(scenario.ExpectedBehavior) &&
            scenario.Aliases.All(HasText) &&
            scenario.Interactions.Count > 0);
        rangeTargets.Should().OnlyContain(target =>
            HasText(target.Id) &&
            HasText(target.Area) &&
            HasText(target.Owner) &&
            HasText(target.DisplayTarget) &&
            HasText(target.ExpectedBehavior) &&
            target.Aliases.All(HasText));
    }

    [Fact]
    public void EveryShortcut_HasExecutableStructuredInteractionsForItsAliases()
    {
        foreach (var scenario in InteractiveValidationInventory.KeyboardShortcuts)
        {
            scenario.Interactions.Should().NotBeEmpty(scenario.Id);
            scenario.Aliases.Should().OnlyContain(
                alias => scenario.Interactions.Any(interaction => interaction.DisplayText == alias),
                scenario.Id);

            foreach (var interaction in scenario.Interactions)
            {
                HasText(interaction.DisplayText).Should().BeTrue(scenario.Id);
                HasText(interaction.Input).Should().BeTrue(scenario.Id);
                Enum.IsDefined(interaction.Context).Should().BeTrue(scenario.Id);

                if (interaction.Kind == ShortcutInteractionKind.MouseWheel)
                {
                    interaction.Steps.Should().BeEmpty(scenario.Id);
                    interaction.Input.Should().NotBe("Keyboard", scenario.Id);
                    continue;
                }

                interaction.Input.Should().Be("Keyboard", scenario.Id);
                interaction.Steps.Should().NotBeEmpty(scenario.Id);
                interaction.Steps.Should().OnlyContain(step => HasText(step.Key));

                if (interaction.Kind is ShortcutInteractionKind.KeySequence or ShortcutInteractionKind.RibbonKeytipSequence)
                    interaction.Steps.Should().HaveCountGreaterThan(1, scenario.Id);
                else
                    interaction.Steps.Should().ContainSingle(scenario.Id);
            }
        }
    }

    [Fact]
    public void NativeAndExternalFlags_AreExplicitlyRepresented()
    {
        InteractiveValidationInventory.KeyboardShortcuts.Should().Contain(scenario => scenario.IsNative);
        InteractiveValidationInventory.KeyboardShortcuts.Should().Contain(scenario => scenario.IsExternal);
        InteractiveValidationInventory.WorksheetRangeTargets.Should().OnlyContain(target => !target.IsNative && !target.IsExternal);
    }

    private static Dictionary<string, int> AreaCounts<T>(
        IEnumerable<T> records,
        Func<T, string> areaSelector) =>
        records.GroupBy(areaSelector, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static bool HasText(string value) => !string.IsNullOrWhiteSpace(value);
}
