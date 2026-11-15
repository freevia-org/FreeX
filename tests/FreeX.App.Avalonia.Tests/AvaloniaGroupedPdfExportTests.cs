using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using FluentAssertions;
using Free.Shared.Pdf;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaGroupedPdfExportTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ExportActiveSheets_UsesWorkbookOrderAndIncludesBlankAndCommentPagesInTheRange()
    {
        await Session.Dispatch(() =>
        {
            using var workbookSession = new WorkbookSessionFactory().CreateNew(viewportHeight: 720, viewportWidth: 1120);
            var window = new MainWindow([], workbookSession);
            try
            {
                var workbook = window.Session.Workbook;
                var first = window.Session.ActiveSheet;
                var firstCell = CellAddress.Parse("A1", first.Id);
                first.SetCell(firstCell, new TextValue("Overview"));
                first.PrintComments = WorksheetPrintComments.AtEnd;
                first.Comments[firstCell] = "Review the overview";
                first.PageFooter = new WorksheetHeaderFooter("", "Page &P of &N", "");
                workbook.AddSheet("Outside group");
                var blank = workbook.AddSheet("Blank");
                blank.PageFooter = new WorksheetHeaderFooter("", "Page &P of &N", "");
                blank.FirstPageNumber = 5;
                window.Session.SelectSheetFromTab(blank.Id, selectRange: false, toggle: true);
                window.Session.IsWorkbookGrouped.Should().BeTrue();
                window.Session.ActiveSheet.Id.Should().Be(blank.Id);
                var options = ExportOptions.ExcelLikeDefault with { Scope = ExportContentScope.ActiveSheet };

                var fullPlan = CreateExportPlan(window, options);
                fullPlan.IsReady.Should().BeTrue();
                fullPlan.PageRequests.Select(page => page.PrintRange.Start.Sheet)
                    .Should().Equal(first.Id, first.Id, blank.Id);
                fullPlan.PageRequests[1].IsCommentSummaryPage.Should().BeTrue();
                var pages = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, fullPlan).Pages;
                pages[0].Ops.OfType<PdfText>().Should().Contain(op => op.Text == "Page 1 of 2");
                pages[2].Ops.OfType<PdfText>().Should().Contain(op => op.Text == "Page 5 of 1");

                var selectedPages = options with { PageRange = new ExportPageRange(2, 3) };
                PortablePdfExportPlanner.TryApplyOptions(fullPlan, selectedPages, out var filteredPlan, out var error)
                    .Should().BeTrue(error);
                filteredPlan.PageRequests.Select(page => page.PrintRange.Start.Sheet).Should().Equal(first.Id, blank.Id);
                var filteredPages = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, filteredPlan).Pages;
                filteredPages[0].Ops.OfType<PdfText>().Should().Contain(op => op.Text == "A1: Review the overview");
                filteredPages[1].Ops.OfType<PdfText>().Should().Contain(op => op.Text == "Page 5 of 1");

                var outsidePages = options with { PageRange = new ExportPageRange(4, 4) };
                PortablePdfExportPlanner.TryApplyOptions(fullPlan, outsidePages, out _, out _).Should().BeFalse();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(ExportContentScope.Selection, 1)]
    [InlineData(ExportContentScope.EntireWorkbook, 3)]
    public async Task GroupedTabsDoNotChangeOtherExportScopes(ExportContentScope scope, int pageCount)
    {
        await Session.Dispatch(() =>
        {
            using var workbookSession = new WorkbookSessionFactory().CreateNew(viewportHeight: 720, viewportWidth: 1120);
            var window = new MainWindow([], workbookSession);
            try
            {
                var workbook = window.Session.Workbook;
                workbook.AddSheet("Outside group");
                var active = workbook.AddSheet("Active");
                window.Session.SelectSheetFromTab(active.Id, selectRange: false, toggle: true);
                window.Session.IsWorkbookGrouped.Should().BeTrue();
                var selection = GridRange.Parse("B2:C3", active.Id);
                window.Session.SelectRange(selection);

                var plan = CreateExportPlan(window, ExportOptions.ExcelLikeDefault with { Scope = scope });

                plan.IsReady.Should().BeTrue();
                plan.TotalPageCount.Should().Be(pageCount);
                if (scope == ExportContentScope.Selection)
                    plan.PageRequests.Should().ContainSingle().Which.PrintRange.Should().Be(selection);
                else
                    plan.PageRequests.Select(page => page.PrintRange.Start.Sheet)
                        .Should().Equal(workbook.Sheets.Select(sheet => sheet.Id));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static PortablePdfExportPlan CreateExportPlan(MainWindow window, ExportOptions options) =>
        (PortablePdfExportPlan)typeof(MainWindow)
            .GetMethod("CreatePortablePdfExportPlan", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [options, WorkbookExportPrintOutputKind.Pdf])!;
}
