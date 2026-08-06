using FluentAssertions;
using static FreeX.App.Host.Tests.LocalizedXamlTestSupport;

namespace FreeX.App.Host.Tests;

public sealed class TableDesignCommandSourceTests
{

    [Fact]
    public void TableDesignHeaderRow_IsHiddenUntilARealHeaderRowCommandExists()
    {
        var xaml = ReadTableDesignTabXaml();
        var source = ReadHostSourceFile("MainWindow.TableDesignCommands.cs");

        xaml.Should().NotContain("TableDesignHeaderRowBtn");
        xaml.Should().NotContain("ribbonWpf:RibbonMetadata.CommandName=\"Header Row\"");
        xaml.Should().NotContain("MainWindow_TooltipDescription_HeaderRowsRemainVisibleForStructuredTablesInFreeX");
        source.Should().NotContain("TableDesignHeaderRowBtn");
    }

    [Fact]
    public void TableDesignTotalRow_DelegatesStyleOptionCommandCompositionToSharedPlanner()
    {
        var source = ReadHostSourceFile("MainWindow.TableDesignCommands.cs");

        source.Should().Contain("TableDesignCommandPlanner.BuildStyleOptionsCommand(");
        source.Should().NotContain("new SetStructuredTableTotalsRowCommand(");
        source.Should().NotContain("new ReapplyStructuredTableStyleCommand(");
        source.Should().NotContain("new CompositeWorkbookCommand(\"Table Style Options\"");
    }

    [Fact]
    public void TableDesignOptions_StayThinAndDoNotBuildModelCommandsInWpf()
    {
        var source = ReadHostSourceFile("MainWindow.TableDesignCommands.cs");

        source.Should().Contain("TableDesignCommandPlanner.BuildStyleOptionsCommand(");
        source.Should().NotContain("new ApplyStructuredTableStyleCommand(");
        source.Should().NotContain("new ReapplyStructuredTableStyleCommand(");
    }

    [Fact]
    public void TableDesignStyleGallery_ConsumesSharedSurfaceDescriptor()
    {
        var source = ReadHostSourceFile("MainWindow.TableDesignCommands.cs");

        source.Should().Contain("TableStyleGalleryPlanner.GetSurface(_workbook.Theme)");
        source.Should().Contain("foreach (var group in surface.Groups)");
        source.Should().Contain("foreach (var item in group.Items)");
        source.Should().Contain("Header = CreateFormatTableGalleryHeader(item)");
        source.Should().Contain("Tag = item");
        source.Should().Contain("RibbonTooltip.SetKeyTip(menuItem, item.KeyTip);");
        source.Should().Contain("ApplyStructuredTableStyle(item.Option);");
        source.Should().NotContain("TableStyleGalleryPlanner.GetOptions(_workbook.Theme)");
        source.Should().NotContain("option.Label.Split");
        source.Should().NotContain("CultureInfo.InvariantCulture");
        source.Should().NotContain("option.Label[(family.Length + 1)..]");
    }

    [Fact]
    public void TableDesignDeferredSliceHandlers_RouteThroughModelCommandsAndPivotCreationApis()
    {
        var source = ReadHostSourceFile("MainWindow.TableDesignCommands.cs");

        source.Should().Contain("new TextEntryDialog(");
        source.Should().Contain("TableNamePlanner.Capture(table)");
        source.Should().Contain("TableNamePlanner.TryCreateRename(");
        source.Should().Contain("TableDesignCommandPlanner.BuildRenameCommand(");
        source.Should().Contain("TableResizePlanner.Capture(table)");
        source.Should().Contain("TableResizePlanner.TryCreateResize(");
        source.Should().Contain("TableDesignCommandPlanner.BuildResizeCommand(");
        source.Should().Contain("PivotCreatePlanner.BuildNewWorksheetCommand(");
        source.Should().Contain("ActivateNewWorksheetAtA1(createdSheetId)");
        source.Should().Contain("PivotCreatePlanner.BuildInPlaceCommand(");
        source.Should().Contain("TableDesignCommandPlanner.BuildConvertToRangePlan(");
        source.Should().Contain("plan.Command");
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
