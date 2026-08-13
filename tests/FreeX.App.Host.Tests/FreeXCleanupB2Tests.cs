using System.Reflection;
using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for FreeX cleanup batch B2 (HIGH finding P97).
/// WPF print/PDF must apply the sheet's configured Page Setup &gt; Scaling ("Adjust to N%") as a
/// direct, unconditional multiplier on every printed element -- the same way the fixed
/// portable/Skia PDF export path (FreeX.App.Services.WorkbookPdfContentBuilder.ResolveScaleRatio/
/// ComputeActualGridSizes) already does -- rather than deriving an independent per-page ratio from that page's own
/// (possibly still-overflowing) drawn geometry capped at 1. Before the fix: a sheet that already
/// fits one page at 100% printed completely unscaled at "Adjust to 50%" (no visible shrink), and
/// "Adjust to 200%" never enlarged anything (ratio capped at 1).
/// </summary>
public sealed class FreeXCleanupB2Tests
{
    [Fact]
    public void RenderWorksheet_AppliesConfiguredScalePercent_ToOnePageSheetThatAlreadyFits()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Small sheet 50 percent");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));

            // Baseline: default 100% scale, single small cell -- fits one page trivially.
            var defaultDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var defaultPage = defaultDocument.Pages[0].GetPageRoot(forceReload: false)!;
            var defaultFontSize = PdfTextOverlayExtractor.Extract(defaultPage)
                .Should().ContainSingle(overlay => overlay.Text == "Hello")
                .Which.FontSize;

            // "Adjust to 50% normal size" on the very same one-page sheet: Excel shrinks every
            // printed element in direct proportion to the configured scale even though the content
            // already fit the page unscaled -- it is never merely a repagination hint.
            sheet.ScaleToFit = new WorksheetScaleToFit(50, null, null);
            var scaledDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            scaledDocument.Pages.Should().HaveCount(1, "the sheet still fits one page at 50%");
            var scaledPage = scaledDocument.Pages[0].GetPageRoot(forceReload: false)!;
            var scaledFontSize = PdfTextOverlayExtractor.Extract(scaledPage)
                .Should().ContainSingle(overlay => overlay.Text == "Hello")
                .Which.FontSize;

            scaledFontSize.Should().BeApproximately(defaultFontSize * 0.5, 0.01,
                "Adjust to 50% must shrink printed text even when the unscaled content already fit one page");
        });
    }

    [Fact]
    public void RenderWorksheet_AppliesConfiguredScalePercent_AboveOneHundredEnlargesContent()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Enlarge 200 percent");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));

            var defaultDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var defaultPage = defaultDocument.Pages[0].GetPageRoot(forceReload: false)!;
            var defaultFontSize = PdfTextOverlayExtractor.Extract(defaultPage)
                .Should().ContainSingle(overlay => overlay.Text == "Hello")
                .Which.FontSize;

            // "Adjust to 200% normal size": the old code capped scaleRatio at 1 (shrink-only), so
            // enlargement never happened. The fix must apply scale percentages above 100 too.
            sheet.ScaleToFit = new WorksheetScaleToFit(200, null, null);
            var enlargedDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var enlargedPage = enlargedDocument.Pages[0].GetPageRoot(forceReload: false)!;
            var enlargedFontSize = PdfTextOverlayExtractor.Extract(enlargedPage)
                .Should().ContainSingle(overlay => overlay.Text == "Hello")
                .Which.FontSize;

            enlargedFontSize.Should().BeApproximately(defaultFontSize * 2.0, 0.01,
                "Adjust to 200% must enlarge printed text instead of being capped at 100%");
        });
    }

    [Fact]
    public void RenderWorksheet_AppliesSameConfiguredScalePercent_ToEveryPageOfAMultiPagePrintout()
    {
        StaTestRunner.Run(() =>
        {
            // A sheet whose Fit-to-1-page-wide request inflates the per-page row capacity so the
            // last page is only partially filled -- before the fix, a partially-filled page derived
            // scaleRatio=1 (its own unscaled content already fits) while a fully-packed page derived
            // a real shrink, mixing two different visual scales in the same printout.
            var workbook = new Workbook("Multi page scale");
            var sheet = workbook.AddSheet("Sheet1");
            for (uint row = 1; row <= 100; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Row {row}"));

            sheet.ScaleToFit = new WorksheetScaleToFit(50, null, null);
            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            document.Pages.Count.Should().BeGreaterThan(1, "100 rows at 50% scale must still span multiple pages");

            var fontSizesByPage = document.Pages
                .Select(pageContent =>
                {
                    var page = pageContent.GetPageRoot(forceReload: false)!;
                    return PdfTextOverlayExtractor.Extract(page)
                        .Where(overlay => overlay.Text.StartsWith("Row ", StringComparison.Ordinal))
                        .Select(overlay => overlay.FontSize)
                        .ToList();
                })
                .Where(sizes => sizes.Count > 0)
                .ToList();

            fontSizesByPage.Should().HaveCountGreaterThan(1);
            var distinctFontSizes = fontSizesByPage.SelectMany(sizes => sizes).Distinct().ToList();
            distinctFontSizes.Should().ContainSingle(
                "every page of the same printout must share one configured scale instead of each page " +
                "deriving its own ratio from its own (possibly partially-filled) content extent");
        });
    }
}

