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
        source.Should().Contain("TableStyleGalleryPlanner.GetOptions(_workbook.Theme)");
        source.Should().Contain("RibbonTooltip.SetKeyTip(menuItem, $\"{family[0]}{option.Label[(family.Length + 1)..]}\");");
        source.Should().Contain("menuItem.Click += FormatTableGalleryMenuItem_Click;");
        source.Should().Contain("TableStyleGalleryPlanner.GetOption(variant, _workbook.Theme)");
        source.Should().Contain("new CreateTableDialog(");
        source.Should().Contain("request => ApplyCreateTableRangeSelection(dialog, request)");
        source.Should().Contain("TableCreationPlanner.BuildStyledCommand(");
        source.Should().Contain("GroupedSheetRangePlanner.RemapRangeToSheet(dialog.Result.Range, sheetId)");
        source.Should().Contain("dialog.Result.TableStyleName");
        source.Should().Contain("tableStyle.Banding");
        source.Should().NotContain("new CreateStyledStructuredTableCommand(");
    }
}
