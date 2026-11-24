namespace FreeW.App.Presentation.Dialogs;

/// <summary>
/// Which Avalonia first-column width a field grid takes. WPF sizes every label column to Auto, so only
/// the Avalonia renderer maps this onto <see cref="TablePropertiesDialogVisualMetrics"/>.
/// </summary>
public enum TablePropertiesLabelColumn
{
    Main,
    Row,
    Margin,
    CellSpacing,
}

/// <summary>One node of a Table Properties tab, rendered in order by each native host.</summary>
public abstract record TablePropertiesSurfaceNode;

/// <summary>One grid row: either a text label or a toggle check box in the first column, plus its field.</summary>
public sealed record TablePropertiesFieldRow(
    string? Label,
    string? ToggleAutomationId,
    string FieldAutomationId);

/// <summary>A two-column grid of label-or-toggle and field rows.</summary>
public sealed record TablePropertiesFieldGridNode(
    TablePropertiesLabelColumn LabelColumn,
    IReadOnlyList<TablePropertiesFieldRow> Rows,
    bool IsPositionGrid = false) : TablePropertiesSurfaceNode;

/// <summary>A semi-bold section header; secondary headers use the tighter secondary-section top inset.</summary>
public sealed record TablePropertiesHeaderNode(string Text, bool IsSecondary = false)
    : TablePropertiesSurfaceNode;

/// <summary>A single control placed directly in the stack (a check box or the like).</summary>
public sealed record TablePropertiesControlNode(string AutomationId) : TablePropertiesSurfaceNode;

/// <summary>Check boxes stacked under a secondary-section top inset (the Row tab's break/header options).</summary>
public sealed record TablePropertiesCheckStackNode(IReadOnlyList<string> AutomationIds)
    : TablePropertiesSurfaceNode;

/// <summary>An expanded Expander whose content is a stack of nodes (the Cell tab's Positioning pane).</summary>
public sealed record TablePropertiesExpanderNode(
    string Header,
    IReadOnlyList<TablePropertiesSurfaceNode> Content) : TablePropertiesSurfaceNode;

public sealed record TablePropertiesTabSpec(
    TablePropertiesDialogTabKind Kind,
    string Label,
    string AutomationId,
    IReadOnlyList<TablePropertiesSurfaceNode> Content);

/// <summary>
/// The declarative Table Properties tab layout both hosts render: which rows, headers and controls sit
/// on which tab, in which order, with which automation ids. Native code only builds the widgets.
/// </summary>
public static class TablePropertiesSurfacePlanner
{
    public static IReadOnlyList<TablePropertiesTabSpec> Tabs { get; } = BuildTabs();

