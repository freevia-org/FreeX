using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R80-render-gridlines-borders-5-1 / R80-border-theme-color-1: cell BORDER
/// colors had no theme-color reference at all (unlike font/fill, see R19_ThemeCellColorTests), so a
/// theme-colored border was flattened to a baked RGB literal at load time
/// (<see cref="XlsxClosedXmlCellMapper.MapStyle"/> / <see cref="XlsxCellBorderStyleReader"/>) and always
/// re-emitted as a literal <c>&lt;color rgb="…"/&gt;</c> on save
/// (<see cref="XlsxClosedXmlCellMapper.ApplyStyle"/>), destroying the theme link on round-trip and never
/// re-coloring when the workbook theme changed. The fix adds <see cref="CellBorder.ThemeColor"/>
/// (mirroring <c>CellStyle.FontThemeColor</c>/<c>FillThemeColor</c>) and threads it through both the
/// ClosedXML-backed border mapper and the native raw-XML <see cref="XlsxCellBorderStyleReader"/> path.
/// </summary>
public sealed class R80_border_theme_color_Tests
{
    private const double Tint = 0.4;

    // ---- MapStyle (ClosedXML load path): the border's theme link must be recorded, not just resolved ----

    [Fact]
    public void MapStyle_BorderColorTheme_PopulatesBorderThemeColorWithSlotAndTint()
    {
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);
        cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.TopBorderColor = XLColor.FromTheme(XLThemeColor.Accent1, Tint);

        var style = XlsxClosedXmlCellMapper.MapStyle(cell.Style, WorkbookTheme.Office);

        style.BorderTop.ThemeColor.Should().Be(
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, Tint),
            "a theme-linked border color must record its theme slot+tint, not merely a baked RGB");
        style.BorderTop.Color.Should().Be(WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, Tint),
            "the baked RGB fallback must still resolve correctly for renderers that don't resolve against a theme");
    }

    [Fact]
    public void MapStyle_PlainRgbBorderColor_LeavesBorderThemeColorNull()
    {
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);
        cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.TopBorderColor = XLColor.FromArgb(255, 10, 20, 30);

        var style = XlsxClosedXmlCellMapper.MapStyle(cell.Style, WorkbookTheme.Office);

        style.BorderTop.ThemeColor.Should().BeNull("a concrete (non-theme) RGB border color must not fabricate a theme link");
        style.BorderTop.Color.Should().Be(new CellColor(10, 20, 30));
    }

    // ---- ApplyStyle (ClosedXML save path): a theme-linked border must write a theme color, not baked RGB ----

    [Fact]
    public void ApplyStyle_BorderThemeColor_WritesThemeColor_NotBakedRgb()
    {
        var style = new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thin, WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, Tint),
                new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, Tint)),
        };
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);

        XlsxClosedXmlCellMapper.ApplyStyle(cell, style);

        cell.Style.Border.TopBorderColor.ColorType.Should().Be(XLColorType.Theme,
            "a theme-linked border color must be written as <color theme=.../>, not a baked FromArgb literal");
        cell.Style.Border.TopBorderColor.ThemeColor.Should().Be(XLThemeColor.Accent1);
        cell.Style.Border.TopBorderColor.ThemeTint.Should().BeApproximately(Tint, 0.0001);
    }

    [Fact]
    public void ApplyStyle_PlainRgbBorderColor_WritesBakedRgb_NoRegression()
    {
        var style = new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thin, new CellColor(91, 155, 213)),
        };
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);

        XlsxClosedXmlCellMapper.ApplyStyle(cell, style);

        cell.Style.Border.TopBorderColor.ColorType.Should().Be(XLColorType.Color,
            "a plain RGB border color (no theme link) must keep writing a baked literal, exactly as before this fix");
        cell.Style.Border.TopBorderColor.Color.R.Should().Be(91);
        cell.Style.Border.TopBorderColor.Color.G.Should().Be(155);
        cell.Style.Border.TopBorderColor.Color.B.Should().Be(213);
    }

    // ---- Native raw-XML border reader (XlsxCellBorderStyleReader): theme attr must populate ThemeColor ----

    [Fact]
    public void CellBorderStyleReader_ThemeColorAttribute_PopulatesBorderThemeColor()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var stylesXml = new XDocument(
            new XElement(ns + "styleSheet",
                new XElement(ns + "borders",
                    new XElement(ns + "border",
                        new XElement(ns + "left"),
                        new XElement(ns + "right"),
                        new XElement(ns + "top"),
                        new XElement(ns + "bottom")),
                    new XElement(ns + "border",
                        new XElement(ns + "left"),
                        new XElement(ns + "right"),
                        new XElement(ns + "top", new XAttribute("style", "medium"),
                            new XElement(ns + "color", new XAttribute("theme", "4"), new XAttribute("tint", Tint.ToString("R")))),
                        new XElement(ns + "bottom"))),
                new XElement(ns + "cellXfs",
                    new XElement(ns + "xf", new XAttribute("borderId", "0")),
                    new XElement(ns + "xf", new XAttribute("borderId", "1")))));

        var table = XlsxCellBorderStyleReader.Read(stylesXml, WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        table.TryGetVisibleBorders(1, out var borders).Should().BeTrue();
        borders.Top.ThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, Tint),
            "OOXML theme index 4 is accent1 in Excel's standard clrScheme ordering");
        borders.Top.Color.Should().Be(WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, Tint));
    }

    [Fact]
    public void CellBorderStyleReader_RgbColorAttribute_LeavesBorderThemeColorNull_NoRegression()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var stylesXml = new XDocument(
            new XElement(ns + "styleSheet",
                new XElement(ns + "borders",
                    new XElement(ns + "border",
                        new XElement(ns + "left"),
                        new XElement(ns + "right"),
                        new XElement(ns + "top"),
                        new XElement(ns + "bottom")),
                    new XElement(ns + "border",
                        new XElement(ns + "left"),
                        new XElement(ns + "right"),
                        new XElement(ns + "top", new XAttribute("style", "medium"),
                            new XElement(ns + "color", new XAttribute("rgb", "FF1F4E79"))),
                        new XElement(ns + "bottom"))),
                new XElement(ns + "cellXfs",
                    new XElement(ns + "xf", new XAttribute("borderId", "0")),
                    new XElement(ns + "xf", new XAttribute("borderId", "1")))));

        var table = XlsxCellBorderStyleReader.Read(stylesXml, WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        table.TryGetVisibleBorders(1, out var borders).Should().BeTrue();
        borders.Top.ThemeColor.Should().BeNull("a concrete rgb= border color must not fabricate a theme link");
        borders.Top.Should().Be(new CellBorder(BorderStyle.Medium, CellColor.FromArgb(0x1F, 0x4E, 0x79)));
    }

    // ---- End-to-end: a full XLSX save/load round trip must preserve the border's theme link ----

    [Fact]
    public void XlsxFileAdapter_CellWithThemeBorderColor_RoundTripsAsThemeNotBakedRgb()
    {
        var workbook = new Workbook("ThemeBorderColorRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new TextValue("Hello")));
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thin, workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent1, Tint),
                new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, Tint)),
        });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedCell = reloadedSheet!.GetCell(1, 1);
        var reloadedStyle = reloaded.GetStyle(reloadedCell!.StyleId);

        reloadedStyle.BorderTop.ThemeColor.Should().Be(
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, Tint),
            "a theme-linked border color must survive a full save/reload round trip as a theme+tint reference " +
            "(not a baked RGB literal), so it re-colors correctly if the workbook theme later changes");
    }
}
