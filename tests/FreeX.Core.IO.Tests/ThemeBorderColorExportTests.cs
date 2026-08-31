using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// freex-theme-border-color-F1 (export half): a cell border authored against a theme slot carries only
/// a live <see cref="CellBorder.ThemeColor"/> link plus whatever RGB was baked in at load time. HTML and
/// ODF have no theme-color concept, so every exporter must flatten the border through the workbook's
/// CURRENT theme — the same way it already flattens font and fill colors — or a theme change recolors
/// the exported fonts and fills while leaving the borders on the old palette.
///
/// Every assertion compares against <c>border.ResolveColor(theme)</c> as ground truth rather than a
/// hard-coded RGB, so the tests stay honest if the theme tint math ever changes.
/// </summary>
public sealed class ThemeBorderColorExportTests
{
    private const int StaleR = 0x01;
    private const int StaleG = 0x02;
    private const int StaleB = 0x03;

    private static WorkbookTheme ThemeWithAccent1(CellColor accent1) =>
        WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, accent1);

    /// <summary>A border whose live theme link disagrees with the RGB baked in at load time.</summary>
    private static CellBorder ThemeBackedBorder() =>
        new(
            BorderStyle.Thin,
            new CellColor(StaleR, StaleG, StaleB),
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1));

    private static Workbook WorkbookWithBorderedCell(CellBorder border, WorkbookTheme theme)
    {
        var wb = new Workbook("Untitled") { Theme = theme };
        var sheet = wb.AddSheet("Sheet1");
        var styleId = wb.RegisterStyle(new CellStyle
        {
            BorderTop = border,
            BorderRight = border,
            BorderBottom = border,
            BorderLeft = border,
        });
        var cell = Cell.FromValue(new NumberValue(1));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);
        return wb;
    }

    private static string Hex(CellColor c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string StaleHex() => Hex(new CellColor(StaleR, StaleG, StaleB));

    // ── HTML export ──────────────────────────────────────────────────────────

    private static string SaveHtml(Workbook wb)
    {
        using var stream = new MemoryStream();
        new HtmlFileAdapter().Save(wb, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public void HtmlExport_FlattensThemeBackedBorderThroughTheCurrentTheme()
    {
        var border = ThemeBackedBorder();
        var themeA = ThemeWithAccent1(new CellColor(200, 10, 20));
        var themeB = ThemeWithAccent1(new CellColor(20, 200, 10));

        var htmlA = SaveHtml(WorkbookWithBorderedCell(border, themeA));
        var htmlB = SaveHtml(WorkbookWithBorderedCell(border, themeB));

        htmlA.Should().Contain(Hex(border.ResolveColor(themeA)));
        htmlB.Should().Contain(Hex(border.ResolveColor(themeB)));
        // The RGB baked in at load time must never reach the export.
        htmlA.Should().NotContain(StaleHex());
        htmlB.Should().NotContain(StaleHex());
    }

    [Fact]
    public void HtmlExport_KeepsLiteralRgbBorderConstantAcrossThemeChanges()
    {
        var border = new CellBorder(BorderStyle.Thin, new CellColor(0, 112, 192));
        border.ThemeColor.Should().BeNull();

        var htmlA = SaveHtml(WorkbookWithBorderedCell(border, ThemeWithAccent1(new CellColor(200, 10, 20))));
        var htmlB = SaveHtml(WorkbookWithBorderedCell(border, ThemeWithAccent1(new CellColor(20, 200, 10))));

        htmlA.Should().Contain(Hex(new CellColor(0, 112, 192)));
        htmlB.Should().Contain(Hex(new CellColor(0, 112, 192)));
    }

    // ── ODS export ───────────────────────────────────────────────────────────

    private static string SaveOdsStylesXml(Workbook wb)
    {
        using var stream = new MemoryStream();
        new OdsFileAdapter().Save(wb, stream);
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("content.xml");
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void OdsExport_FlattensThemeBackedBorderThroughTheCurrentTheme()
    {
        var border = ThemeBackedBorder();
        var themeA = ThemeWithAccent1(new CellColor(200, 10, 20));
        var themeB = ThemeWithAccent1(new CellColor(20, 200, 10));

        var odsA = SaveOdsStylesXml(WorkbookWithBorderedCell(border, themeA));
        var odsB = SaveOdsStylesXml(WorkbookWithBorderedCell(border, themeB));

        // Both the visible fo:border value and the freex-* exact-recovery hint must agree with the
        // live theme, or a round-trip would resurrect the stale color the hint carried.
        odsA.Should().Contain(Hex(border.ResolveColor(themeA)));
        odsB.Should().Contain(Hex(border.ResolveColor(themeB)));
        odsA.Should().NotContain(StaleHex());
        odsB.Should().NotContain(StaleHex());
    }

    [Fact]
    public void OdsExport_KeepsLiteralRgbBorderConstantAcrossThemeChanges()
    {
        var border = new CellBorder(BorderStyle.Thin, new CellColor(0, 112, 192));

        var odsA = SaveOdsStylesXml(WorkbookWithBorderedCell(border, ThemeWithAccent1(new CellColor(200, 10, 20))));
        var odsB = SaveOdsStylesXml(WorkbookWithBorderedCell(border, ThemeWithAccent1(new CellColor(20, 200, 10))));

        odsA.Should().Contain(Hex(new CellColor(0, 112, 192)));
        odsB.Should().Contain(Hex(new CellColor(0, 112, 192)));
    }

    // ── XLSX round-trip guard ────────────────────────────────────────────────

    [Fact]
    public void XlsxExport_StillPersistsTheThemeLinkRatherThanFlatteningIt()
    {
        // The exporters above flatten because their formats have no theme concept. XLSX DOES, so it
        // must keep round-tripping <color theme="n"/> (R80-border-theme-color-1) — this guards against
        // the flattening fix being copy-pasted into the OOXML writer, which would bake every themed
        // border to a literal RGB on save.
        var border = ThemeBackedBorder();
        var wb = WorkbookWithBorderedCell(border, ThemeWithAccent1(new CellColor(200, 10, 20)));

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var stylesEntry = archive.GetEntry("xl/styles.xml");
        stylesEntry.Should().NotBeNull();
        using var entryStream = stylesEntry!.Open();
        var styles = XDocument.Load(entryStream);

        var ns = styles.Root!.Name.Namespace;
        var borderColors = styles.Descendants(ns + "borders").Descendants(ns + "color").ToList();
        borderColors.Should().NotBeEmpty();
        borderColors.Any(c => c.Attribute("theme") != null)
            .Should().BeTrue("a theme-backed border must round-trip its theme link, not a baked RGB");
    }
}
