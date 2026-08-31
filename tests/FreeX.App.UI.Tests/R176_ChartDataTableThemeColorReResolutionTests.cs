using System.Reflection;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Annotations;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R176-chart-data-table-theme-color-reresolution: <see cref="ChartDataTableModel"/> stores its
/// fill/border/text colors two ways -- a live theme reference
/// (FillThemeColor/BorderThemeColor/TextThemeColor, a WorkbookThemeColorSlot+tint) meant to be
/// re-resolved against the CURRENT WorkbookTheme at paint time, plus a concrete baked
/// FillColor/BorderColor/TextColor field that is only correct at the moment the chart was
/// created/loaded. Before this fix, ChartRenderer.Annotations.cs' AddChartDataTableAnnotations read
/// the baked fields RAW (ToOxyColor(chart.DataTable.FillColor) etc.), ignoring the sibling theme
/// references entirely -- so a chart Data Table colored via the ribbon's Theme Colors picker kept
/// showing its stale baked RGB forever after a Theme Colors swap. Same bug class as R175
/// (CellBorder / Sheet.TabColor) and R114 (CellStyle font/fill).
/// </summary>
public sealed class R176_ChartDataTableThemeColorReResolutionTests
{
    private static readonly CellColor StaleBakedFill = new(255, 242, 204);
    private static readonly CellColor StaleBakedBorder = new(191, 144, 0);
    private static readonly CellColor StaleBakedText = new(112, 48, 160);

    private static readonly CellColor NewThemeFill = new(10, 20, 230);
    private static readonly CellColor NewThemeBorder = new(230, 120, 10);
    private static readonly CellColor NewThemeText = new(0, 140, 60);

