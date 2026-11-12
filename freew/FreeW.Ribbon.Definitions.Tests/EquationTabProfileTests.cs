using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.Ribbon.Definitions.Tests;

/// <summary>
/// The Equation contextual tab: declared once, realized by both profiles with the same commands, and
/// built entirely from the shared presentation catalogs.
/// </summary>
public sealed class EquationTabProfileTests
{
    public static TheoryData<string> Profiles() => new() { "WPF", "Avalonia" };

    private static FreeWRibbonCapabilities Capabilities(string profile) =>
        profile == "WPF" ? FreeWRibbonCapabilities.Wpf : FreeWRibbonCapabilities.Avalonia;

    private static RibbonTab EquationTab(string profile) =>
        FreeWRibbon.Build(Capabilities(profile)).FindTab("equation")!;

    private static IEnumerable<string> CommandIds(RibbonGroup group)
    {
        foreach (var control in group.Controls)
        {
            if (!string.IsNullOrWhiteSpace(control.CommandId.Value))
                yield return control.CommandId.Value;

            if (control is RibbonDropdown dropdown)
            {
                foreach (var item in dropdown.Menu.Items.Where(item => item.Kind == RibbonMenuItemKind.Command))
                    yield return item.CommandId!.Value.Value;
            }
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void TabIsContextual_OnTheEquationSelection_WithWordsThreeGroups(string profile)
    {
        var tab = EquationTab(profile);

        tab.IsContextual.Should().BeTrue();
        tab.Header.Should().Be("Equation");
        tab.Context!.ActivationKey.Should().Be(Capabilities(profile).EquationContextKey).And.Be("equation");
        tab.Groups.Select(group => (group.Id, group.Header)).Should().Equal(
            ("equation-tools", "Tools"),
            ("equation-symbols", "Symbols"),
            ("equation-structures", "Structures"));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void ToolsGroup_OffersTheGalleryEditAndConversions(string profile)
    {
        var tools = EquationTab(profile).FindGroup("equation-tools")!;

        CommandIds(tools).Should().Equal(
        [
            EquationToolsRibbonWorkflow.GalleryCommandId,
            EquationToolsRibbonWorkflow.NewCommandId,
            .. EquationPresetCatalog.BuiltInEquations.Select(preset => preset.CommandId),
            EquationToolsRibbonWorkflow.EditCommandId,
            EquationToolsRibbonWorkflow.ProfessionalCommandId,
            EquationToolsRibbonWorkflow.LinearCommandId,
            EquationToolsRibbonWorkflow.InlineCommandId,
            EquationToolsRibbonWorkflow.DisplayCommandId,
            EquationToolsRibbonWorkflow.JustifyCommandId,
            EquationToolsRibbonWorkflow.JustifyLeftCommandId,
            EquationToolsRibbonWorkflow.JustifyCenterCommandId,
            EquationToolsRibbonWorkflow.JustifyRightCommandId,
            EquationToolsRibbonWorkflow.JustifyCenterAsGroupCommandId,
        ]);

        var gallery = tools.Controls.OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == EquationToolsRibbonWorkflow.GalleryCommandId);
        gallery.Menu.Items.Where(item => item.Kind == RibbonMenuItemKind.Command).Select(item => item.Header).Should().Equal(
            "Insert New Equation",
            "Area of Circle",
            "Binomial Theorem",
            "Expansion of a Sum",
            "Fourier Series",
            "Pythagorean Theorem",
            "Quadratic Formula",
            "Taylor Expansion",
            "Trig Identity 1",
            "Trig Identity 2");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void SymbolsGroup_HasOneGalleryPerCatalogSet(string profile)
    {
        var symbols = EquationTab(profile).FindGroup("equation-symbols")!;
        var galleries = symbols.Controls.OfType<RibbonDropdown>().ToList();

        galleries.Select(gallery => (gallery.CommandId.Value, gallery.Label)).Should().Equal(
            EquationSymbolCatalog.Sets.Select(set => (set.CommandId, set.Label)));
        for (var index = 0; index < galleries.Count; index++)
        {
            var set = EquationSymbolCatalog.Sets[index];
            galleries[index].Menu.Items.Select(item => (item.CommandId!.Value.Value, item.Header)).Should().Equal(
                set.Symbols.Select(symbol => (symbol.CommandId, $"{symbol.Text}   {symbol.Label}")));
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void StructuresGroup_HasWordsElevenGalleries_FromTheCatalog(string profile)
    {
        var structures = EquationTab(profile).FindGroup("equation-structures")!;
        var galleries = structures.Controls.OfType<RibbonDropdown>().ToList();

        galleries.Select(gallery => gallery.Label).Should().Equal(
            "Fraction", "Script", "Radical", "Integral", "Large Operator", "Bracket", "Function", "Accent",
            "Limit and Log", "Operator", "Matrix");
        for (var index = 0; index < galleries.Count; index++)
        {
            var group = EquationStructureCatalog.Groups[index];
            galleries[index].CommandId.Value.Should().Be(group.CommandId);
            galleries[index].Menu.Items.Select(item => (item.CommandId!.Value.Value, item.Header)).Should().Equal(
                group.Structures.Select(structure => (structure.CommandId, structure.Label)));
        }
    }

    [Fact]
    public void BothProfilesExposeExactlyTheSameEquationTabCommands()
    {
        var wpf = EquationTab("WPF").Groups.SelectMany(CommandIds).ToList();
        var avalonia = EquationTab("Avalonia").Groups.SelectMany(CommandIds).ToList();

        avalonia.Should().Equal(wpf);
        wpf.Should().OnlyHaveUniqueItems();

        // Every command on the tab is one the shared workflows register, so neither host can miss one.
        var registered = EquationToolsRibbonWorkflow.CommandIds
            .Concat(EquationPresetCatalog.Presets.Select(preset => preset.CommandId))
            .ToHashSet(StringComparer.Ordinal);
        wpf.Should().OnlyContain(commandId => registered.Contains(commandId));
        EquationToolsRibbonWorkflow.CommandIds.Should().BeSubsetOf(wpf, "every workflow command is reachable from the tab");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void InsertEquationMenu_ListsWordsBuiltInEquations(string profile)
    {
        var symbols = FreeWRibbon.Build(Capabilities(profile)).FindTab("insert")!.FindGroup("symbols")!;
        var menuCommands = symbols.Controls
            .Where(control => control.CommandId.Value == "freew.equation")
            .SelectMany(control => control switch
            {
                RibbonDropdown dropdown => dropdown.Menu.Items,
                RibbonSplitButton split => split.Menu.Items,
                _ => []
            })
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => item.CommandId!.Value.Value)
            .ToList();

        menuCommands.Should().Contain(EquationPresetCatalog.BuiltInEquations.Select(preset => preset.CommandId));
        menuCommands.Should().OnlyHaveUniqueItems();
        if (profile == "WPF")
            menuCommands.Should().Contain(EquationToolsRibbonWorkflow.NewCommandId);
    }
}
