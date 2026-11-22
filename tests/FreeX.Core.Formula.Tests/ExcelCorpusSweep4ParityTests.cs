using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regressions found by the 2026-11 real-workbook corpus re-sweep, each verified against real
/// desktop Excel (COM, en-US):
///  * approximate-match VLOOKUP/HLOOKUP/MATCH/LOOKUP binary-search (unsorted data) semantics,
///  * HYPERLINK link_location longer than 255 characters is #VALUE!,
///  * TEXT(number,"") is "",
///  * GETPIVOTDATA matches a numeric source header by its displayed text ("1844.00").
/// </summary>
public sealed class ExcelCorpusSweep4ParityTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook wb, Sheet sheet) Column(params double[] values)
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        for (var i = 0; i < values.Length; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 1), new NumberValue(values[i]));
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 2), new NumberValue((i + 1) * 100));
        }

        wb.DefineNamedRange("Tbl", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, (uint)values.Length, 2)));
        return (wb, sheet);
    }

    // Expected values below were read back from real Excel for these exact unsorted vectors.
    [Theory]
    [InlineData(new double[] { 13, 9, 26, 25, 6 }, 26, 3)]
    [InlineData(new double[] { 13, 9, 12, 6, 10, 15, 27, 18, 6, 17, 8 }, 15, 6)]
    [InlineData(new double[] { 24, 5, 28, 15, 12, 16, 15, 23 }, 16, 6)]
    [InlineData(new double[] { 21, 7, 13, 11, 8 }, 13, 3)]
    [InlineData(new double[] { 4, 26, 12, 24, 10, 26, 1, 22 }, 18, 1)]
    [InlineData(new double[] { 4, 26, 12, 24, 10, 26, 1, 22 }, 5, 1)]
    [InlineData(new double[] { 2, 25, 5, 16 }, 21, 1)]
    public void ApproximateLookups_UnsortedData_FollowExcelBinarySearch(double[] data, double key, int expectedPosition)
    {
        var (wb, sheet) = Column(data);
        var last = data.Length;
        var k = key.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Direct-range fast paths.
        _eval.Evaluate($"=MATCH({k},A1:A{last},1)", sheet, wb).Should().Be(new NumberValue(expectedPosition));
        _eval.Evaluate($"=VLOOKUP({k},A1:B{last},2,TRUE)", sheet, wb).Should().Be(new NumberValue(expectedPosition * 100));
        // Named-range (general) paths.
        _eval.Evaluate($"=MATCH({k},INDEX(Tbl,0,1),1)", sheet, wb).Should().Be(new NumberValue(expectedPosition));
        _eval.Evaluate($"=VLOOKUP({k},Tbl,2,TRUE)", sheet, wb).Should().Be(new NumberValue(expectedPosition * 100));
    }

    [Fact]
    public void Vlookup_UnsortedText_UsesBinarySearch()
    {
        // Excel: VLOOKUP("Australian Dollar", unsorted currency table) lands on the binary-probe row.
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        string[] keys = ["Zebra", "Apple", "Mango", "Banana", "Cherry", "Peach"];
        for (var i = 0; i < keys.Length; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 1), new TextValue(keys[i]));
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 2), new NumberValue(i + 1));
        }

        // Excel (verified): the binary probe visits "Mango" then "Zebra", both greater than the key,
        // so VLOOKUP yields #N/A (a linear last-<= scan would wrongly return Banana's row 4).
        _eval.Evaluate("=VLOOKUP(\"Banana\",A1:B6,2,TRUE)", sheet, wb).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=VLOOKUP(\"Cherry\",A1:B6,2,TRUE)", sheet, wb).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=MATCH(\"Peach\",A1:A6,1)", sheet, wb).Should().Be(new NumberValue(6));
        _eval.Evaluate("=LOOKUP(\"Banana\",A1:A6,B1:B6)", sheet, wb).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Hyperlink_LinkLongerThan255_IsValueError()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        _eval.Evaluate("=HYPERLINK(REPT(\"a\",256),\"x\")", sheet, wb).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=HYPERLINK(REPT(\"a\",300))", sheet, wb).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=HYPERLINK(REPT(\"a\",255),\"x\")", sheet, wb).Should().Be(new TextValue("x"));
    }

    [Fact]
    public void Text_NumberWithEmptyFormat_IsEmptyString()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2029480));

        _eval.Evaluate("=TEXT(A1,\"\")", sheet, wb).Should().Be(new TextValue(""));
        _eval.Evaluate("=TEXT(\"abc\",\"\")", sheet, wb).Should().Be(new TextValue("abc"));
        // Numeric-looking text is coerced to a number first, so it also yields "" (Excel verified).
        _eval.Evaluate("=TEXT(\"2029480\",\"\")", sheet, wb).Should().Be(new TextValue(""));
        _eval.Evaluate("=TEXT(A1,\"General\")", sheet, wb).Should().Be(new TextValue("2029480"));
    }

    [Fact]
    public void GetPivotData_NumericSourceHeader_MatchesDisplayedText()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var headerStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "0.00" });

        // Source header A1 is the NUMBER 1844 shown as "1844.00" (Excel's cache field name).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1844));
        sheet.GetCell(1, 1)!.StyleId = headerStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Client"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(45));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("West"));

        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new TextValue("Client"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 6), new TextValue("Work (h)"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 5), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 6), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 5), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 6), new NumberValue(45));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 5), new TextValue("Grand Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 6), new NumberValue(70));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 8, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(0, "Work (h)", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"1844.00\",E5,\"Client\",\"West\")", sheet, wb).Should().Be(new NumberValue(45));
        _eval.Evaluate("=GETPIVOTDATA(\"Work (h)\",E5,\"Client\",\"West\")", sheet, wb).Should().Be(new NumberValue(45));
        _eval.Evaluate("=GETPIVOTDATA(\"1844\",E5,\"Client\",\"West\")", sheet, wb).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void NamedSingleEmptyCell_TopLevel_IsZeroLikeABareReference()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var other = wb.AddSheet("Other");
        wb.DefineNamedRange("Supplier", new GridRange(new CellAddress(other.Id, 3, 3), new CellAddress(other.Id, 3, 3)));

        // Excel (verified on a corpus workbook): =Supplier over an empty single cell displays 0.
        _eval.Evaluate("=Supplier", sheet, wb).Should().Be(new NumberValue(0));
    }
}
