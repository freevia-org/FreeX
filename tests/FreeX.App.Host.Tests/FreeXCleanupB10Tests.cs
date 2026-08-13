using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression test for cleanup finding P96: typing a page range into the Print Preview settings
/// panel used to re-parent already-parented <see cref="PageContent"/> objects into a second
/// <see cref="FixedDocument"/>, which throws <see cref="InvalidOperationException"/> from WPF's
/// PageContentCollection ("already the logical child of another element"). See
/// MainWindow.PrintExport.cs's BuildActiveSheetPrintPreview.
/// </summary>
public sealed class FreeXCleanupB10Tests
{
    [Fact]
    public void BuildActiveSheetPrintPreview_WithPageRange_DoesNotThrowAndReturnsRequestedPages()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var viewportService = new ViewportService();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                viewportService,
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                // Startup can replace the constructor workbook, so build the fixture in the
                // authoritative session workbook used by the print-preview production path.
                sheet = window.Session.Workbook.GetSheetAt(0);
                // Three non-overlapping print areas reliably yield 3 pages (mirrors
                // PrintRendererMultiAreaTests), giving us a real multi-page document to slice.
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
                sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("E1"));
                sheet.SetCell(new CellAddress(sheet.Id, 1, 9), new TextValue("I1"));
                sheet.SetPrintAreas([
                    new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
                    new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 2, 7)),
                    new GridRange(new CellAddress(sheet.Id, 1, 9), new CellAddress(sheet.Id, 2, 11)),
                ]);

                var buildPreview = typeof(MainWindow)
                    .GetMethod("BuildActiveSheetPrintPreview", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(nameof(MainWindow), "BuildActiveSheetPrintPreview");
                var currentSheetIdField = typeof(MainWindow)
                    .GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(nameof(MainWindow), "_currentSheetId");
                currentSheetIdField.SetValue(window, sheet.Id);

                // Sanity check: confirm the unranged render really does produce 3+ pages, so the
                // page-range slice below is exercising real multi-page re-parenting, not a no-op.
                var fullSettings = new PrintPreviewSettings();
                var fullResult = ((FixedDocument Document, PrintSettingsPlan Settings))
                    buildPreview.Invoke(window, [fullSettings])!;
                fullResult.Document.Pages.Count.Should().BeGreaterThanOrEqualTo(3);

                var rangedSettings = new PrintPreviewSettings(PageFrom: 1, PageTo: 1);

                Action act = () =>
                {
                    var (document, _) = ((FixedDocument Document, PrintSettingsPlan Settings))
                        buildPreview.Invoke(window, [rangedSettings])!;
                    document.Pages.Count.Should().Be(1, "PageFrom=1/PageTo=1 should slice the preview down to exactly one page");
                };

                act.Should().NotThrow(
                    "applying a page range must not re-parent PageContent objects still owned by the source FixedDocument");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            }
        });
    }
}
