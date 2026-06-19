using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests for the SYLK (.slk) adapter: single-sheet, value + R1C1-formula round-trips, the doubled-";"
/// field escape, and the coarse number-format subset on value-bearing cells. Formula references reuse
/// the shared <see cref="R1C1FormulaConverter"/>.
/// </summary>
public sealed class SlkFileAdapterTests
{
    private static (Workbook Workbook, Sheet Sheet) RoundTrip(Workbook source)
    {
        var adapter = new SlkFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(source, stream);
        stream.Position = 0;
        var wb = adapter.Load(stream);
        return (wb, wb.Sheets.Single());
    }

    private static Workbook NewWorkbook(out Sheet sheet)
    {
        var wb = new Workbook("Untitled");
        sheet = wb.AddSheet("Sheet1");
        return wb;
    }

    [Fact]
    public void RoundTrips_NumberTextBoolErrorValues()
    {
        var wb = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(3.14));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), ErrorValue.DivByZero);

        var (_, got) = RoundTrip(wb);
        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new NumberValue(3.14));
        got.GetValue(new CellAddress(got.Id, 1, 2)).Should().Be(new TextValue("hello"));
        got.GetValue(new CellAddress(got.Id, 2, 1)).Should().Be(new BoolValue(true));
        got.GetValue(new CellAddress(got.Id, 2, 2)).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void RoundTrips_A1FormulaThroughR1C1()
    {
        var wb = NewWorkbook(out var sheet);
        // B2 = A2 + the cell above it; relative refs exercise the R1C1 conversion.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("A2+B1"));

        var (_, got) = RoundTrip(wb);
        var cell = got.GetCell(new CellAddress(got.Id, 2, 2))!;
        cell.HasFormula.Should().BeTrue();
        cell.FormulaText.Should().Be("A2+B1");
    }

    [Fact]
    public void RoundTrips_AbsoluteFormulaReferences()
    {
        var wb = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), Cell.FromFormula("$A$1*C2"));

        var (_, got) = RoundTrip(wb);
        got.GetCell(new CellAddress(got.Id, 3, 3))!.FormulaText.Should().Be("$A$1*C2");
    }

    [Fact]
    public void RoundTrips_NumberFormatOnValueBearingCell()
    {
        var wb = NewWorkbook(out var sheet);
        var styleId = wb.RegisterStyle(new CellStyle { NumberFormat = "0.00%" });
        var cell = Cell.FromValue(new NumberValue(0.5));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var (gotWb, got) = RoundTrip(wb);
        var gotCell = got.GetCell(new CellAddress(got.Id, 1, 1))!;
        gotWb.GetStyle(gotCell.StyleId).NumberFormat.Should().Be("0.00%");
    }

    [Fact]
    public void EscapesSemicolonsInTextValues()
    {
        var wb = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a;b;c"));

        var (_, got) = RoundTrip(wb);
        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new TextValue("a;b;c"));
    }

    [Fact]
    public void Save_EmitsIdHeaderAndEndRecord()
    {
        var wb = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        using var stream = new MemoryStream();
        new SlkFileAdapter().Save(wb, stream);
        var text = Encoding.UTF8.GetString(stream.ToArray());

        text.Should().StartWith("ID;P");
        text.TrimEnd().Should().EndWith("E");
        text.Should().Contain("C;Y1;X1;K1");
    }

    [Fact]
    public void Load_IgnoresUnknownRecordsAndStopsAtEnd()
    {
        var slk = "ID;PWXL\r\nB;Y1;X1\r\nO;L\r\nC;Y1;X1;K42\r\nE\r\nC;Y2;X1;K99\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(slk));

        var sheet = new SlkFileAdapter().Load(stream).Sheets.Single();
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(42));
        // The cell after the E (end) record must not be loaded.
        sheet.GetCell(new CellAddress(sheet.Id, 2, 1)).Should().BeNull();
    }
}
