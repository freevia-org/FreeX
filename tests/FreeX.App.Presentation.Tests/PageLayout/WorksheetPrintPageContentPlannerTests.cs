using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class WorksheetPrintPageContentPlannerTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Build_ResolvesWpfContentPolicyAndPortableProfileDifferences()
    {
        var workbook = new Workbook("Print content");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(source, new TextValue("Jump"));
        sheet.SetCell(destination, new TextValue("Target"));
        sheet.PrintArea = new GridRange(source, destination);
        sheet.ScaleToFit = new WorksheetScaleToFit(50, null, null);
        sheet.DifferentFirstPageHeaderFooter = true;
        sheet.FirstPageHeader = new WorksheetHeaderFooter("First", "&P of &N", "&G");
        sheet.FirstPageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            null,
            null,
            new WorksheetHeaderFooterPicture([1, 2, 3], "image/png"));
        sheet.PrintComments = WorksheetPrintComments.AsDisplayed;
        sheet.Comments[source] = "Pinned note";
        sheet.ShownComments.Add(source);
        sheet.Hyperlinks[source] = "Sheet1!B2";
        sheet.HyperlinkMetadata[source] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);

        WorksheetPrintRenderPlanner.TryBuild(sheet, null, false, out var renderPlan).Should().BeTrue();
        var page = renderPlan.Pages.Should().ContainSingle().Subject;

        var wpf = WorksheetPrintPageContentPlanner.Build(
            workbook,
            sheet,
            renderPlan,
            page,
            Measurer,
            WorksheetPrintMaterializationProfile.WpfNative,
            new DateTime(2026, 8, 10));
        var avalonia = WorksheetPrintPageContentPlanner.Build(
            workbook,
            sheet,
            renderPlan,
            page,
            Measurer,
            WorksheetPrintMaterializationProfile.AvaloniaPreview,
            new DateTime(2026, 8, 10));

        wpf.Should().NotBeNull();
        wpf!.Transform.ScaleRatio.Should().BeApproximately(0.5, 0.0001);
        wpf.Transform.ApplyNativeTransform.Should().BeTrue();
        wpf.HeaderFooter.Header.Left.Should().Be("First");
        wpf.HeaderFooter.HeaderPictures.Right.Should().NotBeNull();
        wpf.HeaderFooter.HeaderBand.Left.Height.Should().Be(48);
        wpf.Comments.DisplayedComments.Should().ContainSingle()
            .Which.Text.Should().Be("Pinned note");
        wpf.Hyperlinks.Should().ContainKey((1u, 1u));
        wpf.Hyperlinks[(1u, 1u)].TargetAddress.Should().Be(destination);
        wpf.CellDestinations.Should().ContainKey((2u, 2u));

        avalonia.Should().NotBeNull();
        avalonia!.Transform.ScaleRatio.Should().BeApproximately(0.5, 0.0001);
        avalonia.Transform.ApplyNativeTransform.Should().BeFalse();
        avalonia.HeaderFooter.Header.Left.Should().Be("First");
        // R168-presentation-preview-headerfooter-picture-1: the portable preview profile now carries
        // header/footer pictures and sizes its bands to them, like the WPF profile above -- the render
        // model resolves each section's picture and the preview canvas paints it as a real image, so
        // the platforms where this is the only preview no longer show a text-only, ungrown band that
        // disagrees with the PDF they export. The band still uses this profile's own 16-unit base line
        // height; it grows to 48 here because the picture is taller than that.
        avalonia.HeaderFooter.HeaderPictures.Right.Should().NotBeNull();
        avalonia.HeaderFooter.HeaderBand.Left.Height.Should().Be(48);
        avalonia.Comments.DisplayedComments.Should().BeEmpty();
        avalonia.Hyperlinks.Should().BeEmpty();
        avalonia.CellDestinations.Should().BeEmpty();
        avalonia.PortableLayout.Cells.Should().NotBeEmpty();
    }

    [Fact]
    public void CellGeometryPlanner_ResolvesMergeOverflowBorderAndFillPolicy()
    {
        var workbook = new Workbook("Cell geometry");
        var sheet = workbook.AddSheet("Sheet1");
        var measurement = new PrintGridMeasurement(
            HeaderWidth: 0,
            HeaderHeight: 0,
            ColumnWidth: 10,
            RowHeight: 20,
            ColumnOffsets: [0, 10, 25, 45],
            RowOffsets: [0, 20, 50]);
        var columns = new uint[] { 1, 2, 3 };
        var rows = new uint[] { 1, 2 };
        var cells = new Dictionary<(uint Row, uint Col), DisplayCell>();

        WorksheetPrintCellGeometryPlanner.MeasureMergedColumnSpan(measurement, columns, 0, 2)
            .Should().Be(25);
        WorksheetPrintCellGeometryPlanner.MeasureMergedRowSpan(measurement, rows, 0, 2)
            .Should().Be(50);
        WorksheetPrintCellGeometryPlanner.MeasureOverflowWidth(
                measurement,
                columns,
                0,
                1,
                cells,
                sheet,
                scanLeft: false)
            .Should().Be(45);

        var thin = new CellBorder(BorderStyle.Thin, CellColor.Black);
        var thick = new CellBorder(BorderStyle.Thick, CellColor.Black);
        WorksheetPrintCellGeometryPlanner.ResolveBorderWinner(thin, thick).Should().Be(thick);
        WorksheetPrintCellGeometryPlanner.HasVisibleFill(
                new CellStyle { FillPatternStyle = CellFillPatternStyle.LightGrid })
            .Should().BeTrue();
    }

    [Fact]
    public void ComputeTotalPageCount_IncludesSharedCommentAppendixPlan()
    {
        var workbook = new Workbook("Comment appendix");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("Value"));
        sheet.Comments[address] = "Note";
        sheet.PrintComments = WorksheetPrintComments.AtEnd;

        WorksheetPrintRenderPlanner.TryBuild(sheet, null, false, out var renderPlan).Should().BeTrue();

        var commentPages = WorksheetPrintPageContentPlanner.BuildCommentSummaryPages(sheet, renderPlan);
        commentPages.Should().ContainSingle();
        WorksheetPrintPageContentPlanner.ComputeTotalPageCount(sheet, renderPlan)
            .Should().Be(renderPlan.GridPageCount + commentPages.Count);
    }

    [Fact]
    public void ResolveScaleRatio_PreservesAuthoredEnlargementPastPrintableBounds()
    {
        WorksheetPrintPageContentPlanner.ResolveScaleRatio(
                effectiveScalePercent: 200,
                printedWidth: 500,
                printedHeight: 100,
                printableWidth: 500,
                printableHeight: 500)
            .Should().Be(2.0);
    }

    [Fact]
    public void HyperlinkPlanner_UsesCaseInsensitiveSheetIndex_AndSkipsMissingSheet()
    {
        var workbook = new Workbook("Print hyperlinks");
        var sourceSheet = workbook.AddSheet("Source");
        var destinationSheet = workbook.AddSheet("Data");
        var resolvedAddress = new CellAddress(sourceSheet.Id, 1, 1);
        var missingAddress = new CellAddress(sourceSheet.Id, 2, 1);
        sourceSheet.Hyperlinks[resolvedAddress] = "data!B2";
        sourceSheet.Hyperlinks[missingAddress] = "Missing!C3";
        sourceSheet.HyperlinkMetadata[resolvedAddress] =
            new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);
        sourceSheet.HyperlinkMetadata[missingAddress] =
            new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);

        var hyperlinks = WorksheetPrintHyperlinkPlanner.BuildPrintableHyperlinks(workbook, sourceSheet);

        hyperlinks.Should().ContainSingle();
        hyperlinks[(1u, 1u)].TargetAddress.Should().Be(new CellAddress(destinationSheet.Id, 2, 2));
        hyperlinks.Should().NotContainKey((2u, 1u));
    }
}
