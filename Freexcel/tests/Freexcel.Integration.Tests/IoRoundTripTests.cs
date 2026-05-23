using System.IO;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Integration.Tests;

public class IoRoundTripTests
{
    [Fact]
    public void Csv_RoundTrip_PreservesNumbersAndText()
    {
        var wb    = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(3.14));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("a,b")); // needs quoting

        var adapter = new CsvFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(wb, ms);
        ms.Position = 0;

        var wb2    = adapter.Load(ms);
        var sheet2 = wb2.Sheets[0];

        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 1)).Should().Be(new NumberValue(3.14));
        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 2)).Should().Be(new TextValue("hello"));
        sheet2.GetValue(new CellAddress(sheet2.Id, 2, 1)).Should().Be(new TextValue("a,b"),
            "comma inside a cell should survive CSV round-trip via RFC 4180 quoting");
    }

    [Fact]
    public void Csv_RoundTrip_PreservesDateAndTimeValues()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays));

        var adapter = new CsvFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(wb, ms);
        ms.Position = 0;

        var wb2 = adapter.Load(ms);
        var sheet2 = wb2.Sheets[0];

        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 1)).Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 2)).Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 3)).Should().Be(new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays));
    }

    [Fact]
    public void Csv_RoundTrip_PreservesLeadingBlankColumns()
    {
        var adapter = new CsvFileAdapter();
        using var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(",offset\r\n"));

        var workbook = adapter.Load(source);
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        System.Text.Encoding.UTF8.GetString(saved.ToArray()).Should().Be(",offset\r\n");
    }

    [Fact]
    public void Json_RoundTrip_PreservesValuesAndFormulas()
    {
        var wb    = new Workbook("Book1");
        var sheet = wb.AddSheet("Sheet1");
        var a1    = new CellAddress(sheet.Id, 1, 1);
        var b1    = new CellAddress(sheet.Id, 1, 2);
        var c1    = new CellAddress(sheet.Id, 1, 3);

        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetFormula(b1, "A1*2");
        sheet.SetCell(c1, new TextValue("hello"));

        var adapter = new NativeJsonAdapter();
        using var ms = new MemoryStream();
        adapter.Save(wb, ms);
        ms.Position = 0;

        var wb2    = adapter.Load(ms);
        wb2.Name.Should().Be("Book1");
        var sheet2 = wb2.Sheets[0];
        sheet2.Name.Should().Be("Sheet1");

        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 1)).Should().Be(new NumberValue(5));
        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 3)).Should().Be(new TextValue("hello"));

        // Formula text is preserved — the loaded cell should carry the formula
        var b1Loaded = sheet2.GetCell(new CellAddress(sheet2.Id, 1, 2));
        b1Loaded.Should().NotBeNull();
        b1Loaded!.HasFormula.Should().BeTrue("formula text must survive a JSON round-trip");
        b1Loaded.FormulaText.Should().Be("A1*2");
    }

    [Fact]
    public void Json_RoundTrip_MultiSheet_PreservesSheetNames()
    {
        var wb = new Workbook("Multi");
        wb.AddSheet("Alpha");
        wb.AddSheet("Beta");

        var adapter = new NativeJsonAdapter();
        using var ms = new MemoryStream();
        adapter.Save(wb, ms);
        ms.Position = 0;

        var wb2 = adapter.Load(ms);
        wb2.Sheets.Should().HaveCount(2);
        wb2.Sheets[0].Name.Should().Be("Alpha");
        wb2.Sheets[1].Name.Should().Be("Beta");
    }
}
