using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DrawCommandSourceTests
{

    [Fact]
    public void DrawHandlers_RouteThroughExpectedDrawingCommandsDialogsAndTargetResolution()
    {
        var insertSource = DialogSourceTestSupport.ReadHostSources("MainWindow.InsertCommands.cs");
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Drawing.cs");

        insertSource.Should().Contain("private void PicturesBtn_Click(object sender, RoutedEventArgs e) => InsertPictureBtn_Click(sender, e);");
        insertSource.Should().Contain("private void ShapesBtn_Click(object sender, RoutedEventArgs e) => DrawRectBtn_Click(sender, e);");
        source.Should().Contain("private async void InsertPictureBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("InsertObjectPlacementPlanner.CreateInsertPictureCommand(");
        source.Should().Contain("DrawRectBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("InsertDrawingShape(DrawingShapeKind.Rectangle)");
        source.Should().Contain("DrawEllipseBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("InsertDrawingShape(DrawingShapeKind.Ellipse)");
        source.Should().Contain("DrawLineBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("InsertDrawingShape(DrawingShapeKind.Line)");
        DialogSourceTestSupport.ReadHostSources("MainWindow.ShapeGallery.cs")
            .Should()
            .Contain("ShapeGalleryMenuItem_Click")
            .And.Contain("InsertDrawingShape(kind)");
        source.Should().Contain("private void BringForwardBtn_Click(object sender, RoutedEventArgs e) => ReorderSelectedDrawingObject(forward: true);");
        source.Should().Contain("private void SendBackwardBtn_Click(object sender, RoutedEventArgs e) => ReorderSelectedDrawingObject(forward: false);");
        source.Should().Contain("private void SelectionPaneBtn_Click(object sender, RoutedEventArgs e) => ShowSelectionPaneDialog();");
        source.Should().Contain("private void ObjectRotateBtn_Click(object sender, RoutedEventArgs e) => RotateSelectedDrawingObject();");
        source.Should().Contain("private void ObjectSizeBtn_Click(object sender, RoutedEventArgs e) => ResizeSelectedDrawingObject();");
        source.Should().Contain("private void ObjectFillBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingObjectFill();");
        source.Should().Contain("private void ObjectOutlineBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingObjectColor(isFill: false);");
        source.Should().Contain("private void ObjectGradientBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingShapeGradient();");
        source.Should().Contain("private void ObjectEffectsBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("OpenRibbonContextMenu(button, menu);");
        source.Should().Contain("private void ShapeEffectsMenu_Opened(object sender, RoutedEventArgs e)");
        source.Should().Contain("menuItem.IsChecked = preset == currentPreset;");
        source.Should().Contain("private void ShapeEffectPresetMenuItem_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("SetSelectedDrawingShapeEffect(preset);");
        source.Should().Contain("new MoveSelectionPaneObjectCommand(");
        source.Should().Contain("var target = GetTargetDrawingZOrderObject(sheetId, currentTarget.Kind);");
        source.Should().Contain("private DrawingObjectTarget? GetTargetTransformDrawingObject(");
        source.Should().Contain("includePictures: true");
        source.Should().Contain("FreeX.App.UI.ObjectKind.Picture => DrawingObjectTargetKind.Picture");
        source.Should().Contain("DrawingObjectTargetKind.Picture => new ResizePictureCommand(");
        source.Should().Contain("DrawingObjectTargetKind.Picture => new RotatePictureCommand(");
        source.Should().Contain("new ObjectSizeDialog(target.Width, target.Height, UiText.Get(\"MainWindowMessage_ObjectSizeTitle\"))");
        source.Should().Contain("new RotationDialog(target.RotationDegrees, UiText.Get(\"MainWindowMessage_RotateObjectTitle\"))");
        source.Should().Contain("new SetDrawingShapeColorsCommand(");
        source.Should().Contain("new SetTextBoxColorsCommand(");
        source.Should().Contain("new ShapeGradientDialog");
        source.Should().Contain("new SetDrawingShapeGradientCommand(");
        source.Should().Contain("new SetDrawingShapeEffectCommand(");
        source.Should().NotContain("new ShapeEffectsDialog(shape.GetEffectiveEffectPreset())");
        source.Should().NotContain("dialog.Result.Preset");
        source.Should().NotContain("shape.GetEffectiveEffectPreset() == DrawingShapeEffectPreset.None");
        source.Should().Contain("EnterPictureCropMode(picture);");
        source.Should().Contain("private void PictureCropDialogMenuItem_Click(object sender, RoutedEventArgs e) =>");
        source.Should().Contain("new SetPictureCropCommand(");
        source.Should().Contain("DrawingTargetResolver.GetTargetDrawingObject(");
        source.Should().Contain("selectedObjectId");
    }

    [Fact]
    public void DrawShapeFormatting_RemembersCurrentShapeFillAndOutlineForFutureInsertions()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs", "MainWindow.Drawing.cs");

        source.Should().Contain("private CellColor? _currentShapeFillColor;");
        source.Should().Contain("private bool _currentShapeHasFill = true;");
        source.Should().Contain("private CellColor? _currentShapeOutlineColor;");
        source.Should().Contain("fillColor: ResolveCurrentShapeFillColor()");
        source.Should().Contain("hasFill: ResolveCurrentShapeHasFill()");
        source.Should().Contain("outlineColor: ResolveCurrentShapeOutlineColor()");
        source.Should().Contain("TryShowColorPicker(title, initial, allowNoColor: true, out var selectedColor, UiText.Get(\"FormatCells_NoFill\"))");
        source.Should().Contain("hasFill ? \"Object Fill\" : \"Object No Fill\"");
        source.Should().Contain("RememberCurrentShapeFill(target.Kind, selectedColor);");
        source.Should().Contain("RememberCurrentShapeColor(target.Kind, isFill, color);");
        source.Should().Contain("target.FillThemeColor?.Resolve(_workbook.Theme)");
        source.Should().Contain("target.OutlineThemeColor?.Resolve(_workbook.Theme)");
        source.Should().Contain("hasFill: hasFill");
        source.Should().Contain("updateFill: isFill");
        source.Should().Contain("updateOutline: !isFill");
    }

    [Fact]
    public void PictureCropMode_RoutesThroughUndoableCropCommandWithGridCropHandles()
    {
        var windowSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var drawingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Drawing.cs");

        windowSource.Should().Contain("SheetGrid.PictureCropped += OnPictureCropped;");
        drawingSource.Should().Contain("private void OnPictureCropped");
        drawingSource.Should().Contain("EnterPictureCropMode(picture);");
        drawingSource.Should().NotContain("new PictureCropDialog(picture)");
        drawingSource.Should().Contain("new SetPictureCropCommand(");
        drawingSource.Should().Contain("TryExecuteCommand(");
    }

}
