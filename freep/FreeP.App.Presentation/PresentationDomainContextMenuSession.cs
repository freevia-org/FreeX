using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationDomainContextMenuKind
{
    Chart,
    Table,
}

public enum PresentationDomainContextMenuEntryKind
{
    Command,
    Separator,
    Submenu,
}

public enum PresentationDomainContextActionKind
{
    SetWaterfallPointTotal,
    FormatChartPoint,
    FormatChartSeries,
    FormatChartAxis,
    FormatChartText,
    FormatChartArea,
    OpenChartOptions,
    DistributeTableRows,
    DistributeTableColumns,
    InsertTableRowAbove,
    InsertTableRowBelow,
    InsertTableColumnLeft,
    InsertTableColumnRight,
    DeleteTableRow,
    DeleteTableColumn,
    SetTableColumnWidth,
    MergeTableCell,
    SplitTableCell,
}

public sealed record PresentationDomainContextAction(
    PresentationDomainContextActionKind Kind,
    uint ShapeId,
    int SeriesIndex = -1,
    int PointIndex = -1,
    bool BoolValue = false,
    long LongValue = 0,
    ChartAxisKind? AxisKind = null,
    ChartTextTarget? TextTarget = null,
    ChartAreaFormattingTarget? AreaTarget = null);

public sealed record PresentationDomainContextMenuEntryPlan(
    PresentationDomainContextMenuEntryKind Kind,
    string Text,
    bool IsEnabled,
    PresentationDomainContextAction? Action = null,
    IReadOnlyList<PresentationDomainContextMenuEntryPlan>? Children = null);

public sealed record PresentationDomainContextMenuPlan(
    PresentationDomainContextMenuKind Kind,
    IReadOnlyList<PresentationDomainContextMenuEntryPlan> Entries);

public sealed record PresentationDomainContextMenuSessionCallbacks(
    Action<int, int> OpenChartPointOptions,
    Action<int> OpenChartSeriesOptions,
    Action<ChartAxisKind> OpenChartAxisOptions,
    Action<ChartTextTarget> OpenChartTextOptions,
    Action<ChartAreaFormattingTarget> OpenChartAreaOptions,
    Action OpenChartOptions);

/// <summary>
/// Owns renderer-neutral chart/table context targeting, menu projection, enablement, and commands.
/// Hosts retain native pointer coordinates, menu controls, placement, and inline-editor adaptation.
/// </summary>
public sealed class PresentationDomainContextMenuSession
{
    private readonly Func<EditingSession> _getEditor;
    private readonly PresentationDomainContextMenuSessionCallbacks _callbacks;

