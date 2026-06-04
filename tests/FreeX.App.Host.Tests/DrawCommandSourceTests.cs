using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DrawCommandSourceTests
{
    [Fact]
    public void DrawTab_DoesNotSurfaceOutOfScopeInkCommands()
    {
        var xaml = ReadMainWindowXaml();

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
        var button = ExtractElementByTitle(ReadMainWindowXaml(), title, "Button");

        button.ShouldContainLocalizedAttribute("Content", content);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Crop...", "C", "PictureCropDialogMenuItem_Click")]
    [InlineData("Reset Crop", "R", "PictureResetCropMenuItem_Click")]
    public void DrawCropMenu_ExposesExpectedHeadersKeyTipsAndHandlers(
        string header,
        string keyTip,
        string handler)
    {
        var item = ExtractMenuItemElementByHeader(ReadMainWindowXaml(), header, handler);

        item.ShouldContainLocalizedAttribute("Header", header);
        item.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        item.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void DrawHandlers_RouteThroughExpectedDrawingCommandsDialogsAndTargetResolution()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Drawing.cs"));

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
        source.Should().Contain("DrawingTargetResolver.GetTargetDrawingObject(sheet, SheetGrid.SelectedRange?.Start, preferredKind)");
    }

    [Fact]
    public void InteractivePictureCropEvent_RoutesThroughUndoableCropCommand()
    {
        var windowSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml.cs"));
        var drawingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Drawing.cs"));

        windowSource.Should().Contain("SheetGrid.PictureCropped += OnPictureCropped;");
        drawingSource.Should().Contain("private void OnPictureCropped(Guid id, double left, double top, double right, double bottom)");
        drawingSource.Should().Contain("new SetPictureCropCommand(_currentSheetId, id, left, top, right, bottom)");
        drawingSource.Should().Contain("TryExecuteCommand(");
    }

    private static string ReadMainWindowXaml() =>
        LocalizedXamlTestSupport.ReadMainWindowXaml();

    private static string ExtractElementByTitle(string xaml, string title, string elementName)
    {
        var titleIndex = xaml.IndexOf($"local:RibbonMetadata.CommandName=\"{title}\"", StringComparison.Ordinal);
        titleIndex.Should().BeGreaterThanOrEqualTo(0, $"the {title} Draw command should be present");

        var start = xaml.LastIndexOf($"<{elementName}", titleIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {title} Draw command should be a {elementName}");

        var selfClosingEnd = xaml.IndexOf("/>", titleIndex, StringComparison.Ordinal);
        var closingEnd = xaml.IndexOf($"</{elementName}>", titleIndex, StringComparison.Ordinal);
        var end = closingEnd >= 0 && (selfClosingEnd < 0 || closingEnd < selfClosingEnd)
            ? closingEnd + elementName.Length + 3
            : selfClosingEnd + 2;

        end.Should().BeGreaterThan(titleIndex, $"the {title} Draw element should have a closing marker");
        return xaml[start..end];
    }

    private static string ExtractMenuItemElementByHeader(string xaml, string header, string handler)
        => xaml.ExtractElementByLocalizedAttributeValue("MenuItem", "Header", header, $"Click=\"{handler}\"");
}
