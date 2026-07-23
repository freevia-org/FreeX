using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R75-io-styles-fonts-4-3: a font's <c>charset</c> and <c>family</c>
/// (family-numbering) attributes were never read or written by
/// <see cref="XlsxClosedXmlCellMapper"/>.MapStyle/ApplyStyle, so they were silently dropped on any
/// full rebuild save — a Symbol/Wingdings-charset font would lose its charset and Excel would pick
/// the wrong glyph substitution on reopen.
///
/// <see cref="CellStyle.Charset"/>/<see cref="CellStyle.FontFamily"/> default to 1 ("Default"/unset)
/// and 2 (Swiss) respectively — ClosedXML's own sentinel values for "no charset/family specified"
/// (see <c>XLFontValue.Default</c> and <c>WorkbookStylesPartWriter</c>'s emission guards) — so a
/// plain font that never carried either attribute keeps emitting neither on save.
/// </summary>
public sealed class R75_FontCharsetFamilyRoundTripTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void MapStyle_ReadsSymbolCharsetAndFamily()
    {
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);
        cell.Style.Font.FontCharSet = XLFontCharSet.Symbol; // 2
        cell.Style.Font.FontFamilyNumbering = XLFontFamilyNumberingValues.Roman; // 1

        var style = XlsxClosedXmlCellMapper.MapStyle(cell.Style, WorkbookTheme.Office);

        style.Charset.Should().Be(2, "XLFontCharSet.Symbol must be read as raw OOXML charset code 2");
        style.FontFamily.Should().Be(1, "XLFontFamilyNumberingValues.Roman must be read as raw OOXML family code 1");
    }

    [Fact]
    public void MapStyle_PlainFont_ReadsClosedXmlDefaultSentinels()
    {
        // A font that never had charset/family touched at all must read back ClosedXML's own
        // "unset" sentinels (Default=1 / Swiss=2), matching CellStyle.Default exactly.
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);
        cell.Style.Font.Bold = true;

        var style = XlsxClosedXmlCellMapper.MapStyle(cell.Style, WorkbookTheme.Office);

        style.Charset.Should().Be(CellStyle.Default.Charset);
        style.FontFamily.Should().Be(CellStyle.Default.FontFamily);
    }

    [Fact]
    public void ApplyStyle_WritesSymbolCharsetAndFamily()
    {
        var style = new CellStyle { Charset = 2, FontFamily = 1 };
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);

        XlsxClosedXmlCellMapper.ApplyStyle(cell, style);

        cell.Style.Font.FontCharSet.Should().Be(XLFontCharSet.Symbol);
        cell.Style.Font.FontFamilyNumbering.Should().Be(XLFontFamilyNumberingValues.Roman);
    }

    [Fact]
    public void ApplyStyle_PlainStyle_DoesNotTouchCharsetOrFamily_NoRegressionToNameSizeBold()
    {
        // Sibling/no-regression: a style that never set Charset/FontFamily (i.e. the defaults) must
        // leave ClosedXML's own font defaults untouched, and name/size/bold must still apply normally.
        var style = new CellStyle { Bold = true, FontName = "Arial", FontSize = 14 };
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);
        var defaultCharset = cell.Style.Font.FontCharSet;
        var defaultFamily = cell.Style.Font.FontFamilyNumbering;

        XlsxClosedXmlCellMapper.ApplyStyle(cell, style);

        cell.Style.Font.FontCharSet.Should().Be(defaultCharset, "an untouched Charset must not perturb ClosedXML's own default");
        cell.Style.Font.FontFamilyNumbering.Should().Be(defaultFamily, "an untouched FontFamily must not perturb ClosedXML's own default");
        cell.Style.Font.Bold.Should().BeTrue();
        cell.Style.Font.FontName.Should().Be("Arial");
        cell.Style.Font.FontSize.Should().Be(14);
    }

    [Fact]
    public void XlsxAdapter_SymbolCharsetFont_RoundTrips_ThroughRealWorkbookSave()
    {
        var workbook = new Workbook("FontCharsetFamilyRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new TextValue("àáâ")));
        var styleId = workbook.RegisterStyle(new CellStyle { Charset = 2, FontFamily = 1, FontName = "Wingdings" });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedCell = reloadedSheet!.GetCell(1, 1);
        var reloadedStyle = reloaded.GetStyle(reloadedCell!.StyleId);

        reloadedStyle.Charset.Should().Be(2, "the Symbol charset must survive a full save/reload round trip");
        reloadedStyle.FontFamily.Should().Be(1, "the Roman family numbering must survive a full save/reload round trip");
        reloadedStyle.FontName.Should().Be("Wingdings");
    }

    [Fact]
    public void XlsxAdapter_PlainFont_EmitsNoCharsetAttribute_AndDefaultFamilyOnly()
    {
        // No-regression guard at the raw-XML level: a plain font (charset/family never touched)
        // must not gain a spurious <charset> element — ClosedXML's writer specially guards charset,
        // omitting it whenever the value equals XLFontCharSet.Default (the same sentinel
        // CellStyle.Default.Charset uses). <family>, unlike <charset>, has no such guard: ClosedXML
        // always expands it for any non-baseline font (the same way it always expands <sz>/<name>),
        // so it may legitimately be present — but only with the untouched default numbering (2 =
        // Swiss), never some other value FreeX injected.
        var workbook = new Workbook("PlainFontNoCharsetFamily");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new TextValue("plain")));
        var styleId = workbook.RegisterStyle(new CellStyle { Bold = true });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var stylesEntry = archive.GetEntry("xl/styles.xml");
        stylesEntry.Should().NotBeNull();
        using var stylesStream = stylesEntry!.Open();
        var stylesXml = XDocument.Load(stylesStream);

        var fonts = stylesXml.Root!.Element(WorkbookNs + "fonts")!.Elements(WorkbookNs + "font").ToList();
        fonts.Should().NotBeEmpty();
        foreach (var font in fonts)
        {
            font.Element(WorkbookNs + "charset").Should().BeNull("a plain font must not gain a spurious <charset> element");
            var family = font.Element(WorkbookNs + "family");
            if (family is not null)
                family.Attribute("val")!.Value.Should().Be("2", "an untouched family must stay at the default (Swiss) numbering, never a value FreeX injected");
        }
    }
}
