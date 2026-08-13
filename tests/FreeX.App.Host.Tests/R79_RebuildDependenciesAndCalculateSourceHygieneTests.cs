using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R79-calc-volatile-recalc-5-3: Ctrl+Alt+Shift+F9's RebuildDependenciesAndCalculate
/// (MainWindow.WorkbookUiState.cs) must not redundantly rebuild the dependency graph twice.
/// RecalcEngine.RecalculateAllFormulas already calls RebuildFormulaDependencies as its own first
/// step, so an explicit call to it immediately beforehand walks every sheet/formula cell and
/// re-registers dependency edges TWICE for a single keypress.
/// </summary>
public sealed class R79_RebuildDependenciesAndCalculateSourceHygieneTests
{
    [Fact]
    public void RebuildDependenciesAndCalculate_DoesNotExplicitlyCallRebuildFormulaDependencies()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookUiState.cs");

        source.Should().NotContain(
            "_recalcEngine.RebuildFormulaDependencies(_workbook);",
            "RecalcEngine.RecalculateAllFormulas already rebuilds the dependency graph as its own first step, " +
            "so an explicit call here would redundantly rebuild it a second time for every Ctrl+Alt+Shift+F9 press");
    }

    [Fact]
    public void RebuildDependenciesAndCalculate_StillCallsRecalculateAllFormulas()
    {
        // No-regression sibling: removing the redundant explicit rebuild call must not
        // accidentally remove the full rebuild+recalc behavior itself.
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookUiState.cs");

        source.Should().Contain("private void RebuildDependenciesAndCalculate()");
        source.Should().Contain("_session.RecalculateWorkbook();");
    }
}
