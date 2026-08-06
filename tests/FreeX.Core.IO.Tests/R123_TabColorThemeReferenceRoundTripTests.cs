using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R123-tab-theme-color-1: every other themed color in FreeX (font, fill, fill-pattern, cell borders,
/// and CF dxfs) is captured as BOTH a baked <see cref="CellColor"/> AND a
/// <see cref="WorkbookThemeColorReference"/> (slot+tint) via <c>XlsxClosedXmlCellMapper.MapColor</c> +
/// <c>MapThemeColorReference</c>, so styles can re-resolve live against whatever <see cref="WorkbookTheme"/>
/// is current (see R19-theme-extlst-1 / R80-border-theme-color-1). <see cref="Sheet.TabColor"/> never got
/// the same treatment: the loader at <c>XlsxFileAdapter.cs</c> called only the baked-only
/// <c>XlsxClosedXmlCellMapper.MapColor</c> overload, and the saver at <c>XlsxFileAdapter.Save.cs</c> always
/// emitted a literal <c>&lt;tabColor rgb="…"/&gt;</c>, so a theme-relative
/// <c>&lt;tabColor theme="n" tint="t"/&gt;</c> was permanently baked to RGB at load and silently downgraded
/// to a hardcoded literal on save, losing the theme link (and never re-coloring live when the workbook
/// theme changes in-app).
///
/// The fix adds <see cref="Sheet.TabThemeColor"/> (mirroring <c>CellStyle.FillThemeColor</c>), populates it
/// via <c>XlsxClosedXmlCellMapper.MapThemeColorReference</c> alongside the existing baked <c>MapColor</c>
/// call, and prefers it on save via <c>XlsxClosedXmlCellMapper.ToXLColor</c>. Setting
/// <see cref="Sheet.TabColor"/> directly (e.g. an explicit tab-color picker via
/// <c>SetSheetTabColorCommand</c>) clears <see cref="Sheet.TabThemeColor"/> automatically so an explicit
/// RGB pick can never resurrect a stale theme link on the next save.
/// </summary>
public sealed class R123_TabColorThemeReferenceRoundTripTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const double Tint = 0.4;

    private static (MemoryStream Saved, Workbook Loaded) SaveAndReload(Workbook workbook)
    {
        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var loaded = adapter.Load(saved);
        saved.Position = 0;
        return (saved, loaded);
    }

    private static XElement LoadSheetPrElement(Stream xlsxStream)
    {
        xlsxStream.Position = 0;
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        XDocument doc;
        using (var entryStream = entry.Open())
            doc = XDocument.Load(entryStream);

        return doc.Root!.Element(WorkbookNs + "sheetPr")!;
    }

    // ---- End-to-end: a full XLSX save/load round trip must preserve the tab color's theme link ----

    [Fact]
    public void XlsxFileAdapter_ThemeRelativeTabColor_RoundTripsThemeReference_NotBakedRgb()
    {
        var workbook = new Workbook("R123TabColorThemeRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
        var themeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, Tint);
        sheet.TabColor = workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent1, Tint);
        sheet.TabThemeColor = themeColor;

        var (saved, loaded) = SaveAndReload(workbook);

        // The saved XML must carry a theme+tint reference, not a flattened literal rgb.
        var tabColorElement = LoadSheetPrElement(saved).Element(WorkbookNs + "tabColor");
        tabColorElement.Should().NotBeNull("a theme-linked tab color must be written as <tabColor theme=.../>");
        tabColorElement!.Attribute("theme").Should().NotBeNull(
            "the tab color must round-trip as a theme reference, not a baked <tabColor rgb=.../> literal");
        tabColorElement.Attribute("rgb").Should().BeNull(
            "a theme-linked tab color must not also be downgraded to a literal rgb attribute");

        // The reloaded in-memory model must carry the theme reference forward too, so it can re-resolve
        // live and round-trip again on a subsequent save.
        var reloadedSheet = loaded.GetSheetAt(0)!;
        reloadedSheet.TabThemeColor.Should().Be(themeColor,
            "a theme-linked tab color must survive a full save/reload round trip as a theme+tint reference " +
            "(not a baked RGB literal), so it re-colors correctly if the workbook theme later changes");
        reloadedSheet.TabColor.Should().Be(workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent1, Tint),
            "the baked RGB fallback must still resolve correctly for renderers that don't resolve against a theme");
    }

    // ---- No-regression sibling: a plain literal tab color must keep round-tripping as baked RGB ----

    [Fact]
    public void XlsxFileAdapter_PlainRgbTabColor_RoundTripsAsBakedRgb_NoRegression()
    {
        var workbook = new Workbook("R123TabColorPlainRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
        sheet.TabColor = new CellColor(91, 155, 213);

        var (saved, loaded) = SaveAndReload(workbook);

        var tabColorElement = LoadSheetPrElement(saved).Element(WorkbookNs + "tabColor");
        tabColorElement.Should().NotBeNull();
        tabColorElement!.Attribute("rgb").Should().NotBeNull(
            "a plain (non-theme) tab color must keep writing a baked literal, exactly as before this fix");
        tabColorElement.Attribute("theme").Should().BeNull();

        var reloadedSheet = loaded.GetSheetAt(0)!;
        reloadedSheet.TabThemeColor.Should().BeNull("a concrete RGB tab color must not fabricate a theme link");
        reloadedSheet.TabColor.Should().Be(new CellColor(91, 155, 213));
    }

    // ---- Model-level choke point: an explicit TabColor set must clear a stale theme reference ----

    [Fact]
    public void Sheet_SettingTabColorDirectly_ClearsExistingTabThemeColor()
    {
        var workbook = new Workbook("R123TabColorExplicitOverride");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TabColor = workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent2, Tint);
        sheet.TabThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, Tint);

        // Simulates an explicit tab-color pick (e.g. SetSheetTabColorCommand) overwriting a previously
        // theme-linked tab color with a literal RGB choice.
        sheet.TabColor = new CellColor(0, 176, 80);

        sheet.TabThemeColor.Should().BeNull(
            "assigning TabColor directly must clear any stale TabThemeColor so a subsequent save doesn't " +
            "resurrect the old theme link instead of the newly chosen literal color");
        sheet.TabColor.Should().Be(new CellColor(0, 176, 80));
    }

    // ---- Model-level: ResolveTabColor must re-resolve live against whatever theme is passed in ----

    [Fact]
    public void Sheet_ResolveTabColor_PrefersLiveThemeResolutionOverBakedColor()
    {
        var sheet = new Workbook("R123ResolveTabColor").AddSheet("Sheet1");
        var staleTheme = WorkbookTheme.Office;
        sheet.TabColor = staleTheme.ResolveColor(WorkbookThemeColorSlot.Accent1, 0);
        sheet.TabThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0);

        var newTheme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 30, 30));
        var resolved = sheet.ResolveTabColor(newTheme);

        resolved.Should().Be(new CellColor(200, 30, 30),
            "a theme-linked tab color must re-resolve against the CURRENT theme rather than the color baked in at load time");
        resolved.Should().NotBe(sheet.TabColor, "the stale baked color must not be what a theme-change repaint uses");
    }
}
