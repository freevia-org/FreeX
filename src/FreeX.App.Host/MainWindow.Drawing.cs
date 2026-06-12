using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

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

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = UiText.Get("MainWindowDialog_InsertPictureTitle"),
            Filter = UiText.Get("MainWindowDialog_ImageFilesFilter"),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        byte[] bytes;
        try
        {
            bytes = await System.IO.File.ReadAllBytesAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowOwnedMessage(
                UiText.Format("MainWindowMessage_InsertPictureReadFailed", ex.Message),
                UiText.Get("MainWindowMessage_InsertPictureTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var contentType = DrawingInputParser.GetImageContentType(dialog.FileName);
        InsertPictureCommand? currentSheetCommand = null;
        if (!TryExecuteGroupedSheetCommand(
                "Insert Picture",
                sheetId =>
                {
                    var command = InsertObjectPlacementPlanner.CreateInsertPictureCommand(
                        sheetId,
                        new CellAddress(sheetId, range.Start.Row, range.Start.Col),
                        bytes,
                        contentType);
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
        if (picture is null)
            return new FailedWorkbookCommand(UiText.Get("MainWindowMessage_PictureWasNotFound"));

        var commands = new List<IWorkbookCommand>
        {
            new ResizePictureCommand(sheetId, picture.Id, result.Width, result.Height),
            new RotatePictureCommand(sheetId, picture.Id, result.RotationDegrees),
            new SetPictureLockAspectRatioCommand(sheetId, picture.Id, result.LockAspectRatio),
            new SetPictureAltTextCommand(sheetId, picture.Id, result.AltText)
        };
        if (picture.Kind == PictureKind.Image)
        {
            commands.Add(new SetPictureCropCommand(
                sheetId,
                picture.Id,
                result.CropLeft,
                result.CropTop,
                result.CropRight,
                result.CropBottom));
        }

        return new CompositeWorkbookCommand("Format Picture", commands);
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
                sheetId => new RotatePictureCommand(
                    sheetId,
                    GetTargetPicture(sheetId)?.Id ?? Guid.Empty,
                    dialog.Result.Degrees)))
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

        var dialog = new PictureCropDialog(picture) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Crop Picture",
                sheetId => new SetPictureCropCommand(
                    sheetId,
                    GetTargetPicture(sheetId)?.Id ?? Guid.Empty,
                    dialog.Result.Left,
                    dialog.Result.Top,
                    dialog.Result.Right,
                    dialog.Result.Bottom)))
            return;

        UpdateViewport();
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
                sheetId => new SetPictureCropCommand(
                    sheetId,
                    GetTargetPicture(sheetId)?.Id ?? Guid.Empty,
                    0, 0, 0, 0)))
            return;

        UpdateViewport();
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
        var dialog = new TextEntryDialog(
            UiText.Get("MainWindowDialog_InsertTextBoxTitle"),
            UiText.Get("MainWindowDialog_TextEntryLabel"),
            "") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        AddTextBoxCommand? currentSheetCommand = null;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Insert Text Box",
                sheetId =>
                {
                    var currentAnchor = SheetGrid.SelectedRange?.Start ?? anchor;
                    var command = new AddTextBoxCommand(sheetId, new CellAddress(sheetId, currentAnchor.Row, currentAnchor.Col), dialog.Result.Text);
                    if (sheetId == _currentSheetId)
                        currentSheetCommand = command;
                    return command;
                }))
            return;

        if (currentSheetCommand is not null)
            SelectInsertedDrawingObject(currentSheetCommand.TextBoxId, FreeX.App.UI.ObjectKind.TextBox, anchor);
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
                "Insert Shape",
                sheetId =>
                {
                    var currentAnchor = SheetGrid.SelectedRange?.Start ?? anchor;
                    var command = new AddDrawingShapeCommand(
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

        var title = forward ? "Bring Forward" : "Send Backward";
        if (!TryExecuteRepeatableGroupedSheetCommand(
                title,
                sheetId =>
                {
                    var target = GetTargetDrawingZOrderObject(sheetId, currentTarget.Kind);
                    return new MoveSelectionPaneObjectCommand(
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
                "Object Size",
                sheetId =>
                {
                    var groupedTarget = GetTargetTransformDrawingObject(sheetId, target.Kind);
                    return target.Kind switch
                    {
                        DrawingObjectTargetKind.Picture => new ResizePictureCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, dialog.Result.Width, dialog.Result.Height),
                        DrawingObjectTargetKind.Shape => new ResizeDrawingShapeCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, dialog.Result.Width, dialog.Result.Height),
                        _ => new ResizeTextBoxCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, dialog.Result.Width, dialog.Result.Height)
                    };
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
                "Rotate Object",
                sheetId =>
                {
                    var groupedTarget = GetTargetTransformDrawingObject(sheetId, target.Kind);
                    return target.Kind switch
                    {
                        DrawingObjectTargetKind.Picture => new RotatePictureCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, dialog.Result.Degrees),
                        DrawingObjectTargetKind.Shape => new RotateDrawingShapeCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, dialog.Result.Degrees),
                        _ => new RotateTextBoxCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, dialog.Result.Degrees)
                    };
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
                hasFill ? "Object Fill" : "Object No Fill",
                sheetId =>
                {
                    var groupedTarget = GetTargetDrawingObject(sheetId, target.Kind);
                    if (target.Kind == DrawingObjectTargetKind.Shape)
                    {
                        return new SetDrawingShapeColorsCommand(
                            sheetId,
                            groupedTarget?.Id ?? Guid.Empty,
                            selectedColor,
                            null,
                            updateFill: true,
                            updateOutline: false,
                            hasFill: hasFill);
                    }

                    return new SetTextBoxColorsCommand(
                        sheetId,
                        groupedTarget?.Id ?? Guid.Empty,
                        selectedColor,
                        null,
                        updateFill: true,
                        updateOutline: false,
                        hasFill: hasFill);
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
                isFill ? "Object Fill" : "Object Outline",
                sheetId =>
                {
                    var groupedTarget = GetTargetDrawingObject(sheetId, target.Kind);
                    if (target.Kind == DrawingObjectTargetKind.Shape)
                    {
                        return new SetDrawingShapeColorsCommand(
                            sheetId,
                            groupedTarget?.Id ?? Guid.Empty,
                            isFill ? color : null,
                            isFill ? null : color,
                            updateFill: isFill,
                            updateOutline: !isFill);
                    }

                    return new SetTextBoxColorsCommand(
                        sheetId,
                        groupedTarget?.Id ?? Guid.Empty,
                        isFill ? color : groupedTarget?.FillColor,
                        isFill ? groupedTarget?.OutlineColor : color,
                        updateFill: isFill,
                        updateOutline: !isFill);
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
        !target.HasFill
            ? null
            : target.Kind switch
            {
                DrawingObjectTargetKind.Shape =>
                    target.FillThemeColor?.Resolve(_workbook.Theme) ??
                    target.FillColor ??
                    DrawingShapeModel.ResolveDefaultFillColor(_workbook.Theme),
                DrawingObjectTargetKind.TextBox =>
                    target.FillThemeColor?.Resolve(_workbook.Theme) ??
                    target.FillColor ??
                    CellColor.White,
                _ => CellColor.White
            };

    private CellColor ResolveDrawingObjectOutlineColor(DrawingObjectTarget target) =>
        target.Kind switch
        {
            DrawingObjectTargetKind.Shape =>
                target.OutlineThemeColor?.Resolve(_workbook.Theme) ??
                target.OutlineColor ??
                DrawingShapeModel.ResolveDefaultOutlineColor(_workbook.Theme),
            DrawingObjectTargetKind.TextBox =>
                target.OutlineThemeColor?.Resolve(_workbook.Theme) ??
                target.OutlineColor ??
                new CellColor(89, 89, 89),
            _ => CellColor.Black
        };

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
        var endColor = shape.GradientFillEndColor ?? ShapeGradientDialogPlanner.DefaultEndColor;
        var dialog = new ShapeGradientDialog(startColor, endColor, shape.GetEffectiveGradientFillDirection()) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Shape Gradient",
                sheetId => new SetDrawingShapeGradientCommand(
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

    private void ShapeEffectsMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        var currentPreset = GetTargetDrawingShape(_currentSheetId)?.GetEffectiveEffectPreset()
            ?? DrawingShapeEffectPreset.None;
        currentPreset = ShapeEffectsDialogPlanner.NormalizePreset(currentPreset);

        foreach (var item in menu.Items)
        {
            if (item is MenuItem { Tag: DrawingShapeEffectPreset preset } menuItem)
                menuItem.IsChecked = preset == currentPreset;
        }
    }

    private void ShapeEffectPresetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DrawingShapeEffectPreset preset })
            SetSelectedDrawingShapeEffect(preset);
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

        if (!Enum.IsDefined(preset))
            return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Shape Effects",
                sheetId => new SetDrawingShapeEffectCommand(
                    sheetId,
                    GetTargetDrawingShape(sheetId)?.Id ?? Guid.Empty,
                    preset)))
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

        var items = SelectionPanePlanner.BuildItems(sheet);
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
        var selectedKind = preferredKind ?? GetSelectedDrawingObjectTargetKind();
        var selectedObjectId = selectedKind is null ? Guid.Empty : SheetGrid.SelectedObjectId;
        return DrawingTargetResolver.GetTargetDrawingObject(
            sheet,
            SheetGrid.SelectedRange?.Start,
            selectedKind,
            selectedObjectId,
            includePictures);
    }

    private DrawingObjectTargetKind? GetSelectedDrawingObjectTargetKind() =>
        SheetGrid.SelectedObjectKind switch
        {
            FreeX.App.UI.ObjectKind.Picture => DrawingObjectTargetKind.Picture,
            FreeX.App.UI.ObjectKind.Shape => DrawingObjectTargetKind.Shape,
            FreeX.App.UI.ObjectKind.TextBox => DrawingObjectTargetKind.TextBox,
            _ => null
        };

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
        IWorkbookCommand cmd = kind switch
        {
            FreeX.App.UI.ObjectKind.Picture  => new RepositionPictureCommand(_currentSheetId, id, anchor),
            FreeX.App.UI.ObjectKind.Shape    => new RepositionShapeCommand(_currentSheetId, id, anchor),
            FreeX.App.UI.ObjectKind.TextBox  => new RepositionTextBoxCommand(_currentSheetId, id, anchor),
            _ => null!
        };
        if (cmd is null) return;
        TryExecuteCommand(cmd, "Move Object");
        UpdateViewport();
    }

    private void OnChartBoundsChanged(Guid id, double left, double top, double width, double height)
    {
        if (!TryExecuteCommand(new SetChartBoundsCommand(_currentSheetId, id, left, top, width, height), "Chart Bounds"))
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
        IWorkbookCommand cmd = kind switch
        {
            FreeX.App.UI.ObjectKind.Picture  => new ResizePictureCommand(_currentSheetId, id, width, height, flipHorizontal, flipVertical),
            FreeX.App.UI.ObjectKind.Shape    => new ResizeDrawingShapeCommand(_currentSheetId, id, width, height, flipHorizontal, flipVertical),
            FreeX.App.UI.ObjectKind.TextBox  => new ResizeTextBoxCommand(_currentSheetId, id, width, height, flipHorizontal, flipVertical),
            _ => null!
        };
        if (cmd is null) return;
        TryExecuteCommand(cmd, "Resize Object");
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
        IReadOnlyList<IWorkbookCommand>? commands = kind switch
        {
            FreeX.App.UI.ObjectKind.Picture =>
            [
                new RepositionPictureCommand(_currentSheetId, id, anchor),
                new ResizePictureCommand(_currentSheetId, id, width, height, flipHorizontal, flipVertical)
            ],
            FreeX.App.UI.ObjectKind.Shape =>
            [
                new RepositionShapeCommand(_currentSheetId, id, anchor),
                new ResizeDrawingShapeCommand(_currentSheetId, id, width, height, flipHorizontal, flipVertical)
            ],
            FreeX.App.UI.ObjectKind.TextBox =>
            [
                new RepositionTextBoxCommand(_currentSheetId, id, anchor),
                new ResizeTextBoxCommand(_currentSheetId, id, width, height, flipHorizontal, flipVertical)
            ],
            _ => null
        };
        if (commands is null) return;
        TryExecuteCommand(
            new CompositeWorkbookCommand("Resize Object", commands),
            "Resize Object");
        UpdateViewport();
    }

    private void OnObjectRotated(Guid id, FreeX.App.UI.ObjectKind kind, double degrees)
    {
        var rotationKind = kind switch
        {
            FreeX.App.UI.ObjectKind.Picture => SelectionPaneObjectKind.Picture,
            FreeX.App.UI.ObjectKind.Shape   => SelectionPaneObjectKind.Shape,
            FreeX.App.UI.ObjectKind.TextBox => SelectionPaneObjectKind.TextBox,
            _ => (SelectionPaneObjectKind?)null
        };
        if (rotationKind is not { } resolvedKind) return;
        TryExecuteCommand(
            new SetDrawingObjectRotationCommand(_currentSheetId, resolvedKind, id, degrees),
            "Rotate Object");
        UpdateViewport();
    }

}
