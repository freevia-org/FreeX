using FluentAssertions;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// font-text-measurement-F1: <see cref="WorkbookPdfContentBuilder"/> hardcoded its own dependency-free
/// character-count-times-a-flat-factor <c>PortablePdfTextMeasurer</c> at every alignment-sensitive call
/// site (cell text, row/column heading centering, chart axis/label layout, header/footer runs) with no
/// way to supply a real measurer -- so a caller whose PDF backend draws with real font glyph metrics
/// (Skia's <c>SkiaPdfWriter</c>, which measures every run's actual advance via
/// <c>SKFont.MeasureText</c>) had no way to make the precomputed text position agree with what actually
/// gets drawn. <see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/> and
/// <see cref="WorkbookPdfContentBuilder.BuildPageWithPageSetup"/> now accept an optional
/// <see cref="ITextMeasurer"/>, defaulting to the existing heuristic when omitted (so every pre-existing
/// caller/test is unaffected) but honored end to end when supplied.
/// </summary>
public sealed class R151_PdfTextMeasurerInjectionTests
{
    /// <summary>
    /// Deterministic fake that reports a fixed width for every non-empty string, regardless of its
    /// length or the default heuristic's length-based formula -- so a test can assert the exact,
    /// independently-known effect of the reported width on the computed draw position.
    /// </summary>
    private sealed class FixedWidthTextMeasurer : ITextMeasurer
    {
        private readonly double _width;
        public FixedWidthTextMeasurer(double width) => _width = width;

        public TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic) =>
            string.IsNullOrEmpty(text) ? TextSize.Empty : new TextSize(_width, fontSize * 1.2);
    }

    [Fact]
    public void BuildWithPageSetup_InjectedMeasurer_DrivesRightAlignedCellTextX_ByExactlyItsOwnWidthDelta()
    {
        // Right-aligned text X = cellRight - measuredWidth - (fixed pad/indent, identical across both
        // builds below). So swapping only the measurer's reported width from 50 to 80 must shift X by
        // EXACTLY -30 -- proving BuildWithPageSetup actually threads the supplied ITextMeasurer into the
        // cell-text alignment math, rather than always consulting its own internal heuristic regardless
        // of what is passed in (which is what happened pre-fix: there was no such parameter at all).
        var xWith50 = BuildSingleRightAlignedCellTextX(new FixedWidthTextMeasurer(50.0));
        var xWith80 = BuildSingleRightAlignedCellTextX(new FixedWidthTextMeasurer(80.0));

        (xWith50 - xWith80).Should().BeApproximately(30.0, 0.01,
            "the right-aligned cell's X must move left by exactly the injected measurer's extra " +
            "reported width, proving the supplied measurer -- not a hardcoded heuristic -- drives the " +
            "position");
    }

    [Fact]
    public void BuildWithPageSetup_NoMeasurerSupplied_StillMatchesTheOriginalHeuristic_NoRegression()
    {
        // Sibling no-regression check: every pre-existing caller (the dependency-free portable path,
        // every test that calls BuildWithPageSetup without a measurer argument) must keep computing
        // exactly the same X it always did -- length * fontSize * 0.54 for a non-bold run, the same
        // formula R87_HeaderFooterPdfTests independently re-derives for the header/footer call site.
        var (defaultX, fontSize) = BuildSingleRightAlignedCellTextXAndFontSize(measurer: null);
        var xWithFixed80 = BuildSingleRightAlignedCellTextX(new FixedWidthTextMeasurer(80.0));

        var heuristicWidth = "42".Length * fontSize * 0.54; // non-bold, matches PortablePdfTextMeasurer.
        var expectedDelta = 80.0 - heuristicWidth; // defaultX - xWithFixed80 == fixedWidth - heuristicWidth... see below.

        // defaultX = cellRight - heuristicWidth - pad; xWithFixed80 = cellRight - 80 - pad.
        // defaultX - xWithFixed80 = 80 - heuristicWidth.
        (defaultX - xWithFixed80).Should().BeApproximately(expectedDelta, 0.01,
            "omitting the textMeasurer argument must preserve the exact pre-existing heuristic-driven " +
            "position (length * fontSize * 0.54 for a non-bold run) so no existing caller or pinned " +
            "test observes a behavior change");
    }

    private static double BuildSingleRightAlignedCellTextX(ITextMeasurer? measurer) =>
        BuildSingleRightAlignedCellTextXAndFontSize(measurer).X;

    private static (double X, double FontSize) BuildSingleRightAlignedCellTextXAndFontSize(ITextMeasurer? measurer)
    {
        var workbook = new Workbook("Measure");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ColumnWidths[1] = 40; // wide column so the right-aligned pad math is unambiguous.

        var style = workbook.RegisterStyle(new CellStyle { HorizontalAlignment = HorizontalAlignment.Right });
        var cell = Cell.FromValue(new NumberValue(42));
        cell.StyleId = style;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        exportPlan.IsReady.Should().BeTrue(exportPlan.StatusText);

        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);

        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan, textMeasurer: measurer);
        doc.Pages.Should().NotBeEmpty();

        var op = doc.Pages[0].Ops.OfType<PdfText>().First(t => t.Text.Contains("42"));
        return (op.X, op.FontSize);
    }
}
