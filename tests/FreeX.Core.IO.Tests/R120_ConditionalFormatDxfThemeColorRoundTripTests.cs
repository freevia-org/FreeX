using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R120-io-cf-dxf-theme-color-1: a conditional-format rule's differential style (dxf) whose font/fill/
/// border color was picked from the workbook theme gallery (an OOXML &lt;color theme="N" tint="T"/&gt;
/// element) must keep that theme link through a save/load round-trip instead of being baked to a flat
/// literal rgb the moment it's read.
///
/// Before this fix, <see cref="XlsxDifferentialStyleReader"/> called the non-theme-reference-preserving
/// <see cref="XlsxColorReader.TryReadCellColor(XElement?,WorkbookTheme,WorkbookIndexedColorPalette,out CellColor)"/>
/// overload for every dxf color (font/fill/fillPattern/border), so <see cref="CellStyle.FontThemeColor"/>/
/// <see cref="CellStyle.FillThemeColor"/>/<see cref="CellStyle.FillPatternThemeColor"/>/
/// <see cref="CellBorder.ThemeColor"/> were never populated for CF rules (only for plain, non-CF cell
/// styles), and <see cref="XlsxAdvancedConditionalFormatWriter"/> always re-emitted a literal rgb=
/// attribute on save -- so the theme link was destroyed on first load and could never come back, even if
/// the in-memory model had somewhere to put it.
///
/// This corpus-real: docs/fidelity/2026-06-17-contextures-cf-table-visual.md documents an Excel-authored
/// file (contextures/05_conditional-formatting_expiry-dates.xlsx) whose dxf uses
/// &lt;bgColor theme="7" tint="0.4"/&gt;.
/// </summary>
public sealed class R120_ConditionalFormatDxfThemeColorRoundTripTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

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

    private static XElement LoadSingleDxfElement(Stream xlsxStream)
    {
        xlsxStream.Position = 0;
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/styles.xml")!;
        XDocument doc;
        using (var entryStream = entry.Open())
            doc = XDocument.Load(entryStream);

        return doc.Root!
            .Element(WorkbookNs + "dxfs")!
            .Elements(WorkbookNs + "dxf")
            .Single();
    }

    // ── Fill color (bgColor/fgColor via a Solid pattern) ───────────────────────────────────────

    [Fact]
    public void Save_ThemeLinkedFillColorRule_EmitsThemeAttributeNotFlatRgb()
    {
        var workbook = new Workbook("R120CfDxfThemeFill");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var themeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, 0.4);
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.Blanks,
            FormatIfTrue = new CellStyle
            {
                FillPatternStyle = CellFillPatternStyle.Solid,
                FillColor = workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent4, 0.4),
                FillThemeColor = themeColor,
            },
        });

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        var dxf = LoadSingleDxfElement(stream);
        var fgColor = dxf.Element(WorkbookNs + "fill")!.Element(WorkbookNs + "patternFill")!.Element(WorkbookNs + "fgColor")!;
        ((string?)fgColor.Attribute("theme")).Should().Be(
            "7",
            "Accent4 is OOXML theme index 7 -- the color must round-trip as a theme reference, not a flat rgb=");
        ((string?)fgColor.Attribute("rgb")).Should().BeNull("a theme-referenced color must not also carry a baked rgb= attribute");
        double.Parse(((string?)fgColor.Attribute("tint"))!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(0.4, 1e-9);
    }

    [Fact]
    public void RoundTrip_ThemeLinkedFillColorRule_ReloadsWithThemeReferenceIntact()
    {
        var workbook = new Workbook("R120CfDxfThemeFillRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var themeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, 0.4);
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.Blanks,
            FormatIfTrue = new CellStyle
            {
                FillPatternStyle = CellFillPatternStyle.Solid,
                FillColor = workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent4, 0.4),
                FillThemeColor = themeColor,
            },
        });

        var (_, loaded) = SaveAndReload(workbook);

        var reloadedRule = loaded.GetSheetAt(0)!.ConditionalFormats.Should().ContainSingle().Subject;
        reloadedRule.FormatIfTrue.Should().NotBeNull();
        reloadedRule.FormatIfTrue!.FillThemeColor.Should().Be(
            themeColor,
            "the theme link (slot+tint) must survive load, not just the RGB it resolved to at save time -- " +
            "this is what lets the rule keep following the workbook theme instead of staying pinned to " +
            "whatever color the ORIGINAL theme happened to resolve to");
        reloadedRule.FormatIfTrue!.FillColor.Should().Be(workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent4, 0.4));
    }

    // ── Font color ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ThemeLinkedFontColorRule_ReloadsWithThemeReferenceIntact()
    {
        var workbook = new Workbook("R120CfDxfThemeFont");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var themeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25);
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.NoBlanks,
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                FontColor = workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent2, -0.25),
                FontThemeColor = themeColor,
            },
        });

        var (saved, loaded) = SaveAndReload(workbook);

        var dxf = LoadSingleDxfElement(saved);
        var color = dxf.Element(WorkbookNs + "font")!.Element(WorkbookNs + "color")!;
        ((string?)color.Attribute("theme")).Should().Be("5", "Accent2 is OOXML theme index 5");

        var reloadedRule = loaded.GetSheetAt(0)!.ConditionalFormats.Should().ContainSingle().Subject;
        reloadedRule.FormatIfTrue!.FontThemeColor.Should().Be(themeColor);
    }

    // ── Border color ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ThemeLinkedBorderColorRule_ReloadsWithThemeReferenceIntact()
    {
        var workbook = new Workbook("R120CfDxfThemeBorder");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var themeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0);
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.Errors,
            FormatIfTrue = new CellStyle
            {
                BorderBottom = new CellBorder(
                    BorderStyle.Thick,
                    workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent1, 0),
                    themeColor),
            },
        });

        var (saved, loaded) = SaveAndReload(workbook);

        var dxf = LoadSingleDxfElement(saved);
        var borderColor = dxf.Element(WorkbookNs + "border")!.Element(WorkbookNs + "bottom")!.Element(WorkbookNs + "color")!;
        ((string?)borderColor.Attribute("theme")).Should().Be("4", "Accent1 is OOXML theme index 4");

        var reloadedRule = loaded.GetSheetAt(0)!.ConditionalFormats.Should().ContainSingle().Subject;
        reloadedRule.FormatIfTrue!.BorderBottom.ThemeColor.Should().Be(themeColor);
    }

    // ── No-regression sibling: a plain, non-theme dxf color must still round-trip as a flat rgb ──

    [Fact]
    public void RoundTrip_PlainRgbFillColorRule_StillRoundTripsAsFlatRgbWithNoThemeReference()
    {
        var workbook = new Workbook("R120CfDxfPlainRgbRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var plainColor = new CellColor(0x12, 0x34, 0x56);
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.Blanks,
            FormatIfTrue = new CellStyle
            {
                FillPatternStyle = CellFillPatternStyle.Solid,
                FillColor = plainColor,
                // FillThemeColor intentionally left null: this is an ordinary user-picked solid color,
                // not a theme-gallery pick.
            },
        });

        var (saved, loaded) = SaveAndReload(workbook);

        var dxf = LoadSingleDxfElement(saved);
        var fgColor = dxf.Element(WorkbookNs + "fill")!.Element(WorkbookNs + "patternFill")!.Element(WorkbookNs + "fgColor")!;
        ((string?)fgColor.Attribute("theme")).Should().BeNull("a plain rgb color must never be miswritten as a theme reference");
        ((string?)fgColor.Attribute("rgb")).Should().Be("FF123456");

        var reloadedRule = loaded.GetSheetAt(0)!.ConditionalFormats.Should().ContainSingle().Subject;
        reloadedRule.FormatIfTrue!.FillThemeColor.Should().BeNull("a plain rgb color must not fabricate a theme reference on reload");
        reloadedRule.FormatIfTrue!.FillColor.Should().Be(plainColor);
    }
}
