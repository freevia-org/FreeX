using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Ribbon;
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
/// test in the App.Host.Tests lane). Avalonia coverage is derived from its endpoint dictionaries and
/// callback registrations, with every key validated against the shared definition.
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
    /// The complete set of canonical ids the Avalonia shell binds, derived from its actual endpoint
    /// dictionaries and callback registrations rather than a parallel command inventory.
    /// </summary>
    public static IReadOnlySet<string> AvaloniaBoundCanonicalIds { get; } = BuildAvaloniaBoundIds();

    private static IReadOnlySet<string> BuildAvaloniaBoundIds()
    {
        var ids = ExtractLiteralEndpointIds();

        foreach (var preset in Enum.GetValues<FreeX.App.Services.CellStylePreset>())
            ids.Add(FreeX.App.Services.CellStyleDiffPlanner.GetCellStylePresetDisplayName(preset));

        // Insert ▸ Charts: RegisterChartCommands binds every canonical id the chart factory recognizes to a
        // real InsertChartRibbonCommand (the descriptive chart-type labels), independent of the adapter.
        foreach (var id in CanonicalCommandIds)
            if (FreeX.App.Presentation.Charts.Editing.ChartCommandWorkflowPlanner.ChartTypeForRibbonCommand(id) is not null)
                ids.Add(id);

        foreach (var spec in DrawingObjectContextualRibbonPlanner.CreatePictureShapeCommandSpecs())
            ids.Add(FreeXRibbonCommandCatalog.GetRequired(spec.CommandId).Value);

        return ids;
    }

    private static HashSet<string> ExtractLiteralEndpointIds()
    {
        var root = FunctionalParityMatrix.RepoRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "FreeX.App.Avalonia", "MainWindow.cs"),
            Path.Combine(root, "src", "FreeX.App.Avalonia", "MainWindow.ContextualTabs.cs"),
            Path.Combine(root, "src", "FreeX.App.Avalonia", "MainWindow.HomeBorders.cs"),
            Path.Combine(root, "src", "FreeX.App.Avalonia", "Ribbon", "AvaloniaRibbonHost.cs"),
        };
        var patterns = new[]
        {
            new Regex("^\\s*(?:commands)?\\[\\\"(?<key>(?:[^\\\"\\\\]|\\\\.)*)\\\"\\]\\s*=", RegexOptions.Compiled),
            new Regex("^\\s*Bind\\(\\\"(?<key>(?:[^\\\"\\\\]|\\\\.)*)\\\"", RegexOptions.Compiled),
            new Regex("^\\s*Register\\(registry,\\s*\\\"(?<key>(?:[^\\\"\\\\]|\\\\.)*)\\\"", RegexOptions.Compiled),
        };
        var typedCommandPattern = new Regex(
            "FreeXRibbonCommandIds\\.(?<name>[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        foreach (var line in File.ReadLines(file))
        {
            foreach (Match match in typedCommandPattern.Matches(line))
            {
                var field = typeof(FreeXRibbonCommandIds).GetField(
                    match.Groups["name"].Value,
                    BindingFlags.Public | BindingFlags.Static);
                if (field?.GetRawConstantValue() is string commandId)
                    ids.Add(FreeXRibbonCommandCatalog.GetRequired(commandId).Value);
            }

        foreach (var pattern in patterns)
        {
            var match = pattern.Match(line);
            if (!match.Success)
                continue;

            var value = Regex.Unescape(match.Groups["key"].Value);
            ids.Add(FreeXRibbonCommandCatalog.GetRequired(value).Value);
            break;
        }
        }

        foreach (var descriptor in PageLayoutRibbonActionPlanner.RibbonActionDescriptors)
            ids.Add(FreeXRibbonCommandCatalog.GetRequired(descriptor.CommandId).Value);

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
