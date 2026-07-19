using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.DrawingInteraction;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Free.Shared.Ribbon.Avalonia;

using Free.Shared.Ribbon;

using AvaloniaBorder = Avalonia.Controls.Border;
using AvaloniaCanvas = Avalonia.Controls.Canvas;
using AvaloniaGrid = Avalonia.Controls.Grid;

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
    /// Data, Change Chart Type, Chart Styles, Chart Titles, Size and Properties, Move Chart, Selection Pane.
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
                RunGuarded(ResizeSelectedChartObjectAsync);
                break;
        }
    }

    private async Task ResizeSelectedChartObjectAsync()
    {
        if (_isOpening || _isSaving)
            return;

        var commandLabel = UiText.Get("DrawingInteract_ChartSizeAndProperties");
        if (!TryGetSelectedChart(commandLabel, out var chart))
            return;

        var size = await ShowSizeDialogAsync(chart.Width, chart.Height);
        if (size is not { } chosen || !TryGetSelectedChart(commandLabel, out chart))
            return;

        RunDrawingObjectCommand(
            new SetChartBoundsCommand(
                _session.ActiveSheet.Id,
                chart.Id,
                chart.Left,
                chart.Top,
                chosen.Width,
                chosen.Height),
            UiText.Get("DrawingInteract_Resized"),
            commandLabel);
    }

    /// <summary>
    /// Bring Forward for the selected drawing object, matching the contextual tab handlers and the
    /// cross-kind <see cref="FreeX.Core.Commands.MoveSelectionPaneObjectCommand"/> path.
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

    // Drawing-object selection chrome matches the WPF grid: eight 8px resize handles, a 10px
    // rotation grip 20px above the object, and a 4px hit pad around each affordance.
    private const double DrawingObjectHandleSize = 8;
    private const double DrawingObjectHandleHitPadding = 4;
    private const double DrawingObjectRotationGripDiameter = 10;
    private const double DrawingObjectSelectionHorizontalPadding = 8;
    private const double DrawingObjectSelectionBottomPadding = 8;
    private static readonly double DrawingObjectSelectionTopPadding =
        ObjectDragPlanner.RotationGripOffset +
        (DrawingObjectRotationGripDiameter / 2) +
        DrawingObjectHandleHitPadding;

    private static readonly ObjectDragKind[] DrawingObjectResizeHandleKinds =
    [
        ObjectDragKind.ResizeNW,
        ObjectDragKind.ResizeN,
        ObjectDragKind.ResizeNE,
        ObjectDragKind.ResizeE,
        ObjectDragKind.ResizeSE,
        ObjectDragKind.ResizeS,
        ObjectDragKind.ResizeSW,
        ObjectDragKind.ResizeW,
    ];

    private DrawingObjectDragSession? _drawingObjectDragSession;

    private sealed class DrawingObjectDragSession
    {
        public required DrawingObjectRenderPlan RenderPlan { get; init; }
        public required Control Container { get; init; }
        public required AvaloniaGrid Surface { get; init; }
        public required AvaloniaCanvas Adorner { get; init; }
        public required ObjectDragKind Kind { get; init; }
        public required LayoutRect StartCanvasRect { get; init; }
        public required LayoutPoint StartPointerInCanvas { get; init; }
        public required double StartRotationDegrees { get; init; }
        public required bool StartFlipHorizontal { get; init; }
        public required bool StartFlipVertical { get; init; }
        public LayoutRect CurrentCanvasRect { get; set; }
        public double CurrentRotationDegrees { get; set; }
        public bool CurrentFlipHorizontal { get; set; }
        public bool CurrentFlipVertical { get; set; }
        public bool Moved { get; set; }
    }

    private AvaloniaCanvas CreateDrawingObjectSelectionAdorner(
        double width,
        double height,
        double rotationDegrees)
    {
        var layer = new AvaloniaCanvas
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };

        layer.Children.Add(new AvaloniaBorder
        {
            BorderBrush = SelectionBorder,
            BorderThickness = new Thickness(1.5),
            IsHitTestVisible = false,
        });
        layer.Children.Add(new AvaloniaBorder
        {
            Width = 1,
            Background = SelectionBorder,
            IsHitTestVisible = false,
        });
        layer.Children.Add(new global::Avalonia.Controls.Shapes.Ellipse
        {
            Width = DrawingObjectRotationGripDiameter,
            Height = DrawingObjectRotationGripDiameter,
            Fill = Brushes.White,
            Stroke = SelectionBorder,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        });

        foreach (var _ in DrawingObjectResizeHandleKinds)
        {
            layer.Children.Add(new AvaloniaBorder
            {
                Width = DrawingObjectHandleSize,
                Height = DrawingObjectHandleSize,
                Background = Brushes.White,
                BorderBrush = SelectionBorder,
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
            });
        }

        LayoutDrawingObjectSelectionAdorner(layer, width, height, rotationDegrees);
        return layer;
    }

    private static void LayoutDrawingObjectSelectionAdorner(
        AvaloniaCanvas layer,
        double width,
        double height,
        double rotationDegrees)
    {
        var objectWidth = Math.Max(1, width);
        var objectHeight = Math.Max(1, height);
        var layerWidth = objectWidth + (DrawingObjectSelectionHorizontalPadding * 2);
        var layerHeight = objectHeight + DrawingObjectSelectionTopPadding + DrawingObjectSelectionBottomPadding;
        var objectRect = new LayoutRect(
            DrawingObjectSelectionHorizontalPadding,
            DrawingObjectSelectionTopPadding,
            objectWidth,
            objectHeight);

        layer.Width = layerWidth;
        layer.Height = layerHeight;
        layer.RenderTransformOrigin = new RelativePoint(
            objectRect.Center.X / layerWidth,
            objectRect.Center.Y / layerHeight,
            RelativeUnit.Relative);
        layer.RenderTransform = Math.Abs(rotationDegrees) <= 0.0001
            ? null
            : new RotateTransform(rotationDegrees);

        if (layer.Children[0] is AvaloniaBorder border)
        {
            border.Width = objectWidth;
            border.Height = objectHeight;
            AvaloniaCanvas.SetLeft(border, objectRect.Left);
            AvaloniaCanvas.SetTop(border, objectRect.Top);
        }

        var rotateCenter = ObjectDragPlanner.RotateHandleCenter(ObjectDragKind.Rotate, objectRect, 0);
        if (layer.Children[1] is AvaloniaBorder connector)
        {
            var connectorTop = rotateCenter.Y + (DrawingObjectRotationGripDiameter / 2);
            connector.Height = Math.Max(0, objectRect.Top - connectorTop);
            AvaloniaCanvas.SetLeft(connector, rotateCenter.X - 0.5);
            AvaloniaCanvas.SetTop(connector, connectorTop);
        }

        if (layer.Children[2] is Control rotateGrip)
        {
            AvaloniaCanvas.SetLeft(rotateGrip, rotateCenter.X - (DrawingObjectRotationGripDiameter / 2));
            AvaloniaCanvas.SetTop(rotateGrip, rotateCenter.Y - (DrawingObjectRotationGripDiameter / 2));
        }

        for (var index = 0; index < DrawingObjectResizeHandleKinds.Length; index++)
        {
            if (layer.Children[index + 3] is not Control handle)
                continue;

            var center = ObjectDragPlanner.RotateHandleCenter(
                DrawingObjectResizeHandleKinds[index],
                objectRect,
                rotationDegrees: 0);
            AvaloniaCanvas.SetLeft(handle, center.X - (DrawingObjectHandleSize / 2));
            AvaloniaCanvas.SetTop(handle, center.Y - (DrawingObjectHandleSize / 2));
        }
    }

    private bool TryBeginDrawingObjectDrag(
        DrawingObjectRenderPlan renderPlan,
        Control container,
        AvaloniaGrid surface,
        AvaloniaCanvas adorner,
        PointerPressedEventArgs args)
    {
        if (container.Parent is not AvaloniaCanvas canvas ||
            DrawingObjectKindMapper.ToDrawingObjectTargetKind(renderPlan.Bounds.Kind) is null)
        {
            return false;
        }

        var objectRect = new LayoutRect(
            DrawingObjectSelectionHorizontalPadding,
            DrawingObjectSelectionTopPadding,
            surface.Bounds.Width > 0 ? surface.Bounds.Width : surface.Width,
            surface.Bounds.Height > 0 ? surface.Bounds.Height : surface.Height);
        var localPoint = args.GetCurrentPoint(container).Position;
        var kind = ObjectDragPlanner.HitTestHandle(
            new LayoutPoint(localPoint.X, localPoint.Y),
            objectRect,
            DrawingObjectHandleSize,
            DrawingObjectHandleHitPadding,
            renderPlan.Bounds.RotationDegrees);
        if (kind == ObjectDragKind.None)
            return false;

        var canvasRect = new LayoutRect(
            AvaloniaCanvas.GetLeft(container) + DrawingObjectSelectionHorizontalPadding,
            AvaloniaCanvas.GetTop(container) + DrawingObjectSelectionTopPadding,
            objectRect.Width,
            objectRect.Height);
        var pointer = args.GetCurrentPoint(canvas).Position;
        _drawingObjectDragSession = new DrawingObjectDragSession
        {
            RenderPlan = renderPlan,
            Container = container,
            Surface = surface,
            Adorner = adorner,
            Kind = kind,
            StartCanvasRect = canvasRect,
            StartPointerInCanvas = new LayoutPoint(pointer.X, pointer.Y),
            StartRotationDegrees = renderPlan.Bounds.RotationDegrees,
            StartFlipHorizontal = renderPlan.Bounds.FlipHorizontal,
            StartFlipVertical = renderPlan.Bounds.FlipVertical,
            CurrentCanvasRect = canvasRect,
            CurrentRotationDegrees = renderPlan.Bounds.RotationDegrees,
            CurrentFlipHorizontal = renderPlan.Bounds.FlipHorizontal,
            CurrentFlipVertical = renderPlan.Bounds.FlipVertical,
        };

        container.Cursor = DrawingObjectDragCursor(kind);
        args.Pointer.Capture(container);
        args.Handled = true;
        return true;
    }

    private void WireDrawingObjectDragMoveRelease(
        DrawingObjectRenderPlan renderPlan,
        Control container,
        AvaloniaGrid surface)
    {
        container.PointerMoved += (_, args) =>
        {
            if (_drawingObjectDragSession is { } session && ReferenceEquals(session.Container, container))
            {
                ContinueDrawingObjectDrag(session, args);
                return;
            }

            var objectRect = new LayoutRect(
                DrawingObjectSelectionHorizontalPadding,
                DrawingObjectSelectionTopPadding,
                surface.Bounds.Width > 0 ? surface.Bounds.Width : surface.Width,
                surface.Bounds.Height > 0 ? surface.Bounds.Height : surface.Height);
            var point = args.GetCurrentPoint(container).Position;
            var kind = ObjectDragPlanner.HitTestHandle(
                new LayoutPoint(point.X, point.Y),
                objectRect,
                DrawingObjectHandleSize,
                DrawingObjectHandleHitPadding,
                renderPlan.Bounds.RotationDegrees);
            container.Cursor = DrawingObjectDragCursor(kind);
        };
        container.PointerExited += (_, _) =>
        {
            if (_drawingObjectDragSession is null)
                container.Cursor = Cursor.Default;
        };
        container.PointerReleased += (_, args) => EndDrawingObjectDrag(container, args);
        container.PointerCaptureLost += (_, _) =>
        {
            if (_drawingObjectDragSession is { } session && ReferenceEquals(session.Container, container))
                _drawingObjectDragSession = null;
        };
    }

    private void ContinueDrawingObjectDrag(DrawingObjectDragSession session, PointerEventArgs args)
    {
        if (session.Container.Parent is not AvaloniaCanvas canvas)
            return;

        var point = args.GetCurrentPoint(canvas).Position;
        if (session.Kind == ObjectDragKind.Rotate)
        {
            var center = session.StartCanvasRect.Center;
            session.CurrentRotationDegrees = ObjectDragPlanner.CalculateRotationDegrees(
                center,
                new LayoutPoint(point.X, point.Y));
        }
        else
        {
            var transform = ObjectDragPlanner.CalculateDragTransform(
                session.Kind,
                session.StartCanvasRect,
                session.StartPointerInCanvas,
                new LayoutPoint(point.X, point.Y));
            session.CurrentCanvasRect = transform.Rect;
            session.CurrentFlipHorizontal = session.StartFlipHorizontal ^ transform.CrossedHorizontally;
            session.CurrentFlipVertical = session.StartFlipVertical ^ transform.CrossedVertically;
        }

        session.Moved = true;
        UpdateDrawingObjectDragPreview(session);
        session.Container.Cursor = DrawingObjectDragCursor(session.Kind);
        args.Handled = true;
    }

    private void UpdateDrawingObjectDragPreview(DrawingObjectDragSession session)
    {
        var rect = session.CurrentCanvasRect;
        var previewBounds = session.RenderPlan.Bounds with
        {
            Width = rect.Width,
            Height = rect.Height,
            RotationDegrees = session.CurrentRotationDegrees,
            FlipHorizontal = session.CurrentFlipHorizontal,
            FlipVertical = session.CurrentFlipVertical,
        };
        var previewPlan = session.RenderPlan with { Bounds = previewBounds };

        session.Surface.Width = Math.Max(1, rect.Width);
        session.Surface.Height = Math.Max(1, rect.Height);
        session.Surface.Children.Clear();
        session.Surface.Children.Add(CreateDrawingObjectVisual(
            previewPlan,
            rect.Width,
            rect.Height,
            _session.Workbook.Theme));

        session.Container.Width = Math.Max(1, rect.Width) + (DrawingObjectSelectionHorizontalPadding * 2);
        session.Container.Height = Math.Max(1, rect.Height) + DrawingObjectSelectionTopPadding + DrawingObjectSelectionBottomPadding;
        AvaloniaCanvas.SetLeft(session.Container, rect.Left - DrawingObjectSelectionHorizontalPadding);
        AvaloniaCanvas.SetTop(session.Container, rect.Top - DrawingObjectSelectionTopPadding);
        LayoutDrawingObjectSelectionAdorner(
            session.Adorner,
            rect.Width,
            rect.Height,
            session.CurrentRotationDegrees);
    }

    private void EndDrawingObjectDrag(Control container, PointerReleasedEventArgs args)
    {
        if (_drawingObjectDragSession is not { } session || !ReferenceEquals(session.Container, container))
            return;

        _drawingObjectDragSession = null;
        args.Pointer.Capture(null);
        if (session.Moved)
            CommitDrawingObjectDrag(session);
        args.Handled = true;
    }

    private void CommitDrawingObjectDrag(DrawingObjectDragSession session)
    {
        if (DrawingObjectKindMapper.ToDrawingObjectTargetKind(session.RenderPlan.Bounds.Kind) is not { } targetKind)
        {
            RefreshShell(UiText.Get("Drawing_ObjectNoLongerAvailable"));
            return;
        }

        var sheetId = _session.ActiveSheet.Id;
        IWorkbookCommand? command = null;
        string successStatus;
        string failureTitle;

        if (session.Kind == ObjectDragKind.Rotate)
        {
            command = DrawingObjectCommandPlanner.BuildRotateCommand(
                sheetId,
                targetKind,
                session.RenderPlan.Bounds.Id,
                session.CurrentRotationDegrees);
            successStatus = FormatDrawingObjectResourceText(DrawingObjectActionPlanner.RotationSuccess(
                new FormatPicturePlanner.RotationResult(session.CurrentRotationDegrees)));
            failureTitle = DrawingObjectActionPlanner.RotateObjectCommandTitle;
        }
        else if (session.Kind == ObjectDragKind.Move)
        {
            if (!TryResolveCellAddressFromSheetGridPosition(
                    new Point(session.CurrentCanvasRect.Left, session.CurrentCanvasRect.Top),
                    out var anchor))
            {
                RefreshShell(UiText.Get("Drawing_ObjectNoLongerAvailable"));
                return;
            }

            command = DrawingObjectCommandPlanner.BuildMoveCommand(
                sheetId,
                targetKind,
                session.RenderPlan.Bounds.Id,
                anchor);
            successStatus = UiText.Get("DrawingInteract_Moved");
            failureTitle = DrawingObjectActionPlanner.MoveObjectCommandTitle;
        }
        else
        {
            var zoomFactor = Math.Max(0.01, GetActiveZoomFactor());
            var width = Math.Max(ObjectDragPlanner.MinimumObjectSize, session.CurrentCanvasRect.Width / zoomFactor);
            var height = Math.Max(ObjectDragPlanner.MinimumObjectSize, session.CurrentCanvasRect.Height / zoomFactor);
            var movedTopLeft =
                Math.Abs(session.CurrentCanvasRect.Left - session.StartCanvasRect.Left) > 0.5 ||
                Math.Abs(session.CurrentCanvasRect.Top - session.StartCanvasRect.Top) > 0.5;

            if (movedTopLeft && TryResolveCellAddressFromSheetGridPosition(
                    new Point(session.CurrentCanvasRect.Left, session.CurrentCanvasRect.Top),
                    out var anchor))
            {
                command = DrawingObjectCommandPlanner.BuildResizeWithAnchorCommand(
                    sheetId,
                    targetKind,
                    session.RenderPlan.Bounds.Id,
                    anchor,
                    width,
                    height,
                    session.CurrentFlipHorizontal,
                    session.CurrentFlipVertical);
            }
            else
            {
                command = DrawingObjectCommandPlanner.BuildResizeCommand(
                    sheetId,
                    targetKind,
                    session.RenderPlan.Bounds.Id,
                    width,
                    height,
                    session.CurrentFlipHorizontal,
                    session.CurrentFlipVertical);
            }

            successStatus = FormatDrawingObjectResourceText(DrawingObjectActionPlanner.ResizeSuccess(
                new ObjectSizeDialogSize(width, height)));
            failureTitle = DrawingObjectActionPlanner.ResizeObjectCommandTitle;
        }

        var result = _session.ExecuteReviewCommand(command);
        RefreshShell(result.Success
            ? successStatus
            : result.ErrorMessage ?? UiText.Format("InsertLoc_DrawingCommandFailed", failureTitle));
    }

    private static Cursor DrawingObjectDragCursor(ObjectDragKind kind) =>
        new(kind switch
        {
            ObjectDragKind.Move => StandardCursorType.SizeAll,
            ObjectDragKind.ResizeNW => StandardCursorType.TopLeftCorner,
            ObjectDragKind.ResizeSE => StandardCursorType.BottomRightCorner,
            ObjectDragKind.ResizeNE => StandardCursorType.TopRightCorner,
            ObjectDragKind.ResizeSW => StandardCursorType.BottomLeftCorner,
            ObjectDragKind.ResizeN or ObjectDragKind.ResizeS => StandardCursorType.SizeNorthSouth,
            ObjectDragKind.ResizeE or ObjectDragKind.ResizeW => StandardCursorType.SizeWestEast,
            ObjectDragKind.Rotate => StandardCursorType.Cross,
            _ => StandardCursorType.Arrow,
        });

    // -------------------------------------------------------------------------------------------------------
    // Chart drag-to-move and handle-resize.
    //
    // The selected chart's container is positioned via Canvas.Left/Top with a fixed Width/Height in
    // overlay-canvas pixels. A drag captures the pointer and updates those four properties live (using
    // the portable ObjectDragPlanner for all geometry); on release it converts the final canvas-pixel
    // rectangle back into the chart's sheet-pixel Left/Top/Width/Height and applies one undoable
    // SetChartBoundsCommand. The overlay is then rebuilt from the model, so the live preview and the
    // committed state are always identical. Pictures/shapes use a cell-anchor + offset model rather than
    // absolute pixels, so direct drag for them is deferred (see the structured report).
    // -------------------------------------------------------------------------------------------------------

    private const double ChartHandleSize = 9;

    private ChartDragSession? _chartDragSession;

    private sealed class ChartDragSession
    {
        public required ChartModel Chart { get; init; }
        public required Control Container { get; init; }
        public required ObjectDragKind Kind { get; init; }
        public required LayoutRect StartCanvasRect { get; init; }
        public required Point StartPointerInCanvas { get; init; }
        public bool Moved { get; set; }
    }

    /// <summary>
    /// Begins a chart move/resize drag if the press lands on the selected chart's body or a resize
    /// handle. Returns true (and captures the pointer) when a drag started, so the caller skips the
    /// plain select path. The drag kind is resolved by <see cref="ObjectDragPlanner.HitTestHandle"/> in
    /// the container's local pixel space (which equals canvas space — no scaling between them).
    /// </summary>
    private bool TryBeginChartDrag(ChartModel chart, Control container, PointerPressedEventArgs args)
    {
        if (container.Parent is not AvaloniaCanvas canvas)
            return false;

        var localRect = new LayoutRect(0, 0, container.Bounds.Width, container.Bounds.Height);
        var localPoint = args.GetCurrentPoint(container).Position;
        var kind = ObjectDragPlanner.HitTestHandle(
            new LayoutPoint(localPoint.X, localPoint.Y),
            localRect,
            ChartHandleSize,
            handleHitPadding: 4);

        // Rotation is not offered for charts; treat the grip like the body.
        if (kind == ObjectDragKind.Rotate)
            kind = ObjectDragKind.Move;
        if (kind == ObjectDragKind.None)
            kind = ObjectDragKind.Move;

        var canvasRect = new LayoutRect(
            AvaloniaCanvas.GetLeft(container),
            AvaloniaCanvas.GetTop(container),
            container.Bounds.Width,
            container.Bounds.Height);

        _chartDragSession = new ChartDragSession
        {
            Chart = chart,
            Container = container,
            Kind = kind,
            StartCanvasRect = canvasRect,
            StartPointerInCanvas = args.GetCurrentPoint(canvas).Position,
        };

        args.Pointer.Capture(container);
        args.Handled = true;
        return true;
    }

    /// <summary>Attaches the move/release handlers used while a chart drag is in progress.</summary>
    private void WireChartDragMoveRelease(ChartModel chart, Control container)
    {
        container.PointerMoved += (_, args) =>
        {
            if (_chartDragSession is not { } session || !ReferenceEquals(session.Container, container))
                return;
            if (container.Parent is not AvaloniaCanvas canvas)
                return;

            var pointer = args.GetCurrentPoint(canvas).Position;
            var transform = ObjectDragPlanner.CalculateDragTransform(
                session.Kind,
                session.StartCanvasRect,
                new LayoutPoint(session.StartPointerInCanvas.X, session.StartPointerInCanvas.Y),
                new LayoutPoint(pointer.X, pointer.Y));

            var rect = transform.Rect;
            AvaloniaCanvas.SetLeft(container, rect.Left);
            AvaloniaCanvas.SetTop(container, rect.Top);
            container.Width = Math.Max(1, rect.Width);
            container.Height = Math.Max(1, rect.Height);

            // Keep the selection adorner's border + handles tracking the live preview size.
            if (container is Panel panel && panel.Children.Count > 0 &&
                panel.Children[^1] is AvaloniaCanvas adorner)
            {
                LayoutChartAdornerHandles(adorner, rect.Width, rect.Height);
            }

            session.Moved = true;
            args.Handled = true;
        };

        container.PointerReleased += (_, args) =>
        {
            if (_chartDragSession is not { } session || !ReferenceEquals(session.Container, container))
                return;

            args.Pointer.Capture(null);
            var committed = session.Moved;
            _chartDragSession = null;
            if (committed)
                CommitChartDrag(session, container);
            args.Handled = true;
        };

        container.PointerCaptureLost += (_, _) =>
        {
            if (_chartDragSession is { } session && ReferenceEquals(session.Container, container))
                _chartDragSession = null;
        };
    }

    /// <summary>
    /// Converts the dragged container's final canvas-pixel rectangle into sheet-pixel chart bounds and
    /// applies one undoable <see cref="SetChartBoundsCommand"/>. The header gutter and zoom factor are
    /// removed so the stored bounds are zoom-independent, matching how <c>AddChartOverlays</c> lays the
    /// chart back out.
    /// </summary>
    private void CommitChartDrag(ChartDragSession session, Control container)
    {
        var sheet = _session.ActiveSheet;
        var zoomFactor = GetActiveZoomFactor();
        if (zoomFactor <= 0)
            zoomFactor = 1;

        var showHeadings = sheet.ShowHeadings;
        var headerLeft = showHeadings ? GetRowHeaderWidth(_session.Viewport, zoomFactor) : 0;
        var headerTop = showHeadings ? GetColumnHeaderHeight(_session.Viewport, zoomFactor) : 0;

        var canvasLeft = AvaloniaCanvas.GetLeft(container);
        var canvasTop = AvaloniaCanvas.GetTop(container);
        var sheetLeft = Math.Max(0, (canvasLeft - headerLeft) / zoomFactor);
        var sheetTop = Math.Max(0, (canvasTop - headerTop) / zoomFactor);
        var sheetWidth = Math.Max(ObjectDragPlanner.MinimumObjectSize, container.Width / zoomFactor);
        var sheetHeight = Math.Max(ObjectDragPlanner.MinimumObjectSize, container.Height / zoomFactor);

        var command = new SetChartBoundsCommand(sheet.Id, session.Chart.Id, sheetLeft, sheetTop, sheetWidth, sheetHeight);
        var status = session.Kind == ObjectDragKind.Move
            ? UiText.Get("DrawingInteract_Moved")
            : UiText.Get("DrawingInteract_Resized");
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            // The live drag preview already mutated the container's Canvas.Left/Top/Width/Height
            // directly (see WireChartDragMoveRelease's PointerMoved handler) without touching the
            // model. When the model rejects the resulting bounds (e.g. a protected sheet without
            // the "Allow users to edit objects" permission), the ordinary drawing-command failure
            // path (RunDrawingObjectCommand) only sets the status-bar text via ShowEditIssue and
            // never rebuilds the sheet grid, so the chart would otherwise stay visually stuck at
            // the rejected drop position. Call RefreshShell here (not just ShowEditIssue) so the
            // overlay snaps back to the committed model geometry, matching the WPF shell's
            // unconditional repaint-from-model on every chart drag release.
            RefreshShell(result.ErrorMessage ?? UiText.Format("InsertLoc_DrawingCommandFailed", "Chart Bounds"));
            return;
        }

        RefreshShell(status);
    }

    // Per-handle position factors (0 = left/top edge, 0.5 = center, 1 = right/bottom edge), in the
    // corner-then-edge order ObjectDragPlanner uses.
    private static readonly (double Fx, double Fy)[] HandleFactors =
    {
        (0, 0), (0.5, 0), (1, 0),
        (0, 0.5), (1, 0.5),
        (0, 1), (0.5, 1), (1, 1),
    };

    /// <summary>
    /// Selection adorner for a chart: the dashed selection border plus the eight resize handles at the
    /// corners and edge midpoints, laid out to match <see cref="ObjectDragPlanner"/>'s handle hit-zones
    /// so the visible grips line up with where dragging actually resizes. The first child is the border
    /// (stretched to fill); the remaining eight are the handles, repositioned live during a drag by
    /// <see cref="LayoutChartAdornerHandles"/>.
    /// </summary>
    private Control CreateChartSelectionAdorner(double width, double height)
    {
        var layer = new AvaloniaCanvas
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };

        var border = new AvaloniaBorder
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            BorderBrush = SelectionBorder,
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
        };
        AvaloniaCanvas.SetLeft(border, 0);
        AvaloniaCanvas.SetTop(border, 0);
        layer.Children.Add(border);

        foreach (var _ in HandleFactors)
        {
            layer.Children.Add(new AvaloniaBorder
            {
                Width = ChartHandleSize,
                Height = ChartHandleSize,
                Background = Brushes.White,
                BorderBrush = SelectionBorder,
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
            });
        }

        LayoutChartAdornerHandles(layer, width, height);
        return layer;
    }

    /// <summary>Positions the adorner border + the eight handles for a chart of the given pixel size.</summary>
    private static void LayoutChartAdornerHandles(AvaloniaCanvas layer, double width, double height)
    {
        if (layer.Children.Count < 1 + HandleFactors.Length)
            return;

        if (layer.Children[0] is AvaloniaBorder border)
        {
            border.Width = Math.Max(1, width);
            border.Height = Math.Max(1, height);
        }

        for (var i = 0; i < HandleFactors.Length; i++)
        {
            if (layer.Children[i + 1] is not Control handle)
                continue;
            var (fx, fy) = HandleFactors[i];
            AvaloniaCanvas.SetLeft(handle, (fx * width) - (ChartHandleSize / 2));
            AvaloniaCanvas.SetTop(handle, (fy * height) - (ChartHandleSize / 2));
        }
    }
}
