using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R100: a chart with a grouped/multi-level category axis (Excel's
/// &lt;c:cat&gt;&lt;c:multiLvlStrRef&gt;&lt;c:f&gt;...&lt;/c:f&gt;) captures that raw XML verbatim on
/// load (see <see cref="ChartModel.MultiLevelCategoryXml"/>) and re-emits it verbatim on save in
/// preference to the recomputed &lt;c:cat&gt;. Prior to this fix, RewriteChartVerbatimFormulas
/// rewrote every other verbatim chart formula collection (VerbatimSeriesFormulas,
/// SeriesRangeDataLabels, error-bar range formulas) on structural row/column insert/delete, but
/// never touched MultiLevelCategoryXml — so the embedded &lt;c:f&gt; formula kept pointing at the
/// pre-edit cells after the edit while the ordinary series ranges correctly shifted.
/// </summary>
public sealed class R100_MultiLevelCategoryAxisShiftTests
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // Builds the raw XML exactly as XlsxChartSeriesRangeReader.CaptureSeriesRoundTripMetadata
    // captures it: cat.ToString(SaveOptions.DisableFormatting) where cat is <c:cat><c:multiLvlStrRef>
    // <c:f>...</c:f>...</c:multiLvlStrRef></c:cat>.
    private static string BuildMultiLevelCategoryXml(string formula) =>
        new XElement(C + "cat",
            new XElement(C + "multiLvlStrRef",
                new XElement(C + "f", formula),
                new XElement(C + "multiLvlStrCache",
                    new XElement(C + "ptCount", new XAttribute("val", "2")))))
        .ToString(SaveOptions.DisableFormatting);

    private static string? ExtractFormula(string rawXml)
    {
        var element = XElement.Parse(rawXml);
        return element.Descendants(C + "f").FirstOrDefault()?.Value;
    }

    [Fact]
    public void InsertRows_ShiftsMultiLevelCategoryXmlFormula_AndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        const string originalFormula = "Sheet1!$A$2:$B$10";
        var chart = new ChartModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 2, 1),
                new CellAddress(sheet.Id, 10, 2)),
            Type = ChartType.Column,
            MultiLevelCategoryXml =
            [
                new ChartSeriesRawXmlEntry(0, BuildMultiLevelCategoryXml(originalFormula))
            ]
        };
        sheet.Charts.Add(chart);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        cmd.Apply(ctx);

        var afterInsert = chart.MultiLevelCategoryXml.Should().ContainSingle().Subject;
        ExtractFormula(afterInsert.RawXml).Should().Be("Sheet1!$A$3:$B$11",
            because: "inserting a row before row 1 must shift the embedded <c:f> just like the " +
                     "ordinary series/value formulas do, so the multi-level category axis still " +
                     "points at the post-edit cells");

        cmd.Revert(ctx);

        var afterUndo = chart.MultiLevelCategoryXml.Should().ContainSingle().Subject;
        ExtractFormula(afterUndo.RawXml).Should().Be(originalFormula,
            because: "undo must restore the original verbatim multi-level category XML");
    }

    [Fact]
    public void InsertColumns_ShiftsMultiLevelCategoryXmlFormula_AndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        const string originalFormula = "Sheet1!$B$1:$C$10";
        var chart = new ChartModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 2),
                new CellAddress(sheet.Id, 10, 3)),
            Type = ChartType.Column,
            MultiLevelCategoryXml =
            [
                new ChartSeriesRawXmlEntry(0, BuildMultiLevelCategoryXml(originalFormula))
            ]
        };
        sheet.Charts.Add(chart);

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1);
        cmd.Apply(ctx);

        var afterInsert = chart.MultiLevelCategoryXml.Should().ContainSingle().Subject;
        ExtractFormula(afterInsert.RawXml).Should().Be("Sheet1!$C$1:$D$10",
            because: "inserting a column before column A must shift the embedded <c:f> the same " +
                     "way an insert row shifts it, so both structural axes are covered");

        cmd.Revert(ctx);

        var afterUndo = chart.MultiLevelCategoryXml.Should().ContainSingle().Subject;
        ExtractFormula(afterUndo.RawXml).Should().Be(originalFormula,
            because: "undo must restore the original verbatim multi-level category XML");
    }

    [Fact]
    public void DeleteRows_ShiftsMultiLevelCategoryXmlFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        const string originalFormula = "Sheet1!$A$5:$B$10";
        var chart = new ChartModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 5, 1),
                new CellAddress(sheet.Id, 10, 2)),
            Type = ChartType.Column,
            MultiLevelCategoryXml =
            [
                new ChartSeriesRawXmlEntry(0, BuildMultiLevelCategoryXml(originalFormula))
            ]
        };
        sheet.Charts.Add(chart);

        // Delete row 1 (before the referenced range) — the range shifts up by one row.
        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 1, count: 1);
        cmd.Apply(ctx);

        var afterDelete = chart.MultiLevelCategoryXml.Should().ContainSingle().Subject;
        ExtractFormula(afterDelete.RawXml).Should().Be("Sheet1!$A$4:$B$9",
            because: "deleting a row above the referenced range must shift the embedded <c:f> up, " +
                     "matching the DeleteRowsCommand sibling path to InsertRows/InsertColumns");
    }

    [Fact]
    public void InsertRows_LeavesOtherVerbatimCollectionsAndUnrelatedMultiLevelEntriesUntouched()
    {
        // No-regression sibling: an ordinary VerbatimSeriesFormulas entry on the SAME chart must
        // still shift exactly as before, and a MultiLevelCategoryXml entry whose formula does not
        // reference the edited sheet at all must be left byte-for-byte unchanged.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);

        const string unrelatedFormula = "Sheet2!$A$1:$B$5";
        var chart = new ChartModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet1.Id, 1, 1),
                new CellAddress(sheet1.Id, 5, 3)),
            Type = ChartType.Column,
            VerbatimSeriesFormulas =
            [
                new ChartSeriesVerbatimFormulas(
                    SeriesIndex: 0,
                    ValFormula: "(Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5)",
                    CatFormula: "Sheet1!$B$1:$B$5",
                    TxFormula:  null)
            ],
            MultiLevelCategoryXml =
            [
                new ChartSeriesRawXmlEntry(0, BuildMultiLevelCategoryXml(unrelatedFormula))
            ]
        };
        sheet1.Charts.Add(chart);

        var cmd = new InsertRowsCommand(sheet1.Id, beforeRow: 1, count: 1);
        cmd.Apply(ctx);

        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("(Sheet1!$A$2:$A$6,Sheet1!$C$2:$C$6)",
            because: "the pre-existing verbatim series formula shift behaviour must be unaffected " +
                     "by adding multi-level category shifting alongside it");

        ExtractFormula(chart.MultiLevelCategoryXml[0].RawXml).Should().Be(unrelatedFormula,
            because: "a multi-level category formula on a different sheet than the one being " +
                     "structurally edited must not be rewritten");
    }
}