    private static WorkbookTheme SwappedTheme() =>
        WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, NewThemeFill)
            .WithColor(WorkbookThemeColorSlot.Accent2, NewThemeBorder)
            .WithColor(WorkbookThemeColorSlot.Accent3, NewThemeText);

    private static ChartModel BuildChart(ChartDataTableModel dataTable)
    {
        var sheetId = SheetId.New();
        return new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            DataTable = dataTable
        };
    }

    private static ViewportModel BuildViewport() => new(
        [
            new DisplayCell(1, 1, null, "Quarter", null, StyleId.Default, null),
            new DisplayCell(1, 2, null, "North", null, StyleId.Default, null),
            new DisplayCell(2, 1, null, "Q1", null, StyleId.Default, null),
            new DisplayCell(2, 2, null, "10", null, StyleId.Default, null)
        ],
        [],
        []);

    private static List<TextAnnotation> DataTableAnnotations(PlotModel model) =>
        model.Annotations
            .OfType<TextAnnotation>()
            .Where(annotation => annotation.Text?.Contains("North", StringComparison.Ordinal) == true ||
                                 annotation.Text?.Contains("Q1", StringComparison.Ordinal) == true)
            .ToList();

    private static PlotModel Render(ChartDataTableModel dataTable, WorkbookTheme theme)
    {
        var method = typeof(ChartRenderer).GetMethod(
            "BuildPlotModel",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(ChartModel), typeof(ViewportModel), typeof(WorkbookTheme)]);
        method.Should().NotBeNull();
        return method!.Invoke(null, [BuildChart(dataTable), BuildViewport(), theme])
            .Should().BeOfType<PlotModel>().Subject;
    }

    [Fact]
    public void DataTable_ThemeColorReferences_ReResolveAgainstCurrentTheme_NotStaleBakedColors()
    {
        var model = Render(
            new ChartDataTableModel
            {
                ShowOutline = true,
                FillColor = StaleBakedFill,
                FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
                BorderColor = StaleBakedBorder,
                BorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
                TextColor = StaleBakedText,
                TextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3)
            },
            SwappedTheme());

        var annotations = DataTableAnnotations(model);
        annotations.Should().HaveCount(2);

        annotations.Should().OnlyContain(
            annotation => annotation.Background == OxyColor.FromRgb(NewThemeFill.R, NewThemeFill.G, NewThemeFill.B),
            "the Data Table's FillThemeColor must be re-resolved against the CURRENT theme, not the stale baked FillColor");
        annotations.Should().OnlyContain(
            annotation => annotation.Stroke == OxyColor.FromRgb(NewThemeBorder.R, NewThemeBorder.G, NewThemeBorder.B),
            "the Data Table's BorderThemeColor must be re-resolved against the CURRENT theme, not the stale baked BorderColor");
        annotations.Should().OnlyContain(
            annotation => annotation.TextColor == OxyColor.FromRgb(NewThemeText.R, NewThemeText.G, NewThemeText.B),
            "the Data Table's TextThemeColor must be re-resolved against the CURRENT theme, not the stale baked TextColor");
    }

    [Fact]
    public void DataTable_ThemeOnlyColors_WithNoBakedFallback_StillRender()
    {
        // Reachable from the ribbon's Theme Colors picker: only the theme reference is set, with no
        // baked concrete color to fall back on.
        var model = Render(
            new ChartDataTableModel
            {
                ShowOutline = true,
                FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
                BorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
                TextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3)
            },
            SwappedTheme());

        var annotations = DataTableAnnotations(model);
        annotations.Should().HaveCount(2);
        annotations.Should().OnlyContain(
            annotation => annotation.Background == OxyColor.FromRgb(NewThemeFill.R, NewThemeFill.G, NewThemeFill.B));
        annotations.Should().OnlyContain(
            annotation => annotation.Stroke == OxyColor.FromRgb(NewThemeBorder.R, NewThemeBorder.G, NewThemeBorder.B));
        annotations.Should().OnlyContain(
            annotation => annotation.TextColor == OxyColor.FromRgb(NewThemeText.R, NewThemeText.G, NewThemeText.B));
    }

    [Fact]
    public void DataTable_PlainExplicitColors_WithNoThemeReference_AreUnaffectedByThemeSwap_NoRegression()
    {
        var model = Render(
            new ChartDataTableModel
            {
                ShowOutline = true,
                FillColor = StaleBakedFill,
                BorderColor = StaleBakedBorder,
                TextColor = StaleBakedText
                // No *ThemeColor references at all: plain, non-themed colors.
            },
            SwappedTheme());

        var annotations = DataTableAnnotations(model);
        annotations.Should().HaveCount(2);
        annotations.Should().OnlyContain(
            annotation => annotation.Background == OxyColor.FromRgb(StaleBakedFill.R, StaleBakedFill.G, StaleBakedFill.B),
            "a Data Table with no theme reference must keep its explicit colors regardless of the active theme");
        annotations.Should().OnlyContain(
            annotation => annotation.Stroke == OxyColor.FromRgb(StaleBakedBorder.R, StaleBakedBorder.G, StaleBakedBorder.B));
        annotations.Should().OnlyContain(
            annotation => annotation.TextColor == OxyColor.FromRgb(StaleBakedText.R, StaleBakedText.G, StaleBakedText.B));
    }

    [Fact]
    public void DataTable_SameReferences_ResolveDifferently_UnderTwoDifferentThemes()
    {
        ChartDataTableModel DataTable() => new()
        {
            ShowOutline = true,
            FillColor = StaleBakedFill,
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1)
        };

        var before = DataTableAnnotations(Render(DataTable(), WorkbookTheme.Office));
        var after = DataTableAnnotations(Render(DataTable(), SwappedTheme()));

        before.Should().NotBeEmpty();
        after.Should().NotBeEmpty();
        before[0].Background.Should().NotBe(after[0].Background,
            "swapping the workbook's Theme Colors must change the Data Table's rendered fill");
        after[0].Background.Should().Be(OxyColor.FromRgb(NewThemeFill.R, NewThemeFill.G, NewThemeFill.B));
    }
}
