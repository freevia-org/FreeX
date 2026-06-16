using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeBorderCommandSourceTests
{

    [Fact]
    public void BorderMenuHandlers_RouteThroughBorderServicesAndFormatCells()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        SourceMethodExtractor.ExtractMethodSource(source, "private void BorderPickerBtn_Click(")
            .Should().Contain("ApplySelectedBorderPreset();");
        source.Should().Contain("private enum RibbonBorderPreset");
        source.Should().Contain("_selectedBorderPreset = preset;");
        source.Should().Contain("BorderShortcutService.GetAllBorderDiff(_borderPickerStyle, _borderPickerColor)");
        source.Should().Contain("BorderShortcutService.GetClearBorderDiff()");
        source.Should().Contain("BorderShortcutService.GetSingleBorderDiff(BorderEdge.Bottom, _borderPickerStyle, _borderPickerColor)");
        source.Should().Contain("BorderShortcutService.GetOutlineBorderDiff(range, address, _borderPickerStyle, _borderPickerColor)");
        source.Should().Contain("BorderShortcutService.GetInsideBorderDiff(range, address, _borderPickerStyle, _borderPickerColor)");
        source.Should().Contain("SelectionStyleCommandPlanner.CreatePerCellStyleCommand(");
        source.Should().Contain("BeginBorderDrawMode(BorderDrawMode.Draw)");
        source.Should().Contain("BorderDrawPlanner.CommandTitle(mode)");
        source.Should().Contain("BorderDrawPlanner.CreateCommand(");
        source.Should().Contain("OpenFormatCellsDialog(FormatCellsDialogTab.Border)");
    }
}
