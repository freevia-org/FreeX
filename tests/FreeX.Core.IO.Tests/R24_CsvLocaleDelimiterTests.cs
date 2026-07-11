using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-24 regression test for R24-localization-parsing-1: real Excel's plain File&gt;Open/Save-As
/// ".csv" (no "sep=" directive) parses/writes using the OS Regional Settings "List separator", not
/// literally a comma -- on a de-DE/fr-FR/es-ES/etc. machine that separator is ';' because ',' is the
/// decimal mark. CsvFileAdapter used to hardcode ',' regardless of locale, so a genuine
/// semicolon-delimited European export with decimal-comma numbers (no "sep=" line, the common
/// real-world shape) was torn apart: each row's lone decimal comma was misread as an extra field
/// separator.
/// </summary>
public sealed class R24_CsvLocaleDelimiterTests
{
    [Fact]
    public void Load_UsesCurrentCultureListSeparatorWhenNoSeparatorDirectiveIsPresent()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");
        // No "sep=" directive: real Excel (and, after the fix, FreeX) falls back to the OS list
        // separator, ';', for a de-DE machine. Before the fix this hardcoded ',' and split each row's
        // decimal-comma price into a spurious extra column ("Apfel" / "1" / "50;3").
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name;Preis;Menge\r\nApfel;1,50;3\r\nBirne;2,25;5\r\n"));

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Preis"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new TextValue("Menge"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Apfel"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(1.5));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new NumberValue(3));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(new TextValue("Birne"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new NumberValue(2.25));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 3)).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Load_StillUsesCommaUnderAnEnglishListSeparatorCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name,Amount\r\nAlice,3.5\r\n"));

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
    }

    [Fact]
    public void Load_SeparatorDirectiveStillOverridesTheLocaleListSeparator()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");
        // An explicit "sep=," directive must win over the de-DE locale's ';' default.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=,\r\nName,Amount\r\nAlice,3.5\r\n"));

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
    }

    [Fact]
    public void Save_WritesFieldsWithTheCurrentCultureListSeparator()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apfel"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(3));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        var csv = Encoding.UTF8.GetString(stream.ToArray());
        csv.Should().Be("Apfel;3\r\n");
    }
}
