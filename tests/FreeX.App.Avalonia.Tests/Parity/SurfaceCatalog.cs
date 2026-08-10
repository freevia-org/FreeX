using System;
using System.Collections.Generic;
using System.Linq;

using FreeX.App.Avalonia.Ribbon;
using FreeX.Ribbon.Definitions;
using Free.Shared.Ribbon;

namespace FreeX.App.Avalonia.Tests.Parity;

/// <summary>
/// The portable, enumerable catalog of every FreeX app surface — the canonical structure that drives the
/// functional parity matrix. The ribbon portion is DERIVED by enumerating the single-source shared ribbon
/// definition (<see cref="FreeXRibbon.Build"/>); it is never hand-duplicated. The dialog / backstage-pane /
/// context-menu portions are explicit, documented lists (those surfaces are not declaratively enumerable),
/// grounded in the live shells' dialog automation ids and backstage rail.
///
/// "Handled by the WPF shell" is sourced from the generated WPF handler map snapshot
/// (<c>docs/parity/wpf-handler-ids.txt</c>, kept in lock-step with <c>FreeXRibbonHandlerMap</c> by a guard
/// test in the App.Host.Tests lane). "Bound by the Avalonia shell" is sourced from
/// <see cref="AvaloniaCommandIdAdapter"/> — the documented single-source map of every Avalonia handler id to
/// its canonical control, which the keystone test already proves maps only to real shared-definition ids.
/// </summary>
public static class SurfaceCatalog
{
    /// <summary>One ribbon command: its canonical id, owning tab/group, display label and keytip.</summary>
    public sealed record RibbonCommandEntry(
        string CommandId,
        string TabHeader,
        string TabId,
        string GroupHeader,
        string GroupId,
        string ControlKind,
        string Display,
        string? KeyTip,
        bool IsContextual,
        bool IsMenuItem);

    /// <summary>The complete, ordered list of ribbon commands (controls + menu items) the shared definition emits.</summary>
    public static IReadOnlyList<RibbonCommandEntry> RibbonCommands { get; } = BuildRibbonCommands();

    /// <summary>Distinct canonical command ids across the whole ribbon (controls + menu items), sorted.</summary>
    public static IReadOnlyList<string> CanonicalCommandIds { get; } = RibbonCommands
        .Select(c => c.CommandId)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// The complete set of canonical ids the Avalonia shell binds a handler for. The shell wires commands
    /// through three statically-enumerable sources, all unioned here:
    /// <list type="bullet">
    ///   <item>the dotted handler ids in <see cref="AvaloniaCommandIdAdapter"/>, projected to canonical;</item>
    ///   <item>the raw-canonical <c>ExtraCommands</c> menu/gallery wirings (<see cref="AvaloniaExtraCommandIds.RawCanonical"/>);</item>
    ///   <item>the Home ▸ Styles ▸ Cell Styles gallery presets, whose display name IS the canonical id.</item>
    /// </list>
    /// This mirrors exactly how <c>MainWindow</c> assembles its ribbon callbacks, so the parity matrix counts
    /// the real Avalonia binding surface rather than the adapter subset alone.
    /// </summary>
    public static IReadOnlySet<string> AvaloniaBoundCanonicalIds { get; } = BuildAvaloniaBoundIds();

    private static IReadOnlySet<string> BuildAvaloniaBoundIds()
    {
        var ids = AvaloniaCommandIdAdapter.AvaloniaIds
            .Select(AvaloniaCommandIdAdapter.ToCanonical)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var raw in AvaloniaExtraCommandIds.RawCanonical)
            ids.Add(raw);

        foreach (var preset in Enum.GetValues<FreeX.App.Services.CellStylePreset>())
            ids.Add(FreeX.App.Services.CellStyleDiffPlanner.GetCellStylePresetDisplayName(preset));

        // Insert ▸ Charts: RegisterChartCommands binds every canonical id the chart factory recognizes to a
        // real InsertChartRibbonCommand (the descriptive chart-type labels), independent of the adapter.
        foreach (var id in CanonicalCommandIds)
            if (FreeX.App.Presentation.Charts.Editing.ChartCommandWorkflowPlanner.ChartTypeForRibbonCommand(id) is not null)
                ids.Add(id);

