using FluentAssertions;
using Free.Shared.Pdf;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R92-consumer-wiring-sweep-1: <see cref="WorkbookPdfContentBuilder"/> never emitted a
/// <see cref="PdfImage"/> op for a sheet picture at all -- an Insert &gt; Pictures image (or a raster
/// non-linked Paste Special &gt; Picture) silently never appeared in PDF export on either platform
/// (FreeX's own portable PDF writer and the Avalonia/Skia PDF writer both consume this same builder).
/// These tests exercise the real product entry point, <see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/>.
/// </summary>
public sealed class R92_PicturePdfExportTests
{
    [Fact]
    public void BuildWithPageSetup_EmitsPictureImageOpFromSharedPrintLayout()
    {
        var workbook = new Workbook { Name = "PictureEvidence.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        var imageBytes = new byte[] { 137, 80, 78, 71, 1, 2, 3, 4 };
        sheet.Pictures.Add(new PictureModel
        {
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 80,
            ImageBytes = imageBytes,
            ContentType = "image/png",
            CropLeft = 0.1,
            CropRight = 0.2
        });
        var exportPlan = CreatePageSetupPdfPlan(workbook);

        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan);
        var ops = document.Pages.Should().ContainSingle().Subject.Ops;

        var image = ops.OfType<PdfImage>().Should().ContainSingle().Subject;
        image.ContentType.Should().Be("image/png");
        image.ImageBytes.Should().BeSameAs(imageBytes);
        image.SourceCrop.Left.Should().Be(0.1);
        image.SourceCrop.Right.Should().Be(0.2);
        image.Width.Should().BeGreaterThan(0);
        image.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BuildWithPageSetup_SkipsHiddenAndCellRangeSnapshotPictures()
    {
        var workbook = new Workbook { Name = "PictureEvidenceNoRegression.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.Pictures.Add(new PictureModel
        {
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheet.Id, 2, 2),
            ImageBytes = [1, 2, 3],
            ContentType = "image/png",
            IsVisible = false
        });
        sheet.Pictures.Add(new PictureModel
        {
            Kind = PictureKind.CellRangeSnapshot,
            Anchor = new CellAddress(sheet.Id, 3, 3)
        });
        var exportPlan = CreatePageSetupPdfPlan(workbook);

        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan);
        var ops = document.Pages.Should().ContainSingle().Subject.Ops;

        ops.OfType<PdfImage>().Should().BeEmpty();
    }

    private static PortablePdfExportPlan CreatePageSetupPdfPlan(Workbook workbook)
    {
        var printPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: 0));

        printPlan.IsReady.Should().BeTrue(printPlan.StatusText);
        return PortablePdfExportPlanner.CreatePlan(printPlan, workbook);
    }
}
