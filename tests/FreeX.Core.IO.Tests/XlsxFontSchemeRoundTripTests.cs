using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Verifies that CellStyle.FontScheme round-trips through the XLSX format via ClosedXML
/// (XLFontScheme.Minor / Major / None mapping in XlsxClosedXmlCellMapper).
/// </summary>
public sealed class XlsxFontSchemeRoundTripTests
{
    [Theory]
    [InlineData(CellFontScheme.Minor)]
    [InlineData(CellFontScheme.Major)]
    [InlineData(CellFontScheme.None)]
    public void FontScheme_RoundTripsViaXlsx(CellFontScheme scheme)
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var addr = new CellAddress(sheet.Id, 1, 1);

        var styleId = wb.RegisterStyle(new CellStyle
        {
            FontName = "Calibri",
            FontSize = 11,
            FontScheme = scheme,
        });
        var cell = Cell.FromValue(new NumberValue(42));
        cell.StyleId = styleId;
        sheet.SetCell(addr, cell);

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(wb, ms);
        ms.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(ms);

        var reloadedCell = reloaded.Sheets[0].GetCell(addr);
        reloadedCell.Should().NotBeNull();
        var reloadedStyle = reloaded.GetStyle(reloadedCell!.StyleId);
        reloadedStyle.FontScheme.Should().Be(scheme,
            $"FontScheme.{scheme} must survive an XLSX save/load round-trip");
    }
}
