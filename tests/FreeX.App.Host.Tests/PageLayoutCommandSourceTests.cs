using System.IO;
using FluentAssertions;

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
    [InlineData("Scale to Fit", "Scale", "SF", "ScaleToFitBtn_Click")]
    [InlineData("Print Titles", "Print Titles", "PT", "PrintTitlesBtn_Click")]
    public void PageLayoutButtons_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = ExtractElementByTitle(ReadMainWindowXaml(), title, "Button");

        button.Should().Contain($"Content=\"{content}\"");
        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
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
    [InlineData("Portrait", "P", "OrientPortraitMenuItem_Click")]
    [InlineData("Landscape", "L", "OrientLandscapeMenuItem_Click")]
    [InlineData("Letter (8.5x11)", "L", "SizeLetter_Click")]
    [InlineData("A4 (210x297 mm)", "A", "SizeA4_Click")]
    [InlineData("Legal (8.5x14)", "G", "SizeLegal_Click")]
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
        var item = ExtractMenuItemElementByHeader(ReadMainWindowXaml(), header, handler);

        item.Should().Contain($"Header=\"{header}\"");
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
        var checkBox = ExtractElementByTitle(ReadMainWindowXaml(), title, "CheckBox");

        checkBox.Should().Contain("Content=\"View\"");
        checkBox.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
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
        var checkBox = ExtractElementByTitle(ReadMainWindowXaml(), title, "CheckBox");

        checkBox.Should().Contain("Content=\"Print\"");
        checkBox.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        checkBox.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        checkBox.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void PageLayoutHandlers_RouteThroughExpectedThemePageSetupAndPrintCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PageLayout.cs"));

        source.Should().Contain("WorkbookThemeWorkflow.CreateColorfulTheme()");
        source.Should().Contain("WorkbookThemeWorkflow.CreateGrayscaleTheme()");
        source.Should().Contain("new WorkbookThemeDialog(_workbook.Theme)");
        source.Should().Contain("new SetWorkbookThemeCommand(theme)");
        source.Should().Contain("new SetPageMarginsCommand(sheetId, WorksheetPageMargins.Normal)");
        source.Should().Contain("new SetPageOrientationCommand(sheetId, WorksheetPageOrientation.Portrait)");
        source.Should().Contain("new SetPaperSizeCommand(sheetId, WorksheetPaperSize.Letter)");
        source.Should().Contain("new SetPrintAreaCommand(sheetId, GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId))");
        source.Should().Contain("new ClearPrintAreaCommand(sheetId)");
        source.Should().Contain("ShowPageSetupDialog(PageSetupInitialFocusTarget.ScaleToFit)");
        source.Should().Contain("new PageBreakDialog(defaultValue)");
        source.Should().Contain("new SetPageBreaksCommand(sheetId, rowBreaks, columnBreaks)");
        source.Should().Contain("PageSetupCommandBuilder.Build(sheetId, dialog)");
        source.Should().Contain("new SetPrintOptionsCommand(_currentSheetId, isChecked, sheet?.PrintHeadings ?? false)");
        source.Should().Contain("new SetPrintOptionsCommand(_currentSheetId, sheet?.PrintGridlines ?? false, isChecked)");
    }

    private static string ReadMainWindowXaml() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

    private static string ExtractElementByTitle(string xaml, string title, string elementName)
    {
        var titleIndex = xaml.IndexOf($"local:RibbonTooltip.Title=\"{title}\"", StringComparison.Ordinal);
        titleIndex.Should().BeGreaterThanOrEqualTo(0, $"the {title} Page Layout command should be present");

        var start = xaml.LastIndexOf($"<{elementName}", titleIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {title} Page Layout command should be a {elementName}");

        var selfClosingEnd = xaml.IndexOf("/>", titleIndex, StringComparison.Ordinal);
        var closingEnd = xaml.IndexOf($"</{elementName}>", titleIndex, StringComparison.Ordinal);
        var end = closingEnd >= 0 && (selfClosingEnd < 0 || closingEnd < selfClosingEnd)
            ? closingEnd + elementName.Length + 3
            : selfClosingEnd + 2;

        end.Should().BeGreaterThan(titleIndex, $"the {title} Page Layout element should have a closing marker");
        return xaml[start..end];
    }

    private static string ExtractMenuItemElementByHeader(string xaml, string header, string handler)
    {
        var searchIndex = 0;
        while (true)
        {
            var headerIndex = xaml.IndexOf($"Header=\"{header}\"", searchIndex, StringComparison.Ordinal);
            headerIndex.Should().BeGreaterThanOrEqualTo(0, $"the {header} Page Layout menu item should be present");

            var start = xaml.LastIndexOf("<MenuItem", headerIndex, StringComparison.Ordinal);
            start.Should().BeGreaterThanOrEqualTo(0, $"the {header} Page Layout command should be a MenuItem");

            var end = xaml.IndexOf("/>", headerIndex, StringComparison.Ordinal);
            end.Should().BeGreaterThan(headerIndex, $"the {header} Page Layout menu item should be self-closing");
            var item = xaml[start..(end + 2)];
            if (item.Contains($"Click=\"{handler}\"", StringComparison.Ordinal))
                return item;

            searchIndex = end + 2;
        }
    }
}
