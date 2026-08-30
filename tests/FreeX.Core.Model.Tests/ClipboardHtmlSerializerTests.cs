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

    // R175 F2 (clipboard half): AppendBorderCss read border.Color directly, unlike the
    // ResolveFontColor/ResolveFillColor calls a few lines above it in the same BuildCellCss method,
    // so a border set via the ribbon's Theme Colors picker copied to the clipboard with the color
    // baked in at load time instead of the CURRENT workbook theme.
    [Fact]
    public void Serialize_ThemeColoredBorder_UsesCurrentThemeColor_NotTheColorBakedAtLoadTime()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        var oldTheme = WorkbookTheme.Office;
        var staleBakedColor = oldTheme.GetColor(WorkbookThemeColorSlot.Accent1);
        var newTheme = oldTheme.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 20, 20));

        var border = new CellBorder(
            BorderStyle.Thick,
            staleBakedColor,
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1));
        var style = new CellStyle { BorderTop = border };
        var viewport = new ViewportModel(
            [new DisplayCell(1, 1, new TextValue("x"), "x", null, default, null, style)],
            [],
            []);

        var expected = border.ResolveColor(newTheme);
        expected.Should().NotBe(staleBakedColor, "the test theme swap must actually change Accent1");
        var expectedHex = $"#{expected.R:X2}{expected.G:X2}{expected.B:X2}";
        var staleHex = $"#{staleBakedColor.R:X2}{staleBakedColor.G:X2}{staleBakedColor.B:X2}";

        var payload = ClipboardHtmlSerializer.Serialize(viewport, sheet, new GridRange(address, address), newTheme);

        payload.Should().NotBeNull();
        payload!.Fragment.Should().Contain($"border-top:3px solid {expectedHex};",
            "the copied border must follow the CURRENT theme's Accent1, not the color baked in at load time");
        payload.Fragment.Should().NotContain(staleHex,
            "the copied border must not still show the stale load-time color after the theme changed");
    }

    [Fact]
    public void Serialize_ExplicitRgbBorder_StillCopiesItsOwnColor_NoRegression()
    {
        // Sibling/no-regression case: a border with NO ThemeColor (a plain RGB swatch, not a Theme
        // Color) must keep copying its own authored color regardless of the workbook theme.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        var explicitColor = new CellColor(10, 200, 30);
        var theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 20, 20));

        var border = new CellBorder(BorderStyle.Thick, explicitColor, ThemeColor: null);
        var style = new CellStyle { BorderTop = border };
        var viewport = new ViewportModel(
            [new DisplayCell(1, 1, new TextValue("x"), "x", null, default, null, style)],
            [],
            []);

        var payload = ClipboardHtmlSerializer.Serialize(viewport, sheet, new GridRange(address, address), theme);

        payload.Should().NotBeNull();
        payload!.Fragment.Should().Contain("border-top:3px solid #0AC81E;",
            "an explicit-RGB border must keep copying its own authored color regardless of the workbook theme");
    }
}