    public PresentationDomainContextMenuSession(
        Func<EditingSession> getEditor,
        PresentationDomainContextMenuSessionCallbacks callbacks)
    {
        _getEditor = getEditor ?? throw new ArgumentNullException(nameof(getEditor));
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public PresentationDomainContextMenuPlan? BuildAtSlidePoint(double slideX, double slideY)
    {
        var editor = _getEditor();
        if (editor.CurrentSlide is not { } slide)
            return null;

        var shapeId = ShapeHitTester.HitTest(
            slide,
            editor.Presentation,
            slideX,
            slideY);
        var shape = shapeId is uint id ? SlideShapeTraversal.FindById(slide, id) : null;
        if (shape?.Kind == SlideShapeKind.Chart
            && shape.Chart is not null
            && ChartSubtargetHitTester.TryHitTest(
                slide,
                editor.Presentation,
                slideX,
                slideY,
                out var chartHit))
        {
            return BuildChart(chartHit);
        }

        if (shape?.Kind != SlideShapeKind.Table || shape.Table is null)
            return null;

        var cell = TableCellHitTester.HitTest(shape, slideX, slideY);
        if (cell is null)
            return null;

        editor.Select(shape.Id);
        editor.SetActiveTableCell(cell.Value.Row, cell.Value.Col);
        return BuildTable(shape.Id);
    }

    public PresentationDomainContextMenuPlan BuildChart(ChartSubtargetHit hit)
    {
        var editor = _getEditor();
        editor.Select(hit.ShapeId);
        var entries = new List<PresentationDomainContextMenuEntryPlan>();

        switch (hit.Kind)
        {
            case ChartSubtargetKind.Point:
            {
                var chart = editor.CurrentSlide is { } slide
                    ? SlideShapeTraversal.FindById(slide, hit.ShapeId)?.Chart
                    : null;
                if (chart?.ChartType == ChartType.Waterfall && hit.PointIndex >= 0)
                {
                    var isTotal = chart.WaterfallTotalPointIndices?.Contains(hit.PointIndex) == true;
                    entries.Add(Command(
                        isTotal ? "Clear Total" : "Set as Total",
                        new PresentationDomainContextAction(
                            PresentationDomainContextActionKind.SetWaterfallPointTotal,
                            hit.ShapeId,
                            PointIndex: hit.PointIndex,
                            BoolValue: !isTotal)));
                    entries.Add(Separator());
                }
                entries.Add(Command(
                    "Format Data Point...",
                    ChartAction(PresentationDomainContextActionKind.FormatChartPoint, hit)));
                entries.Add(Command(
                    "Format Data Series...",
                    ChartAction(PresentationDomainContextActionKind.FormatChartSeries, hit)));
                break;
            }
            case ChartSubtargetKind.DataLabel:
                entries.Add(Command(
                    "Format Data Label...",
                    ChartAction(PresentationDomainContextActionKind.FormatChartPoint, hit)));
                entries.Add(Command(
                    "Format Data Series...",
                    ChartAction(PresentationDomainContextActionKind.FormatChartSeries, hit)));
                break;
            case ChartSubtargetKind.Series:
                entries.Add(Command(
                    "Format Data Series...",
                    ChartAction(PresentationDomainContextActionKind.FormatChartSeries, hit)));
                break;
            case ChartSubtargetKind.CategoryAxis:
                entries.Add(Command(
                    "Format Category Axis...",
                    ChartAction(
                        PresentationDomainContextActionKind.FormatChartAxis,
                        hit,
                        axisKind: ChartAxisKind.Category)));
                break;
            case ChartSubtargetKind.ValueAxis:
                entries.Add(Command(
                    "Format Value Axis...",
                    ChartAction(
                        PresentationDomainContextActionKind.FormatChartAxis,
                        hit,
                        axisKind: ChartAxisKind.Value)));
                break;
            case ChartSubtargetKind.AxisTitle:
                entries.Add(Command(
                    "Format Axis...",
                    ChartAction(
                        PresentationDomainContextActionKind.FormatChartAxis,
                        hit,
                        axisKind: hit.AxisKind ?? ChartAxisKind.Value)));
                break;
            case ChartSubtargetKind.Title:
                entries.Add(Command(
                    "Format Chart Title...",
                    ChartAction(
                        PresentationDomainContextActionKind.FormatChartText,
                        hit,
                        textTarget: ChartTextTarget.Title)));
                break;
            case ChartSubtargetKind.Legend:
                entries.Add(Command(
                    "Format Chart Legend...",
                    ChartAction(
                        PresentationDomainContextActionKind.FormatChartText,
                        hit,
                        textTarget: ChartTextTarget.Legend)));
                break;
            case ChartSubtargetKind.PlotArea:
                entries.Add(Command(
                    "Format Plot Area...",
                    ChartAction(
                        PresentationDomainContextActionKind.FormatChartArea,
                        hit,
                        areaTarget: ChartAreaFormattingTarget.PlotArea)));
                break;
            default:
                entries.Add(Command(
                    "Format Chart Area...",
                    ChartAction(
                        PresentationDomainContextActionKind.FormatChartArea,
                        hit,
                        areaTarget: ChartAreaFormattingTarget.ChartArea)));
                break;
        }

        entries.Add(Separator());
        entries.Add(Command(
            "Chart Options...",
            ChartAction(PresentationDomainContextActionKind.OpenChartOptions, hit)));
        return new PresentationDomainContextMenuPlan(
            PresentationDomainContextMenuKind.Chart,
            entries);
    }

    public PresentationDomainContextMenuPlan? BuildTable(uint shapeId)
    {
        var editor = _getEditor();
        var shape = editor.CurrentSlide is { } slide
            ? SlideShapeTraversal.FindById(slide, shapeId)
            : null;
        if (shape?.Kind != SlideShapeKind.Table || shape.Table is null)
            return null;

        editor.Select(shapeId);
        var state = CurrentTableState();
        var widths = new[]
        {
            ("0.75 in", 0.75),
            ("1.00 in", 1.00),
            ("1.25 in", 1.25),
            ("1.50 in", 1.50),
            ("2.00 in", 2.00),
        }.Select(option => Command(
            option.Item1,
            new PresentationDomainContextAction(
                PresentationDomainContextActionKind.SetTableColumnWidth,
                shapeId,
                LongValue: (long)Math.Round(option.Item2 * DrawingMlCoordinateUnits.EmuPerInch)),
            state.HasActiveCell)).ToArray();

        return new PresentationDomainContextMenuPlan(
            PresentationDomainContextMenuKind.Table,
            [
                TableCommand("Insert Row Above", PresentationDomainContextActionKind.InsertTableRowAbove, shapeId, state.CanInsertRow),
                TableCommand("Insert Row Below", PresentationDomainContextActionKind.InsertTableRowBelow, shapeId, state.CanInsertRow),
                Separator(),
                TableCommand("Insert Column Left", PresentationDomainContextActionKind.InsertTableColumnLeft, shapeId, state.CanInsertColumn),
                TableCommand("Insert Column Right", PresentationDomainContextActionKind.InsertTableColumnRight, shapeId, state.CanInsertColumn),
                Separator(),
                TableCommand("Delete Row", PresentationDomainContextActionKind.DeleteTableRow, shapeId, state.CanDeleteRow),
                TableCommand("Delete Column", PresentationDomainContextActionKind.DeleteTableColumn, shapeId, state.CanDeleteColumn),
                Separator(),
                new PresentationDomainContextMenuEntryPlan(
                    PresentationDomainContextMenuEntryKind.Submenu,
                    "Column Width",
                    state.HasActiveCell,
                    Children: widths),
                TableCommand(
                    "Merge with Right Cell",
                    PresentationDomainContextActionKind.MergeTableCell,
                    shapeId,
                    state.CanMergeWithRight || state.CanMergeWithBelow),
                TableCommand("Split Cell", PresentationDomainContextActionKind.SplitTableCell, shapeId, state.CanSplitCell),
            ]);
    }

    public TableCellEditState CurrentTableState()
    {
        var editor = _getEditor();
        return TableCellEditPlanner.PlanSelectedCell(
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            editor.ActiveTableCell);
    }

    public bool CanExecuteCurrentTableAction(PresentationDomainContextActionKind kind)
    {
        var state = CurrentTableState();
        return CanExecuteTableAction(kind, state);
    }

    public bool ExecuteCurrentTableAction(
        PresentationDomainContextActionKind kind,
        Func<PresentationDomainContextAction, bool>? tryExecuteInline = null)
    {
        var state = CurrentTableState();
        return state.ShapeId is uint shapeId
            && Execute(
                new PresentationDomainContextAction(kind, shapeId),
                tryExecuteInline);
    }

    public bool Execute(
        PresentationDomainContextAction action,
        Func<PresentationDomainContextAction, bool>? tryExecuteInline = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsTableAction(action.Kind) && tryExecuteInline?.Invoke(action) == true)
            return true;

        var editor = _getEditor();
        editor.Select(action.ShapeId);
        if (IsTableAction(action.Kind))
        {
            var state = CurrentTableState();
            if (!CanExecuteTableAction(action.Kind, state))
                return false;

            if (PresentationTableStructureActionDispatcher.IsSupported(action.Kind))
            {
                return PresentationTableStructureActionDispatcher.TryExecute(
                    action.Kind,
                    state,
                    action.ShapeId,
                    static () => { },
                    editor);
            }
        }

        switch (action.Kind)
        {
            case PresentationDomainContextActionKind.SetWaterfallPointTotal:
                editor.SetWaterfallPointTotal(action.PointIndex, action.BoolValue);
                return true;
            case PresentationDomainContextActionKind.FormatChartPoint:
                _callbacks.OpenChartPointOptions(action.SeriesIndex, action.PointIndex);
                return true;
            case PresentationDomainContextActionKind.FormatChartSeries:
                _callbacks.OpenChartSeriesOptions(action.SeriesIndex);
                return true;
            case PresentationDomainContextActionKind.FormatChartAxis:
                _callbacks.OpenChartAxisOptions(action.AxisKind ?? ChartAxisKind.Value);
                return true;
            case PresentationDomainContextActionKind.FormatChartText:
                _callbacks.OpenChartTextOptions(action.TextTarget ?? ChartTextTarget.Title);
                return true;
            case PresentationDomainContextActionKind.FormatChartArea:
                _callbacks.OpenChartAreaOptions(action.AreaTarget ?? ChartAreaFormattingTarget.ChartArea);
                return true;
            case PresentationDomainContextActionKind.OpenChartOptions:
                _callbacks.OpenChartOptions();
                return true;
            case PresentationDomainContextActionKind.SetTableColumnWidth:
                return editor.TryApplyActiveTableColumnWidth(action.LongValue);
            default:
                return false;
        }
    }

