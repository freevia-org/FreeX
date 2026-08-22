using FluentAssertions;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia.Tests;

public sealed class WorkbookPerformanceSourceTests
{
    [Fact]
    public void ReviewCheckPerformance_UsesSharedReadOnlyReport()
    {
        var source = TestWorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", "MainWindow.cs");

        source.Should().Contain("[FreeXRibbonCommandIds.ReviewTranslate] = () => RunGuarded(ShowTranslateDialogAsync)");
        source.Should().Contain("[FreeXRibbonCommandIds.ReviewCheckPerformance] = () => RunGuarded(ShowWorkbookPerformanceDialogAsync)");
        source.Should().Contain("WorkbookPerformanceService.Analyze(_session.Workbook)");
        source.Should().Contain("WorkbookPerformanceFormatter.Format(report)");
        source.Should().Contain("UiText.Get(\"WorkbookPerformance_ReportHelpText\")");
        source.Should().NotContain("ClearStyleOnlyEntries");
    }
}
