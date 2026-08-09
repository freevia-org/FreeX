using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
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
            // Clipboard commands intentionally use the same shell entry points as keyboard,
            // ribbon, and native-menu activation. Object Cut remains pending until Paste succeeds.
            case WorksheetContextMenuAction.Cut:
                RunGuarded(CutSelectedRangeToClipboardAsync);
                break;
            case WorksheetContextMenuAction.Copy:
                RunGuarded(CopySelectedRangeToClipboardAsync);
                break;
            case WorksheetContextMenuAction.Paste:
                RunGuarded(PasteClipboardTextAsync);
                break;

            // --- Picture ---
            case WorksheetContextMenuAction.FormatPicture:
                RunGuarded(OpenFormatPictureDialogAsync);
                break;
            case WorksheetContextMenuAction.CropPicture:
                BeginSelectedPictureCropMode();
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

            // --- Shared Picture/Shape/TextBox/Chart ---
            case WorksheetContextMenuAction.DeleteObject:
                TryDeleteSelectedDrawingObject();
                break;
        }
    }

    /// <summary>
    /// R121-model-drawing-delete-1: Delete-key/context-menu entry point for removing the currently
    /// selected picture/text box/shape/chart outright. Mirrors the WPF host's identically-named
    /// MainWindow.Drawing.cs method. Deliberately does NOT fall back to "whatever object the active
    /// cell happens to be anchored under" -- only a GENUINELY selected object
    /// (<see cref="_selectedDrawingObjectKind"/>/<see cref="_selectedDrawingObjectId"/>) is deletable,
    /// so a plain cell selection with no object picked returns false and the caller falls through to
    /// its ordinary Clear Contents behavior.
    /// </summary>
    private bool TryDeleteSelectedDrawingObject()
    {
        if (_selectedDrawingObjectKind is not { } kind || _selectedDrawingObjectId is not { } objectId)
            return false;

        var command = DrawingObjectCommandPlanner.BuildDeleteCommand(_session.ActiveSheet.Id, kind, objectId);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Format(
                "InsertLoc_DrawingCommandFailed", DrawingObjectActionPlanner.DeleteObjectCommandTitle));
            return true;
        }

        ClearSelectedDrawingObject();
        RefreshShell(UiText.Get("DrawingInteract_Deleted"));
        return true;
    }

    // R129-model-drawing-nudge-1: shared "is a picture/shape/text box/chart genuinely selected"
    // check, mirroring the WPF host's HasSelectedDrawingObject (MainWindow.Drawing.cs). Used to gate
    // arrow-key nudge, Escape-deselect, F2, and Ctrl+D/Ctrl+R fill so all four agree with the
    // existing Delete/Backspace guards on when a drawing object -- not a cell -- owns the keyboard.
    private bool HasSelectedDrawingObject() =>
        _selectedDrawingObjectKind is not null && _selectedDrawingObjectId is not null;

    private void NudgeSelectedDrawingObject(Key key, bool fine)
    {
        var modifiers = fine ? KeyModifiers.Control : KeyModifiers.None;
        if (!TryPlanSelectedDrawingObjectNudge(key, modifiers, out var plan))
            return;

        ExecuteSelectedDrawingObjectNudge(plan);
    }

    private bool TryPlanSelectedDrawingObjectNudge(
        Key key,
        KeyModifiers modifiers,
        out DrawingObjectNudgePlan plan) =>
        DrawingObjectNudgePlanner.TryPlan(
            ToDrawingObjectNudgeDirection(key),
            ToDrawingObjectNudgeModifiers(modifiers),
            _selectedDrawingObjectKind,
            _selectedDrawingObjectId,
            out plan);

    private void ExecuteSelectedDrawingObjectNudge(DrawingObjectNudgePlan plan)
    {
        var command = DrawingObjectCommandPlanner.BuildNudgeCommand(
            _session.ActiveSheet.Id,
            plan.Kind,
            plan.ObjectId,
            plan.DeltaX,
            plan.DeltaY);
        RunDrawingObjectCommand(command, "Ready", DrawingObjectActionPlanner.MoveObjectCommandTitle);
    }

    private static DrawingObjectNudgeDirection? ToDrawingObjectNudgeDirection(Key key) =>
        key switch
        {
            Key.Up => DrawingObjectNudgeDirection.Up,
            Key.Down => DrawingObjectNudgeDirection.Down,
            Key.Left => DrawingObjectNudgeDirection.Left,
            Key.Right => DrawingObjectNudgeDirection.Right,
            _ => null
        };

    private static DrawingObjectNudgeModifiers ToDrawingObjectNudgeModifiers(KeyModifiers modifiers)
    {
        var result = DrawingObjectNudgeModifiers.None;
        if ((modifiers & KeyModifiers.Control) != 0)
            result |= DrawingObjectNudgeModifiers.Control;
        if ((modifiers & KeyModifiers.Shift) != 0)
            result |= DrawingObjectNudgeModifiers.Shift;
        if ((modifiers & KeyModifiers.Alt) != 0)
            result |= DrawingObjectNudgeModifiers.Alt;
        if ((modifiers & KeyModifiers.Meta) != 0)
            result |= DrawingObjectNudgeModifiers.Meta;
        return result;
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
            ChartCommandWorkflowPlanner.BuildBoundsCommand(
                _session.ActiveSheet.Id,
                chart,
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
    private PictureCropDragSession? _pictureCropDragSession;
    private bool _isPictureCropMode;

    private sealed class DrawingObjectDragSession
    {
        public required DrawingObjectRenderPlan RenderPlan { get; init; }
        public required Control Container { get; init; }
        public required AvaloniaGrid Surface { get; init; }
        public required AvaloniaCanvas Adorner { get; init; }
        public required ObjectDragKind Kind { get; init; }
        public required LayoutRect StartCanvasRect { get; init; }
        public required LayoutPoint StartPointerInCanvas { get; init; }
        public required CellAddress StartAnchor { get; init; }
        public required double StartRotationDegrees { get; init; }
        public required bool StartFlipHorizontal { get; init; }
        public required bool StartFlipVertical { get; init; }
        public LayoutRect CurrentCanvasRect { get; set; }
        public double CurrentRotationDegrees { get; set; }
        public bool CurrentFlipHorizontal { get; set; }
        public bool CurrentFlipVertical { get; set; }
        public bool Moved { get; set; }
    }

    private sealed class PictureCropDragSession
    {
        public required DrawingObjectRenderPlan RenderPlan { get; init; }
        public required Control Container { get; init; }
        public required AvaloniaGrid Surface { get; init; }
        public required LayoutRect PictureRect { get; init; }
        public required LayoutPoint StartPointerInCanvas { get; init; }
        public required PictureCropHandle Handle { get; init; }
        public required PictureCropRatios StartCrop { get; init; }
        public PictureCropRatios CurrentCrop { get; set; }
        public AvaloniaCanvas? Adorner { get; set; }
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

    private AvaloniaCanvas CreatePictureCropSelectionAdorner(
        double width,
        double height,
        PictureCropRatios crop)
    {
        var layer = new AvaloniaCanvas
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };

        var objectRect = new LayoutRect(
            DrawingObjectSelectionHorizontalPadding,
            DrawingObjectSelectionTopPadding,
            Math.Max(1, width),
            Math.Max(1, height));
        var visibleRect = PictureCropPlanner.CalculateVisibleCropRect(objectRect, crop);
        var shade = new SolidColorBrush(Color.FromArgb(76, 0, 0, 0));

        void AddShade(double left, double top, double shadeWidth, double shadeHeight)
        {
            if (shadeWidth <= 0 || shadeHeight <= 0)
                return;

            var border = new AvaloniaBorder
            {
                Width = shadeWidth,
                Height = shadeHeight,
                Background = shade,
                IsHitTestVisible = false,
            };
            AvaloniaCanvas.SetLeft(border, left);
            AvaloniaCanvas.SetTop(border, top);
            layer.Children.Add(border);
        }

        AddShade(objectRect.Left, objectRect.Top, objectRect.Width, visibleRect.Top - objectRect.Top);
        AddShade(
            objectRect.Left,
            visibleRect.Bottom,
            objectRect.Width,
            objectRect.Bottom - visibleRect.Bottom);
        AddShade(
            objectRect.Left,
            visibleRect.Top,
            visibleRect.Left - objectRect.Left,
            visibleRect.Height);
        AddShade(
            visibleRect.Right,
            visibleRect.Top,
            objectRect.Right - visibleRect.Right,
            visibleRect.Height);

        var cropBorder = new AvaloniaBorder
        {
            Width = Math.Max(1, visibleRect.Width),
            Height = Math.Max(1, visibleRect.Height),
            BorderBrush = SelectionBorder,
            BorderThickness = new Thickness(1.5),
            IsHitTestVisible = false,
        };
        AvaloniaCanvas.SetLeft(cropBorder, visibleRect.Left);
        AvaloniaCanvas.SetTop(cropBorder, visibleRect.Top);
        layer.Children.Add(cropBorder);

        foreach (var (_, center) in PictureCropPlanner.GetHandleCenters(objectRect))
        {
            var handle = new AvaloniaBorder
            {
                Width = PictureCropPlanner.DefaultHandleSize,
                Height = PictureCropPlanner.DefaultHandleSize,
                Background = Brushes.White,
                BorderBrush = SelectionBorder,
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
            };
            AvaloniaCanvas.SetLeft(handle, center.X - PictureCropPlanner.DefaultHandleSize / 2);
            AvaloniaCanvas.SetTop(handle, center.Y - PictureCropPlanner.DefaultHandleSize / 2);
            layer.Children.Add(handle);
        }

        layer.Width = objectRect.Width + DrawingObjectSelectionHorizontalPadding * 2;
        layer.Height = objectRect.Height + DrawingObjectSelectionTopPadding + DrawingObjectSelectionBottomPadding;
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

    private bool TryBeginPictureCropDrag(
        DrawingObjectRenderPlan renderPlan,
        Control container,
        AvaloniaGrid surface,
        AvaloniaCanvas adorner,
        PointerPressedEventArgs args)
    {
        if (!_isPictureCropMode || renderPlan.Bounds.Kind != SelectionPaneObjectKind.Picture ||
            renderPlan.Bounds.PictureKind != PictureKind.Image ||
            container.Parent is not AvaloniaCanvas canvas)
        {
            return false;
        }

        var localPoint = args.GetCurrentPoint(container).Position;
        var pictureRect = DrawingObjectObjectRect(surface);
        var handle = PictureCropPlanner.HitTestHandle(new LayoutPoint(localPoint.X, localPoint.Y), pictureRect);
        if (handle == PictureCropHandle.None)
            return false;

        var pointInCanvas = args.GetCurrentPoint(canvas).Position;
        var startCrop = new PictureCropRatios(
            renderPlan.Bounds.CropLeft,
            renderPlan.Bounds.CropTop,
            renderPlan.Bounds.CropRight,
            renderPlan.Bounds.CropBottom);
        _pictureCropDragSession = new PictureCropDragSession
        {
            RenderPlan = renderPlan,
            Container = container,
            Surface = surface,
            Adorner = adorner,
            PictureRect = pictureRect,
            StartPointerInCanvas = new LayoutPoint(pointInCanvas.X, pointInCanvas.Y),
            Handle = handle,
            StartCrop = startCrop,
            CurrentCrop = startCrop,
        };
        args.Pointer.Capture(container);
        container.Focus();
        container.Cursor = PictureCropCursor(handle);
        args.Handled = true;
        return true;
    }

    private void WirePictureCropDragMoveRelease(
        DrawingObjectRenderPlan renderPlan,
        Control container,
        AvaloniaGrid surface)
    {
        container.PointerMoved += (_, args) =>
        {
            if (_pictureCropDragSession is { } session && ReferenceEquals(session.Container, container))
            {
                if (container.Parent is AvaloniaCanvas canvas)
                {
                    var point = args.GetCurrentPoint(canvas).Position;
                    var crop = PictureCropPlanner.CalculateCrop(
                        session.Handle,
                        session.StartCrop,
                        session.PictureRect,
                        session.StartPointerInCanvas,
                        new LayoutPoint(point.X, point.Y));
                    session.CurrentCrop = crop;
                    session.Moved = true;
                    UpdatePictureCropPreview(session);
                    container.Cursor = PictureCropCursor(session.Handle);
                }

                args.Handled = true;
                return;
            }

            var localPoint = args.GetCurrentPoint(container).Position;
            var handle = PictureCropPlanner.HitTestHandle(
                new LayoutPoint(localPoint.X, localPoint.Y),
                DrawingObjectObjectRect(surface));
            container.Cursor = PictureCropCursor(handle);
        };
        container.PointerExited += (_, _) =>
        {
            if (_pictureCropDragSession is null)
                container.Cursor = Cursor.Default;
        };
        container.PointerReleased += (_, args) => EndPictureCropDrag(container, args);
        container.PointerCaptureLost += (_, _) => CancelPictureCropDrag(container);
    }

    private void UpdatePictureCropPreview(PictureCropDragSession session)
    {
        var crop = session.CurrentCrop;
        var previewPlan = session.RenderPlan with
        {
            PrimitiveKind = DrawingObjectRenderPrimitiveKind.CroppedImage,
            Crop = new DrawingPictureCrop(crop.Left, crop.Top, crop.Right, crop.Bottom),
        };
        session.Surface.Children.Clear();
        session.Surface.Children.Add(CreateDrawingObjectVisual(
            previewPlan,
            session.PictureRect.Width,
            session.PictureRect.Height,
            _session.Workbook.Theme));

        if (session.Adorner is { } previousAdorner && session.Container is AvaloniaGrid container)
        {
            var replacement = CreatePictureCropSelectionAdorner(
                session.PictureRect.Width,
                session.PictureRect.Height,
                crop);
            var index = container.Children.IndexOf(previousAdorner);
            if (index >= 0)
            {
                container.Children.RemoveAt(index);
                container.Children.Insert(index, replacement);
            }

            session.Adorner = replacement;
        }
    }

    private void EndPictureCropDrag(Control container, PointerReleasedEventArgs args)
    {
        if (_pictureCropDragSession is not { } session || !ReferenceEquals(session.Container, container))
            return;

        _pictureCropDragSession = null;
        args.Pointer.Capture(null);
        container.Cursor = Cursor.Default;
        if (!session.Moved)
        {
            args.Handled = true;
            return;
        }

        ApplyPictureCrop(session.RenderPlan.Bounds.Id, session.CurrentCrop);
        args.Handled = true;
    }

    private bool ApplyPictureCrop(Guid pictureId, PictureCropRatios crop)
    {
        var result = _session.ExecuteReviewCommand(PictureCropDialogPlanner.BuildCommand(
            _session.ActiveSheet.Id,
            pictureId,
            crop.Left,
            crop.Top,
            crop.Right,
            crop.Bottom));
        RefreshShell(result.Success
            ? UiText.Get("PictureCrop_Applied")
            : result.ErrorMessage ?? UiText.Get("PictureCrop_Applied"));
        return result.Success;
    }

    private void CancelPictureCropDrag(Control container)
    {
        if (_pictureCropDragSession is not { } session || !ReferenceEquals(session.Container, container))
            return;

        _pictureCropDragSession = null;
        container.Cursor = Cursor.Default;
        RefreshShell(string.Empty);
    }

    private static Cursor PictureCropCursor(PictureCropHandle handle) =>
        new(handle switch
        {
            PictureCropHandle.CropNW => StandardCursorType.TopLeftCorner,
            PictureCropHandle.CropSE => StandardCursorType.BottomRightCorner,
            PictureCropHandle.CropNE => StandardCursorType.TopRightCorner,
            PictureCropHandle.CropSW => StandardCursorType.BottomLeftCorner,
            PictureCropHandle.CropN or PictureCropHandle.CropS => StandardCursorType.SizeNorthSouth,
            PictureCropHandle.CropE or PictureCropHandle.CropW => StandardCursorType.SizeWestEast,
            _ => StandardCursorType.Arrow,
        });

    private void EnterPictureCropMode(PictureModel picture)
    {
        if (picture.Kind != PictureKind.Image)
        {
            ShowEditIssue(PictureCropDialogPlanner.NotImageMessage);
            return;
        }

        _isPictureCropMode = true;
        _selectedDrawingObjectKind = SelectionPaneObjectKind.Picture;
        _selectedDrawingObjectId = picture.Id;
        _ribbonContextSource.OnDrawingObjectSelected(SelectionPaneObjectKind.Picture);
        RefreshShell(UiText.Get("PictureCrop_Title"));
    }

    private void BeginSelectedPictureCropMode()
    {
        if (_isOpening || _isSaving)
            return;

        if (ResolveSelectedPicture() is { } picture)
            EnterPictureCropMode(picture);
    }

    private void ExitPictureCropMode()
    {
        if (!_isPictureCropMode && _pictureCropDragSession is null)
            return;

        _pictureCropDragSession = null;
        _isPictureCropMode = false;
        RefreshShell(string.Empty);
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

        var localPoint = args.GetCurrentPoint(container).Position;
        var objectRect = DrawingObjectObjectRect(surface);
        var kind = ObjectDragPlanner.HitTestHandle(new LayoutPoint(localPoint.X, localPoint.Y), objectRect,
            DrawingObjectHandleSize, DrawingObjectHandleHitPadding, renderPlan.Bounds.RotationDegrees);
        if (kind == ObjectDragKind.None)
            return false;

        var pointer = args.GetCurrentPoint(canvas).Position;
        if (!TryBeginDrawingObjectDragAtPoint(
                renderPlan,
                container,
                surface,
                adorner,
                new LayoutPoint(localPoint.X, localPoint.Y),
                new LayoutPoint(pointer.X, pointer.Y),
                kind))
            return false;

        args.Pointer.Capture(container);
        args.Handled = true;
        return true;
    }

    private LayoutRect DrawingObjectObjectRect(AvaloniaGrid surface) => new(
        DrawingObjectSelectionHorizontalPadding,
        DrawingObjectSelectionTopPadding,
        surface.Bounds.Width > 0 ? surface.Bounds.Width : surface.Width,
        surface.Bounds.Height > 0 ? surface.Bounds.Height : surface.Height);

    private bool TryBeginDrawingObjectDragAtPoint(
        DrawingObjectRenderPlan renderPlan,
        Control container,
        AvaloniaGrid surface,
        AvaloniaCanvas adorner,
        LayoutPoint localPoint,
        LayoutPoint pointerInCanvas,
        ObjectDragKind? expectedKind = null)
    {
        if (container.Parent is not AvaloniaCanvas)
            return false;

        var objectRect = DrawingObjectObjectRect(surface);
        var kind = ObjectDragPlanner.HitTestHandle(
            localPoint,
            objectRect,
            DrawingObjectHandleSize,
            DrawingObjectHandleHitPadding,
            renderPlan.Bounds.RotationDegrees);
        if (kind == ObjectDragKind.None || expectedKind is { } expected && kind != expected)
            return false;

        var canvasRect = new LayoutRect(
            AvaloniaCanvas.GetLeft(container) + DrawingObjectSelectionHorizontalPadding,
            AvaloniaCanvas.GetTop(container) + DrawingObjectSelectionTopPadding,
            objectRect.Width,
            objectRect.Height);
        _drawingObjectDragSession = new DrawingObjectDragSession
        {
            RenderPlan = renderPlan,
            Container = container,
            Surface = surface,
            Adorner = adorner,
            Kind = kind,
            StartCanvasRect = canvasRect,
            StartPointerInCanvas = pointerInCanvas,
            StartAnchor = new CellAddress(
                _session.ActiveSheet.Id,
                renderPlan.Bounds.AnchorRow,
                renderPlan.Bounds.AnchorCol),
            StartRotationDegrees = renderPlan.Bounds.RotationDegrees,
            StartFlipHorizontal = renderPlan.Bounds.FlipHorizontal,
            StartFlipVertical = renderPlan.Bounds.FlipVertical,
            CurrentCanvasRect = canvasRect,
            CurrentRotationDegrees = renderPlan.Bounds.RotationDegrees,
            CurrentFlipHorizontal = renderPlan.Bounds.FlipHorizontal,
            CurrentFlipVertical = renderPlan.Bounds.FlipVertical,
        };
        container.Cursor = DrawingObjectDragCursor(kind);
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
        container.PointerCaptureLost += (_, _) => CancelDrawingObjectDrag(container);
    }

    private void ContinueDrawingObjectDrag(DrawingObjectDragSession session, PointerEventArgs args)
    {
        if (session.Container.Parent is not AvaloniaCanvas canvas)
            return;

        var point = args.GetCurrentPoint(canvas).Position;
        ContinueDrawingObjectDragAtPoint(session, new LayoutPoint(point.X, point.Y));
        args.Handled = true;
    }

    private void ContinueDrawingObjectDragAtPoint(
        DrawingObjectDragSession session,
        LayoutPoint point)
    {
        if (session.Kind == ObjectDragKind.Rotate)
        {
            var center = session.StartCanvasRect.Center;
            session.CurrentRotationDegrees = ObjectDragPlanner.CalculateRotationDegrees(
                center,
                point);
        }
        else
        {
            var transform = ObjectDragPlanner.CalculateDragTransform(
                session.Kind,
                session.StartCanvasRect,
                session.StartPointerInCanvas,
                point);
            session.CurrentCanvasRect = transform.Rect;
            session.CurrentFlipHorizontal = session.StartFlipHorizontal ^ transform.CrossedHorizontally;
            session.CurrentFlipVertical = session.StartFlipVertical ^ transform.CrossedVertically;
        }

        session.Moved = true;
        UpdateDrawingObjectDragPreview(session);
        session.Container.Cursor = DrawingObjectDragCursor(session.Kind);
    }

    private void CancelDrawingObjectDrag(Control container)
    {
        if (_drawingObjectDragSession is not { } session || !ReferenceEquals(session.Container, container))
            return;

        _drawingObjectDragSession = null;
        container.Cursor = Cursor.Default;
        // Capture can be revoked without PointerReleased (window deactivation, an overlay rebuild,
        // or platform cancellation). Discard the live preview just as WPF does from OnLostMouseCapture.
        RefreshShell(string.Empty);
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
        CellAddress? currentAnchor = null;
        if (session.Kind != ObjectDragKind.Rotate &&
            TryResolveCellAddressFromSheetGridPosition(
                new Point(session.CurrentCanvasRect.Left, session.CurrentCanvasRect.Top),
                out var resolvedAnchor))
        {
            currentAnchor = resolvedAnchor;
        }

        var zoomFactor = Math.Max(0.01, GetActiveZoomFactor());
        var plan = ObjectDragPlanner.PlanCommit(
            session.Kind,
            session.StartCanvasRect,
            session.CurrentCanvasRect,
            session.StartAnchor,
            currentAnchor,
            Math.Max(ObjectDragPlanner.MinimumObjectSize, session.CurrentCanvasRect.Width / zoomFactor),
            Math.Max(ObjectDragPlanner.MinimumObjectSize, session.CurrentCanvasRect.Height / zoomFactor),
            session.CurrentRotationDegrees,
            session.StartFlipHorizontal,
            session.StartFlipVertical,
            session.CurrentFlipHorizontal,
            session.CurrentFlipVertical);
        if (plan.Kind == ObjectDragCommitKind.Unavailable)
        {
            RefreshShell(UiText.Get("Drawing_ObjectNoLongerAvailable"));
            return;
        }

        if (plan.Kind == ObjectDragCommitKind.None)
        {
            RefreshShell(string.Empty);
            return;
        }

        var command = DrawingObjectCommandPlanner.BuildDragCommitCommand(
            sheetId,
            targetKind,
            session.RenderPlan.Bounds.Id,
            plan)!;
        var successStatus = FormatDrawingObjectResourceText(DrawingObjectActionPlanner.DragCommitSuccess(plan));
        var failureTitle = DrawingObjectActionPlanner.DragCommitCommandTitle(plan.Kind);

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
    // absolute pixels, so their direct drag commits through the shared object command planner below.
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
            var minimumChartWidth = DrawingObjectMinimumSizePlanner.MinimumWidth(DrawingObjectMinimumSizeKind.Chart);
            var minimumChartHeight = DrawingObjectMinimumSizePlanner.MinimumHeight(DrawingObjectMinimumSizeKind.Chart);
            var transform = ObjectDragPlanner.CalculateDragTransform(
                session.Kind,
                session.StartCanvasRect,
                new LayoutPoint(session.StartPointerInCanvas.X, session.StartPointerInCanvas.Y),
                new LayoutPoint(pointer.X, pointer.Y),
                Math.Min(minimumChartWidth, minimumChartHeight));

            var rect = ObjectDragPlanner.ClampResizeToMinimums(
                session.Kind,
                transform,
                minimumChartWidth,
                minimumChartHeight);
            AvaloniaCanvas.SetLeft(container, rect.Left);
            AvaloniaCanvas.SetTop(container, rect.Top);
            container.Width = rect.Width;
            container.Height = rect.Height;

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

            var committed = session.Moved;
            _chartDragSession = null;
            // Clear the session before releasing capture: Avalonia may raise PointerCaptureLost
            // synchronously, and that event must take the cancellation path only for an active drag.
            args.Pointer.Capture(null);
            container.Cursor = Cursor.Default;
            if (committed)
                CommitChartDrag(session, container);
            args.Handled = true;
        };

        container.PointerCaptureLost += (_, _) =>
        {
            if (_chartDragSession is { } session && ReferenceEquals(session.Container, container))
            {
                _chartDragSession = null;
                container.Cursor = Cursor.Default;
                // Capture can be revoked without PointerReleased (window deactivation, an overlay
                // rebuild, or platform cancellation). Discard the live preview just as the shared
                // drawing-object path does from OnLostMouseCapture.
                RefreshShell(string.Empty);
            }
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
        var minimumChartWidth = DrawingObjectMinimumSizePlanner.MinimumWidth(DrawingObjectMinimumSizeKind.Chart);
        var minimumChartHeight = DrawingObjectMinimumSizePlanner.MinimumHeight(DrawingObjectMinimumSizeKind.Chart);
        var sheetWidth = Math.Max(minimumChartWidth, container.Width / zoomFactor);
        var sheetHeight = Math.Max(minimumChartHeight, container.Height / zoomFactor);

        var command = ChartCommandWorkflowPlanner.BuildBoundsCommand(
            sheet.Id,
            session.Chart,
            sheetLeft,
            sheetTop,
            sheetWidth,
            sheetHeight);
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
