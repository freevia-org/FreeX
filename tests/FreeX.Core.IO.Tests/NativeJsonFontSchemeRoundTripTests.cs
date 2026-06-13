using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Verifies that CellStyle.FontScheme round-trips through the native JSON (.fxl) format
/// via NativeJsonAdapter (CellStyleDto.FontScheme field).
/// </summary>
public sealed class NativeJsonFontSchemeRoundTripTests
{
    [Theory]
    [InlineData(CellFontScheme.Minor)]
    [InlineData(CellFontScheme.Major)]
    [InlineData(CellFontScheme.None)]
    public void FontScheme_RoundTripsViaNativeJson(CellFontScheme scheme)
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

        var reloaded = RoundTrip(wb);

        var reloadedCell = reloaded.Sheets[0].GetCell(addr);
        reloadedCell.Should().NotBeNull();
        var reloadedStyle = reloaded.GetStyle(reloadedCell!.StyleId);
        reloadedStyle.FontScheme.Should().Be(scheme,
            $"FontScheme.{scheme} must survive a NativeJson save/load round-trip");
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
