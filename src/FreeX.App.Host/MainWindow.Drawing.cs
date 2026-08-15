using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeX.App.Presentation.Charts.Editing;
using System.Windows.Input;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Free.Shared.Ribbon.Wpf;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void TextBoxBtn_Click(object sender, RoutedEventArgs e)
    {
        InsertTextBox();
    }
    private async void InsertPictureBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var result = WpfFileDialogService.ShowOpenDialog(
            this,
            UiText.Get("MainWindowDialog_ImageFilesFilter"),
            checkFileExists: true,
            multiselect: false,
            title: UiText.Get("MainWindowDialog_InsertPictureTitle"));
        if (!result.Chosen) return;

        var readResult = await FileByteReadWorkflow.ReadLocalPathAsync(result.FileName!);
        if (readResult.Outcome == FileByteReadOutcome.Canceled)
            return;
        if (!readResult.IsReadable)
        {
            ShowOwnedMessage(
                UiText.Format("MainWindowMessage_InsertPictureReadFailed", readResult.FailureMessage),
                UiText.Get("MainWindowMessage_InsertPictureTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var bytes = readResult.Bytes;
        var contentType = DrawingInputParser.GetImageContentType(result.FileName!);
        InsertPictureCommand? currentSheetCommand = null;
        if (!TryExecuteGroupedSheetCommand(
                "Insert Picture",
                sheetId =>
                {
                    var command = PictureInsertionPlacementPlanner.CreateInsertPictureCommand(
                        sheetId,
                        new CellAddress(sheetId, range.Start.Row, range.Start.Col),
                        bytes,
                        contentType,
                        DecodePictureInsertionSize(bytes));
                    if (sheetId == _currentSheetId)
                        currentSheetCommand = command;
                    return command;
                }))
            return;

        if (currentSheetCommand is not null)
            SelectInsertedDrawingObject(currentSheetCommand.PictureId, FreeX.App.UI.ObjectKind.Picture, range.Start);
        else
        {
            SetActiveCell(range.Start);
            UpdateViewport();
        }
    }

    private static PictureInsertionSize? DecodePictureInsertionSize(byte[] imageBytes) =>
        ImageDimensionDecoder.TryDecode(imageBytes, out var decoded)
            ? new PictureInsertionSize(decoded.Width, decoded.Height)
            : null;

    private void PictureSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        var picture = GetTargetPicture(_currentSheetId);
        if (picture is null)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_NoPictureFoundOnSheet"),
                UiText.Get("MainWindowMessage_PictureSizeTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new FormatPictureDialog(picture) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Format Picture",
                sheetId => CreateFormatPictureCommand(sheetId, GetTargetPicture(sheetId), dialog.Result)))
            return;

        UpdateViewport();
    }

    private static IWorkbookCommand CreateFormatPictureCommand(
        SheetId sheetId,
        PictureModel? picture,
        FormatPictureDialogResult result)
    {
        var formatResult = new FormatPicturePlanner.FormatObjectResult(
            result.Width,
            result.Height,
            result.RotationDegrees,
            result.LockAspectRatio,
            result.AltText);
        var pictureResult = new FormatPicturePlanner.PictureFormatResult(
            formatResult,
            new PictureCropDialogPlanner.CropResult(
                result.CropLeft,
                result.CropTop,
                result.CropRight,
                result.CropBottom));
        return DrawingObjectFormatCommandPolicy.BuildPictureFormatCommand(
            sheetId,
            picture,
            pictureResult,
            "Format Picture",
            UiText.Get("MainWindowMessage_PictureWasNotFound"));
    }

    private void PictureRotateBtn_Click(object sender, RoutedEventArgs e)
    {
        var picture = GetTargetPicture(_currentSheetId);
        if (picture is null)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_NoPictureFoundOnSheet"),
                UiText.Get("MainWindowMessage_RotatePictureTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new RotationDialog(picture.RotationDegrees, UiText.Get("MainWindowMessage_RotatePictureTitle")) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Rotate Picture",
                sheetId => DrawingObjectFormatCommandPolicy.BuildRotationCommand(
                    sheetId,
                    DrawingObjectTargetKind.Picture,
                    GetTargetPicture(sheetId)?.Id ?? Guid.Empty,
                    new FormatPicturePlanner.RotationResult(dialog.Result.Degrees))))
            return;

        UpdateViewport();
    }

    private void PictureCropBtn_Click(object sender, RoutedEventArgs e)
    {
        var picture = GetTargetPicture(_currentSheetId);
        if (picture is null)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_NoPictureFoundOnSheet"),
                UiText.Get("MainWindowMessage_CropPictureTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (picture.Kind != PictureKind.Image)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_CropRequiresInsertedImage"),
                UiText.Get("MainWindowMessage_CropPictureTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        EnterPictureCropMode(picture);
    }

    private void PictureCropDialogMenuItem_Click(object sender, RoutedEventArgs e) =>
        PictureCropBtn_Click(sender, e);

    private void PictureResetCropMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var picture = GetTargetPicture(_currentSheetId);
        if (picture is null)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_NoPictureFoundOnSheet"),
                UiText.Get("MainWindowMessage_ResetCropTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (picture.Kind != PictureKind.Image)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_CropRequiresInsertedImage"),
                UiText.Get("MainWindowMessage_ResetCropTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Reset Crop",
                sheetId => PictureCropDialogPlanner.BuildResetCommand(
                    sheetId,
                    GetTargetPicture(sheetId)?.Id ?? Guid.Empty)))
            return;

        UpdateViewport();
        EnterPictureCropMode(picture);
    }

    private void EnterPictureCropMode(PictureModel picture)
    {
        SetActiveCell(picture.Anchor);
        EnsureCellVisible(picture.Anchor);
        SheetGrid.SelectedObjectId = picture.Id;
        SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.Picture;
        SheetGrid.IsPictureCropMode = true;
        SheetGrid.Focus();
        SheetGrid.InvalidateVisual();
    }

    private PictureModel? GetTargetPicture(SheetId sheetId)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (GetSelectedPictureOnSheet(sheet) is { } selectedPicture)
            return selectedPicture;

        return DrawingTargetResolver.GetTargetPicture(sheet, SheetGrid.SelectedRange?.Start);
    }

    private void DrawRectBtn_Click(object sender, RoutedEventArgs e)    => InsertDrawingShape(DrawingShapeKind.Rectangle);
    private void DrawEllipseBtn_Click(object sender, RoutedEventArgs e) => InsertDrawingShape(DrawingShapeKind.Ellipse);
    private void DrawLineBtn_Click(object sender, RoutedEventArgs e)    => InsertDrawingShape(DrawingShapeKind.Line);
    private void DrawTextBtn_Click(object sender, RoutedEventArgs e)    => InsertTextBox();
    private void BringForwardBtn_Click(object sender, RoutedEventArgs e) => ReorderSelectedDrawingObject(forward: true);
    private void SendBackwardBtn_Click(object sender, RoutedEventArgs e) => ReorderSelectedDrawingObject(forward: false);
    private void SelectionPaneBtn_Click(object sender, RoutedEventArgs e) => ShowSelectionPaneDialog();
    private void ObjectSizeBtn_Click(object sender, RoutedEventArgs e) => ResizeSelectedDrawingObject();
    private void ObjectRotateBtn_Click(object sender, RoutedEventArgs e) => RotateSelectedDrawingObject();
    private void ObjectFillBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingObjectFill();
    private void ObjectOutlineBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingObjectColor(isFill: false);
    private void ObjectGradientBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingShapeGradient();
    private void ObjectEffectsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ButtonBase button && button.ContextMenu is { } menu)
            OpenRibbonContextMenu(button, menu);
    }

    // ── Page Layout tab ───────────────────────────────────────────────────────

    private void InsertTextBox()
    {
        var anchor = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
        AddTextBoxCommand? currentSheetCommand = null;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                DrawingObjectActionPlanner.InsertTextBoxCommandTitle,
                sheetId =>
                {
                    var currentAnchor = SheetGrid.SelectedRange?.Start ?? anchor;
                    var command = DrawingInsertionPlanner.BuildInlineEditTextBoxCommand(
                        sheetId,
                        new CellAddress(sheetId, currentAnchor.Row, currentAnchor.Col));
                    if (sheetId == _currentSheetId)
                        currentSheetCommand = command;
                    return command;
                }))
            return;

        if (currentSheetCommand is not null)
        {
            SelectInsertedDrawingObject(currentSheetCommand.TextBoxId, FreeX.App.UI.ObjectKind.TextBox, anchor);
            BeginTextBoxInlineEdit(currentSheetCommand.TextBoxId);
        }
        else
        {
            SetActiveCell(anchor);
            EnsureCellVisible(anchor);
            UpdateViewport();
        }
    }

    private void InsertDrawingShape(DrawingShapeKind kind)
    {
        var anchor = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
        AddDrawingShapeCommand? currentSheetCommand = null;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                DrawingObjectActionPlanner.InsertShapeCommandTitle,
                sheetId =>
                {
                    var currentAnchor = SheetGrid.SelectedRange?.Start ?? anchor;
                    var command = DrawingInsertionPlanner.BuildShapeCommand(
                        sheetId,
                        new CellAddress(sheetId, currentAnchor.Row, currentAnchor.Col),
                        kind,
                        fillColor: ResolveCurrentShapeFillColor(),
                        outlineColor: ResolveCurrentShapeOutlineColor(),
                        hasFill: ResolveCurrentShapeHasFill());
                    if (sheetId == _currentSheetId)
                        currentSheetCommand = command;
                    return command;
                }))
            return;

        if (currentSheetCommand is not null)
            SelectInsertedDrawingObject(currentSheetCommand.ShapeId, FreeX.App.UI.ObjectKind.Shape, anchor);
        else
        {
            SetActiveCell(anchor);
            EnsureCellVisible(anchor);
            UpdateViewport();
        }
    }

    private void SelectInsertedDrawingObject(Guid objectId, FreeX.App.UI.ObjectKind kind, CellAddress anchor)
    {
        SetActiveCell(anchor);
        EnsureCellVisible(anchor);
        SheetGrid.SelectedObjectId = objectId;
        SheetGrid.SelectedObjectKind = kind;
        UpdateViewport();
    }

    private void ReorderSelectedDrawingObject(bool forward)
    {
        var currentTarget = GetTargetDrawingZOrderObject(_currentSheetId);
        if (currentTarget is null)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_NoDrawingObjectOnSheet"),
                UiText.Get("MainWindowMessage_DrawTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var title = DrawingObjectActionPlanner.ZOrderCommandTitle(forward);
        if (!TryExecuteRepeatableGroupedSheetCommand(
                title,
                sheetId =>
                {
                    var target = GetTargetDrawingZOrderObject(sheetId, currentTarget.Kind);
                    return DrawingObjectCommandPlanner.BuildZOrderCommand(
                        sheetId,
                        target?.Kind ?? currentTarget.Kind,
                        target?.Id ?? Guid.Empty,
                        forward);
                }))
            return;

        SetActiveCell(currentTarget.Anchor);
        EnsureCellVisible(currentTarget.Anchor);
        UpdateViewport();
    }

    // R121-model-drawing-delete-1: Delete-key/context-menu/Selection-Pane entry point for removing a
    // selected picture/text box/shape/chart outright. Unlike ResizeSelectedDrawingObject etc., this
    // deliberately does NOT fall back to "the last object anchored at the active cell" -- Excel only
    // deletes an object that is GENUINELY selected (SheetGrid.SelectedObjectId/-Kind), never one merely
    // under the cursor, so a plain cell selection with no object picked must fall through to
    // ExecuteClearSelection's ordinary Clear Contents behavior instead.
    private bool TryDeleteSelectedDrawingObject()
    {
        var kind = ToSelectionPaneObjectKindIncludingChart(SheetGrid.SelectedObjectKind);
        var objectId = SheetGrid.SelectedObjectId;
        if (kind is null || objectId == Guid.Empty)
            return false;

        var command = DrawingObjectCommandPlanner.BuildDeleteCommand(_currentSheetId, kind.Value, objectId);
        if (!TryExecuteCommand(command, DrawingObjectActionPlanner.DeleteObjectCommandTitle, out _))
        {
            // Rejected (e.g. protection) -- the key press is still "handled" (an object was selected),
            // ShowCommandError already surfaced why.
            return true;
        }

        SheetGrid.SelectedObjectId = Guid.Empty;
        SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.None;
        UpdateViewport();
        return true;
    }

    // R123-model-drawing-backspace-1: shared "is a picture/shape/text box/chart genuinely selected"
    // check, used by TryDeleteSelectedDrawingObject (Delete key) AND the Backspace
    // (ClearSelectionAndEdit) handler in MainWindow.KeyboardCommands.cs, so both keys agree on when
    // a drawing object -- not a cell -- owns the current selection. Matches Excel: with an object
    // selected, Backspace is a total no-op (no delete, no cell clear, no edit-mode entry).
    private bool HasSelectedDrawingObject() =>
        ToSelectionPaneObjectKindIncludingChart(SheetGrid.SelectedObjectKind) is not null
        && SheetGrid.SelectedObjectId != Guid.Empty;

    // R129-model-drawing-nudge-1: Up/Down/Left/Right entry point for MainWindow_KeyDown, invoked
    // only when HasSelectedDrawingObject() is true (see MainWindow.Selection.cs). Mirrors
    // TryDeleteSelectedDrawingObject's shape -- read the selection straight off SheetGrid, build the
    // matching per-kind command via DrawingObjectCommandPlanner, execute it, and refresh the
    // viewport. Deliberately does NOT move the active cell/anchor -- Excel leaves the underlying
    // cell selection alone while an object owns the arrow keys.
    private void NudgeSelectedDrawingObject(Key key, bool fine)
    {
        var modifiers = fine ? ModifierKeys.Control : ModifierKeys.None;
        if (!TryPlanSelectedDrawingObjectNudge(key, modifiers, out var plan))
            return;

        ExecuteSelectedDrawingObjectNudge(plan);
    }

    private bool TryPlanSelectedDrawingObjectNudge(
        Key key,
        ModifierKeys modifiers,
        out DrawingObjectNudgePlan plan) =>
        DrawingObjectNudgePlanner.TryPlan(
            ToDrawingObjectNudgeDirection(key),
            ToDrawingObjectNudgeModifiers(modifiers),
            ToSelectionPaneObjectKindIncludingChart(SheetGrid.SelectedObjectKind),
            SheetGrid.SelectedObjectId,
            out plan);

    private void ExecuteSelectedDrawingObjectNudge(DrawingObjectNudgePlan plan)
    {
        var command = DrawingObjectCommandPlanner.BuildNudgeCommand(
            _currentSheetId,
            plan.Kind,
            plan.ObjectId,
            plan.DeltaX,
            plan.DeltaY);
        if (!TryExecuteCommand(command, DrawingObjectActionPlanner.MoveObjectCommandTitle, out _))
            return;

        UpdateViewport();
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

    private static DrawingObjectNudgeModifiers ToDrawingObjectNudgeModifiers(ModifierKeys modifiers)
    {
        var result = DrawingObjectNudgeModifiers.None;
        if ((modifiers & ModifierKeys.Control) != 0)
            result |= DrawingObjectNudgeModifiers.Control;
        if ((modifiers & ModifierKeys.Shift) != 0)
            result |= DrawingObjectNudgeModifiers.Shift;
        if ((modifiers & ModifierKeys.Alt) != 0)
            result |= DrawingObjectNudgeModifiers.Alt;
        if ((modifiers & ModifierKeys.Windows) != 0)
            result |= DrawingObjectNudgeModifiers.Meta;
        return result;
    }

    private static SelectionPaneObjectKind? ToSelectionPaneObjectKindIncludingChart(FreeX.App.UI.ObjectKind kind) =>
        kind switch
        {
            FreeX.App.UI.ObjectKind.Picture => SelectionPaneObjectKind.Picture,
            FreeX.App.UI.ObjectKind.Shape => SelectionPaneObjectKind.Shape,
            FreeX.App.UI.ObjectKind.TextBox => SelectionPaneObjectKind.TextBox,
            FreeX.App.UI.ObjectKind.Chart => SelectionPaneObjectKind.Chart,
            _ => null
        };

    private void ResizeSelectedDrawingObject()
    {
        var target = GetTargetTransformDrawingObject(_currentSheetId);
        if (target is null)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_NoDrawingObjectOnSheet"),
                UiText.Get("MainWindowMessage_ObjectSizeTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new ObjectSizeDialog(target.Width, target.Height, UiText.Get("MainWindowMessage_ObjectSizeTitle")) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                DrawingObjectActionPlanner.ObjectSizeCommandTitle,
                sheetId =>
                {
                    var groupedTarget = GetTargetTransformDrawingObject(sheetId, target.Kind);
                    return DrawingObjectFormatCommandPolicy.BuildResizeCommand(
                        sheetId,
                        target.Kind,
                        groupedTarget?.Id ?? Guid.Empty,
                        new ObjectSizeDialogSize(dialog.Result.Width, dialog.Result.Height));
                }))
            return;

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
    }

    private void RotateSelectedDrawingObject()
    {
        var target = GetTargetTransformDrawingObject(_currentSheetId);
        if (target is null)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_NoDrawingObjectOnSheet"),
                UiText.Get("MainWindowMessage_RotateObjectTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new RotationDialog(target.RotationDegrees, UiText.Get("MainWindowMessage_RotateObjectTitle")) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                DrawingObjectActionPlanner.RotateObjectCommandTitle,
                sheetId =>
                {
                    var groupedTarget = GetTargetTransformDrawingObject(sheetId, target.Kind);
                    return DrawingObjectFormatCommandPolicy.BuildRotationCommand(
                        sheetId,
                        target.Kind,
                        groupedTarget?.Id ?? Guid.Empty,
                        new FormatPicturePlanner.RotationResult(dialog.Result.Degrees));
                }))
            return;

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
    }

    private void SetSelectedDrawingObjectFill()
    {
        var target = GetTargetDrawingObject(_currentSheetId);
        if (target is null)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_NoDrawingObjectOnSheet"),
                UiText.Get("MainWindowMessage_ObjectFillTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var title = UiText.Get("MainWindowMessage_ObjectFillTitle");
        var initial = ResolveDrawingObjectFillColor(target);
        if (!TryShowColorPicker(title, initial, allowNoColor: true, out var selectedColor, UiText.Get("FormatCells_NoFill")))
            return;

        RememberCurrentShapeFill(target.Kind, selectedColor);
        var hasFill = selectedColor is not null;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                DrawingObjectActionPlanner.FillCommandTitle(hasFill),
                sheetId =>
                {
                    var groupedTarget = GetTargetDrawingObject(sheetId, target.Kind);
                    return DrawingObjectCommandPlanner.BuildFillColorCommand(
                        sheetId,
                        target.Kind,
                        groupedTarget?.Id ?? Guid.Empty,
                        selectedColor);
                }))
            return;

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
    }

    private void SetSelectedDrawingObjectColor(bool isFill)
    {
        var target = GetTargetDrawingObject(_currentSheetId);
        if (target is null)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_NoDrawingObjectOnSheet"),
                UiText.Get(isFill ? "MainWindowMessage_ObjectFillTitle" : "MainWindowMessage_ObjectOutlineTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var initial = isFill
            ? ResolveDrawingObjectFillColor(target)
            : ResolveDrawingObjectOutlineColor(target);
        var title = UiText.Get(isFill ? "MainWindowMessage_ObjectFillTitle" : "MainWindowMessage_ObjectOutlineTitle");
        if (!TryShowColorPicker(title, initial, allowNoColor: false, out var selectedColor)
            || selectedColor is not { } color)
            return;

        RememberCurrentShapeColor(target.Kind, isFill, color);

        if (!TryExecuteRepeatableGroupedSheetCommand(
                isFill
                    ? DrawingObjectActionPlanner.ObjectFillCommandTitle
                    : DrawingObjectActionPlanner.ObjectOutlineCommandTitle,
                sheetId =>
                {
                    var groupedTarget = GetTargetDrawingObject(sheetId, target.Kind);
                    return isFill
                        ? DrawingObjectCommandPlanner.BuildFillColorCommand(
                            sheetId,
                            target.Kind,
                            groupedTarget?.Id ?? Guid.Empty,
                            color)
                        : DrawingObjectCommandPlanner.BuildOutlineColorCommand(
                            sheetId,
                            target.Kind,
                            groupedTarget?.Id ?? Guid.Empty,
                            color);
                }))
            return;

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
    }

    private CellColor? ResolveCurrentShapeFillColor() =>
        _currentShapeHasFill
            ? _currentShapeFillColor ?? DrawingShapeModel.ResolveDefaultFillColor(_workbook.Theme)
            : null;

    private bool ResolveCurrentShapeHasFill() => _currentShapeHasFill;

    private CellColor ResolveCurrentShapeOutlineColor() =>
        _currentShapeOutlineColor ?? DrawingShapeModel.ResolveDefaultOutlineColor(_workbook.Theme);

    private void RememberCurrentShapeColor(DrawingObjectTargetKind kind, bool isFill, CellColor color)
    {
        if (kind != DrawingObjectTargetKind.Shape)
            return;

        if (isFill)
            RememberCurrentShapeFill(kind, color);
        else
            _currentShapeOutlineColor = color;
    }

    private void RememberCurrentShapeFill(DrawingObjectTargetKind kind, CellColor? color)
    {
        if (kind != DrawingObjectTargetKind.Shape)
            return;

        _currentShapeHasFill = color is not null;
        _currentShapeFillColor = color;
    }

    private CellColor? ResolveDrawingObjectFillColor(DrawingObjectTarget target) =>
        DrawingObjectFormatCommandPolicy.ResolveFillColor(target, _workbook.Theme);

    private CellColor ResolveDrawingObjectOutlineColor(DrawingObjectTarget target) =>
        DrawingObjectFormatCommandPolicy.ResolveOutlineColor(target, _workbook.Theme);

    private void SetSelectedDrawingShapeGradient()
    {
        var shape = GetTargetDrawingShape(_currentSheetId);
        if (shape is null)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_NoDrawingShapesOnSheet"),
                UiText.Get("MainWindowMessage_ShapeGradientTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var startColor = shape.FillThemeColor?.Resolve(_workbook.Theme)
            ?? shape.FillColor
            ?? DrawingShapeModel.ResolveDefaultFillColor(_workbook.Theme);
        var endColor = shape.GradientFillEndColor ?? ShapeGradientPlanner.DefaultEndColor;
        var dialog = new ShapeGradientDialog(startColor, endColor, shape.GetEffectiveGradientFillDirection()) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                DrawingObjectActionPlanner.ShapeGradientCommandTitle,
                sheetId => ShapeGradientPlanner.BuildCommand(
                    sheetId,
                    GetTargetDrawingShape(sheetId)?.Id ?? Guid.Empty,
                    dialog.Result.StartColor,
                    dialog.Result.EndColor,
                    dialog.Result.Direction)))
            return;

        SetActiveCell(shape.Anchor);
        EnsureCellVisible(shape.Anchor);
        UpdateViewport();
    }

    private void ShapeEffectPresetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DrawingShapeEffectPreset taggedPreset })
        {
            SetSelectedDrawingShapeEffect(taggedPreset);
            return;
        }

        if (sender is DependencyObject element
            && RibbonMetadata.TryGetCommandName(element, out var commandId)
            && DrawingObjectContextualRibbonPlanner.TryResolveShapeEffectPreset(commandId, out var preset))
        {
            SetSelectedDrawingShapeEffect(preset);
        }
    }

    private void SetSelectedDrawingShapeEffect(DrawingShapeEffectPreset preset)
    {
        var shape = GetTargetDrawingShape(_currentSheetId);
        if (shape is null)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_NoDrawingShapeOnSheet"),
                UiText.Get("MainWindowMessage_ShapeEffectsTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var normalizedPreset = ShapeEffectsPlanner.NormalizePreset(preset);
        if (normalizedPreset != preset)
            return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                DrawingObjectActionPlanner.ShapeEffectsCommandTitle,
                sheetId => ShapeEffectsPlanner.BuildCommand(
                    sheetId,
                    GetTargetDrawingShape(sheetId)?.Id ?? Guid.Empty,
                    normalizedPreset)))
            return;

        SetActiveCell(shape.Anchor);
        EnsureCellVisible(shape.Anchor);
        UpdateViewport();
    }

    private void ShowSelectionPaneDialog()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var items = SelectionPaneDialog.BuildItems(sheet);
        if (items.Count == 0)
        {
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_NoObjectsOnSheet"),
                UiText.Get("MainWindowMessage_SelectionPaneTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SelectionPaneDialog(items) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplySelectionPaneChanges(dialog.Result);
    }

    private void ApplySelectionPaneChanges(SelectionPaneDialogResult result)
    {
        if (!SelectionPaneGroupedCommandPlanner.HasChanges(result))
            return;

        if (TryExecuteGroupedSheetCommand(
                "Selection Pane",
                sheetId => SelectionPaneGroupedCommandPlanner.CreateCommand(_workbook, _currentSheetId, sheetId, result)))
            UpdateViewport();
    }

    private DrawingShapeModel? GetTargetDrawingShape(SheetId sheetId)
    {
        var sheet = _workbook.GetSheet(sheetId);
        return DrawingTargetResolver.GetTargetDrawingShape(sheet, SheetGrid.SelectedRange?.Start);
    }

    private DrawingObjectTarget? GetTargetDrawingObject(
        SheetId sheetId,
        DrawingObjectTargetKind? preferredKind = null)
    {
        return GetTargetDrawingObject(sheetId, preferredKind, includePictures: false);
    }

    private DrawingObjectTarget? GetTargetTransformDrawingObject(
        SheetId sheetId,
        DrawingObjectTargetKind? preferredKind = null)
    {
        return GetTargetDrawingObject(sheetId, preferredKind, includePictures: true);
    }

    private DrawingObjectTarget? GetTargetDrawingObject(
        SheetId sheetId,
        DrawingObjectTargetKind? preferredKind,
        bool includePictures)
    {
        var sheet = _workbook.GetSheet(sheetId);
        var selectedKind = preferredKind is { } kind
            ? DrawingObjectKindMapper.ToSelectionPaneObjectKind(kind)
            : GetSelectedDrawingObjectSelectionKind();
        var selectedObjectId = selectedKind is null ? Guid.Empty : SheetGrid.SelectedObjectId;
        return DrawingTargetResolver.GetTargetDrawingObject(
            sheet,
            SheetGrid.SelectedRange?.Start,
            selectedKind,
            selectedObjectId,
            includePictures);
    }

    private SelectionPaneObjectKind? GetSelectedDrawingObjectSelectionKind() =>
        SheetGrid.SelectedObjectKind switch
        {
            FreeX.App.UI.ObjectKind.Picture => SelectionPaneObjectKind.Picture,
            FreeX.App.UI.ObjectKind.Shape => SelectionPaneObjectKind.Shape,
            FreeX.App.UI.ObjectKind.TextBox => SelectionPaneObjectKind.TextBox,
            _ => null
        };

    private DrawingObjectTargetKind? GetSelectedDrawingObjectTargetKind() =>
        GetSelectedDrawingObjectSelectionKind() is { } kind
            ? DrawingObjectKindMapper.ToDrawingObjectTargetKind(kind)
            : null;

    private DrawingObjectZOrderTarget? GetTargetDrawingZOrderObject(
        SheetId sheetId,
        SelectionPaneObjectKind? preferredKind = null)
    {
        var sheet = _workbook.GetSheet(sheetId);
        return DrawingTargetResolver.GetTargetDrawingZOrderObject(sheet, SheetGrid.SelectedRange?.Start, preferredKind);
    }

    private void OnObjectMoved(Guid id, FreeX.App.UI.ObjectKind kind, Core.Model.CellAddress newAnchor)
    {
        var anchor = new Core.Model.CellAddress(_currentSheetId, newAnchor.Row, newAnchor.Col);
        var targetKind = ToDrawingObjectTargetKind(kind);
        if (targetKind is null) return;
        var cmd = DrawingObjectCommandPlanner.BuildMoveCommand(_currentSheetId, targetKind.Value, id, anchor);
        TryExecuteCommand(cmd, DrawingObjectActionPlanner.MoveObjectCommandTitle);
        UpdateViewport();
    }

    private void OnChartBoundsChanged(Guid id, double left, double top, double width, double height)
    {
        if (!TryExecuteCommand(
                ChartCommandWorkflowPlanner.BuildBoundsCommand(
                    _currentSheetId,
                    id,
                    left,
                    top,
                    width,
                    height),
                "Chart Bounds"))
            return;

        SheetGrid.SelectedObjectId = id;
        SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.Chart;
        UpdateViewport();
    }

    private void OnObjectResized(
        Guid id,
        FreeX.App.UI.ObjectKind kind,
        double width,
        double height,
        bool flipHorizontal,
        bool flipVertical)
    {
        var targetKind = ToDrawingObjectTargetKind(kind);
        if (targetKind is null) return;
        var cmd = DrawingObjectCommandPlanner.BuildResizeCommand(
            _currentSheetId,
            targetKind.Value,
            id,
            width,
            height,
            flipHorizontal,
            flipVertical);
        TryExecuteCommand(cmd, DrawingObjectActionPlanner.ResizeObjectCommandTitle);
        UpdateViewport();
    }

    // Resizing from a handle that moves the top-left corner (N/W/NW/NE/SW) changes both the
    // anchor cell and the size; commit them together so the operation round-trips as one undo step.
    private void OnObjectResizedWithAnchor(
        Guid id,
        FreeX.App.UI.ObjectKind kind,
        Core.Model.CellAddress newAnchor,
        double width,
        double height,
        bool flipHorizontal,
        bool flipVertical)
    {
        var anchor = new Core.Model.CellAddress(_currentSheetId, newAnchor.Row, newAnchor.Col);
        var targetKind = ToDrawingObjectTargetKind(kind);
        if (targetKind is null) return;
        TryExecuteCommand(
            DrawingObjectCommandPlanner.BuildResizeWithAnchorCommand(
                _currentSheetId,
                targetKind.Value,
                id,
                anchor,
                width,
                height,
                flipHorizontal,
                flipVertical),
            DrawingObjectActionPlanner.ResizeObjectCommandTitle);
        UpdateViewport();
    }

    private void OnObjectRotated(Guid id, FreeX.App.UI.ObjectKind kind, double degrees)
    {
        var targetKind = ToDrawingObjectTargetKind(kind);
        if (targetKind is null) return;
        TryExecuteCommand(
            DrawingObjectCommandPlanner.BuildRotateCommand(_currentSheetId, targetKind.Value, id, degrees),
            DrawingObjectActionPlanner.RotateObjectCommandTitle);
        UpdateViewport();
    }

    private static DrawingObjectTargetKind? ToDrawingObjectTargetKind(FreeX.App.UI.ObjectKind kind) =>
        kind switch
        {
            FreeX.App.UI.ObjectKind.Picture => DrawingObjectTargetKind.Picture,
            FreeX.App.UI.ObjectKind.Shape => DrawingObjectTargetKind.Shape,
            FreeX.App.UI.ObjectKind.TextBox => DrawingObjectTargetKind.TextBox,
            _ => null
        };

    private void OnPictureCropped(Guid id, FreeX.App.UI.PictureCropRatios crop)
    {
        if (!TryExecuteCommand(
                PictureCropDialogPlanner.BuildCommand(
                    _currentSheetId,
                    id,
                    crop.Left,
                    crop.Top,
                    crop.Right,
                    crop.Bottom),
                DrawingObjectActionPlanner.CropPictureCommandTitle))
        {
            return;
        }

        SheetGrid.SelectedObjectId = id;
        SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.Picture;
        SheetGrid.IsPictureCropMode = true;
        UpdateViewport();
    }

}
