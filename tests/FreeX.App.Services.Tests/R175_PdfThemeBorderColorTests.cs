using FluentAssertions;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// freex-theme-border-color-F1 (round-175 meta-F3 follow-up): <see cref="WorkbookPdfContentBuilder"/>
/// is the exclusive content builder behind the Avalonia/Skia Save-As-PDF path
/// (<c>SkiaPdfDocumentExporter</c>) and the portable fallback path
/// (<c>PortablePdfDocumentExporter</c>). Its <c>DrawBorderEdge</c> read <c>border.Color</c> -- the RGB
/// baked in at load time -- while the fills and fonts a few hundred lines up already resolved through
/// <c>CellStyle.ResolveFillColor</c>/<c>ResolveFontColor</c>. So swapping the workbook theme recolored
/// every exported fill and font but left the theme-backed borders on the old palette.
/// </summary>
public sealed class R175_PdfThemeBorderColorTests
{
    // Deliberately unlike the stock Office Accent1 (21, 96, 130) so "followed the new theme" and
    // "kept the load-time baked color" can never be confused for one another.
    private static readonly CellColor SwappedAccent1 = new(7, 200, 111);

    [Fact]
    public void BuildWithPageSetup_ThemeBackedBorder_UsesSwappedThemeAccentColor()
    {
        var (workbook, sheet, border) = CreateThemeBorderWorkbook();
        var bakedIn = border.Color;

        workbook.Theme = workbook.Theme.WithColor(WorkbookThemeColorSlot.Accent1, SwappedAccent1);

        // Ground truth is the model's own resolver, not a hard-coded RGB.
        var expected = border.ResolveColor(workbook.Theme);
        expected.Should().NotBe(bakedIn,
            "the swapped accent must actually differ from the load-time baked color, or this test proves nothing");

        var lines = BuildPageSetupLines(workbook);

        lines.Should().Contain(l => Matches(l.Color, expected),
            "a theme-backed border must re-resolve against the workbook's current theme, like the fills/fonts alongside it");
        lines.Should().NotContain(l => Matches(l.Color, bakedIn),
            "the load-time baked color belongs to the OLD theme and must no longer be exported");
    }

    [Fact]
    public void Build_LegacyFixedGeometryPath_ThemeBackedBorder_UsesSwappedThemeAccentColor()
    {
        // The legacy fixed-geometry Build overload (PortablePdfDocumentExporter.CreateDocument, and
        // through it AvaloniaPdfDocumentExporter's Skia-unavailable fallback) shares DrawCellBorders,
        // so it must resolve the theme too.
        var (workbook, _, border) = CreateThemeBorderWorkbook();
        var bakedIn = border.Color;

        workbook.Theme = workbook.Theme.WithColor(WorkbookThemeColorSlot.Accent1, SwappedAccent1);
        var expected = border.ResolveColor(workbook.Theme);

        var doc = WorkbookPdfContentBuilder.Build(workbook, CreatePdfPlan(workbook), new PortablePdfDocumentOptions());
        var lines = doc.Pages.SelectMany(p => p.Ops).OfType<PdfLine>().ToList();

        lines.Should().Contain(l => Matches(l.Color, expected));
        lines.Should().NotContain(l => Matches(l.Color, bakedIn));
    }

    [Fact]
    public void BuildWithPageSetup_ExplicitRgbBorder_KeepsItsOwnColorAcrossThemeChange()
    {
        // No-regression sibling: a border with no ThemeColor is pinned to its authored RGB and must
        // be completely unaffected by a theme swap (CellBorder.ResolveColor falls back to Color).
        var explicitColor = new CellColor(200, 0, 0);
        var (workbook, _, _) = CreateBorderWorkbook(new CellBorder(BorderStyle.Thick, explicitColor));

        workbook.Theme = workbook.Theme.WithColor(WorkbookThemeColorSlot.Accent1, SwappedAccent1);

        var lines = BuildPageSetupLines(workbook);

        lines.Should().Contain(l => Matches(l.Color, explicitColor),
            "an explicitly colored border does not follow the theme and must keep its authored RGB");
        lines.Should().NotContain(l => Matches(l.Color, SwappedAccent1));
    }

    private static bool Matches(PdfColor color, CellColor expected) =>
        color.R == expected.R && color.G == expected.G && color.B == expected.B;

    private static PortablePdfExportPlan CreatePdfPlan(Workbook workbook)
    {
        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        exportPlan.IsReady.Should().BeTrue(exportPlan.StatusText);

        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);
        return pdfPlan;
    }

    private static List<PdfLine> BuildPageSetupLines(Workbook workbook)
    {
        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, CreatePdfPlan(workbook));
        doc.Pages.Should().NotBeEmpty();
        return doc.Pages[0].Ops.OfType<PdfLine>().ToList();
    }

    private static (Workbook Workbook, Sheet Sheet, CellBorder Border) CreateThemeBorderWorkbook()
    {
        var workbook = new Workbook("ThemeBorders");
        var themeRef = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1);
        // Color = the RGB resolved against the ORIGINAL theme at load time (what the buggy code read).
        var border = new CellBorder(BorderStyle.Thick, themeRef.Resolve(workbook.Theme), themeRef);
        return CreateBorderWorkbook(border, workbook);
    }

    private static (Workbook Workbook, Sheet Sheet, CellBorder Border) CreateBorderWorkbook(
        CellBorder border, Workbook? existing = null)
    {
        var workbook = existing ?? new Workbook("Borders");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintGridlines = false; // Excel's default -- keeps gridline PdfLines out of the assertions.

        var styleId = workbook.RegisterStyle(new CellStyle
        {
            BorderTop = border,
            BorderBottom = border,
            BorderLeft = border,
            BorderRight = border,
        });
        var cell = Cell.FromValue(new TextValue("Total"));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        return (workbook, sheet, border);
    }
}
