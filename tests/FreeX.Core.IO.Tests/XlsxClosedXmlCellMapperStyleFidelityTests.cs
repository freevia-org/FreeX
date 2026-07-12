using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for two round-25 XlsxClosedXmlCellMapper findings:
///
/// R25-cell-alignment-render-deep-2 — "Center Across Selection" (OOXML horizontal
/// alignment "centerContinuous") was silently downgraded to General on import, flipping
/// the cell's text from centered to flush-left. FreeX.Core.Model.HorizontalAlignment has
/// no dedicated CenterContinuous member, so the fix maps it to the closest available
/// approximation (plain Center) instead of discarding it to General.
///
/// R25-io-styles-deep-2 — a cell whose ONLY formatting is a gradient fill (no other
/// distinguishing font/border/numFmt) round-tripped through ClosedXML as indistinguishable
/// from CellStyle.Default, so ClosedXML silently dropped the cell (and its restorable style
/// slot) entirely on any full-rebuild save. ApplyStyle now stamps a solid placeholder fill
/// (the gradient's first stop color) so the cell keeps its own distinct, restorable cellXf;
/// XlsxStylesheetMetadataPreserver.MergeStylesheetGradientFills overwrites that placeholder
/// with the real gradient content afterward (its signature match excludes fillId).
/// </summary>
public sealed class XlsxClosedXmlCellMapperStyleFidelityTests
{
    // ------------------------------------------------------------------
    // R25-cell-alignment-render-deep-2: centerContinuous alignment mapping
    // ------------------------------------------------------------------

    [Fact]
    public void MapStyle_CenterContinuous_MapsToCenter_NotGeneral()
    {
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("Sheet1").Cell("B10");
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.CenterContinuous;

        var style = XlsxClosedXmlCellMapper.MapStyle(cell.Style, WorkbookTheme.Office);

        // The historical bug: centerContinuous fell through the switch's default arm to
        // General (left-aligned for text), silently re-laying out the sheet on every open.
        style.HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        style.HorizontalAlignment.Should().NotBe(HorizontalAlignment.General);
    }

    [Theory]
    [InlineData(XLAlignmentHorizontalValues.General, HorizontalAlignment.General)]
    [InlineData(XLAlignmentHorizontalValues.Left, HorizontalAlignment.Left)]
    [InlineData(XLAlignmentHorizontalValues.Center, HorizontalAlignment.Center)]
    [InlineData(XLAlignmentHorizontalValues.Right, HorizontalAlignment.Right)]
    [InlineData(XLAlignmentHorizontalValues.Justify, HorizontalAlignment.Justify)]
    [InlineData(XLAlignmentHorizontalValues.Distributed, HorizontalAlignment.Distributed)]
    [InlineData(XLAlignmentHorizontalValues.Fill, HorizontalAlignment.Fill)]
    public void MapStyle_OtherHorizontalAlignments_StillMapUnchanged(
        XLAlignmentHorizontalValues xlValue, HorizontalAlignment expected)
    {
        // Sibling regression guard: the new centerContinuous arm must not disturb any of the
        // other already-correct horizontal alignment mappings.
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("Sheet1").Cell("A1");
        cell.Style.Alignment.Horizontal = xlValue;

        var style = XlsxClosedXmlCellMapper.MapStyle(cell.Style, WorkbookTheme.Office);

        style.HorizontalAlignment.Should().Be(expected);
    }

    [Fact]
    public void ApplyStyle_PlainCenter_StillRoundTripsAsCenter_ThroughRealWorkbookSave()
    {
        // Sibling regression guard: plain (non-continuous) Center alignment set via ApplyStyle
        // must still save and reload as Center, unaffected by the centerContinuous read fix.
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var cell = workbook.AddWorksheet("Sheet1").Cell("A1");
            cell.Value = "Title";
            XlsxClosedXmlCellMapper.ApplyStyle(cell, new CellStyle { HorizontalAlignment = HorizontalAlignment.Center });
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        using var reloaded = new XLWorkbook(stream);
        var reloadedCell = reloaded.Worksheet("Sheet1").Cell("A1");
        reloadedCell.Style.Alignment.Horizontal.Should().Be(XLAlignmentHorizontalValues.Center);

        var style = XlsxClosedXmlCellMapper.MapStyle(reloadedCell.Style, WorkbookTheme.Office);
        style.HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
    }

    // ------------------------------------------------------------------
    // R25-io-styles-deep-2: gradient-only cell no longer vanishes on full rebuild
    // ------------------------------------------------------------------

    [Fact]
    public void ApplyStyle_GradientFillOnly_StampsDistinguishingSolidPlaceholder_NotLeftAsDefault()
    {
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("Sheet1").Cell("A1"); // blank cell — only formatting is the gradient
        var gradientOnlyStyle = new CellStyle
        {
            GradientFill = new CellGradientFill
            {
                Type = CellGradientFillType.Linear,
                Degree = 90,
                Stops = [new CellGradientStop(0.0, new CellColor(0, 112, 192)), new CellGradientStop(1.0, new CellColor(255, 140, 0))],
            },
        };

        XlsxClosedXmlCellMapper.ApplyStyle(cell, gradientOnlyStyle);

        // Before the fix, GradientFill was never inspected here, so a gradient-only style left
        // the ClosedXML fill completely untouched (PatternType.None) — indistinguishable from
        // CellStyle.Default and liable to be silently dropped by ClosedXML on save.
        cell.Style.Fill.PatternType.Should().NotBe(XLFillPatternValues.None);
        cell.Style.Fill.PatternType.Should().Be(XLFillPatternValues.Solid);
        cell.Style.Fill.BackgroundColor.Color.Should().Be(System.Drawing.Color.FromArgb(255, 0, 112, 192));
    }

