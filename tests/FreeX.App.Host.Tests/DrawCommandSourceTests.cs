using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DrawCommandSourceTests
{
    [Fact]
    public void DrawTab_DoesNotSurfaceOutOfScopeInkCommands()
    {
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();

        xaml.Should().NotContain("DrawToolsGroup");
        xaml.Should().NotContain("DrawPensGroup");
        xaml.Should().NotContain("DrawConvertGroup");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Draw with Touch\"");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Eraser\"");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Lasso Select\"");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Pen\"");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Pencil\"");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Highlighter\"");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Add Pen\"");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Ink to Shape\"");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Ink to Math\"");
    }

    [Theory]
    [InlineData("Pictures", "Pictures", "IP", "PicturesBtn_Click")]
    [InlineData("Shapes", "Shapes", "SH", "ShapesBtn_Click")]
    public void DrawIllustrationsCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = LocalizedXamlTestSupport.ReadMainWindowXaml()
            .ExtractButtonElementByInvariantCommandName(title, $"Click=\"{handler}\"");

        button.ShouldContainLocalizedAttribute("Content", content);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Bring Forward", "Bring Forward", "BF", "BringForwardBtn_Click")]
    [InlineData("Send Backward", "Send Backward", "SB", "SendBackwardBtn_Click")]
    [InlineData("Selection Pane", "Selection Pane", "SP", "SelectionPaneBtn_Click")]
    [InlineData("Rotate Object", "Rotate", "RO", "ObjectRotateBtn_Click")]
    [InlineData("Object Size", "Size", "SZ", "ObjectSizeBtn_Click")]
    [InlineData("Shape Fill", "Fill", "OF", "ObjectFillBtn_Click")]
    [InlineData("Object Outline", "Outline", "OO", "ObjectOutlineBtn_Click")]
    [InlineData("Crop Picture", "Crop", "C", "PictureCropBtn_Click")]
    [InlineData("Shape Gradient", "Gradient", "G", "ObjectGradientBtn_Click")]
    [InlineData("Shape Effects", "Effects", "FX", "ObjectEffectsBtn_Click")]
    public void DrawArrangeAndFormatCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = LocalizedXamlTestSupport.ReadMainWindowXaml()
            .ExtractElementByInvariantCommandName("Button", title);

        button.ShouldContainLocalizedAttribute("Content", content);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Bring Forward", "DrawBringForwardButton", "Move the selected shape one step closer to the front.")]
    [InlineData("Send Backward", "DrawSendBackwardButton", "Move the selected shape one step closer to the back.")]
    [InlineData("Selection Pane", "DrawSelectionPaneButton", "List sheet objects and control visibility or stacking order.")]
    [InlineData("Rotate Object", "DrawRotateObjectButton", "Rotate the selected or most recent drawing object.")]
    [InlineData("Object Size", "DrawObjectSizeButton", "Resize the selected or most recent drawing object.")]
    [InlineData("Shape Fill", "DrawShapeFillButton", "Change the fill color of the selected drawing object.")]
    [InlineData("Object Outline", "DrawObjectOutlineButton", "Change the outline color of the selected drawing object.")]
    [InlineData("Crop Picture", "DrawCropPictureButton", "Open crop controls for the selected or most recent inserted picture.")]
    [InlineData("Shape Gradient", "DrawShapeGradientButton", "Open gradient fill controls for the selected shape.")]
    [InlineData("Shape Effects", "DrawShapeEffectsButton", "Choose no effect, shadow, inner shadow, reflection, glow, soft edges, bevel, or 3-D rotation for the selected shape.")]
    public void DrawArrangeAndFormatCommands_ExposeStableAutomationMetadata(
        string title,
        string automationId,
        string helpText)
    {
        var button = LocalizedXamlTestSupport.ReadMainWindowXaml()
            .ExtractElementByInvariantCommandName("Button", title);

        button.ShouldContainLocalizedAttribute("AutomationProperties.Name", title);
        button.Should().Contain($"AutomationProperties.AutomationId=\"{automationId}\"");
        button.ShouldContainLocalizedAttribute("AutomationProperties.HelpText", helpText);
    }

    [Theory]
    [InlineData("Crop...", "C", "PictureCropDialogMenuItem_Click")]
    [InlineData("Reset Crop", "R", "PictureResetCropMenuItem_Click")]
    public void DrawCropMenu_ExposesExpectedHeadersKeyTipsAndHandlers(
        string header,
        string keyTip,
        string handler)
    {
        var item = LocalizedXamlTestSupport.ReadMainWindowXaml()
            .ExtractElementByLocalizedAttributeValue("MenuItem", "Header", header, $"Click=\"{handler}\"");

        item.ShouldContainLocalizedAttribute("Header", header);
        item.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        item.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void DrawHandlers_RouteThroughExpectedDrawingCommandsDialogsAndTargetResolution()
    {
        var insertSource = DialogSourceTestSupport.ReadHostSources("MainWindow.InsertCommands.cs");
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Drawing.cs");

        insertSource.Should().Contain("private void PicturesBtn_Click(object sender, RoutedEventArgs e) => InsertPictureBtn_Click(sender, e);");
        insertSource.Should().Contain("private void ShapesBtn_Click(object sender, RoutedEventArgs e) => DrawRectBtn_Click(sender, e);");
        source.Should().Contain("private void InsertPictureBtn_Click(object sender, RoutedEventArgs e)");
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
        source.Should().Contain("private void ObjectFillBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingObjectColor(isFill: true);");
        source.Should().Contain("private void ObjectOutlineBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingObjectColor(isFill: false);");
        source.Should().Contain("private void ObjectGradientBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingShapeGradient();");
        source.Should().Contain("private void ObjectEffectsBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingShapeEffect();");
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
        source.Should().Contain("new ShapeEffectsDialog(shape.GetEffectiveEffectPreset())");
        source.Should().Contain("new SetDrawingShapeEffectCommand(");
        source.Should().Contain("dialog.Result.Preset");
        source.Should().NotContain("shape.GetEffectiveEffectPreset() == DrawingShapeEffectPreset.None");
        source.Should().Contain("new PictureCropDialog(picture)");
        source.Should().Contain("private void PictureCropDialogMenuItem_Click(object sender, RoutedEventArgs e) =>");
        source.Should().Contain("new SetPictureCropCommand(");
        source.Should().Contain("DrawingTargetResolver.GetTargetDrawingObject(");
        source.Should().Contain("selectedObjectId");
    }

    [Fact]
    public void PictureCropDialog_RoutesThroughUndoableCropCommandWithoutGridCropHandles()
    {
        var windowSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var drawingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Drawing.cs");

        windowSource.Should().NotContain("SheetGrid.PictureCropped += OnPictureCropped;");
        drawingSource.Should().NotContain("private void OnPictureCropped");
        drawingSource.Should().Contain("new PictureCropDialog(picture)");
        drawingSource.Should().Contain("new SetPictureCropCommand(");
        drawingSource.Should().Contain("TryExecuteCommand(");
    }

}
