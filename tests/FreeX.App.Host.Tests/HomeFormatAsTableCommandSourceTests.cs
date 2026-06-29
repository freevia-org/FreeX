using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeFormatAsTableCommandSourceTests
{

    [Fact]
    public void FormatAsTableHandlers_RouteThroughGalleryPlannerAndStructuredTableCommand()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        source.Should().Contain("private void FormatTableBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("PopulateFormatTableGalleryMenu();");
        source.Should().Contain("TableStyleGalleryPlanner.GetSurface(_workbook.Theme)");
        source.Should().Contain("foreach (var group in surface.Groups)");
        source.Should().Contain("foreach (var item in group.Items)");
        source.Should().Contain("RibbonTooltip.SetKeyTip(menuItem, item.KeyTip);");
        source.Should().Contain("Tag = item");
        source.Should().Contain("menuItem.Click += FormatTableGalleryMenuItem_Click;");
        source.Should().Contain("ApplyTableFormat(item.Option);");
        source.Should().Contain("new CreateTableDialog(");
        source.Should().Contain("request => ApplyCreateTableRangeSelection(dialog, request)");
        source.Should().Contain("TableCreationPlanner.BuildStyledCommand(");
        source.Should().Contain("GroupedSheetRangePlanner.RemapRangeToSheet(dialog.Result.Range, sheetId)");
        source.Should().Contain("dialog.Result.TableStyleName");
        source.Should().Contain("tableStyle.Banding");
        source.Should().NotContain("new CreateStyledStructuredTableCommand(");
    }
}