/// <summary>
/// Regression test for FreeX cleanup batch B2 (HIGH finding P28).
/// The WPF host must bind <see cref="GridView.IsSheetRightToLeft"/> to the active sheet's
/// <see cref="Sheet.IsRightToLeft"/> flag (Excel's <c>sheetView rightToLeft="1"</c>) whenever the
/// viewport refreshes, the same way <c>MainWindow.Viewport.cs</c> already binds
/// <c>SheetGrid.ActiveSheetId</c> -- mirroring the Avalonia shell's
/// <c>_session.ActiveSheet.IsRightToLeft</c> wiring (MainWindow.cs). Before the fix, nothing in the
/// WPF host ever wrote to the dependency property, so it stayed at its default <see langword="false"/>
/// and RTL sheets always rendered LTR on Windows regardless of the loaded workbook's setting.
/// </summary>
public sealed class FreeXCleanupB2RightToLeftTests
{
    [Fact]
    public void UpdateViewport_WhenActiveSheetIsRightToLeft_BindsGridViewIsSheetRightToLeft()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RightToLeftHarness.Create();

            harness.Sheet.IsRightToLeft = true;
            harness.RefreshViewport();

            harness.GridIsSheetRightToLeft.Should().BeTrue(
                "the WPF host must bind GridView.IsSheetRightToLeft to the active sheet's IsRightToLeft flag");
        });
    }

    [Fact]
    public void UpdateViewport_WhenActiveSheetIsLeftToRight_KeepsGridViewIsSheetRightToLeftFalse()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RightToLeftHarness.Create();

            harness.Sheet.IsRightToLeft = false;
            harness.RefreshViewport();

            harness.GridIsSheetRightToLeft.Should().BeFalse(
                "a plain LTR sheet must not be mirrored");
        });
    }

    [Fact]
    public void UpdateViewport_WhenSwitchingActiveSheet_ReflectsTheNewlyActiveSheetsDirection()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RightToLeftHarness.Create();
            harness.Sheet.IsRightToLeft = true;
            harness.RefreshViewport();
            harness.GridIsSheetRightToLeft.Should().BeTrue();

            // Switching to a second, LTR sheet must flip the grid back to LTR instead of leaking the
            // previous sheet's direction forward.
            var secondSheet = harness.Workbook.AddSheet("Sheet2");
            harness.SetCurrentSheetId(secondSheet.Id);
            harness.RefreshViewport();

            harness.GridIsSheetRightToLeft.Should().BeFalse(
                "the newly activated LTR sheet must not inherit the previous sheet's RTL direction");
        });
    }

    private sealed class RightToLeftHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _updateViewport;
        private readonly FieldInfo _currentSheetIdField;

        private RightToLeftHarness(MainWindow window, Workbook workbook)
        {
            _window = window;
            Workbook = workbook;
            _updateViewport = typeof(MainWindow)
                .GetMethod("UpdateViewport", BindingFlags.Instance | BindingFlags.NonPublic, [])
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateViewport");
            _currentSheetIdField = typeof(MainWindow)
                .GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_currentSheetId");
        }

        public Workbook Workbook { get; }

        public Sheet Sheet => Workbook.Sheets[0];

        public bool GridIsSheetRightToLeft => ((SheetGridView)_window.FindName("SheetGrid")).IsSheetRightToLeft;

        public void RefreshViewport()
        {
            _updateViewport.Invoke(_window, []);
            PumpDispatcher();
        }

        public void SetCurrentSheetId(SheetId sheetId)
        {
            _currentSheetIdField.SetValue(_window, sheetId);
            PumpDispatcher();
        }

        public static RightToLeftHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance,
                options: new AppOptions())
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.AdoptWorkbookForParityCapture(workbook);
            window.Show();
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();
            return new RightToLeftHarness(window, workbook);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
