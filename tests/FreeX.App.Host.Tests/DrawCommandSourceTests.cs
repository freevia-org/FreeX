using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DrawCommandSourceTests
{
    [Theory]
    [InlineData("Draw with Touch", "Draw", "DT")]
    [InlineData("Eraser", "Eraser", "E")]
    [InlineData("Lasso Select", "Lasso Select", "LS")]
    [InlineData("Pen", "Pen", "P")]
    [InlineData("Pencil", "Pencil", "N")]
    [InlineData("Highlighter", "Highlighter", "H")]
    [InlineData("Add Pen", "Add Pen", "AP")]
    [InlineData("Ink to Shape", "Ink to Shape", "IS")]
    [InlineData("Ink to Math", "Ink to Math", "IM")]
    public void DrawInkCommands_RemainDisabledWithoutClickHandlers(
        string title,
        string content,
        string keyTip)
    {
        var button = ExtractElementByTitle(ReadMainWindowXaml(), title, "Button");

        button.ShouldContainLocalizedAttribute("Content", content);
        button.Should().Contain("IsEnabled=\"False\"");
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().NotContain("Click=");
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

        source.Should().Contain("private void BringForwardBtn_Click(object sender, RoutedEventArgs e) => ReorderSelectedDrawingShape(forward: true);");
        source.Should().Contain("private void SendBackwardBtn_Click(object sender, RoutedEventArgs e) => ReorderSelectedDrawingShape(forward: false);");
        source.Should().Contain("private void SelectionPaneBtn_Click(object sender, RoutedEventArgs e) => ShowSelectionPaneDialog();");
        source.Should().Contain("private void ObjectRotateBtn_Click(object sender, RoutedEventArgs e) => RotateSelectedDrawingObject();");
        source.Should().Contain("private void ObjectSizeBtn_Click(object sender, RoutedEventArgs e) => ResizeSelectedDrawingObject();");
        source.Should().Contain("private void ObjectFillBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingObjectColor(isFill: true);");
        source.Should().Contain("private void ObjectOutlineBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingObjectColor(isFill: false);");
        source.Should().Contain("private void ObjectGradientBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingShapeGradient();");
        source.Should().Contain("private void ObjectEffectsBtn_Click(object sender, RoutedEventArgs e) => ToggleSelectedDrawingShapeEffect();");
        source.Should().Contain("new BringDrawingShapeForwardCommand(sheetId, target?.Id ?? Guid.Empty)");
        source.Should().Contain("new SendDrawingShapeBackwardCommand(sheetId, target?.Id ?? Guid.Empty)");
        source.Should().Contain("new ObjectSizeDialog(target.Width, target.Height, UiText.Get(\"MainWindowMessage_ObjectSizeTitle\"))");
        source.Should().Contain("new RotationDialog(target.RotationDegrees, UiText.Get(\"MainWindowMessage_RotateObjectTitle\"))");
        source.Should().Contain("new SetDrawingShapeColorsCommand(");
        source.Should().Contain("new SetTextBoxColorsCommand(");
        source.Should().Contain("new ShapeGradientDialog");
        source.Should().Contain("new SetDrawingShapeGradientCommand(");
        source.Should().Contain("new SetDrawingShapeEffectCommand(");
        source.Should().Contain("shape.GetEffectiveEffectPreset() == DrawingShapeEffectPreset.None");
        source.Should().Contain("DrawingShapeEffectPreset.Shadow");
        source.Should().Contain("new PictureCropDialog(picture)");
        source.Should().Contain("private void PictureCropDialogMenuItem_Click(object sender, RoutedEventArgs e) =>");
        source.Should().Contain("new SetPictureCropCommand(");
        source.Should().Contain("DrawingTargetResolver.GetTargetDrawingObject(sheet, SheetGrid.SelectedRange?.Start, preferredKind)");
    }

    private static string ReadMainWindowXaml() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

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
