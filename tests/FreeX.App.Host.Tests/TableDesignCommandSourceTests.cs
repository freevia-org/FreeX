using FluentAssertions;
using static FreeX.App.Host.Tests.LocalizedXamlTestSupport;

namespace FreeX.App.Host.Tests;

public sealed class TableDesignCommandSourceTests
{
    [Theory]
    [InlineData("Table Name", "Table Name", "N", "TableDesignTableNameBtn_Click")]
    [InlineData("Resize Table", "Resize Table", "Z", "TableDesignResizeTableBtn_Click")]
    [InlineData("Summarize with PivotTable", "Summarize with PivotTable", "S", "TableDesignSummarizeWithPivotTableBtn_Click")]
    [InlineData("Convert to Range", "Convert to Range", "V", "TableDesignConvertToRangeBtn_Click")]
    public void TableDesignDeferredSliceCommands_AreEnabledAndRouted(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = ReadTableDesignTabXaml().ExtractButtonElementByInvariantCommandName(title);

        button.ShouldContainLocalizedAttribute("Content", content);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
        button.Should().NotContain("IsEnabled=\"False\"");
        button.Should().NotContain("Deferred");
    }

    [Fact]
    public void TableDesignHeaderRow_IsHiddenUntilARealHeaderRowCommandExists()
    {
        var xaml = ReadTableDesignTabXaml();
        var source = ReadHostSourceFile("MainWindow.TableDesignCommands.cs");

        xaml.Should().NotContain("TableDesignHeaderRowBtn");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Header Row\"");
        xaml.Should().NotContain("MainWindow_TooltipDescription_HeaderRowsRemainVisibleForStructuredTablesInFreeX");
        source.Should().NotContain("TableDesignHeaderRowBtn");
    }

    [Fact]
    public void TableDesignTotalRow_UsesPhysicalTotalsRowCommandAndReappliesKnownGalleryStyle()
    {
        var source = ReadHostSourceFile("MainWindow.TableDesignCommands.cs");

        source.Should().Contain("new SetStructuredTableTotalsRowCommand(");
        source.Should().Contain("var totalsRowChanged = false;");
        source.Should().Contain("if (totalsRowShown is { } showTotals && showTotals != table.TotalsRowShown)");
        source.Should().Contain("if (styleOptionChanged || totalsRowChanged)");
        source.Should().Contain("new CompositeWorkbookCommand(\"Table Style Options\", commands)");
        source.Should().NotContain("totalsRowShown: totalsRowShown");
    }

    [Fact]
    public void TableDesignOptions_ReapplyNonGalleryStylesInsteadOfMetadataOnlyConfigure()
    {
        var source = ReadHostSourceFile("MainWindow.TableDesignCommands.cs");

        source.Should().Contain("else if (styleOptionChanged)");
        source.Should().Contain("new ReapplyStructuredTableStyleCommand(");
        source.Should().Contain("else if (totalsRowChanged)");
    }

    [Fact]
    public void TableDesignDeferredSliceHandlers_RouteThroughModelCommandsAndPivotCreationApis()
    {
        var source = ReadHostSourceFile("MainWindow.TableDesignCommands.cs");

        source.Should().Contain("new TextEntryDialog(");
        source.Should().Contain("new RenameStructuredTableCommand(_currentSheetId, table.Id, dialog.Result.Text)");
        source.Should().Contain("new ResizeStructuredTableCommand(_currentSheetId, table.Id, newRange)");
        source.Should().Contain("new AddPivotTableToNewWorksheetCommand(");
        source.Should().Contain("new AddPivotTableCommand(");
        source.Should().Contain("new ConvertStructuredTableToRangeCommand(_currentSheetId, table.Id)");
        source.Should().Contain("_messageService.AskYesNo(");
    }

    private static string ReadTableDesignTabXaml()
    {
        var xaml = ReadMainWindowXaml();
        var start = xaml.IndexOf("Header=\"{local:Loc Key=MainWindow_Header_TableDesign}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the Table Design contextual tab should be present");

        var end = xaml.IndexOf("Header=\"{local:Loc Key=MainWindow_Header_PivotTableAnalyze}\"", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the PivotTable Analyze contextual tab should follow Table Design");
        return xaml[start..end];
    }

}
