using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class PivotDesignCommandSourceTests
{
    [Theory]
    [InlineData("Grand Totals", "Grand Totals", "G", "PivotGrandTotalsBtn_Click")]
    [InlineData("Subtotals", "Subtotals", "S", "PivotSubtotalsBtn_Click")]
    [InlineData("Report Layout", "Report Layout", "L", "PivotReportLayoutBtn_Click")]
    [InlineData("Blank Rows", "Blank Rows", "B", "PivotBlankRowsBtn_Click")]
    [InlineData("Banded Rows", "Banded Rows", "R", "PivotBandedRowsBtn_Click")]
    [InlineData("Banded Columns", "Banded Columns", "C", "PivotBandedColumnsBtn_Click")]
    [InlineData("Row Headers", "Row Headers", "H", "PivotRowHeadersBtn_Click")]
    [InlineData("Column Headers", "Column Headers", "O", "PivotColumnHeadersBtn_Click")]
    [InlineData("PivotTable Styles", "Styles", "Y", "PivotStyleGalleryBtn_Click")]
    public void PivotDesignCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title);

        button.Should().Contain($"Content=\"{content}\"");
        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void PivotDesignHandlers_RouteThroughExpectedOptionsDialogStyleGalleryAndCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PivotDesignCommands.cs"));

        source.Should().Contain("private void PivotGrandTotalsBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("private void PivotSubtotalsBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("private void PivotReportLayoutBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("ShowPivotTableOptionsDialog();");
        source.Should().Contain("private void PivotStyleGalleryBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("ShowPivotStyleGalleryDialog();");
        source.Should().Contain("new PivotTableOptionsDialog(pivotTable, cache)");
        source.Should().Contain("new PivotStyleGalleryDialog(pivotTable.StyleName)");
        source.Should().Contain("new ConfigurePivotTableOptionsCommand(");
        source.Should().Contain("!pivotTable.BlankLineAfterItems");
        source.Should().Contain("!pivotTable.ShowRowHeaders");
        source.Should().Contain("!pivotTable.ShowColumnHeaders");
        source.Should().Contain("!pivotTable.ShowRowStripes");
        source.Should().Contain("!pivotTable.ShowColumnStripes");
        source.Should().Contain("styleName: dialog.Result.StyleName");
        source.Should().Contain("TryExecuteCommand(");
        source.Should().Contain("UpdateViewport();");
    }

    private static string ReadMainWindowXaml() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

    private static string ExtractButtonElementByTitle(string xaml, string title)
    {
        var titleIndex = xaml.IndexOf($"local:RibbonTooltip.Title=\"{title}\"", StringComparison.Ordinal);
        titleIndex.Should().BeGreaterThanOrEqualTo(0, $"the {title} PivotTable Design command should be present");

        var start = xaml.LastIndexOf("<Button", titleIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {title} PivotTable Design command should be a Button");

        var selfClosingEnd = xaml.IndexOf("/>", titleIndex, StringComparison.Ordinal);
        var closingEnd = xaml.IndexOf("</Button>", titleIndex, StringComparison.Ordinal);
        var end = closingEnd >= 0 && (selfClosingEnd < 0 || closingEnd < selfClosingEnd)
            ? closingEnd + "</Button>".Length
            : selfClosingEnd + 2;

        end.Should().BeGreaterThan(titleIndex, $"the {title} PivotTable Design button should have a closing marker");
        return xaml[start..end];
    }
}
