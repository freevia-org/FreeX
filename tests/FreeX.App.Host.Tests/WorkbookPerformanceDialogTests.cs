using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookPerformanceDialogTests
{
    [Fact]
    public void CreateMessage_ProvidesActionableRangeAndClearFormatsGuidance()
    {
        var sheetId = SheetId.New();
        var report = new WorkbookPerformanceReport([
            new WorkbookPerformanceIssue(
                sheetId,
                "Data",
                new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 50, 10)),
                new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
                48),
        ]);

        var message = WorkbookPerformanceDialog.CreateMessage(report);

        message.Should().Contain("Data: 48 formatting-only cells")
            .And.Contain("Content: A1:B2")
            .And.Contain("Used range: A1:J50")
            .And.Contain("Home > Clear > Clear Formats");
    }

    [Fact]
    public void ReviewRibbon_MapsCheckPerformanceToReadOnlyReportHandler()
    {
        FreeXRibbonHandlerMap.Handlers.Should().Contain("Check Performance", "CheckPerformanceBtn_Click");
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs", "WorkbookPerformanceDialog.cs");
        source.Should().Contain("WorkbookPerformanceService.Analyze(_workbook)");
        source.Should().NotContain("ClearStyleOnlyEntries");
        source.Should().Contain("This report does not change the workbook.");
    }
}
