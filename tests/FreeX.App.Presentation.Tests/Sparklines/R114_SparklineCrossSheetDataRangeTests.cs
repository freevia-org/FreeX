using FluentAssertions;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Sparklines;

/// <summary>
/// R114 finding: Excel's "Edit Data" dialog lets a sparkline's Data Range live on a different sheet
/// than the one it is anchored/displayed on (e.g. a sparkline hosted on Sheet1 whose data is
/// Sheet2!$A$1:$E$1), and <c>XlsxSparklineMapper</c> round-trips that cross-sheet reference (see its
/// doc comments). Pre-fix, <see cref="SparklineSeriesReader.ReadSeries"/> read every cell of
/// <see cref="SparklineModel.DataRange"/> via the HOST sheet only, ignoring
/// <see cref="SparklineModel.DataRange"/>'s own <c>Start.Sheet</c> -- so a cross-sheet sparkline
/// silently pulled whatever happened to sit at the same row/col coordinates on the host sheet
/// instead of the real source sheet.
/// </summary>
public sealed class R114_SparklineCrossSheetDataRangeTests
{
    [Fact]
    public void ReadSeries_CrossSheetDataRange_ReadsFromSourceSheetNotHostSheet()
    {
        var workbook = new Workbook();
        var hostSheet = workbook.AddSheet("Sheet1");
        var dataSheet = workbook.AddSheet("Sheet2");

        // Host sheet has DIFFERENT values at the same A1:E1 coordinates the sparkline's data range
        // uses -- if the reader ever falls back to reading the host sheet by row/col, this test's
        // assertion (Sheet2's own values) fails.
        for (uint col = 1; col <= 5; col++)
            hostSheet.SetCell(new CellAddress(hostSheet.Id, 1, col), new NumberValue(900 + col));

        for (uint col = 1; col <= 5; col++)
            dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, col), new NumberValue(col * 10));

        var sparkline = new SparklineModel
        {
            Kind = SparklineKind.Line,
            // Data range's own sheet is Sheet2 (dataSheet), NOT the host sheet the sparkline is
            // drawn on (Sheet1/hostSheet) -- mirrors Excel's cross-sheet sparkline data range.
            DataRange = new GridRange(
                new CellAddress(dataSheet.Id, 1, 1),
                new CellAddress(dataSheet.Id, 1, 5)),
            Location = new CellAddress(hostSheet.Id, 2, 1),
        };
        hostSheet.Sparklines.Add(sparkline);

        var series = SparklineSeriesReader.ReadSeries(workbook, hostSheet, sparkline);

        series.Should().Equal(new double[] { 10, 20, 30, 40, 50 },
            "the sparkline's data range lives on Sheet2, so its values -- not Sheet1's -- must be read");
    }

    [Fact]
    public void BuildValues_MixOfSameSheetAndCrossSheetSparklines_ResolvesEachIndependently()
    {
        var workbook = new Workbook();
        var hostSheet = workbook.AddSheet("Sheet1");
        var otherSheet = workbook.AddSheet("Sheet2");

        hostSheet.SetCell(new CellAddress(hostSheet.Id, 1, 1), new NumberValue(1));
        hostSheet.SetCell(new CellAddress(hostSheet.Id, 1, 2), new NumberValue(2));
        otherSheet.SetCell(new CellAddress(otherSheet.Id, 1, 1), new NumberValue(100));
        otherSheet.SetCell(new CellAddress(otherSheet.Id, 1, 2), new NumberValue(200));

        var sameSheetSparkline = new SparklineModel
        {
            Kind = SparklineKind.Line,
            DataRange = new GridRange(
                new CellAddress(hostSheet.Id, 1, 1),
                new CellAddress(hostSheet.Id, 1, 2)),
            Location = new CellAddress(hostSheet.Id, 2, 1),
        };
        var crossSheetSparkline = new SparklineModel
        {
            Kind = SparklineKind.Line,
            DataRange = new GridRange(
                new CellAddress(otherSheet.Id, 1, 1),
                new CellAddress(otherSheet.Id, 1, 2)),
            Location = new CellAddress(hostSheet.Id, 3, 1),
        };
        hostSheet.Sparklines.Add(sameSheetSparkline);
        hostSheet.Sparklines.Add(crossSheetSparkline);

        var values = SparklineSeriesReader.BuildValues(workbook, hostSheet);

        values[sameSheetSparkline.Id].Should().Equal(new double[] { 1, 2 },
            "a same-sheet sparkline (data range's sheet == host sheet) must keep reading its own sheet");
        values[crossSheetSparkline.Id].Should().Equal(new double[] { 100, 200 },
            "a cross-sheet sparkline's data range must resolve to its OWN sheet even though a sibling on the same host sheet is same-sheet");
    }

    [Fact]
    public void ReadSeries_CrossSheetDataRange_UsesSourceSheetsHiddenRowsNotHostSheets()
    {
        var workbook = new Workbook();
        var hostSheet = workbook.AddSheet("Sheet1");
        var dataSheet = workbook.AddSheet("Sheet2");

        for (uint row = 1; row <= 3; row++)
            dataSheet.SetCell(new CellAddress(dataSheet.Id, row, 1), new NumberValue(row));

        // Row 2 is hidden on the DATA sheet -- not on the host sheet, which has no hidden rows at
        // all -- so if hidden-row filtering incorrectly checked the host sheet, row 2 would NOT be
        // skipped and the series would have 3 values instead of 2.
        dataSheet.HiddenRows.Add(2);

        var sparkline = new SparklineModel
        {
            Kind = SparklineKind.Line,
            DataRange = new GridRange(
                new CellAddress(dataSheet.Id, 1, 1),
                new CellAddress(dataSheet.Id, 3, 1)),
            Location = new CellAddress(hostSheet.Id, 1, 2),
        };
        hostSheet.Sparklines.Add(sparkline);

        var series = SparklineSeriesReader.ReadSeries(workbook, hostSheet, sparkline);

        series.Should().Equal(new double[] { 1, 3 },
            "hidden-row filtering must apply to the data range's OWN sheet, not the host sheet");
    }

    [Fact]
    public void ReadSeries_DataRangeSheetNoLongerInWorkbook_FallsBackToHostSheetInsteadOfThrowing()
    {
        // Defends the fallback path: if the data range's sheet id doesn't resolve in the owning
        // workbook (e.g. the source sheet was deleted after the sparkline was created), the reader
        // must fall back to the host sheet rather than throwing or silently producing garbage.
        var workbook = new Workbook();
        var hostSheet = workbook.AddSheet("Sheet1");
        var deletedSheetId = SheetId.New();

        hostSheet.SetCell(new CellAddress(hostSheet.Id, 1, 1), new NumberValue(7));

        var sparkline = new SparklineModel
        {
            Kind = SparklineKind.Line,
            DataRange = new GridRange(
                new CellAddress(deletedSheetId, 1, 1),
                new CellAddress(deletedSheetId, 1, 1)),
            Location = new CellAddress(hostSheet.Id, 2, 1),
        };
        hostSheet.Sparklines.Add(sparkline);

        var series = SparklineSeriesReader.ReadSeries(workbook, hostSheet, sparkline);

        series.Should().Equal(new double[] { 7 },
            "an unresolvable data-range sheet id must fall back to the host sheet, not throw or silently drop the series");
    }
}