    [Fact]
    public void ApplyStyle_PlainFillColor_StillAppliesSolidFill_NoGradientRegression()
    {
        // Sibling regression guard: a normal flat FillColor (no GradientFill at all) must still
        // take the pre-existing solid-fill branch unchanged.
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("Sheet1").Cell("A1");

        XlsxClosedXmlCellMapper.ApplyStyle(cell, new CellStyle { FillColor = new CellColor(10, 20, 30) });

        cell.Style.Fill.PatternType.Should().Be(XLFillPatternValues.Solid);
        cell.Style.Fill.BackgroundColor.Color.Should().Be(System.Drawing.Color.FromArgb(255, 10, 20, 30));
    }

    [Fact]
    public void ApplyStyle_FillPatternStyle_StillAppliesPatternFill_NoGradientRegression()
    {
        // Sibling regression guard: an explicit pattern fill (e.g. DarkGrid) with no gradient
        // must still take the pre-existing pattern-fill branch unchanged.
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("Sheet1").Cell("A1");

        XlsxClosedXmlCellMapper.ApplyStyle(cell, new CellStyle
        {
            FillPatternStyle = CellFillPatternStyle.DarkGrid,
            FillColor = new CellColor(1, 2, 3),
        });

        cell.Style.Fill.PatternType.Should().Be(XLFillPatternValues.DarkGrid);
        cell.Style.Fill.BackgroundColor.Color.Should().Be(System.Drawing.Color.FromArgb(255, 1, 2, 3));
    }

    [Fact]
    public void GradientOnlyCells_AllSurviveFullRebuildSaveReload_NotDroppedOrCollapsedToSharedDefault()
    {
        // Faithful reproduction of the finding's own repro: load a real Excel-authored fixture
        // with THREE gradient-only cells (no other formatting), force a full ClosedXML rebuild
        // by registering and applying a brand-new style to an unrelated cell, save, and reload.
        //
        // Before the fix, ApplyStyle never referenced style.GradientFill, so a gradient-only
        // cell was indistinguishable from CellStyle.Default: ClosedXML silently omitted its <c>
        // element entirely (sheet.GetStyleOnly returned null after reload — the cell vanished).
        // ApplyStyle now stamps a distinct solid placeholder so the cell keeps its own restorable
        // cellXf, and XlsxStylesheetMetadataPreserver then overwrites that placeholder with the real
        // gradient content. A full save runs both mechanisms, so the reloaded cell carries the actual
        // GRADIENT (not merely a placeholder colour) — never dropped, never collapsed onto the
        // workbook's shared default/no-fill slot.
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CellGradientFillLinear.xlsx");
        File.Exists(path).Should().BeTrue($"gradient fixture must be copied to output: {path}");

        var wb = new XlsxFileAdapter().Load(File.OpenRead(path));
        var sheet = wb.GetSheetAt(0)!;

        // Force a full rebuild (defeats both the source-copy fast path and the value-only
        // patch-save path) by applying a brand-new style to an unrelated, far-away cell.
        var boldId = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(30u, 10u, boldId);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(wb, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0)!;

        var style1 = StyleAt(reloaded, reloadedSheet, 2u, 2u);
        var style2 = StyleAt(reloaded, reloadedSheet, 7u, 2u);
        var style3 = StyleAt(reloaded, reloadedSheet, 12u, 2u);

        // None of the three gradient-only cells were dropped or collapsed to the shared default:
        // each carries its own restored gradient (a gradient fill clears the solid FillColor, so the
        // presence of GradientFill — not FillColor — is the post-restore evidence the cell survived).
        style1.GradientFill.Should().NotBeNull("R2C2's gradient must survive the rebuild");
        style2.GradientFill.Should().NotBeNull("R7C2's gradient must survive the rebuild");
        style3.GradientFill.Should().NotBeNull("R12C2's gradient must survive the rebuild");

        // The three must remain distinguishable from each other — none collapsed onto another's
        // (or the shared default) fill, which would silently corrupt unrelated cells too.
        style1.GradientFill!.Stops[0].Color.Should().NotBe(style2.GradientFill!.Stops[0].Color);
        style2.GradientFill!.Stops[0].Color.Should().NotBe(style3.GradientFill!.Stops[0].Color);
        style1.GradientFill!.Stops[0].Color.Should().NotBe(style3.GradientFill!.Stops[0].Color);
    }

    private static CellStyle StyleAt(Workbook wb, Sheet sheet, uint row, uint col)
    {
        var styleId = sheet.GetStyleOnly(row, col);
        styleId.Should().NotBeNull($"cell R{row}C{col} must still carry a style (not dropped)");
        return wb.GetStyle(styleId!.Value);
    }
}
