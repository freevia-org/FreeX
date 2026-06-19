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

    [Fact]
    public void CustomizedDefaultStyleFont_SurvivesNativeJsonRoundTrip()
    {
        // Mirror the XLSX loader shape: workbook default (style 0) customized to a
        // theme minor font (e.g. "Aptos Narrow"). Style-0 cells must keep this font
        // across an fxl save/load instead of reverting to the built-in Calibri default.
        var defaultStyle = new CellStyle
        {
            FontName = "Aptos Narrow",
            FontSize = 11,
            FontScheme = CellFontScheme.Minor,
        };
        var wb = new Workbook("T", defaultStyle);
        var sheet = wb.AddSheet("S");
        var addr = new CellAddress(sheet.Id, 1, 1);

        // Cell uses the default style (StyleId 0) implicitly.
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(42)));
        wb.GetStyle(StyleId.Default).FontName.Should().Be("Aptos Narrow");

        var reloaded = RoundTrip(wb);

        var reloadedDefault = reloaded.GetStyle(StyleId.Default);
        reloadedDefault.FontName.Should().Be("Aptos Narrow",
            "the customized workbook default font must survive an fxl round-trip");
        reloadedDefault.FontScheme.Should().Be(CellFontScheme.Minor,
            "the customized workbook default font scheme must survive an fxl round-trip");

        var reloadedCell = reloaded.Sheets[0].GetCell(addr);
        reloadedCell.Should().NotBeNull();
        reloaded.GetStyle(reloadedCell!.StyleId).FontName.Should().Be("Aptos Narrow");
    }

    [Fact]
    public void UncustomizedDefaultStyle_DoesNotEmitDefaultStyleField()
    {
        // A plain workbook (built-in Calibri default) must not regress: no DefaultStyle
        // payload, and style-0 cells still resolve to the default.
        var wb = new Workbook("Plain");
        var sheet = wb.AddSheet("S");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(1)));

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(wb, stream);
        using var document = System.Text.Json.JsonDocument.Parse(stream.ToArray());
        document.RootElement.TryGetProperty("DefaultStyle", out _)
            .Should().BeFalse("an uncustomized default style must not be persisted");

        stream.Position = 0;
        var reloaded = new NativeJsonAdapter().Load(stream);
        reloaded.GetStyle(StyleId.Default).Should().Be(CellStyle.Default);
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
