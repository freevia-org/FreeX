using System.IO;
using FluentAssertions;

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
        var button = ExtractButtonElementByTitle(ReadTableDesignTabXaml(), title);

        button.ShouldContainLocalizedAttribute("Content", content);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
        button.Should().NotContain("IsEnabled=\"False\"");
        button.Should().NotContain("Deferred");
    }

    [Fact]
    public void TableDesignTotalRow_UsesPhysicalTotalsRowCommandAndReappliesKnownGalleryStyle()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.TableDesignCommands.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.TableDesignCommands.cs"));

        source.Should().Contain("else if (styleOptionChanged)");
        source.Should().Contain("new ReapplyStructuredTableStyleCommand(");
        source.Should().Contain("else if (totalsRowChanged)");
    }

    [Fact]
    public void TableDesignDeferredSliceHandlers_RouteThroughModelCommandsAndPivotCreationApis()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.TableDesignCommands.cs"));

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
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var start = xaml.IndexOf("Header=\"{local:Loc Key=MainWindow_Header_TableDesign}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the Table Design contextual tab should be present");

        var end = xaml.IndexOf("Header=\"{local:Loc Key=MainWindow_Header_PivotTableAnalyze}\"", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the PivotTable Analyze contextual tab should follow Table Design");
        return xaml[start..end];
    }

    private static string ExtractButtonElementByTitle(string xaml, string title)
    {
        var titleIndex = xaml.IndexOf($"local:RibbonMetadata.CommandName=\"{title}\"", StringComparison.Ordinal);
        titleIndex.Should().BeGreaterThanOrEqualTo(0, $"the {title} Table Design command should be present");

        var start = xaml.LastIndexOf("<Button", titleIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {title} Table Design command should be a Button");

        var selfClosingEnd = xaml.IndexOf("/>", titleIndex, StringComparison.Ordinal);
        var closingEnd = xaml.IndexOf("</Button>", titleIndex, StringComparison.Ordinal);
        var end = closingEnd >= 0 && (selfClosingEnd < 0 || closingEnd < selfClosingEnd)
            ? closingEnd + "</Button>".Length
            : selfClosingEnd + 2;

        end.Should().BeGreaterThan(titleIndex, $"the {title} Table Design button should have a closing marker");
        return xaml[start..end];
    }
}
