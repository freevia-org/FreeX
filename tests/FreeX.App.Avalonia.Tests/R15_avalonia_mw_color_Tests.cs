using System.Collections.Generic;
using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round 15 regression guards:
///
///   R15-print-preview-interaction-1 — the Avalonia grid only built the page-break-preview overlay
///     (masks/borders/automatic-break lines/watermarks) when the sheet's view mode was PageLayout or
///     PageBreakPreview (<c>WorksheetViewModeUiStatePlanner...UsesPageBreakPreviewOverlay</c>). Manual
///     (user-inserted) row/column page breaks never drew ANYTHING in Normal view, unlike WPF's
///     <c>GridView.Overlays.cs RenderManualPageBreaks</c> (and Excel itself), which draws dashed-blue
///     manual break lines in every view mode. Fixed by adding
///     <see cref="MainWindow.BuildManualPageBreakLineInstructions"/> — mode-agnostic pure geometry
///     (mirrors RenderManualPageBreaks: one line per manual break that falls inside the current
///     viewport, independent of the print-range/pagination layout used for the preview-only overlay)
///     — and calling it unconditionally from BuildSheetGrid, regardless of view mode.
///
///   R15-color-fill-picker-1 — CellColorPalettePlanner.BuildThemePalette hardcoded the legacy Office
///     2013-2021 palette (Accent 1 = #4472C4) for every Accent column instead of deriving it from the
///     workbook's actual theme (WorkbookTheme.Office has long since moved to the Aptos palette,
///     Accent 1 = #156082), so the color picker's "theme colors" gallery never matched the workbook.
///     Fixed by adding an optional WorkbookTheme parameter (falling back to WorkbookTheme.Office) and
///     deriving the Accent 1-6 base + 5 tint/shade rows from theme.ResolveColor.
/// </summary>
public sealed class R15AvaloniaMwColorTests
{
    // ── R15-print-preview-interaction-1: manual page-break lines must render regardless of mode ──

