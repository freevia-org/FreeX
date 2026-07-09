using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R19-theme-extlst-1: cell-level theme colors (font/fill/fill-pattern) were
/// flattened to a baked RGB literal on load — <see cref="XlsxClosedXmlCellMapper.MapColor"/> resolved
/// the theme+tint immediately and <c>CellStyle</c> never recorded the theme link — and
/// <see cref="XlsxClosedXmlCellMapper.ApplyStyle"/> always wrote back <c>XLColor.FromArgb</c>, so a
/// theme-linked cell color lost its theme link on every round trip and would never re-color when the
/// workbook theme changed. The fix populates/consumes <c>CellStyle.FontThemeColor</c>,
/// <c>FillThemeColor</c>, and <c>FillPatternThemeColor</c> (slot + tint) alongside the existing baked
/// RGB fallback fields.
///
/// Border theme colors are NOT covered here: <see cref="CellBorder"/> has no theme-color field to
/// populate (that would require extending FreeX.Core.Model.CellStyle/CellBorder, which is out of this
/// fixer's assigned file set), so border colors still flatten to baked RGB as before this fix.
/// </summary>
public sealed class R19_theme_cell_color_Tests
{
    // OOXML theme color index 4 ("theme=4" in the finding's failure scenario) is accent1 in Excel's
    // standard clrScheme ordering (0=dk1, 1=lt1, 2=dk2, 3=lt2, 4=accent1, ...) -- ClosedXML exposes this
    // as XLThemeColor.Accent1, which FreeX's own ToWorkbookThemeColorSlot maps to WorkbookThemeColorSlot.Accent1.
    private const double Tint = 0.4;

    // ---- MapStyle (load path): the theme link must be recorded, not just resolved to a baked RGB ----

    [Fact]
    public void MapStyle_FontColorTheme_PopulatesFontThemeColorWithSlotAndTint()
    {
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);
        cell.Style.Font.FontColor = XLColor.FromTheme(XLThemeColor.Accent1, Tint);

        var style = XlsxClosedXmlCellMapper.MapStyle(cell.Style, WorkbookTheme.Office);

        style.FontThemeColor.Should().Be(
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, Tint),
            "a theme-linked font color must record its theme slot+tint, not merely a baked RGB");
        style.FontColor.Should().Be(WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, Tint),
            "the baked RGB fallback must still resolve correctly for non-theme-aware callers");
    }

    [Fact]
    public void MapStyle_PlainRgbFontColor_LeavesFontThemeColorNull()
    {
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);
        cell.Style.Font.FontColor = XLColor.FromArgb(255, 10, 20, 30);

        var style = XlsxClosedXmlCellMapper.MapStyle(cell.Style, WorkbookTheme.Office);

        style.FontThemeColor.Should().BeNull("a concrete (non-theme) RGB color must not fabricate a theme link");
    }

    [Fact]
    public void MapStyle_FillColorTheme_PopulatesFillThemeColorWithSlotAndTint()
    {
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);
        cell.Style.Fill.PatternType = XLFillPatternValues.Solid;
        cell.Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent2, Tint);

        var style = XlsxClosedXmlCellMapper.MapStyle(cell.Style, WorkbookTheme.Office);

        style.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, Tint),
            "a theme-linked fill color must record its theme slot+tint, not merely a baked RGB");
    }

    [Fact]
    public void MapStyle_FillPatternColorTheme_PopulatesFillPatternThemeColorWithSlotAndTint()
    {
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);
        cell.Style.Fill.PatternType = XLFillPatternValues.LightGrid;
        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(255, 200, 200, 200);
        cell.Style.Fill.PatternColor = XLColor.FromTheme(XLThemeColor.Accent3, Tint);

        var style = XlsxClosedXmlCellMapper.MapStyle(cell.Style, WorkbookTheme.Office);

        style.FillPatternThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, Tint),
            "a theme-linked pattern foreground color must record its theme slot+tint, not merely a baked RGB");
    }

    // ---- ApplyStyle (save path): a theme-linked style must write a theme color, not XLColor.FromArgb ----

    [Fact]
    public void ApplyStyle_FontThemeColor_WritesThemeColor_NotBakedRgb()
    {
        var style = new CellStyle { FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, Tint) };
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);

        XlsxClosedXmlCellMapper.ApplyStyle(cell, style);

        cell.Style.Font.FontColor.ColorType.Should().Be(XLColorType.Theme,
            "a theme-linked font color must be written as <color theme=.../>, not a baked FromArgb literal");
        cell.Style.Font.FontColor.ThemeColor.Should().Be(XLThemeColor.Accent1);
        cell.Style.Font.FontColor.ThemeTint.Should().BeApproximately(Tint, 0.0001);
    }

    [Fact]
    public void ApplyStyle_FillThemeColor_WritesThemeColor_NotBakedRgb()
    {
        var style = new CellStyle
        {
            FillPatternStyle = CellFillPatternStyle.Solid,
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, Tint),
        };
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);

        XlsxClosedXmlCellMapper.ApplyStyle(cell, style);

        cell.Style.Fill.BackgroundColor.ColorType.Should().Be(XLColorType.Theme,
            "a theme-linked fill color must be written as <color theme=.../>, not a baked FromArgb literal");
        cell.Style.Fill.BackgroundColor.ThemeColor.Should().Be(XLThemeColor.Accent2);
        cell.Style.Fill.BackgroundColor.ThemeTint.Should().BeApproximately(Tint, 0.0001);
    }

    [Fact]
    public void ApplyStyle_FillPatternThemeColor_WritesThemeColor_NotBakedRgb()
    {
        var style = new CellStyle
        {
            FillPatternStyle = CellFillPatternStyle.LightGrid,
            FillColor = new CellColor(200, 200, 200),
            FillPatternThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, Tint),
        };
        using var xlWorkbook = new XLWorkbook();
        var xlSheet = xlWorkbook.Worksheets.Add("Sheet1");
        var cell = xlSheet.Cell(1, 1);

        XlsxClosedXmlCellMapper.ApplyStyle(cell, style);

        cell.Style.Fill.PatternColor.ColorType.Should().Be(XLColorType.Theme,
            "a theme-linked pattern foreground color must be written as <color theme=.../>, not a baked FromArgb literal");
        cell.Style.Fill.PatternColor.ThemeColor.Should().Be(XLThemeColor.Accent3);
        cell.Style.Fill.PatternColor.ThemeTint.Should().BeApproximately(Tint, 0.0001);
    }

    // ---- End-to-end: a full XLSX save/load round trip must preserve the theme link ----

    [Fact]
    public void XlsxFileAdapter_CellWithThemeFontColor_RoundTripsAsThemeNotBakedRgb()
    {
        var workbook = new Workbook("ThemeFontColorRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new TextValue("Hello")));
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, Tint),
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

        reloadedStyle.FontThemeColor.Should().Be(
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, Tint),
            "a theme-linked font color must survive a full save/reload round trip as a theme+tint reference " +
            "(not a baked RGB literal), so it re-colors correctly if the workbook theme later changes");
    }
}
