using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for review findings G17 (SLK round-trip of #SPILL!/#CALC!/#CIRCULAR!) and G23
/// (CSV/TSV decimal-comma corruption under dot-decimal cultures).
/// </summary>
public sealed class ImportFormatsGroupNRegressionTests
{
    // ---- G17: SLK must round-trip #SPILL!/#CALC!/#CIRCULAR! instead of demoting them to text -----

    private static (Workbook Workbook, Sheet Sheet) SlkRoundTrip(Workbook source)
    {
        var adapter = new SlkFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(source, stream);
        stream.Position = 0;
        var wb = adapter.Load(stream);
        return (wb, wb.Sheets.Single());
    }

    [Fact]
    public void Slk_RoundTrips_SpillCalcCircularErrorValues()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.Spill);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), ErrorValue.Calc);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), ErrorValue.Circular);

        var (_, got) = SlkRoundTrip(wb);

        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(ErrorValue.Spill);
        got.GetValue(new CellAddress(got.Id, 1, 2)).Should().Be(ErrorValue.Calc);
        got.GetValue(new CellAddress(got.Id, 1, 3)).Should().Be(ErrorValue.Circular);
    }

    [Fact]
    public void Slk_DoesNotDemoteSpillErrorToText()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.Spill);

        var (_, got) = SlkRoundTrip(wb);

        var value = got.GetValue(new CellAddress(got.Id, 1, 1));
        value.Should().BeOfType<ErrorValue>();
        value.Should().NotBe(new TextValue("#SPILL!"));
    }

    // ---- G23: CSV/TSV numeric coercion must not corrupt decimal-comma numbers under dot-decimal --
    // ---- cultures (e.g. "1234,56" under en-US must not become 123456). ----------------------------

    [Fact]
    public void Load_DoesNotCorruptDecimalCommaNumberUnderEnUsCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("1234,56\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        // Ambiguous/malformed grouping under en-US: must NOT silently become 123456 (a ~100x
        // corruption). Falls back to text since it is not a valid number under any supported culture.
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("1234,56"));
    }

    [Fact]
    public void Load_StillCoercesValidThousandsGroupedNumberUnderEnUsCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("1,234\t1,234.56\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(1234));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void Load_RejectsMalformedGroupingUnderEnUsCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("12,34\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("12,34"));
    }

    [Fact]
    public void Load_StillCoercesDecimalCommaNumbersUnderFrFrCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("fr-FR");
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("1234,56\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        // Under fr-FR (decimal separator is the comma), this is an unambiguous decimal number.
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void Load_StillCoercesGroupedAndDecimalNumbersUnderDeDeCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("1.234.567\t1.234,56\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(1234567));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(1234.56));
    }
}
