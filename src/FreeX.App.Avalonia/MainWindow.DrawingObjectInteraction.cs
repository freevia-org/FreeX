using Avalonia.Controls;
using Avalonia.Input;

using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;
using FreeX.Ribbon.Avalonia;

using Free.Shared.Ribbon;

namespace FreeX.App.Avalonia;

/// <summary>
/// In-grid drawing-object interaction: the per-target right-click context menus for the selected
/// chart / picture / shape / text box. The menus are produced from the platform-neutral
/// <see cref="WorksheetContextMenuPlanner"/> (the same plan WPF uses), bridged to the shared
/// <see cref="RibbonMenu"/> model, and rendered into an Avalonia <see cref="ContextMenu"/> by
/// <see cref="AvaloniaContextMenuRenderer"/>. Every menu command routes back to an existing,
/// already-wired Format Picture / Format Shape / Crop / Selection Pane / z-order / chart-dialog
/// handler — no new behavior is invented here, only the right-click entry point that was previously
/// unreachable (no in-grid object hit-testing existed).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Right-click entry point for a drawing-object container (picture / shape / text box overlay).
    /// Selects the object first (so the menu's commands resolve against it), then opens the per-target
    /// menu for its kind. Mirrors the worksheet cell menu's "right-click selects, then opens" flow.
    /// </summary>
    private void HandleDrawingObjectPointerContext(
        DrawingObjectBounds drawingObject,
        Control container,
        PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(container).Properties.IsRightButtonPressed)
            return;

        SelectDrawingObject(drawingObject);
        OpenDrawingObjectContextMenu(container, drawingObject.Kind);
        args.Handled = true;
    }

    /// <summary>
    /// Right-click entry point for a chart container. Selects the chart, then opens the Chart menu.
    /// </summary>
    private void HandleChartPointerContext(ChartModel chart, Control container, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(container).Properties.IsRightButtonPressed)
            return;

        SelectChart(chart);
        OpenDrawingObjectContextMenu(container, SelectionPaneObjectKind.Chart);
        args.Handled = true;
    }

    /// <summary>
    /// Builds and opens the per-target object context menu for <paramref name="kind"/> anchored on
    /// <paramref name="anchor"/>, from the shared neutral plan via the same renderer the worksheet
    /// menus use.
    /// </summary>
    private void OpenDrawingObjectContextMenu(Control anchor, SelectionPaneObjectKind kind)
    {
        var targetKind = WorksheetContextMenuPlanner.TargetKindForObject(kind);
        var commands = WorksheetContextMenuPlanner.BuildCommands(targetKind);
        var ribbonMenu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);
        AvaloniaContextMenuRenderer
            .BuildContextMenu(ribbonMenu, DispatchDrawingObjectContextMenuCommand)
            .Open(anchor);
    }

    /// <summary>
    /// Routes a per-target object context-menu command id to the matching already-wired shell handler.
    /// Picture: Format Picture, Crop, Reset Crop, Edit Alt Text, Selection Pane. Shape/Text box: Format
    /// (the Format Picture dialog also handles shapes), Size and Properties, Rotate, Shape Fill/Outline,
    /// Bring Forward / Send Backward, Edit Alt Text, Selection Pane. Chart: Format Chart Area, Select
    /// Data, Change Chart Type, Chart Styles, Chart Titles, Move Chart, Selection Pane. The entries Excel
    /// offers that have no shell handler yet (chart Size and Properties) report an honest status rather
    /// than silently no-opping.
    /// </summary>
    private void DispatchDrawingObjectContextMenuCommand(RibbonCommandId commandId)
    {
        if (!Enum.TryParse<WorksheetContextMenuAction>(commandId.Value, out var action))
            return;

        switch (action)
        {
            // --- Picture ---
            case WorksheetContextMenuAction.FormatPicture:
                RunGuarded(OpenFormatPictureDialogAsync);
                break;
            case WorksheetContextMenuAction.CropPicture:
                RunGuarded(OpenPictureCropDialogAsync);
                break;
            case WorksheetContextMenuAction.ResetPictureCrop:
                ResetSelectedPictureCrop();
                break;

            // --- Shape / Text box (the Format Picture dialog handles shapes via its isPicture branch) ---
            case WorksheetContextMenuAction.FormatDrawingObject:
                RunGuarded(OpenFormatPictureDialogAsync);
                break;
            case WorksheetContextMenuAction.ResizeDrawingObject:
                RunGuarded(ResizeSelectedDrawingObjectAsync);
                break;
            case WorksheetContextMenuAction.RotateDrawingObject:
                RunGuarded(RotateSelectedDrawingObjectAsync);
                break;
            case WorksheetContextMenuAction.ShapeFill:
                RunGuarded(SetSelectedShapeFillColorAsync);
                break;
            case WorksheetContextMenuAction.ShapeOutline:
                RunGuarded(SetSelectedShapeOutlineColorAsync);
                break;

            // --- Shared Picture/Shape ---
            case WorksheetContextMenuAction.EditAltText:
                RunGuarded(EditSelectedDrawingObjectAltTextAsync);
                break;
            case WorksheetContextMenuAction.SelectionPane:
                RunGuarded(OpenSelectionPaneDialogAsync);
                break;
            case WorksheetContextMenuAction.BringForward:
                BringSelectedDrawingObjectForward();
                break;
            case WorksheetContextMenuAction.SendBackward:
                SendSelectedDrawingObjectBackward();
                break;

            // --- Chart ---
            case WorksheetContextMenuAction.FormatChartArea:
                RunGuarded(ShowFormatChartAreaDialog);
                break;
            case WorksheetContextMenuAction.SelectChartData:
                RunGuarded(ShowSelectChartDataDialog);
                break;
            case WorksheetContextMenuAction.ChangeChartType:
                RunGuarded(ShowChangeChartTypeDialog);
                break;
            case WorksheetContextMenuAction.ChartStyles:
                CycleChartStyle();
                break;
            case WorksheetContextMenuAction.ChartTitles:
                RunGuarded(ShowChartTitlesDialog);
                break;
            case WorksheetContextMenuAction.MoveChart:
                RunGuarded(ShowMoveChartDialog);
                break;
            case WorksheetContextMenuAction.ChartSizeAndProperties:
                // No chart size/properties dialog exists in the shell yet; report honestly.
                ReportContextualNotYetAvailable(UiText.Get("DrawingInteract_ChartSizeAndProperties"));
                break;
        }
    }

    /// <summary>
    /// Bring Forward for the selected shape (real shape z-order command) or picture (cross-kind
    /// <see cref="FreeX.Core.Commands.MoveSelectionPaneObjectCommand"/>), matching the contextual tab
    /// handlers. Text boxes are drawing shapes, so they use the shape path.
    /// </summary>
    private void BringSelectedDrawingObjectForward()
    {
        if (_selectedDrawingObjectKind == SelectionPaneObjectKind.Picture)
            BringSelectedPictureForward();
        else
            BringSelectedShapeForward();
    }

    private void SendSelectedDrawingObjectBackward()
    {
        if (_selectedDrawingObjectKind == SelectionPaneObjectKind.Picture)
            SendSelectedPictureBackward();
        else
            SendSelectedShapeBackward();
    }
}
