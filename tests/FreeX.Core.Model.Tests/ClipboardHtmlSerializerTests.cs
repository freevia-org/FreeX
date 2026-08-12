using System.Text;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class ClipboardHtmlSerializerTests
{
    [Fact]
    public void Serialize_ProducesStyledFragmentAndValidCfHtmlPayload()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var style = new CellStyle { Bold = true, FillColor = new CellColor(170, 187, 204) };
        var viewport = new ViewportModel(
            [new DisplayCell(1, 1, new TextValue("Bold Fill"), "Bold Fill", null, default, null, style)],
            [],
            []);

        var payload = ClipboardHtmlSerializer.Serialize(viewport, sheet, new GridRange(address, address), workbook.Theme);

        payload.Should().NotBeNull();
        payload!.Fragment.Should().Contain("<table");
        payload.Fragment.Should().Contain("font-weight:bold");
        payload.Fragment.Should().Contain("background-color:#AABBCC");
        payload.CfHtml.Should().StartWith("Version:0.9\r\n");

        var bytes = Encoding.UTF8.GetBytes(payload.CfHtml);
        var start = ParseOffset(payload.CfHtml, "StartFragment:");
        var end = ParseOffset(payload.CfHtml, "EndFragment:");
        Encoding.UTF8.GetString(bytes, start, end - start).Should().Be(payload.Fragment);
    }

    [Fact]
    public void Serialize_ClipsMergedRegionWithoutDroppingCopiedColumns()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 2));
        var viewport = new ViewportModel(
            [new DisplayCell(2, 2, new TextValue("B2"), "B2", null, default, null)],
            [],
            []);

        var payload = ClipboardHtmlSerializer.Serialize(viewport, sheet, range, workbook.Theme);

        payload.Should().NotBeNull();
        payload!.Fragment.Should().Contain("rowspan=\"2\"");
        payload.Fragment.Should().Contain("B2");
    }

    [Fact]
    public void Serialize_TextValueCell_EmitsMsoNumberFormatTextMarker()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var viewport = new ViewportModel(
            [new DisplayCell(1, 1, new TextValue("00501"), "00501", null, default, null)],
            [],
            []);

        var payload = ClipboardHtmlSerializer.Serialize(viewport, sheet, new GridRange(address, address), workbook.Theme);

        payload.Should().NotBeNull();
        payload!.Fragment.Should().Contain("mso-number-format:'\\@';");
        payload.Fragment.Should().Contain("00501");
    }

    [Fact]
    public void Serialize_TextNumberFormatCell_EmitsMsoNumberFormatTextMarkerEvenWithNumberValue()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var style = new CellStyle { NumberFormat = "@" };
        var viewport = new ViewportModel(
            [new DisplayCell(1, 1, new NumberValue(501), "501", null, default, null, style)],
            [],
            []);

        var payload = ClipboardHtmlSerializer.Serialize(viewport, sheet, new GridRange(address, address), workbook.Theme);

        payload.Should().NotBeNull();
        payload!.Fragment.Should().Contain("mso-number-format:'\\@';");
    }

    [Fact]
    public void Serialize_PlainNumberCell_DoesNotEmitTextFormatMarker()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var viewport = new ViewportModel(
            [new DisplayCell(1, 1, new NumberValue(501), "501", null, default, null)],
            [],
            []);

        var payload = ClipboardHtmlSerializer.Serialize(viewport, sheet, new GridRange(address, address), workbook.Theme);

        payload.Should().NotBeNull();
        payload!.Fragment.Should().NotContain("mso-number-format");
        payload.Fragment.Should().Contain("501");
    }

    [Fact]
    public void Serialize_MultilineCell_ConvertsEmbeddedNewlineToBreakTag()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var viewport = new ViewportModel(
            [new DisplayCell(1, 1, new TextValue("a\nb"), "a\nb", null, default, null)],
            [],
            []);

        var payload = ClipboardHtmlSerializer.Serialize(viewport, sheet, new GridRange(address, address), workbook.Theme);

        payload.Should().NotBeNull();
        payload!.Fragment.Should().Contain("a<br>b");
        payload.Fragment.Should().NotContain("a\nb");
    }

    [Fact]
    public void Serialize_SingleLineCell_DoesNotEmitBreakTag()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var viewport = new ViewportModel(
            [new DisplayCell(1, 1, new TextValue("ab"), "ab", null, default, null)],
            [],
            []);

        var payload = ClipboardHtmlSerializer.Serialize(viewport, sheet, new GridRange(address, address), workbook.Theme);

        payload.Should().NotBeNull();
        payload!.Fragment.Should().NotContain("<br>");
        payload.Fragment.Should().Contain("ab");
    }

    [Fact]
    public void WrapAsCfHtml_DeclaresUtf8CharsetBeforeFragment()
    {
        // R135: an external HTML-aware consumer (Word, a browser, a mail client) that reads a
        // CF_HTML payload with no charset declaration decodes non-ASCII cell text using its own
        // default codepage, mojibaking anything outside ASCII. The pre-fragment preamble (the part
        // between StartHTML and StartFragment) must declare charset=utf-8 to match the UTF-8 bytes
        // WPF's DataObject.SaveHtmlToHandle (DataFormats.Html) actually puts on the OS clipboard.
        var cfHtml = ClipboardHtmlSerializer.WrapAsCfHtml("<table><tr><td>x</td></tr></table>");

        var bytes = Encoding.UTF8.GetBytes(cfHtml);
        var startHtml = ParseOffset(cfHtml, "StartHTML:");
        var startFragment = ParseOffset(cfHtml, "StartFragment:");
        var preamble = Encoding.UTF8.GetString(bytes, startHtml, startFragment - startHtml);

        preamble.ToLowerInvariant().Should().Contain("charset=\"utf-8\"");
    }

    [Fact]
    public void WrapAsCfHtml_NonAsciiFragment_ByteOffsetsStillBoundExactFragment()
    {
        // Sibling/no-regression test: adding the charset meta tag to the pre-fragment preamble
        // must not desynchronize the StartFragment/EndFragment (or StartHTML/EndHTML) byte offsets
        // -- a stale offset would corrupt the paste worse than the missing charset ever did. Uses a
        // fragment with multi-byte UTF-8 characters (Latin-1 + CJK) so a byte/char-count mixup would
        // be caught.
        const string fragment = "<table><tr><td>café 日本語</td></tr></table>";
        var cfHtml = ClipboardHtmlSerializer.WrapAsCfHtml(fragment);

        var bytes = Encoding.UTF8.GetBytes(cfHtml);
        var startHtml = ParseOffset(cfHtml, "StartHTML:");
        var endHtml = ParseOffset(cfHtml, "EndHTML:");
        var startFragment = ParseOffset(cfHtml, "StartFragment:");
        var endFragment = ParseOffset(cfHtml, "EndFragment:");

        endHtml.Should().Be(bytes.Length);
        startHtml.Should().BeLessThan(startFragment);
        endFragment.Should().BeLessThanOrEqualTo(endHtml);
        Encoding.UTF8.GetString(bytes, startFragment, endFragment - startFragment).Should().Be(fragment);
    }

    private static int ParseOffset(string payload, string field)
    {
        var start = payload.IndexOf(field, StringComparison.Ordinal) + field.Length;
        return int.Parse(payload.AsSpan(start, 10));
    }
}