    private static bool CanExecuteTableAction(
        PresentationDomainContextActionKind kind,
        TableCellEditState state) =>
        kind == PresentationDomainContextActionKind.SetTableColumnWidth
            ? state.HasActiveCell
            : PresentationTableStructureActionDispatcher.CanExecute(kind, state);

    private static bool IsTableAction(PresentationDomainContextActionKind kind) =>
        kind == PresentationDomainContextActionKind.SetTableColumnWidth
        || PresentationTableStructureActionDispatcher.IsSupported(kind);

    private static PresentationDomainContextMenuEntryPlan TableCommand(
        string text,
        PresentationDomainContextActionKind kind,
        uint shapeId,
        bool isEnabled) => Command(
            text,
            new PresentationDomainContextAction(kind, shapeId),
            isEnabled);

    private static PresentationDomainContextAction ChartAction(
        PresentationDomainContextActionKind kind,
        ChartSubtargetHit hit,
        ChartAxisKind? axisKind = null,
        ChartTextTarget? textTarget = null,
        ChartAreaFormattingTarget? areaTarget = null) => new(
            kind,
            hit.ShapeId,
            hit.SeriesIndex,
            hit.PointIndex,
            AxisKind: axisKind,
            TextTarget: textTarget,
            AreaTarget: areaTarget);

    private static PresentationDomainContextMenuEntryPlan Command(
        string text,
        PresentationDomainContextAction action,
        bool isEnabled = true) => new(
            PresentationDomainContextMenuEntryKind.Command,
            text,
            isEnabled,
            action);

    private static PresentationDomainContextMenuEntryPlan Separator() => new(
        PresentationDomainContextMenuEntryKind.Separator,
        string.Empty,
        IsEnabled: false);
}
