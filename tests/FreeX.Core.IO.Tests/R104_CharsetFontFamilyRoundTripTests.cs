using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R104: verifies that CellStyle.Charset and CellStyle.FontFamily -- OOXML font fidelity
/// codes that XlsxClosedXmlCellMapper reads/writes on real .xlsx files (e.g. charset=2 for a
/// Symbol/Wingdings-encoded font) -- survive a native JSON (.fxl) save/load round-trip via
/// NativeJsonAdapter, instead of silently reverting to the CellStyle class defaults
/// (Charset=1, FontFamily=2) on reload.
/// </summary>
public sealed class R104_CharsetFontFamilyRoundTripTests
{
    [Fact]
    public void CharsetAndFontFamily_RoundTripViaNativeJson_ForRegisteredCellStyle()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var addr = new CellAddress(sheet.Id, 1, 1);

        // Non-default OOXML charset/family codes, as would be loaded from a real .xlsx
        // whose font carries <charset val="2"/> (Symbol) and <family val="1"/> (Roman).
        var styleId = wb.RegisterStyle(new CellStyle
        {
            FontName = "Wingdings",
            FontSize = 11,
            Charset = 2,
            FontFamily = 1,
        });
        var cell = Cell.FromValue(new NumberValue(42));
        cell.StyleId = styleId;
        sheet.SetCell(addr, cell);

        var reloaded = RoundTrip(wb);

        var reloadedCell = reloaded.Sheets[0].GetCell(addr);
        reloadedCell.Should().NotBeNull();
        var reloadedStyle = reloaded.GetStyle(reloadedCell!.StyleId);
        reloadedStyle.Charset.Should().Be(2,
            "Charset must survive a NativeJson (.fxl) save/load round-trip, matching the OOXML fidelity XlsxClosedXmlCellMapper depends on");
        reloadedStyle.FontFamily.Should().Be(1,
            "FontFamily must survive a NativeJson (.fxl) save/load round-trip, matching the OOXML fidelity XlsxClosedXmlCellMapper depends on");
    }

    [Fact]
    public void DefaultCharsetAndFontFamily_SurviveNativeJsonRoundTrip_NoRegression()
    {
        // Sibling/no-regression coverage: a style that never customizes Charset/FontFamily
        // (the overwhelmingly common case) must keep reading back as the class defaults,
        // and must not force those fields to be persisted in the JSON payload.
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var addr = new CellAddress(sheet.Id, 1, 1);

        var styleId = wb.RegisterStyle(new CellStyle
        {
            FontName = "Calibri",
            FontSize = 11,
            Bold = true,
        });
        var cell = Cell.FromValue(new NumberValue(7));
        cell.StyleId = styleId;
        sheet.SetCell(addr, cell);

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(wb, stream);

        stream.Position = 0;
        var reloaded = adapter.Load(stream);
        var reloadedCell = reloaded.Sheets[0].GetCell(addr);
        reloadedCell.Should().NotBeNull();
        var reloadedStyle = reloaded.GetStyle(reloadedCell!.StyleId);
        reloadedStyle.Charset.Should().Be(1);
        reloadedStyle.FontFamily.Should().Be(2);
        reloadedStyle.Bold.Should().BeTrue("unrelated fields must remain unaffected");
    }

    private static Workbook RoundTrip(Workbook source)
    {
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(source, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }
}
