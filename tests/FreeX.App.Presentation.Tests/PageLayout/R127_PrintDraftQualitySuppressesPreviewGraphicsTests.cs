using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R127-presentation-draft-quality-preview-1: Sheet.PrintDraftQuality ("Draft quality" in Page Setup
/// &gt; Sheet) suppresses charts and raster pictures on the WPF native print/print-preview path
/// (<c>PrintRenderer.HeaderFooter.cs</c>'s <c>!draftQuality</c> guard), but
/// <see cref="PageContentRenderModelBuilder"/> -- the single shared content model consumed by the
/// Avalonia interactive print-preview canvas (<c>PrintPreviewInstructionBuilder</c>) as well as the
/// portable Skia PDF-export path (<c>WorkbookPdfContentBuilder</c>) -- built the chart and picture
/// blocks unconditionally, so the on-screen "Print Preview" a Linux/macOS user sees before exporting
/// never reflected the Draft Quality checkbox at all. These tests exercise the real entry point,
/// <see cref="PageContentRenderModelBuilder.Build"/>.
/// </summary>
public sealed class R127_PrintDraftQualitySuppressesPreviewGraphicsTests
{
    [Fact]
    public void Build_DraftQuality_OmitsChartBlock()
    {
        var (workbook, sheet) = CreateWorkbookWithChartAndPicture();
        sheet.PrintDraftQuality = true;

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.Charts.Should().BeEmpty(
            "the interactive print-preview canvas must honor Draft Quality exactly like the WPF path, " +
            "instead of always showing the chart regardless of the checkbox");
    }

    [Fact]
    public void Build_DraftQuality_OmitsPictureBlock()
    {
        var (workbook, sheet) = CreateWorkbookWithChartAndPicture();
        sheet.PrintDraftQuality = true;

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.Pictures.Should().BeEmpty(
            "raster pictures are 'graphics' too -- Draft Quality must suppress them in the preview the " +
            "same way it suppresses charts");
    }

    [Fact]
    public void Build_NoDraftQuality_StillIncludesChartAndPictureBlocks()
    {
        // No-regression sibling: an ordinary preview (PrintDraftQuality false, the default) must keep
        // showing both blocks exactly as before this fix.
        var (workbook, sheet) = CreateWorkbookWithChartAndPicture();
        sheet.PrintDraftQuality.Should().BeFalse("default");

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.Charts.Should().ContainSingle();
        layout.Pictures.Should().ContainSingle();
    }

    [Fact]
    public void Build_DraftQuality_StillIncludesTextBoxBlock()
    {
        // Sibling family member the fix must NOT touch: text boxes are vector text content, not
        // "graphics" -- Excel's Draft Quality does not suppress them, matching the WPF path's
        // unconditional DrawPrintedTextBoxes call.
        var (workbook, sheet) = CreateWorkbookWithChartAndPicture();
        sheet.PrintDraftQuality = true;
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 5),
            Width = 100,
            Height = 40,
            Text = "Draft note",
        });

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.TextBoxes.Should().ContainSingle(t => t.Text == "Draft note");
    }

    private static PageContentLayout? BuildFirstPage(Workbook workbook, Sheet sheet)
    {
        var printRange = sheet.PrintArea ?? sheet.GetUsedRange()
            ?? new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var pagePlan = PagePaginationPlanner.Paginate(
            printRange,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);
        return PageContentRenderModelBuilder.Build(
            workbook, sheet, pagePlan, 0, new FakeTextMeasurer(), new DateTime(2026, 1, 1));
    }

    private static (Workbook Workbook, Sheet Sheet) CreateWorkbookWithChartAndPicture()
    {
        var workbook = new Workbook { Name = "DraftPreview.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(8));
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 8));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            Left = 300,
            Top = 20,
            Width = 150,
            Height = 100,
        });
        sheet.Pictures.Add(new PictureModel
        {
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheet.Id, 10, 1),
            Width = 96,
            Height = 64,
            ImageBytes = [1, 2, 3],
            ContentType = "image/png",
        });

        return (workbook, sheet);
    }
}
