using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxHyperlinkFontTests
{
    // ClosedXML's SetHyperlink applies its built-in "Hyperlink" style (theme-10 blue font + underline) to a
    // cell, which would override the modelled font. Authors routinely restyle hyperlinks (e.g. black text,
    // or a struck-through red link with no underline), so the modelled font must survive the round-trip.
    [Fact]
    public void XlsxAdapter_RoundTrip_PreservesExplicitFontOnHyperlinkCells()
    {
        var workbook = new Workbook("HyperlinkFontTest");
        var sheet = workbook.AddSheet("S1");

        var blackAddr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(blackAddr, new TextValue("BlackLink"));
        sheet.GetCell(1, 1)!.StyleId = workbook.RegisterStyle(
            new CellStyle { Underline = true, FontColor = new CellColor(0, 0, 0) });
        sheet.Hyperlinks[blackAddr] = "https://example.com/black";

        var redAddr = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(redAddr, new TextValue("RedLink"));
        sheet.GetCell(2, 1)!.StyleId = workbook.RegisterStyle(
            new CellStyle { Italic = true, Strikethrough = true, FontColor = new CellColor(255, 0, 0) });
        sheet.Hyperlinks[redAddr] = "https://example.com/red";

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);
        var loadedSheet = loaded.GetSheetAt(0);

        var black = loaded.GetStyle(loadedSheet.GetCell(1, 1)!.StyleId);
        black.FontColor.Should().Be(new CellColor(0, 0, 0), "an explicit black hyperlink font must not become theme-10 blue");
        black.Underline.Should().BeTrue();

        var red = loaded.GetStyle(loadedSheet.GetCell(2, 1)!.StyleId);
        red.FontColor.Should().Be(new CellColor(255, 0, 0));
        red.Underline.Should().BeFalse("ClosedXML's hyperlink underline must not be forced onto a link the model leaves un-underlined");
        red.Italic.Should().BeTrue();
        red.Strikethrough.Should().BeTrue();
    }
}
