using FluentAssertions;
using System.Text.RegularExpressions;

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
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var button = xaml.ExtractButtonElementByClickHandler(handler);

        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Paste", "P", "PasteMenuItem_Click")]
    [InlineData("Values", "V", "PasteValuesMenuItem_Click")]
    [InlineData("Formulas", "F", "PasteFormulasMenuItem_Click")]
    [InlineData("Formatting", "R", "PasteFormattingMenuItem_Click")]
    [InlineData("Keep Source Column Widths", "W", "PasteKeepSourceColumnWidthsMenuItem_Click")]
    [InlineData("Values & Source Formatting", "A", "PasteValuesAndSourceFormattingMenuItem_Click")]
    [InlineData("Transpose", "T", "PasteTransposeMenuItem_Click")]
    [InlineData("Paste Link", "L", "PasteLinkMenuItem_Click")]
    [InlineData("Picture", "I", "PastePictureMenuItem_Click")]
    [InlineData("Linked Picture", "K", "PasteLinkedPictureMenuItem_Click")]
    [InlineData("Paste Special...", "S", "PasteSpecialBtn_Click")]
    public void PasteMenuItems_ExposeExpectedKeyTipsAndHandlers(
        string header,
        string keyTip,
        string handler)
    {
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var menuItem = xaml.ExtractMenuItemElementByClickHandler(handler);

        ShouldContainLocalizedMenuHeader(menuItem, header);
        menuItem.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        menuItem.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void FormatPainterButton_ExposesDoubleClickPersistentHandler()
    {
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var button = xaml.ExtractButtonElementByClickHandler("FormatPainterBtn_Click");

        button.Should().Contain("PreviewMouseLeftButtonDown=\"FormatPainterBtn_PreviewMouseLeftButtonDown\"");
    }

    [Fact]
    public void ClipboardCommandHandlers_RouteThroughCopyPasteModesAndPasteSpecialPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ClipboardCommands.cs");

        source.Should().Contain("private void CutBtn_Click(object sender, RoutedEventArgs e)   { ExecuteCopy(isCut: true); }");
        source.Should().Contain("private void CopyBtn_Click(object sender, RoutedEventArgs e)  { ExecuteCopy(); }");
        source.Should().Contain("private void PasteBtn_Click(object sender, RoutedEventArgs e) { ExecutePaste(); }");
        source.Should().Contain("private void PasteMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePaste();");
        source.Should().Contain("private void PasteValuesMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePaste(PasteMode.Values);");
        source.Should().Contain("private void PasteFormulasMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePaste(PasteMode.Formulas);");
        source.Should().Contain("private void PasteFormattingMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePaste(PasteMode.Formats);");
        source.Should().Contain("ExecutePaste(PasteMode.All, keepColumnWidths: true)");
        source.Should().Contain("PasteSpecialContentKind.ValuesAndSourceFormatting");
        source.Should().Contain("ExecutePaste(PasteMode.All, new PasteSpecialOptions(Transpose: true))");
        source.Should().Contain("private void PasteLinkMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePasteLink(transpose: false);");
        source.Should().Contain("private void PastePictureMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePasteAsPicture(isLinkedPicture: false);");
        source.Should().Contain("private void PasteLinkedPictureMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePasteAsPicture(isLinkedPicture: true);");
        source.Should().Contain("ClipboardPastePlanner.ToCorePasteMode(mode)");
        source.Should().Contain("ClipboardPastePlanner.ShouldPreserveClipboardVisualAfterPaste(clip.IsCut)");
        source.Should().Contain("ApplyClipboardVisualStateAfterInternalPaste(sourceRange, preserveClipboardVisual)");
        source.Should().Contain("CompletePasteSelection(clip.SourceRange, options, preserveClipboardVisual)");
        source.Should().Contain("PasteSpecialPlanner.CreatePlan(new PasteSpecialDialogSelection(");
        source.Should().Contain("ExecutePaste(plan.PasteMode, plan.Options, plan.KeepColumnWidths");
    }

    [Fact]
    public void FormatPainterHandlers_CaptureSingleAndPersistentSources()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.FormatPainter.cs");

        source.Should().Contain("private void FormatPainterBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("CaptureFormatPainterSource(persistent: false)");
        source.Should().Contain("private void FormatPainterBtn_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)");
        source.Should().Contain("if (e.ClickCount != 2) return;");
        source.Should().Contain("CaptureFormatPainterSource(persistent: true)");
    }

    private static void ShouldContainLocalizedMenuHeader(string menuItem, string expectedHeader)
    {
        var match = Regex.Match(menuItem, @"\bHeader=""(?<value>[^""]+)""");
        match.Success.Should().BeTrue("the paste menu item should declare a Header");

        var resolved = LocalizedXamlTestSupport.ResolveLocalizedValue(match.Groups["value"].Value)
            ?? string.Empty;
        resolved.Replace("_", string.Empty, StringComparison.Ordinal)
            .Should().Be(expectedHeader, "WPF access-key underscores are part of the Excel-style menu label");
    }

}
