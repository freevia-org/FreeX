using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R168-presentation-preview-headerfooter-picture-1: the portable page-content render model -- what
/// the Avalonia print preview paints, and the only print preview Linux/macOS has -- dropped
/// header/footer <c>&amp;G</c> pictures entirely (it passed
/// <c>WorksheetHeaderFooterPictureSet.Empty</c> with <c>sizeToContent: false</c>), so a sheet with a
/// header picture previewed as text on an ungrown band and then exported a PDF, from the same
/// platform and the same workbook, with the picture present and the band grown.
///
/// These pin the model end to end: the picture reaches the layout, at the rectangle the SHARED
/// geometry planner resolves (the same one the WPF print and Skia PDF paths call, so the preview
/// agrees with what prints), and reaches the preview's paint instructions as a real image primitive.
/// </summary>
public sealed class R168_PrintPreviewHeaderFooterPictureTests
{
    private static readonly byte[] ImageBytes = [0x89, 0x50, 0x4E, 0x47];
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Build_HeaderPicture_ReachesTheLayoutAtTheSharedPlannerGeometry()
    {
        var picture = NewPicture(Width: 96, Height: 48);
        var layout = BuildLayout(sheet =>
        {
            sheet.PageHeader = new WorksheetHeaderFooter("&G", "", "");
            sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(picture, null, null);
        });

        var block = layout.HeaderFooterPictureBlocks.Should().ContainSingle().Subject;
        block.ImageBytes.Should().BeSameAs(ImageBytes);
        block.ContentType.Should().Be("image/png");

        // The band grew to the picture's own 48-unit height (it is taller than the 16-unit text line),
        // and the picture fills it at its authored size -- the shared ResolvePictureBounds result.
        block.Bounds.Height.Should().Be(48);
        block.Bounds.Width.Should().Be(96);
        block.Bounds.Left.Should().Be(layout.PrintableArea.Left,
            "a left-aligned header picture sits flush against the section's edge -- the page margin");
    }

    [Fact]
    public void Build_FooterPicture_ReachesTheLayoutToo()
    {
        var picture = NewPicture(Width: 96, Height: 48);
        var layout = BuildLayout(sheet =>
        {
            sheet.PageFooter = new WorksheetHeaderFooter("", "&G", "");
            sheet.PageFooterPictures = new WorksheetHeaderFooterPictureSet(null, picture, null);
        });

        var block = layout.HeaderFooterPictureBlocks.Should().ContainSingle().Subject;
        block.Bounds.Top.Should().BeGreaterThan(layout.PrintableArea.Bottom,
            "the footer band sits below the printable area's bottom edge");
        (block.Bounds.Top + block.Bounds.Height).Should().BeLessThanOrEqualTo(layout.PageBounds.Height + 0.001,
            "and stays on the page");
    }

    [Fact]
    public void Build_OversizedPicture_IsScaledUniformlyIntoTheCappedBand()
    {
        // The shared rules apply here exactly as they do on the export paths: the band grows only to
        // the 25%-of-page cap, and the picture is scaled uniformly into it rather than stretched.
        var picture = NewPicture(Width: 50, Height: 400);
        var layout = BuildLayout(sheet =>
        {
            sheet.PageHeader = new WorksheetHeaderFooter("&G", "", "");
            sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(picture, null, null);
        });

        var block = layout.HeaderFooterPictureBlocks.Should().ContainSingle().Subject;
        block.Bounds.Height.Should().BeLessThanOrEqualTo(
            layout.PageBounds.Height * PageGeometryRules.MaxHeaderFooterBandHeightFraction + 0.001);
        (block.Bounds.Width / block.Bounds.Height).Should().BeApproximately(50.0 / 400.0, 0.001,
            "the picture keeps its source aspect ratio");
    }

    [Fact]
    public void Build_PictureWithoutItsToken_IsNotShown()
    {
        // Mirrors both export paths: a configured picture only appears where the section's text
        // actually carries a picture token.
        var layout = BuildLayout(sheet =>
        {
            sheet.PageHeader = new WorksheetHeaderFooter("Plain header", "", "");
            sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(NewPicture(96, 48), null, null);
        });

        layout.HeaderFooterPictureBlocks.Should().BeEmpty();
    }

    [Fact]
    public void Build_DraftQuality_SuppressesTheHeaderPictureAndItsBandGrowth()
    {
        // Draft quality drops pictures on every path; the band must not grow for one that will not
        // be drawn either.
        var layout = BuildLayout(sheet =>
        {
            sheet.PrintDraftQuality = true;
            sheet.PageHeader = new WorksheetHeaderFooter("&G", "", "");
            sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(NewPicture(96, 48), null, null);
        });

        layout.HeaderFooterPictureBlocks.Should().BeEmpty();
        layout.HeaderRuns.Should().OnlyContain(run => run.Bounds.Height == 16.0);
    }

    [Fact]
    public void BuildPageInstructions_HeaderPicture_IsPaintedAsARealImageNotAPlaceholderBox()
    {
        var layout = BuildLayout(sheet =>
        {
            sheet.PageHeader = new WorksheetHeaderFooter("&G", "", "");
            sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(NewPicture(96, 48), null, null);
        });

        var painting = PrintPreviewInstructionBuilder.Build(layout);

        var image = painting.Instructions
            .Where(instruction => instruction.Kind == PrintPreviewPaintKind.Image)
            .Should().ContainSingle().Subject;
        image.ImageBytes.Should().BeSameAs(ImageBytes);
        image.ImageContentType.Should().Be("image/png");
        image.Width.Should().Be(96);
        image.Height.Should().Be(48);
    }

    private static WorksheetHeaderFooterPicture NewPicture(double Width, double Height) =>
        new(ImageBytes, "image/png", "logo.png", Width, Height);

    private static PageContentLayout BuildLayout(Action<Sheet> configure)
    {
        var workbook = new Workbook("Preview");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins = new WorksheetPageMargins(Left: 1.0, Right: 1.0, Top: 1.0, Bottom: 1.0);
        configure(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("Hi")));

        var printRange = sheet.GetUsedRange()
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
            workbook, sheet, pagePlan, pageIndex: 0, Measurer, new DateTime(2026, 8, 30));
        layout.Should().NotBeNull();
        return layout!;
    }
}