    [Fact]
    public void BuildManualPageBreakLineInstructions_EmitsHorizontalLine_ForManualRowBreakInViewport()
    {
        // Rows 1..3, each 20px tall, stacked with no gaps; row 2 carries a manual break (Excel/WPF
        // draw the break line ABOVE the broken row, at its top offset).
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20), new RowMetric(3, 20, 40)],
            [new ColMetric(1, 60, 0), new ColMetric(2, 60, 60)]);

        var rowPageBreaks = new SortedSet<uint> { 2u };
        var columnPageBreaks = new SortedSet<uint>();

        var lines = MainWindow.BuildManualPageBreakLineInstructions(
            viewport,
            rowPageBreaks,
            columnPageBreaks,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            gridWidth: 150,
            gridHeight: 80);

        // This is the exact call BuildSheetGrid now makes unconditionally (no WorksheetViewMode
        // gating at all) — so the same result applies whether the sheet is in Normal, PageLayout, or
        // PageBreakPreview view. Pre-fix, no such manual-break-only builder existed and Normal view
        // drew nothing for a manual break.
        lines.Should().ContainSingle("row 2 carries the only manual row break, and it is inside the viewport");
        var line = lines[0];
        line.X1.Should().Be(30, "the line must start at the row header's right edge");
        line.X2.Should().Be(150, "the line must span the full grid width, like WPF's RenderManualPageBreaks");
        line.Y1.Should().Be(38, "the break line is drawn above the broken row (row 2's TopOffset 20 + header height 18)");
        line.Y2.Should().Be(38);
    }

    [Fact]
    public void BuildManualPageBreakLineInstructions_EmitsVerticalLine_ForManualColumnBreakInViewport()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0)],
            [new ColMetric(1, 60, 0), new ColMetric(2, 60, 60), new ColMetric(3, 60, 120)]);

        var rowPageBreaks = new SortedSet<uint>();
        var columnPageBreaks = new SortedSet<uint> { 2u };

        var lines = MainWindow.BuildManualPageBreakLineInstructions(
            viewport,
            rowPageBreaks,
            columnPageBreaks,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            gridWidth: 210,
            gridHeight: 100);

        lines.Should().ContainSingle("column 2 carries the only manual column break, and it is inside the viewport");
        var line = lines[0];
        line.X1.Should().Be(90, "the break line is drawn left of the broken column (col 2's LeftOffset 60 + row header width 30)");
        line.X2.Should().Be(90);
        line.Y1.Should().Be(18, "the line must start at the column header's bottom edge");
        line.Y2.Should().Be(100, "the line must span the full grid height, like WPF's RenderManualPageBreaks");
    }

    [Fact]
    public void BuildManualPageBreakLineInstructions_ReturnsEmpty_WhenNoManualBreaksExist()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0)],
            [new ColMetric(1, 60, 0)]);

        MainWindow.BuildManualPageBreakLineInstructions(
                viewport,
                rowPageBreaks: new SortedSet<uint>(),
                columnPageBreaks: new SortedSet<uint>(),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                gridWidth: 60,
                gridHeight: 20)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void BuildManualPageBreakLineInstructions_SkipsManualBreak_OutsideCurrentViewport()
    {
        // A manual break on row 50 exists on the sheet, but the current viewport only shows rows 1-2 —
        // matching WPF's RenderManualPageBreaks, which only draws breaks whose row/col metric is
        // actually present in the visible RowMetrics/ColMetrics.
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20)],
            [new ColMetric(1, 60, 0)]);

        MainWindow.BuildManualPageBreakLineInstructions(
                viewport,
                rowPageBreaks: new SortedSet<uint> { 50u },
                columnPageBreaks: new SortedSet<uint>(),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                gridWidth: 60,
                gridHeight: 40)
            .Should()
            .BeEmpty();
    }

    // ── R15-color-fill-picker-1: theme accent swatches must come from the real workbook theme ──────

    [Fact]
    public void BuildThemePalette_DerivesAccentSwatches_FromCustomWorkbookTheme()
    {
        var customTheme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(0x11, 0x22, 0x33))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(0x44, 0x55, 0x66))
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(0x77, 0x88, 0x99))
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(0xAA, 0xBB, 0xCC))
            .WithColor(WorkbookThemeColorSlot.Accent5, new CellColor(0x12, 0x34, 0x56))
            .WithColor(WorkbookThemeColorSlot.Accent6, new CellColor(0x9A, 0xBC, 0xDE));

        var columns = CellColorPalettePlanner.BuildThemePalette(customTheme);

        columns.Should().HaveCount(10);
        columns[4].Name.Should().Be("Accent 1");
        columns[4].Shades[0].Color.Should().Be(new CellColor(0x11, 0x22, 0x33));
        columns[4].Shades[0].Hex.Should().Be("#112233");

        columns[5].Name.Should().Be("Accent 2");
        columns[5].Shades[0].Color.Should().Be(new CellColor(0x44, 0x55, 0x66));

        columns[6].Name.Should().Be("Accent 3");
        columns[6].Shades[0].Color.Should().Be(new CellColor(0x77, 0x88, 0x99));

        columns[7].Name.Should().Be("Accent 4");
        columns[7].Shades[0].Color.Should().Be(new CellColor(0xAA, 0xBB, 0xCC));

        columns[8].Name.Should().Be("Accent 5");
        columns[8].Shades[0].Color.Should().Be(new CellColor(0x12, 0x34, 0x56));

        columns[9].Name.Should().Be("Accent 6");
        columns[9].Shades[0].Color.Should().Be(new CellColor(0x9A, 0xBC, 0xDE));

        // The old hardcoded legacy value must be gone once a custom theme is supplied.
        columns[4].Shades[0].Hex.Should().NotBe("#4472C4");
    }

    [Fact]
    public void BuildThemePalette_WithoutExplicitTheme_FallsBackToRealWorkbookThemeOffice_NotLegacyHardcodedPalette()
    {
        // Regression guard for the actual bug: the no-argument call used to hardcode the legacy
        // Office 2013-2021 palette. It must now resolve Accent 1 from the REAL default theme
        // (WorkbookTheme.Office, the Aptos palette, Accent1 = #156082) instead.
        var defaultColumns = CellColorPalettePlanner.BuildThemePalette();
        var explicitOfficeColumns = CellColorPalettePlanner.BuildThemePalette(WorkbookTheme.Office);

        defaultColumns[4].Shades[0].Color.Should().Be(WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent1));
        defaultColumns[4].Shades[0].Hex.Should().Be("#156082");
        defaultColumns[4].Shades[0].Hex.Should().NotBe("#4472C4", "the legacy hardcoded palette must no longer be used");

        // Passing WorkbookTheme.Office explicitly must be identical to the null-default fallback.
        defaultColumns[4].Shades[0].Hex.Should().Be(explicitOfficeColumns[4].Shades[0].Hex);
    }

    [Fact]
    public void BuildDefaultSwatches_AcceptsOptionalThemeParameter_AndStillCompilesForUnrelatedCallers()
    {
        // Keep-optional contract: existing no-argument call sites must keep compiling and returning a
        // non-empty deduped palette, while a theme can also be threaded through explicitly.
        CellColorPalettePlanner.BuildDefaultSwatches().Should().NotBeEmpty();
        CellColorPalettePlanner.BuildDefaultSwatches(WorkbookTheme.Office).Should().NotBeEmpty();
    }
}
