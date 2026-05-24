using System;
using System.Collections.Generic;
using System.Windows;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void TextBoxBtn_Click(object sender, RoutedEventArgs e)
    {
        InsertTextBox();
    }
    private void InsertPictureBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Insert Picture",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        byte[] bytes;
        try
        {
            bytes = System.IO.File.ReadAllBytes(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read picture file:\n{ex.Message}",
                "Insert Picture", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var contentType = DrawingInputParser.GetImageContentType(dialog.FileName);
        if (!TryExecuteGroupedSheetCommand(
                "Insert Picture",
                sheetId => new InsertPictureCommand(
                    sheetId,
                    new CellAddress(sheetId, range.Start.Row, range.Start.Col),
                    bytes,
                    contentType)))
            return;

        SetActiveCell(range.Start);
        UpdateViewport();
    }

    private void PictureSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        var picture = GetTargetPicture(_currentSheetId);
        if (picture is null)
        {
            MessageBox.Show("No picture found on this sheet.", "Picture Size", MessageBoxButton.OK, MessageBoxImage.Information);
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
            return new FailedWorkbookCommand("Picture was not found.");

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
            MessageBox.Show("No picture found on this sheet.", "Rotate Picture", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new RotationDialog(picture.RotationDegrees, "Rotate Picture") { Owner = this };
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
            MessageBox.Show("No picture found on this sheet.", "Crop Picture", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (picture.Kind != PictureKind.Image)
        {
            MessageBox.Show("Only inserted image pictures can be cropped.", "Crop Picture", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show("No picture found on this sheet.", "Reset Crop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (picture.Kind != PictureKind.Image)
        {
            MessageBox.Show("Only inserted image pictures can be cropped.", "Reset Crop", MessageBoxButton.OK, MessageBoxImage.Information);
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
        return DrawingTargetResolver.GetTargetPicture(sheet, SheetGrid.SelectedRange?.Start);
    }

    private void DrawRectBtn_Click(object sender, RoutedEventArgs e)    => InsertDrawingShape(DrawingShapeKind.Rectangle);
    private void DrawEllipseBtn_Click(object sender, RoutedEventArgs e) => InsertDrawingShape(DrawingShapeKind.Ellipse);
    private void DrawLineBtn_Click(object sender, RoutedEventArgs e)    => InsertDrawingShape(DrawingShapeKind.Line);
    private void DrawTextBtn_Click(object sender, RoutedEventArgs e)    => InsertTextBox();
    private void BringForwardBtn_Click(object sender, RoutedEventArgs e) => ReorderSelectedDrawingShape(forward: true);
    private void SendBackwardBtn_Click(object sender, RoutedEventArgs e) => ReorderSelectedDrawingShape(forward: false);
    private void SelectionPaneBtn_Click(object sender, RoutedEventArgs e) => ShowSelectionPaneDialog();
    private void ObjectSizeBtn_Click(object sender, RoutedEventArgs e) => ResizeSelectedDrawingObject();
    private void ObjectRotateBtn_Click(object sender, RoutedEventArgs e) => RotateSelectedDrawingObject();
    private void ObjectFillBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingObjectColor(isFill: true);
    private void ObjectOutlineBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingObjectColor(isFill: false);
    private void ObjectGradientBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingShapeGradient();
    private void ObjectEffectsBtn_Click(object sender, RoutedEventArgs e) => ToggleSelectedDrawingShapeEffect();

    // ── Page Layout tab ───────────────────────────────────────────────────────

    private void InsertTextBox()
    {
        var anchor = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
        var dialog = new TextEntryDialog("Insert Text Box", "Text:", "") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Insert Text Box",
                sheetId =>
                {
                    var currentAnchor = SheetGrid.SelectedRange?.Start ?? anchor;
                    return new AddTextBoxCommand(sheetId, new CellAddress(sheetId, currentAnchor.Row, currentAnchor.Col), dialog.Result.Text);
                }))
            return;

        SetActiveCell(anchor);
        EnsureCellVisible(anchor);
        UpdateViewport();
    }

    private void InsertDrawingShape(DrawingShapeKind kind)
    {
        var anchor = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Insert Shape",
                sheetId =>
                {
                    var currentAnchor = SheetGrid.SelectedRange?.Start ?? anchor;
                    return new AddDrawingShapeCommand(sheetId, new CellAddress(sheetId, currentAnchor.Row, currentAnchor.Col), kind);
                }))
            return;

        SetActiveCell(anchor);
        EnsureCellVisible(anchor);
        UpdateViewport();
    }

    private void ReorderSelectedDrawingShape(bool forward)
    {
        var currentShape = GetTargetDrawingShape(_currentSheetId);
        if (currentShape is null)
        {
            MessageBox.Show("No drawing shapes are available on this sheet.",
                "Draw", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var title = forward ? "Bring Forward" : "Send Backward";
        if (!TryExecuteRepeatableGroupedSheetCommand(
                title,
                sheetId =>
                {
                    var target = GetTargetDrawingShape(sheetId);
                    return forward
                        ? new BringDrawingShapeForwardCommand(sheetId, target?.Id ?? Guid.Empty)
                        : new SendDrawingShapeBackwardCommand(sheetId, target?.Id ?? Guid.Empty);
                }))
            return;

        SetActiveCell(currentShape.Anchor);
        EnsureCellVisible(currentShape.Anchor);
        UpdateViewport();
    }

    private void ResizeSelectedDrawingObject()
    {
        var target = GetTargetDrawingObject(_currentSheetId);
        if (target is null)
        {
            MessageBox.Show("No drawing object found on this sheet.", "Object Size", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ObjectSizeDialog(target.Width, target.Height, "Object Size") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Object Size",
                sheetId =>
                {
                    var groupedTarget = GetTargetDrawingObject(sheetId, target.Kind);
                    return target.Kind == DrawingObjectTargetKind.Shape
                        ? new ResizeDrawingShapeCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, dialog.Result.Width, dialog.Result.Height)
                        : new ResizeTextBoxCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, dialog.Result.Width, dialog.Result.Height);
                }))
            return;

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
    }

    private void RotateSelectedDrawingObject()
    {
        var target = GetTargetDrawingObject(_currentSheetId);
        if (target is null)
        {
            MessageBox.Show("No drawing object found on this sheet.", "Rotate Object", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new RotationDialog(target.RotationDegrees, "Rotate Object") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Rotate Object",
                sheetId =>
                {
                    var groupedTarget = GetTargetDrawingObject(sheetId, target.Kind);
                    return target.Kind == DrawingObjectTargetKind.Shape
                        ? new RotateDrawingShapeCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, dialog.Result.Degrees)
                        : new RotateTextBoxCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, dialog.Result.Degrees);
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
            MessageBox.Show("No drawing object found on this sheet.",
                isFill ? "Object Fill" : "Object Outline",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var initial = isFill ? target.FillColor : target.OutlineColor;
        if (!TryShowColorPicker(isFill ? "Object Fill" : "Object Outline", initial, allowNoColor: false, out var selectedColor)
            || selectedColor is not { } color)
            return;

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
                            isFill ? color : groupedTarget?.FillColor,
                            isFill ? groupedTarget?.OutlineColor : color);
                    }

                    return new SetTextBoxColorsCommand(
                        sheetId,
                        groupedTarget?.Id ?? Guid.Empty,
                        isFill ? color : groupedTarget?.FillColor,
                        isFill ? groupedTarget?.OutlineColor : color);
                }))
            return;

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
    }

    private void SetSelectedDrawingShapeGradient()
    {
        var shape = GetTargetDrawingShape(_currentSheetId);
        if (shape is null)
        {
            MessageBox.Show("No drawing shape found on this sheet.", "Shape Gradient", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ShapeGradientDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Shape Gradient",
                sheetId => new SetDrawingShapeGradientCommand(
                    sheetId,
                    GetTargetDrawingShape(sheetId)?.Id ?? Guid.Empty,
                    dialog.Result.StartColor,
                    dialog.Result.EndColor)))
            return;

        SetActiveCell(shape.Anchor);
        EnsureCellVisible(shape.Anchor);
        UpdateViewport();
    }

    private void ToggleSelectedDrawingShapeEffect()
    {
        var shape = GetTargetDrawingShape(_currentSheetId);
        if (shape is null)
        {
            MessageBox.Show("No drawing shape found on this sheet.", "Shape Effects", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var enableShadow = !shape.HasShadowEffect;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Shape Effects",
                sheetId => new SetDrawingShapeEffectCommand(sheetId, GetTargetDrawingShape(sheetId)?.Id ?? Guid.Empty, enableShadow)))
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
            MessageBox.Show("No objects are available on this sheet.", "Selection Pane", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SelectionPaneDialog(items) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplySelectionPaneChanges(dialog.Result);
    }

    private void ApplySelectionPaneChanges(SelectionPaneDialogResult result)
    {
        var commands = new List<IWorkbookCommand>();
        commands.AddRange(result.RenameChanges.Select(change =>
            new RenameSelectionPaneObjectCommand(_currentSheetId, change.Kind, change.Id, change.Name)));
        commands.AddRange(result.VisibilityChanges.Select(change =>
            new SetSelectionPaneObjectVisibilityCommand(_currentSheetId, change.Kind, change.Id, change.IsVisible)));
        commands.AddRange(result.MoveChanges.Select(change =>
            new MoveSelectionPaneObjectCommand(_currentSheetId, change.Kind, change.Id, change.Forward)));

        if (commands.Count == 0)
            return;

        if (TryExecuteCommand(new CompositeWorkbookCommand("Selection Pane", commands), "Selection Pane"))
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
        var sheet = _workbook.GetSheet(sheetId);
        return DrawingTargetResolver.GetTargetDrawingObject(sheet, SheetGrid.SelectedRange?.Start, preferredKind);
    }
}