        return ids;
    }

    /// <summary>
    /// Top-level dialog surfaces present in the app. Explicit (dialogs are not declaratively enumerable);
    /// grounded in the Avalonia dialog automation ids and the WPF <c>*Dialog</c> classes.
    /// </summary>
    public static IReadOnlyList<string> Dialogs { get; } = new[]
    {
        "FormatCells", "Find", "Replace", "GoTo", "GoToSpecial", "Sort", "AdvancedFilter",
        "DataValidation", "ConditionalFormatRule", "ManageConditionalFormats", "InsertFunction",
        "FunctionArguments", "NameManager", "DefineName", "CreateNamesFromSelection", "PasteNames",
        "AllowEditRange", "Consolidate", "RemoveDuplicates", "TextToColumns", "GoalSeek", "DataTable",
        "ScenarioManager", "ForecastSheet", "InsertPivotTable", "PivotTableOptions", "PivotFieldSettings",
        "PivotGroupField", "PivotCalculatedField", "PivotCalculatedItem", "PivotDataSource",
        "PivotStyleGallery", "PivotName", "MovePivot", "PivotChartOptions", "PivotLabelFilter",
        "PivotValueFilter", "PivotItemFilter", "InsertSlicer", "InsertTimeline", "ChangeChartType",
        "SelectChartData", "ChartTitles", "ChartLegend", "MoveChart", "FormatObject", "ShapeGradient",
        "PictureCrop", "ObjectSize", "SelectionPane", "InsertSparkline", "EditSparkline", "TableName",
        "TableResize", "PageSetup", "PageBreak", "MoveCopySheet", "SheetTabColor", "Options", "Hyperlink",
        "ProtectSheet", "ProtectWorkbook", "ReviewSummary", "ShowNotes", "FillSeries", "CustomViews",
        "CustomViewAdd", "GetData", "PasteSpecial", "MoreColors", "About", "Print", "OutlineSettings",
        "SheetOption",
    };

    /// <summary>Backstage (File) panes the shells render.</summary>
    public static IReadOnlyList<string> BackstagePanes { get; } = new[]
    {
        "Home", "New", "Open", "Info", "Save", "SaveAs", "Print", "Export", "Share", "Account", "Options", "Close",
    };

    /// <summary>Right-click context menus the shells expose.</summary>
    public static IReadOnlyList<string> ContextMenus { get; } = new[]
    {
        "CellContextMenu", "RowHeaderContextMenu", "ColumnHeaderContextMenu", "SheetTabContextMenu",
        "ChartContextMenu", "PictureContextMenu", "ShapeContextMenu", "PivotTableContextMenu",
        "StatusBarCustomizeMenu",
    };

    private static IReadOnlyList<RibbonCommandEntry> BuildRibbonCommands()
    {
        var definition = FreeXRibbon.Build();
        var entries = new List<RibbonCommandEntry>();

        foreach (var tab in definition.Tabs)
        {
            var isContextual = tab.Context is not null;
            foreach (var group in tab.Groups)
            foreach (var control in group.Controls)
            {
                if (!string.IsNullOrEmpty(control.CommandId.Value))
                {
                    entries.Add(new RibbonCommandEntry(
                        control.CommandId.Value, tab.Header, tab.Id, group.Header, group.Id,
                        ControlKindName(control), control.Label, control.KeyTip, isContextual, IsMenuItem: false));
                }

                foreach (var (menuId, menuLabel, menuKeyTip) in EnumerateMenuItems(control))
                {
                    entries.Add(new RibbonCommandEntry(
                        menuId, tab.Header, tab.Id, group.Header, group.Id,
                        "MenuItem", menuLabel, menuKeyTip, isContextual, IsMenuItem: true));
                }
            }
        }

        return entries;
    }

    private static string ControlKindName(RibbonControl control) => control switch
    {
        RibbonComboBox => "ComboBox",
        RibbonCheckBox => "CheckBox",
        RibbonToggleButton => "ToggleButton",
        RibbonSplitButton => "SplitButton",
        RibbonDropdown => "Dropdown",
        RibbonGallery => "Gallery",
        RibbonLabel => "Label",
        _ => "Button",
    };

    private static IEnumerable<(string Id, string Label, string? KeyTip)> EnumerateMenuItems(RibbonControl control)
    {
        var menu = control switch
        {
            RibbonSplitButton split => split.Menu,
            RibbonDropdown dropdown => dropdown.Menu,
            _ => null,
        };
        if (menu is null)
            yield break;

        foreach (var item in EnumerateMenuItems(menu.Items))
            yield return item;
    }

    private static IEnumerable<(string Id, string Label, string? KeyTip)> EnumerateMenuItems(
        IReadOnlyList<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } id && !string.IsNullOrEmpty(id.Value))
                yield return (id.Value, item.Header, item.KeyTip);
            foreach (var child in EnumerateMenuItems(item.Children))
                yield return child;
        }
    }
}