    /// <summary>
    /// Which floating-positioning controls are enabled for <paramref name="state"/>, by automation id.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, bool>> ControlEnabledStates(
        TablePropertiesDialogEnabledState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var floating = state.FloatingControlsEnabled;
        return
        [
            new(TablePropertiesDialogPlanner.AllowOverlapAutomationId, floating),
            new(TablePropertiesDialogPlanner.HorizontalAnchorAutomationId, floating),
            new(TablePropertiesDialogPlanner.HorizontalModeAutomationId, floating),
            new(TablePropertiesDialogPlanner.HorizontalOffsetAutomationId, state.HorizontalOffsetEnabled),
            new(TablePropertiesDialogPlanner.VerticalAnchorAutomationId, floating),
            new(TablePropertiesDialogPlanner.VerticalModeAutomationId, floating),
            new(TablePropertiesDialogPlanner.VerticalOffsetAutomationId, state.VerticalOffsetEnabled),
            new(TablePropertiesDialogPlanner.DistanceTopAutomationId, floating),
            new(TablePropertiesDialogPlanner.DistanceLeftAutomationId, floating),
            new(TablePropertiesDialogPlanner.DistanceBottomAutomationId, floating),
            new(TablePropertiesDialogPlanner.DistanceRightAutomationId, floating),
        ];
    }

    private static TablePropertiesFieldRow Labeled(string label, string fieldAutomationId) =>
        new(label, ToggleAutomationId: null, fieldAutomationId);

    private static TablePropertiesFieldRow Toggled(string toggleAutomationId, string fieldAutomationId) =>
        new(Label: null, toggleAutomationId, fieldAutomationId);

    private static TablePropertiesFieldGridNode MarginGrid(
        string top,
        string left,
        string bottom,
        string right) =>
        new(
            TablePropertiesLabelColumn.Margin,
            [
                Labeled(TablePropertiesDialogPlanner.TopLabel, top),
                Labeled(TablePropertiesDialogPlanner.LeftLabel, left),
                Labeled(TablePropertiesDialogPlanner.BottomLabel, bottom),
                Labeled(TablePropertiesDialogPlanner.RightLabel, right),
            ]);

    private static IReadOnlyList<TablePropertiesTabSpec> BuildTabs() =>
    [
        new(
            TablePropertiesDialogTabKind.Table,
            TablePropertiesDialogPlanner.TableTabLabel,
            TablePropertiesDialogPlanner.TableTabAutomationId,
            [
                new TablePropertiesFieldGridNode(
                    TablePropertiesLabelColumn.Main,
                    [
                        Toggled(
                            TablePropertiesDialogPlanner.PreferredWidthToggleAutomationId,
                            TablePropertiesDialogPlanner.PreferredWidthAutomationId),
                        Labeled(
                            TablePropertiesDialogPlanner.AlignmentLabel,
                            TablePropertiesDialogPlanner.AlignmentAutomationId),
                        Labeled(
                            TablePropertiesDialogPlanner.TextWrappingLabel,
                            TablePropertiesDialogPlanner.WrappingAutomationId),
                        Labeled(
                            TablePropertiesDialogPlanner.IndentFromLeftLabel,
                            TablePropertiesDialogPlanner.IndentAutomationId),
                    ]),
                new TablePropertiesHeaderNode(TablePropertiesDialogPlanner.DefaultCellMarginsLabel),
                MarginGrid(
                    TablePropertiesDialogPlanner.DefaultMarginTopAutomationId,
                    TablePropertiesDialogPlanner.DefaultMarginLeftAutomationId,
                    TablePropertiesDialogPlanner.DefaultMarginBottomAutomationId,
                    TablePropertiesDialogPlanner.DefaultMarginRightAutomationId),
                new TablePropertiesFieldGridNode(
                    TablePropertiesLabelColumn.CellSpacing,
                    [
                        Toggled(
                            TablePropertiesDialogPlanner.CellSpacingToggleAutomationId,
                            TablePropertiesDialogPlanner.CellSpacingAutomationId),
                    ]),
            ]),
        new(
            TablePropertiesDialogTabKind.Row,
            TablePropertiesDialogPlanner.RowTabLabel,
            TablePropertiesDialogPlanner.RowTabAutomationId,
            [
                new TablePropertiesFieldGridNode(
                    TablePropertiesLabelColumn.Row,
                    [
                        Toggled(
                            TablePropertiesDialogPlanner.RowHeightToggleAutomationId,
                            TablePropertiesDialogPlanner.RowHeightAutomationId),
                        Labeled(
                            TablePropertiesDialogPlanner.RowHeightRuleLabel,
                            TablePropertiesDialogPlanner.RowRuleAutomationId),
                    ]),
                new TablePropertiesCheckStackNode(
                [
                    TablePropertiesDialogPlanner.AllowRowBreakAutomationId,
                    TablePropertiesDialogPlanner.RepeatHeaderAutomationId,
                ]),
            ]),
        new(
            TablePropertiesDialogTabKind.Column,
            TablePropertiesDialogPlanner.ColumnTabLabel,
            TablePropertiesDialogPlanner.ColumnTabAutomationId,
            [
                new TablePropertiesFieldGridNode(
                    TablePropertiesLabelColumn.Main,
                    [
                        Toggled(
                            TablePropertiesDialogPlanner.ColumnWidthToggleAutomationId,
                            TablePropertiesDialogPlanner.ColumnWidthAutomationId),
                    ]),
            ]),
        new(
            TablePropertiesDialogTabKind.Cell,
            TablePropertiesDialogPlanner.CellTabLabel,
            TablePropertiesDialogPlanner.CellTabAutomationId,
            [
                new TablePropertiesFieldGridNode(
                    TablePropertiesLabelColumn.Main,
                    [
                        Toggled(
                            TablePropertiesDialogPlanner.CellWidthToggleAutomationId,
                            TablePropertiesDialogPlanner.CellWidthAutomationId),
                        Labeled(
                            TablePropertiesDialogPlanner.VerticalAlignmentLabel,
                            TablePropertiesDialogPlanner.CellVerticalAlignmentAutomationId),
                    ]),
                new TablePropertiesExpanderNode(
                    TablePropertiesDialogPlanner.PositioningLabel,
                    [
                        new TablePropertiesFieldGridNode(
                            TablePropertiesLabelColumn.Main,
                            [
                                Labeled(
                                    TablePropertiesDialogPlanner.HorizontalRelativeToLabel,
                                    TablePropertiesDialogPlanner.HorizontalAnchorAutomationId),
                                Labeled(
                                    TablePropertiesDialogPlanner.HorizontalAlignmentLabel,
                                    TablePropertiesDialogPlanner.HorizontalModeAutomationId),
                                Labeled(
                                    TablePropertiesDialogPlanner.HorizontalPositionLabel,
                                    TablePropertiesDialogPlanner.HorizontalOffsetAutomationId),
                                Labeled(
                                    TablePropertiesDialogPlanner.VerticalRelativeToLabel,
                                    TablePropertiesDialogPlanner.VerticalAnchorAutomationId),
                                Labeled(
                                    TablePropertiesDialogPlanner.VerticalAlignmentLabel,
                                    TablePropertiesDialogPlanner.VerticalModeAutomationId),
                                Labeled(
                                    TablePropertiesDialogPlanner.VerticalPositionLabel,
                                    TablePropertiesDialogPlanner.VerticalOffsetAutomationId),
                            ],
                            IsPositionGrid: true),
                        new TablePropertiesHeaderNode(
                            TablePropertiesDialogPlanner.DistanceFromTextLabel,
                            IsSecondary: true),
                        MarginGrid(
                            TablePropertiesDialogPlanner.DistanceTopAutomationId,
                            TablePropertiesDialogPlanner.DistanceLeftAutomationId,
                            TablePropertiesDialogPlanner.DistanceBottomAutomationId,
                            TablePropertiesDialogPlanner.DistanceRightAutomationId),
                        new TablePropertiesControlNode(TablePropertiesDialogPlanner.AllowOverlapAutomationId),
                    ]),
                new TablePropertiesControlNode(TablePropertiesDialogPlanner.CellWrapTextAutomationId),
                new TablePropertiesControlNode(TablePropertiesDialogPlanner.CellFitTextAutomationId),
                new TablePropertiesControlNode(TablePropertiesDialogPlanner.SameMarginsAutomationId),
                new TablePropertiesHeaderNode(TablePropertiesDialogPlanner.CellMarginsLabel),
                MarginGrid(
                    TablePropertiesDialogPlanner.CellMarginTopAutomationId,
                    TablePropertiesDialogPlanner.CellMarginLeftAutomationId,
                    TablePropertiesDialogPlanner.CellMarginBottomAutomationId,
                    TablePropertiesDialogPlanner.CellMarginRightAutomationId),
            ]),
    ];
}
