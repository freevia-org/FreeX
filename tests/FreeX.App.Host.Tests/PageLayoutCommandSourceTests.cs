using FluentAssertions;
using static FreeX.App.Host.Tests.LocalizedXamlTestSupport;

namespace FreeX.App.Host.Tests;

public sealed class PageLayoutCommandSourceTests
{
    [Theory]
    [InlineData("Themes", "Themes", "TH", "ThemeBtn_Click")]
    [InlineData("Theme Colors", "Colors", "TC", "ThemeColorsBtn_Click")]
    [InlineData("Theme Fonts", "Fonts", "TF", "ThemeFontsBtn_Click")]
    [InlineData("Theme Effects", "Effects", "TE", "ThemeEffectsBtn_Click")]
    [InlineData("Margins", "Margins", "M", "PageMarginsBtn_Click")]
    [InlineData("Page Orientation", "Orientation", "OR", "PageOrientBtn_Click")]
    [InlineData("Paper Size", "Size", "SZ", "PageSizeBtn_Click")]
    [InlineData("Print Area", "Print Area", "PA", "PrintAreaBtn_Click")]
    [InlineData("Breaks", "Breaks", "BK", "PageBreaksBtn_Click")]
    [InlineData("Scale to Fit", "...", "SF", "ScaleToFitBtn_Click")]
    [InlineData("Print Titles", "Print Titles", "PT", "PrintTitlesBtn_Click")]
    public void PageLayoutButtons_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = ReadMainWindowXaml()
            .ExtractButtonElementByInvariantCommandName(title, $"Click=\"{handler}\"");

        button.ShouldContainLocalizedAttribute("Content", content);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Office", "O", "ThemeOfficeMenuItem_Click")]
    [InlineData("FreeX Colorful", "C", "ThemeColorfulMenuItem_Click")]
    [InlineData("Grayscale", "G", "ThemeGrayscaleMenuItem_Click")]
    [InlineData("Customize...", "U", "ThemeCustomizeMenuItem_Click")]
    [InlineData("Normal", "N", "MarginNormalMenuItem_Click")]
    [InlineData("Wide", "W", "MarginWideMenuItem_Click")]
    [InlineData("Narrow", "A", "MarginNarrowMenuItem_Click")]
    [InlineData("Custom Margins...", "C", "MarginCustomMenuItem_Click")]
    [InlineData("Portrait", "P", "OrientPortraitMenuItem_Click")]
    [InlineData("Landscape", "L", "OrientLandscapeMenuItem_Click")]
    [InlineData("Letter", "L", "SizeLetter_Click")]
    [InlineData("A4", "A", "SizeA4_Click")]
    [InlineData("Legal", "G", "SizeLegal_Click")]
    [InlineData("Set Print Area", "S", "PrintAreaSetMenuItem_Click")]
    [InlineData("Clear Print Area", "C", "PrintAreaClearMenuItem_Click")]
    [InlineData("Insert Page Break", "I", "InsertPageBreakMenuItem_Click")]
    [InlineData("Remove Page Break", "R", "RemovePageBreakMenuItem_Click")]
    [InlineData("Reset All Page Breaks", "A", "ResetAllPageBreaksMenuItem_Click")]
    public void PageLayoutMenus_ExposeExpectedHeadersKeyTipsAndHandlers(
        string header,
        string keyTip,
        string handler)
    {
        var item = ReadMainWindowXaml()
            .ExtractElementByLocalizedAttributeValue("MenuItem", "Header", header, $"Click=\"{handler}\"");

        item.ShouldContainLocalizedAttribute("Header", header);
        item.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        item.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("View Gridlines", "VG", "ViewGridlinesChk_Changed")]
    [InlineData("View Headings", "VH", "ViewHeadersChk_Changed")]
    public void PageLayoutViewSheetOptionToggles_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var checkBox = ReadMainWindowXaml()
            .ExtractElementByInvariantCommandName("CheckBox", title);

        checkBox.ShouldContainLocalizedAttribute("Content", "View");
        checkBox.ShouldContainInvariantCommandName(title);
        checkBox.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        checkBox.Should().Contain($"Checked=\"{handler}\"");
        checkBox.Should().Contain($"Unchecked=\"{handler}\"");
    }

    [Theory]
    [InlineData("Print Gridlines", "PG", "PrintGridlinesChk_Click")]
    [InlineData("Print Headings", "PH", "PrintHeadingsChk_Click")]
    public void PageLayoutPrintSheetOptionToggles_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var checkBox = ReadMainWindowXaml()
            .ExtractElementByInvariantCommandName("CheckBox", title);

        checkBox.ShouldContainLocalizedAttribute("Content", "Print");
        checkBox.ShouldContainInvariantCommandName(title);
        checkBox.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        checkBox.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void PageLayoutHandlers_RouteThroughExpectedThemePageSetupAndPrintCommands()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.PageLayout.cs");

        source.Should().Contain("WorkbookThemeWorkflow.CreateColorfulTheme()");
        source.Should().Contain("WorkbookThemeWorkflow.CreateGrayscaleTheme()");
        source.Should().Contain("new WorkbookThemeDialog(_workbook.Theme)");
        source.Should().Contain("new SetWorkbookThemeCommand(theme)");
        source.Should().Contain("new SetPageMarginsCommand(sheetId, WorksheetPageMargins.Normal)");
        source.Should().Contain("new SetPageOrientationCommand(sheetId, WorksheetPageOrientation.Portrait)");
        source.Should().Contain("new SetPaperSizeCommand(sheetId, WorksheetPaperSize.Letter)");
        SourceMethodExtractor.ExtractMethodSource(source, "private void PrintAreaBtn_Click(")
            .Should().Contain("OpenRibbonContextMenu(btn, cm);");
        SourceMethodExtractor.ExtractMethodSource(source, "private void PageBreaksBtn_Click(")
            .Should().Contain("OpenRibbonContextMenu(btn, cm);");
        SourceMethodExtractor.ExtractMethodSource(source, "private void BackgroundBtn_Click(")
            .Should().Contain("BackgroundChooseMenuItem_Click(sender, e);");
        source.Should().Contain("new SetPrintAreaCommand(sheetId, GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId))");
        source.Should().Contain("new ClearPrintAreaCommand(sheetId)");
        source.Should().Contain("ShowPageSetupDialog(PageSetupInitialFocusTarget.ScaleToFit)");
        source.Should().Contain("PageBreakSelectionPlanner.Insert(selectedRange, sheet.RowPageBreaks, sheet.ColumnPageBreaks)");
        source.Should().Contain("PageBreakSelectionPlanner.Remove(selectedRange, sheet.RowPageBreaks, sheet.ColumnPageBreaks)");
        source.Should().Contain("new SetPageBreaksCommand(sheetId, rowBreaks, columnBreaks)");
        source.Should().Contain("PageSetupCommandBuilder.Build(sheetId, dialog)");
        source.Should().Contain("new SetPrintOptionsCommand(_currentSheetId, isChecked, sheet?.PrintHeadings ?? false)");
        source.Should().Contain("new SetPrintOptionsCommand(_currentSheetId, sheet?.PrintGridlines ?? false, isChecked)");
    }

}
