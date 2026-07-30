using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R92-consumer-wiring-sweep-1: before this round, <see cref="PageContentRenderModelBuilder"/> (the
/// single shared model consumed by print, Print Preview, and PDF export on every platform) never
/// produced a picture render block at all -- sheet.Pictures was never read anywhere in the render
/// model, so an Insert &gt; Pictures image silently never printed. These tests exercise the real
/// entry point, <see cref="PageContentRenderModelBuilder.Build"/>, with a real Workbook/Sheet.
/// </summary>
public sealed class R92_PagePictureLayoutPlannerTests
{
    private static readonly SheetId TestSheetId = SheetId.New();

    [Fact]
    public void Build_ResolvesVisibleImagePictureIntoPagePictureBlock()
    {
        var workbook = new Workbook { Name = "PictureEvidence.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        var imageBytes = new byte[] { 1, 2, 3, 4, 5 };
        var picture = new PictureModel
        {
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheet.Id, 3, 4),
            Width = 120,
            Height = 80,
            ImageBytes = imageBytes,
            ContentType = "image/png",
            CropLeft = 0.1,
            CropTop = 0.2,
            CropRight = 0.05,
            CropBottom = 0
        };
        sheet.Pictures.Add(picture);

        var blocks = PagePictureLayoutPlanner.Build(
            sheet.Pictures,
            pageRows: [2, 3, 4],
            pageColumns: [3, 4, 5],
            gridLeft: 40,
            gridTop: 20,
            measurement: UniformMeasurement(colWidth: 60, rowHeight: 18));

        var block = blocks.Should().ContainSingle().Subject;
        block.Id.Should().Be(picture.Id);
        block.Bounds.Should().Be(new LayoutRect(100, 38, 120, 80));
        block.ImageBytes.Should().BeSameAs(imageBytes);
        block.ContentType.Should().Be("image/png");
        block.Crop.Left.Should().Be(0.1);
        block.Crop.Top.Should().Be(0.2);
        block.Crop.Right.Should().Be(0.05);
        block.Crop.Bottom.Should().Be(0);
    }

    [Fact]
    public void Build_SkipsHiddenOffPageAndCellRangeSnapshotPictures()
    {
        var workbook = new Workbook { Name = "PictureEvidence2.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        var visible = new PictureModel
        {
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheet.Id, 1, 1),
            ImageBytes = [9, 9, 9],
            ContentType = "image/png"
        };
        var hidden = new PictureModel
        {
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheet.Id, 1, 1),
            ImageBytes = [9, 9, 9],
            ContentType = "image/png",
            IsVisible = false
        };
        var offPage = new PictureModel
        {
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheet.Id, 9, 9),
            ImageBytes = [9, 9, 9],
            ContentType = "image/png"
        };
        // Non-linked Paste Special > Picture default: no raster bytes, only per-cell snapshots --
        // out of scope for this planner (see its class doc comment).
        var cellRangeSnapshot = new PictureModel
        {
            Kind = PictureKind.CellRangeSnapshot,
            Anchor = new CellAddress(sheet.Id, 1, 1)
        };
        // An Image-kind picture with no decoded bytes yet (e.g. still loading) must not crash the
        // planner or produce a block with null/empty bytes a renderer would choke on.
        var noBytes = new PictureModel
        {
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheet.Id, 1, 1),
            ContentType = "image/png"
        };
        sheet.Pictures.AddRange([hidden, visible, offPage, cellRangeSnapshot, noBytes]);

        var blocks = PagePictureLayoutPlanner.Build(
            sheet.Pictures,
            pageRows: [1, 2],
            pageColumns: [1, 2],
            gridLeft: 0,
            gridTop: 0,
            measurement: UniformMeasurement(colWidth: 50, rowHeight: 20));

        blocks.Should().ContainSingle().Which.Id.Should().Be(visible.Id);
    }

    [Fact]
    public void Build_FromRealWorkbook_PageContentLayoutIncludesPictureBlock()
    {
        var workbook = new Workbook { Name = "PictureEvidence3.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.Pictures.Add(new PictureModel
        {
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 96,
            Height = 64,
            ImageBytes = [1, 2, 3],
            ContentType = "image/png"
        });

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
        var layout = PageContentRenderModelBuilder.Build(
            workbook, sheet, pagePlan, 0, new FakeTextMeasurer(), new DateTime(2026, 1, 1));

        layout.Should().NotBeNull();
        layout!.Pictures.Should().ContainSingle();
    }

    private static PrintGridMeasurement UniformMeasurement(double colWidth, double rowHeight) =>
        new(HeaderWidth: 0, HeaderHeight: 0, ColumnWidth: colWidth, RowHeight: rowHeight);
}
