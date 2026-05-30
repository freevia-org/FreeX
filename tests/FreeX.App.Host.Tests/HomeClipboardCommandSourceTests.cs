using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeClipboardCommandSourceTests
{
    [Theory]
    [InlineData("Paste", "V", "PasteBtn_Click")]
    [InlineData("Cut", "X", "CutBtn_Click")]
    [InlineData("Copy", "C", "CopyBtn_Click")]
    [InlineData("Format Painter", "FP", "FormatPainterBtn_Click")]
    public void ClipboardCommandButtons_ExposeExpectedKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var button = ExtractButtonElementByClickHandler(xaml, handler);

        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Paste", "P", "PasteMenuItem_Click")]
    [InlineData("Values", "V", "PasteValuesMenuItem_Click")]
    [InlineData("Formulas", "F", "PasteFormulasMenuItem_Click")]
    [InlineData("Formatting", "R", "PasteFormattingMenuItem_Click")]
    [InlineData("Transpose", "T", "PasteTransposeMenuItem_Click")]
    [InlineData("Paste Special...", "S", "PasteSpecialBtn_Click")]
    public void PasteMenuItems_ExposeExpectedKeyTipsAndHandlers(
        string header,
        string keyTip,
        string handler)
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var menuItem = ExtractMenuItemElementByClickHandler(xaml, handler);

        menuItem.ShouldContainLocalizedAttribute("Header", header);
        menuItem.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        menuItem.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void FormatPainterButton_ExposesDoubleClickPersistentHandler()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var button = ExtractButtonElementByClickHandler(xaml, "FormatPainterBtn_Click");

        button.Should().Contain("PreviewMouseLeftButtonDown=\"FormatPainterBtn_PreviewMouseLeftButtonDown\"");
    }

    [Fact]
    public void ClipboardCommandHandlers_RouteThroughCopyPasteModesAndPasteSpecialPlanner()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ClipboardCommands.cs"));

        source.Should().Contain("private void CutBtn_Click(object sender, RoutedEventArgs e)   { ExecuteCopy(isCut: true); }");
        source.Should().Contain("private void CopyBtn_Click(object sender, RoutedEventArgs e)  { ExecuteCopy(); }");
        source.Should().Contain("private void PasteBtn_Click(object sender, RoutedEventArgs e) { ExecutePaste(); }");
        source.Should().Contain("private void PasteMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePaste();");
        source.Should().Contain("private void PasteValuesMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePaste(PasteMode.Values);");
        source.Should().Contain("private void PasteFormulasMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePaste(PasteMode.Formulas);");
        source.Should().Contain("private void PasteFormattingMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePaste(PasteMode.Formats);");
        source.Should().Contain("ExecutePaste(PasteMode.All, new PasteSpecialOptions(Transpose: true))");
        source.Should().Contain("ClipboardPastePlanner.ToCorePasteMode(mode)");
        source.Should().Contain("PasteSpecialPlanner.CreatePlan(new PasteSpecialDialogSelection(");
        source.Should().Contain("ExecutePaste(plan.PasteMode, plan.Options, plan.KeepColumnWidths");
    }

    [Fact]
    public void FormatPainterHandlers_CaptureSingleAndPersistentSources()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.FormatPainter.cs"));

        source.Should().Contain("private void FormatPainterBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("CaptureFormatPainterSource(persistent: false)");
        source.Should().Contain("private void FormatPainterBtn_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)");
        source.Should().Contain("if (e.ClickCount != 2) return;");
        source.Should().Contain("CaptureFormatPainterSource(persistent: true)");
    }

    private static string ExtractButtonElementByClickHandler(string xaml, string clickHandler)
    {
        var clickIndex = xaml.IndexOf($"Click=\"{clickHandler}\"", StringComparison.Ordinal);
        clickIndex.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} button should be present");

        var start = xaml.LastIndexOf("<Button", clickIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} button should have a Button start tag");

        var end = xaml.IndexOf("</Button>", clickIndex, StringComparison.Ordinal);
        if (end >= clickIndex)
            return xaml.Substring(start, end - start + "</Button>".Length);

        end = xaml.IndexOf("/>", clickIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThanOrEqualTo(clickIndex, $"the {clickHandler} button should have an end tag or be self-closing");
        return xaml.Substring(start, end - start + 2);
    }

    private static string ExtractMenuItemElementByClickHandler(string xaml, string clickHandler)
    {
        var clickIndex = xaml.IndexOf($"Click=\"{clickHandler}\"", StringComparison.Ordinal);
        clickIndex.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} menu item should be present");

        var start = xaml.LastIndexOf("<MenuItem", clickIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} menu item should have a start tag");

        var end = xaml.IndexOf("</MenuItem>", clickIndex, StringComparison.Ordinal);
        if (end >= clickIndex)
            return xaml.Substring(start, end - start + "</MenuItem>".Length);

        end = xaml.IndexOf("/>", clickIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThanOrEqualTo(clickIndex, $"the {clickHandler} menu item should have an end tag or be self-closing");
        return xaml.Substring(start, end - start + 2);
    }
}
