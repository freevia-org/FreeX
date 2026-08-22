using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class WorkbookPerformanceSourceTests
{
    [Fact]
    public void ReviewCheckPerformance_UsesSharedReadOnlyReport()
    {
        var source = TestWorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", "MainWindow.cs");

        source.Should().Contain("[\"Check Performance\"] = () => RunGuarded(ShowWorkbookPerformanceDialogAsync)");
        source.Should().Contain("WorkbookPerformanceService.Analyze(_session.Workbook)");
        source.Should().Contain("WorkbookPerformanceFormatter.Format(report)");
        source.Should().Contain("This report does not change the workbook.");
        source.Should().NotContain("ClearStyleOnlyEntries");
    }
}
