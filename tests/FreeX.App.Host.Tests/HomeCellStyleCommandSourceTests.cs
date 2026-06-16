using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeCellStyleCommandSourceTests
{

    [Fact]
    public void CellStylePresetApplication_UsesWorkbookThemeAndRepeatableStyleDiff()
    {
        var formattingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");
        var workbookUiStateSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookUiState.cs");

        formattingSource.Should().Contain("CellStyleDiffPlanner.GetCellStylePresetDiff(preset, _workbook.Theme)");
        formattingSource.Should().Contain("ApplyStyleDiff(CellStyleDiffPlanner.GetCellStylePresetDiff(preset, _workbook.Theme))");
        workbookUiStateSource.Should().Contain("TryExecuteRepeatableApplyStyle(diff, \"Apply Style\")");
    }
}
