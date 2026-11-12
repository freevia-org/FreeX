using FreeW.App.Presentation.Ribbon;

namespace FreeW.Ribbon.Definitions;

/// <summary>
/// The Equation contextual tab (Word's "Equation" tab), shown while an equation is selected or being
/// edited: Tools, Symbols and Structures. Its galleries come from the shared presentation catalogs, so
/// both renderers list exactly the same symbols and templates.
/// </summary>
internal static partial class FreeWCanonicalRibbonTabs
{
    internal const string EquationTabId = "equation";

    internal static RibbonDefinitionBuilder AddEquationContextualTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities) =>
        builder.ContextualTab(EquationTabId, "Equation",
            new RibbonTabContext(capabilities.EquationContextKey, "Equation Tools", RibbonContextColor.Purple), tab =>
            {
                var topology = new FreeWRibbonTabTopology(tab, capabilities);

                topology.Section(
                    "equation.tools",
                    tab => tab.Group("equation-tools", "Tools", "T", 100, group =>
                        {
                            group.Dropdown(EquationToolsRibbonWorkflow.GalleryCommandId, "Equation", BuildEquationGalleryMenu(), EquationIcon);
                            group.Medium(EquationToolsRibbonWorkflow.EditCommandId, "Edit Equation", RibbonCommandIconKind.Equation);
                            group.RowBreak();
                            group.Medium(EquationToolsRibbonWorkflow.ProfessionalCommandId, "Professional", RibbonCommandIconKind.Equation);
                            group.Medium(EquationToolsRibbonWorkflow.LinearCommandId, "Linear", RibbonCommandIconKind.Equation);
                            group.RowBreak();
                            group.Medium(EquationToolsRibbonWorkflow.InlineCommandId, "Change to Inline", RibbonCommandIconKind.Equation);
                            group.Medium(EquationToolsRibbonWorkflow.DisplayCommandId, "Change to Display", RibbonCommandIconKind.Equation);
                            group.Dropdown(EquationToolsRibbonWorkflow.JustifyCommandId, "Justification", BuildEquationJustificationMenu(), AlignIcon);
                        }),
                    tab => tab.Group("equation-tools", "Tools", null, 100, group =>
                        {
                            group.Dropdown(EquationToolsRibbonWorkflow.GalleryCommandId, "Equation", BuildEquationGalleryMenu(), EquationIcon);
                            group.Button(EquationToolsRibbonWorkflow.EditCommandId, "Edit Equation");
                            group.RowBreak();
                            group.Button(EquationToolsRibbonWorkflow.ProfessionalCommandId, "Professional");
                            group.Button(EquationToolsRibbonWorkflow.LinearCommandId, "Linear");
                            group.RowBreak();
                            group.Button(EquationToolsRibbonWorkflow.InlineCommandId, "Change to Inline");
                            group.Button(EquationToolsRibbonWorkflow.DisplayCommandId, "Change to Display");
                            group.Dropdown(EquationToolsRibbonWorkflow.JustifyCommandId, "Justification", BuildEquationJustificationMenu(), AlignIcon);
                        }));

                topology.Section(
                    "equation.symbols",
                    tab => tab.Group("equation-symbols", "Symbols", "S", 90, group =>
                        {
                            foreach (var set in EquationSymbolCatalog.Sets)
                                group.Dropdown(set.CommandId, set.Label, BuildEquationSymbolMenu(set), SymbolIcon);
                        }));

                topology.Section(
                    "equation.structures",
                    tab => tab.Group("equation-structures", "Structures", "R", 80, group =>
                        {
                            foreach (var structures in EquationStructureCatalog.Groups)
                                group.Dropdown(structures.CommandId, structures.Label, BuildEquationStructureMenu(structures), EquationIcon);
                        }));

                topology.Build();
            });

    private static RibbonDropdown EquationIcon(RibbonDropdown dropdown) =>
        dropdown with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Equation) };

    private static RibbonDropdown SymbolIcon(RibbonDropdown dropdown) =>
        dropdown with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Symbol) };

    private static RibbonDropdown AlignIcon(RibbonDropdown dropdown) =>
        dropdown with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.AlignCenter) };

    /// <summary>Tools &gt; Equation: a new equation, then Word's built-in gallery equations.</summary>
    private static RibbonMenu BuildEquationGalleryMenu()
    {
        var items = new List<RibbonMenuItem>
        {
            new("Insert New Equation", new RibbonCommandId(EquationToolsRibbonWorkflow.NewCommandId)),
            RibbonMenuItem.Separator(),
        };
        items.AddRange(BuiltInEquationMenuItems());
        return new RibbonMenu(items.ToArray());
    }

    /// <summary>One menu item per built-in gallery equation, labelled with its name and linear source.</summary>
    private static IEnumerable<RibbonMenuItem> BuiltInEquationMenuItems() =>
        EquationPresetCatalog.BuiltInEquations.Select(preset =>
            new RibbonMenuItem(preset.BuiltInName!, new RibbonCommandId(preset.CommandId)));

    private static RibbonMenu BuildEquationJustificationMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Left", new RibbonCommandId(EquationToolsRibbonWorkflow.JustifyLeftCommandId)),
            new("Centered", new RibbonCommandId(EquationToolsRibbonWorkflow.JustifyCenterCommandId)),
            new("Right", new RibbonCommandId(EquationToolsRibbonWorkflow.JustifyRightCommandId)),
            new("Centered as a Group", new RibbonCommandId(EquationToolsRibbonWorkflow.JustifyCenterAsGroupCommandId)),
        });

    private static RibbonMenu BuildEquationSymbolMenu(EquationSymbolSet set) =>
        new(set.Symbols
            .Select(symbol => new RibbonMenuItem(
                $"{symbol.Text}   {symbol.Label}",
                new RibbonCommandId(symbol.CommandId)))
            .ToArray());

    private static RibbonMenu BuildEquationStructureMenu(EquationStructureGroup group) =>
        new(group.Structures
            .Select(structure => new RibbonMenuItem(
                structure.Label,
                new RibbonCommandId(structure.CommandId)))
            .ToArray());
}
