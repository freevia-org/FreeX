using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Free.Shared.Opc;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class PptxPackageRetentionTests
{
    private static readonly string[] ExpectedCorpusDeckNames =
    [
        "01-title-slide.pptx",
        "02-autoshapes.pptx",
        "03-mixed-text.pptx",
        "04-picture.pptx",
        "05-table.pptx",
        "06-charts.pptx",
        "07-customgeom.pptx",
        "08-effects.pptx",
        "09-smartart.pptx",
        "10-motionpath.pptx",
        "11-bevel3d.pptx",
        "12-fills.pptx",
        "13-wordart.pptx",
        "14-smartart-live.pptx",
        "15-picture-crop.pptx",
        "15-smartart-grouped-list.pptx",
        "16-bg-tabs-vtext.pptx",
        "17-bullets-autofit.pptx",
        "18-chart-types.pptx",
        "19-chart-labels.pptx",
        "20-columns-gradoutline.pptx",
        "21-comments-notes.pptx",
        "22-chart-baseline-depth.pptx",
        "23-run-baseline.pptx",
        "24-run-baseline-wrap.pptx",
        "25-chart-surface3d-view3d.pptx",
        "26-chart-surface3d-default-tall-frame.pptx",
    ];

    private static readonly string[] WriterOwnedPackagePartPaths =
    [
        "[Content_Types].xml",
        "_rels/.rels",
        OpcPackageProperties.CorePropertiesZipEntry,
        "ppt/presentation.xml",
        "ppt/_rels/presentation.xml.rels",
        "ppt/presProps.xml",
        "ppt/viewProps.xml",
        "ppt/tableStyles.xml",
        "ppt/commentAuthors.xml",
    ];

    private static readonly string[] WriterOwnedPackagePartPrefixes =
    [
        "ppt/slides/",
        "ppt/slideLayouts/",
        "ppt/slideMasters/",
        "ppt/theme/",
        "ppt/charts/",
        "ppt/media/",
        "ppt/comments/",
        "ppt/notesSlides/",
        "ppt/notesMasters/",
        // Preserve-only, but re-emitted by the writer from Presentation.HandoutMasterXml with a
        // regenerated relationship id — so it is writer-owned for retention purposes.
        "ppt/handoutMasters/",
        "ppt/embeddings/",
        "ppt/diagrams/",
    ];

    private static readonly HashSet<string> WriterOwnedRelationshipTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
        OpcPackageProperties.CorePropertiesRelationshipType,
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/presProps",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/viewProps",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/tableStyles",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesSlide",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesMaster",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/handoutMaster",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/commentAuthors",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/video",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/audio",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors",
        "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing",
    };

    private const string CustomXmlRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml";
    private const string ExternalReviewRelType =
        "http://example.com/freep/relationships/reviewLink";
    private const string UnknownViewRelType =
        "http://example.com/freep/relationships/viewState";
    private const string PrinterSettingsRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings";
    private const string UnknownSlideMirrorRelType =
        "http://example.com/freep/relationships/slideMirror";
    private const string PackageRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package";
    private const string ChartContentType =
        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    private const string SpreadsheetWorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string ChartStyleRelType =
        "http://schemas.microsoft.com/office/2011/relationships/chartStyle";
    private const string ChartColorStyleRelType =
        "http://schemas.microsoft.com/office/2011/relationships/chartColorStyle";
    private const string ChartStyleContentType =
        "application/vnd.ms-office.chartstyle+xml";
    private const string ChartColorStyleContentType =
        "application/vnd.ms-office.chartcolorstyle+xml";

    public static IEnumerable<object[]> CorpusDecks() =>
        ExpectedCorpusDeckNames.Select(name => new object[] { name });

    public static IEnumerable<object[]> SemanticEditCorpusDecks()
    {
        yield return ["04-picture.pptx", new[] { "ppt/media/" }];
        yield return ["06-charts.pptx", new[] { "ppt/charts/", "ppt/embeddings/" }];
        yield return ["14-smartart-live.pptx", new[] { "ppt/diagrams/" }];
        yield return ["15-picture-crop.pptx", new[] { "ppt/media/" }];
        yield return ["18-chart-types.pptx", new[] { "ppt/charts/", "ppt/embeddings/" }];
        yield return ["19-chart-labels.pptx", new[] { "ppt/charts/", "ppt/embeddings/" }];
        yield return ["21-comments-notes.pptx", new[] { "ppt/comments/", "ppt/notesSlides/", "ppt/notesMasters/" }];
    }

    [Fact]
    public void RenderCompareCorpus_TracksExpectedDecks()
    {
        var corpusDirectory = FindCorpusDirectory();
        Directory.GetFiles(corpusDirectory, "*.pptx")
            .Select(Path.GetFileName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Should()
            .Equal(ExpectedCorpusDeckNames.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void HiddenSlides_RoundTripShowAttributeWithoutSynthesizingVisibleState()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Add(new Slide());
        presentation.Slides.Add(new Slide());
        presentation.Slides.Add(new Slide());

        using var sourceStream = new MemoryStream();
        PptxPackageWriter.Write(presentation, sourceStream);
        var packageBytes = sourceStream.ToArray();
        using var editableStream = new MemoryStream();
        editableStream.Write(packageBytes);
        editableStream.Position = 0;
        using (var archive = new ZipArchive(editableStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var firstSlide = LoadXml(archive, "ppt/slides/slide1.xml");
            firstSlide.Root!.SetAttributeValue("show", "0");
            WriteXml(archive, "ppt/slides/slide1.xml", firstSlide);

            var secondSlide = LoadXml(archive, "ppt/slides/slide2.xml");
            secondSlide.Root!.SetAttributeValue("show", "false");
            WriteXml(archive, "ppt/slides/slide2.xml", secondSlide);

            var thirdSlide = LoadXml(archive, "ppt/slides/slide3.xml");
            thirdSlide.Root!.SetAttributeValue("show", "1");
            WriteXml(archive, "ppt/slides/slide3.xml", thirdSlide);
        }
        packageBytes = editableStream.ToArray();

        var loaded = PptxPackageReader.Read(new MemoryStream(packageBytes));
        loaded.Slides.Select(slide => slide.IsHidden)
            .Should().Equal(true, true, false, false);

        using var savedStream = new MemoryStream();
        PptxPackageWriter.Write(loaded, savedStream);
        var savedBytes = savedStream.ToArray();
        using var savedArchive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read);
        LoadXml(savedArchive, "ppt/slides/slide1.xml").Root!.Attribute("show")!.Value.Should().Be("0");
        LoadXml(savedArchive, "ppt/slides/slide2.xml").Root!.Attribute("show")!.Value.Should().Be("0");
        LoadXml(savedArchive, "ppt/slides/slide3.xml").Root!.Attribute("show").Should().BeNull();
        LoadXml(savedArchive, "ppt/slides/slide4.xml").Root!.Attribute("show").Should().BeNull();

        var reopened = PptxPackageReader.Read(new MemoryStream(savedBytes));
        reopened.Slides.Select(slide => slide.IsHidden)
            .Should().Equal(true, true, false, false);
    }

    [Theory]
    [MemberData(nameof(CorpusDecks))]
    public void RenderCompareCorpusDeck_OpenSaveReopen_RetainsSharedPackageContract(string deckName)
    {
        var sourcePath = Path.Combine(FindCorpusDirectory(), deckName);
        var loaded = PptxPackageReader.Read(sourcePath);
        loaded.PackageSnapshot.Should().NotBeNull($"{deckName} must be captured by the shared preserve-bag layer");
        loaded.Slides.Should().NotBeEmpty($"{deckName} should load through shared Core.IO before save");

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();
        savedBytes.Should().NotBeEmpty($"{deckName} should save through shared Core.IO");

        using var reopenedStream = new MemoryStream(savedBytes);
        var reopened = PptxPackageReader.Read(reopenedStream);
        reopened.Slides.Should().HaveCount(loaded.Slides.Count, $"{deckName} should reopen after Core.IO save");
        reopened.PackageSnapshot.Should().NotBeNull($"{deckName} should capture a package snapshot after reopen");

        using var sourceArchive = ZipFile.OpenRead(sourcePath);
        using var savedArchive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read);

        AssertPreservedPackageEntries(sourceArchive, savedArchive, deckName);
        AssertPreservedContentTypes(sourceArchive, savedArchive, deckName);
        AssertPreservedRelationships(sourceArchive, savedArchive, deckName);
    }

    [Theory]
    [MemberData(nameof(SemanticEditCorpusDecks))]
    public void RenderCompareHighRiskCorpusDeck_SemanticEdit_RetainsPackageContract(
        string deckName,
        string[] featurePartPrefixes)
    {
        var sourcePath = Path.Combine(FindCorpusDirectory(), deckName);
        var loaded = PptxPackageReader.Read(sourcePath);
        loaded.PackageSnapshot.Should().NotBeNull($"{deckName} must be captured before semantic edits");
        loaded.Slides.Should().NotBeEmpty($"{deckName} should load through shared Core.IO before edit");

        var editShapeName = AddModeledShapeEdit(loaded, deckName);

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();
        savedBytes.Should().NotBeEmpty($"{deckName} should save after a modeled edit");

        using var reopenedStream = new MemoryStream(savedBytes);
        var reopened = PptxPackageReader.Read(reopenedStream);
        reopened.Slides.Should().HaveCount(loaded.Slides.Count, $"{deckName} should reopen after a modeled edit");
        reopened.Slides[0].Shapes.Should().Contain(shape => shape.Name == editShapeName,
            $"{deckName} should retain the writer-owned semantic edit after reopen");

        using var sourceArchive = ZipFile.OpenRead(sourcePath);
        using var savedArchive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read);

        AssertPreservedPackageEntries(sourceArchive, savedArchive, deckName);
        AssertPreservedContentTypes(sourceArchive, savedArchive, deckName);
        AssertPreservedRelationships(sourceArchive, savedArchive, deckName);
        AssertFeaturePackageEntriesStillPresent(sourceArchive, savedArchive, deckName, featurePartPrefixes);
        AssertFeatureContentTypesStillPresent(sourceArchive, savedArchive, deckName, featurePartPrefixes);
        AssertFeatureRelationshipsStillPresent(sourceArchive, savedArchive, deckName, featurePartPrefixes);
    }

    [Fact]
    public void RenderCompareCommentsNotesCorpusDeck_SemanticEdit_RetainsModeledNotesCommentsAndPackageParts()
    {
        const string deckName = "21-comments-notes.pptx";
        var sourcePath = Path.Combine(FindCorpusDirectory(), deckName);
        var loaded = PptxPackageReader.Read(sourcePath);
        loaded.PackageSnapshot.Should().NotBeNull($"{deckName} must be captured before semantic edits");
        loaded.Slides.Should().HaveCount(2);
        loaded.Slides[0].Comments.Should().ContainSingle(comment =>
            comment.Author == "Alice Reviewer" &&
            comment.Initials == "AR" &&
            comment.Text == "Confirm the title before publishing.");
        loaded.Slides[1].Comments.Should().HaveCount(2);
        TextBodyText(loaded.Slides[0].Notes).Should().Contain("review workflow");
        TextBodyText(loaded.Slides[1].Notes).Should().Contain("comment decisions");

        var editShapeName = AddModeledShapeEdit(loaded, deckName);

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using var reopenedStream = new MemoryStream(savedBytes);
        var reopened = PptxPackageReader.Read(reopenedStream);
        reopened.Slides.Should().HaveCount(2);
        reopened.Slides[0].Shapes.Should().Contain(shape => shape.Name == editShapeName);
        reopened.Slides[0].Comments.Should().ContainSingle(comment =>
            comment.Author == "Alice Reviewer" &&
            comment.Initials == "AR" &&
            comment.Text == "Confirm the title before publishing.");
        reopened.Slides[1].Comments.Select(comment => comment.Author)
            .Should().Equal("Bob Reviewer", "Alice Reviewer");
        TextBodyText(reopened.Slides[0].Notes).Should().Contain("package save");
        TextBodyText(reopened.Slides[1].Notes).Should().Contain("comment decisions");

        using var sourceArchive = ZipFile.OpenRead(sourcePath);
        using var savedArchive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read);
        AssertPreservedPackageEntries(sourceArchive, savedArchive, deckName);
        AssertPreservedContentTypes(sourceArchive, savedArchive, deckName);
        AssertPreservedRelationships(sourceArchive, savedArchive, deckName);
        AssertCommentsNotesPackageParts(savedArchive);
    }

    [Fact]
    public void RenderCompareCommentsNotesCorpus_ExplicitZeroTitleTransform_IsRetainedAsHiddenPlaceholderSignal()
    {
        var loaded = PptxPackageReader.Read(Path.Combine(FindCorpusDirectory(), "21-comments-notes.pptx"));
        var title = loaded.Slides[1].Shapes.Single(shape => shape.Name == "Title 1");

        title.Placeholder!.Type.Should().Be(PlaceholderType.Title);
        title.ExtentCxEmu.Should().Be(0);
        title.ExtentCyEmu.Should().Be(0);
        title.HasExplicitZeroExtentTransform.Should().BeTrue();
    }

    [Fact]
    public void RenderCompareChartLabelsCorpus_ChartDataSemanticEdit_RegeneratesOnlyEditedWorkbookAndPreservesNeighborFormulaRanges()
    {
        const string deckName = "19-chart-labels.pptx";
        var sourcePath = Path.Combine(FindCorpusDirectory(), deckName);
        byte[] sourceChart1Workbook;
        byte[] sourceChart2Workbook;
        byte[] sourceChart3Workbook;

        using (var sourceArchive = ZipFile.OpenRead(sourcePath))
        {
            sourceChart1Workbook = ReadBytes(sourceArchive, "ppt/embeddings/Microsoft_Excel_Worksheet.xlsx");
            sourceChart2Workbook = ReadBytes(sourceArchive, "ppt/embeddings/Microsoft_Excel_Worksheet1.xlsx");
            sourceChart3Workbook = ReadBytes(sourceArchive, "ppt/embeddings/Microsoft_Excel_Worksheet2.xlsx");
        }

        var loaded = PptxPackageReader.Read(sourcePath);
        loaded.PackageSnapshot.Should().NotBeNull($"{deckName} must be captured before chart data edits");
        loaded.Slides.Should().HaveCount(3);

        var chartShapes = loaded.Slides
            .SelectMany((slide, slideIndex) => slide.Shapes
                .Where(shape => shape.Kind == SlideShapeKind.Chart)
                .Select(shape => (SlideIndex: slideIndex, Shape: shape)))
            .ToArray();
        chartShapes.Should().HaveCount(3);
        chartShapes.Should().OnlyContain(shape => shape.Shape.Chart != null);
        chartShapes[0].Shape.Chart!.SourcePartPath.Should().Be("ppt/charts/chart1.xml");
        chartShapes[1].Shape.Chart!.SourcePartPath.Should().Be("ppt/charts/chart2.xml");
        chartShapes[2].Shape.Chart!.SourcePartPath.Should().Be("ppt/charts/chart3.xml");

        AssertChartFormulaReferences(
            chartShapes[0].Shape.Chart!,
            ("Sheet1!$B$1", "Sheet1!$A$2:$A$5", "Sheet1!$B$2:$B$5"),
            ("Sheet1!$C$1", "Sheet1!$A$2:$A$5", "Sheet1!$C$2:$C$5"),
            ("Sheet1!$D$1", "Sheet1!$A$2:$A$5", "Sheet1!$D$2:$D$5"));
        AssertChartFormulaReferences(
            chartShapes[1].Shape.Chart!,
            ("Sheet1!$B$1", "Sheet1!$A$2:$A$5", "Sheet1!$B$2:$B$5"));
        AssertChartFormulaReferences(
            chartShapes[2].Shape.Chart!,
            ("Sheet1!$B$1", "Sheet1!$A$2:$A$5", "Sheet1!$B$2:$B$5"),
            ("Sheet1!$D$1", "Sheet1!$A$2:$A$5", "Sheet1!$D$2:$D$5"),
            ("Sheet1!$C$1", "Sheet1!$A$2:$A$5", "Sheet1!$C$2:$C$5"));

        chartShapes[0].Shape.Chart!.RegenerateWorkbookOnSave.Should().BeFalse();
        chartShapes[1].Shape.Chart!.RegenerateWorkbookOnSave.Should().BeFalse();
        chartShapes[2].Shape.Chart!.RegenerateWorkbookOnSave.Should().BeFalse();

        new ReplaceChartDataCommand(
            slideIndex: chartShapes[1].SlideIndex,
            shapeId: chartShapes[1].Shape.Id,
            categories: ["Edited Jan", "Edited Feb", "Edited Mar"],
            seriesNames: ["Edited Actual", "Edited Forecast"],
            values:
            [
                new double?[] { 111, 222, 333 },
                new double?[] { 444, 555, 666 },
            ]).Apply(loaded);

        chartShapes[0].Shape.Chart!.RegenerateWorkbookOnSave.Should().BeFalse();
        chartShapes[1].Shape.Chart!.RegenerateWorkbookOnSave.Should().BeTrue();
        chartShapes[2].Shape.Chart!.RegenerateWorkbookOnSave.Should().BeFalse();

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            ReadBytes(archive, "ppt/embeddings/Microsoft_Excel_Worksheet.xlsx")
                .Should()
                .Equal(sourceChart1Workbook, "the first unedited PowerPoint-authored workbook should stay byte-for-byte");
            sourceChart2Workbook.Should().NotBeEmpty("the edited chart starts with an authored PowerPoint workbook sidecar");
            archive.GetEntry("ppt/embeddings/Microsoft_Excel_Worksheet1.xlsx").Should().BeNull(
                "the edited chart should drop its stale authored workbook sidecar");
            ReadBytes(archive, "ppt/embeddings/Microsoft_Excel_Worksheet2.xlsx")
                .Should()
                .Equal(sourceChart3Workbook, "the third unedited PowerPoint-authored workbook should stay byte-for-byte");
            archive.GetEntry("ppt/embeddings/chartWorkbook2.xlsx").Should().NotBeNull(
                "the edited second chart should receive a regenerated workbook at its writer-owned chart index");

            var chart1Xml = LoadXml(archive, "ppt/charts/chart1.xml");
            var chart1Text = chart1Xml.ToString(SaveOptions.DisableFormatting);
            chart1Text.Should().Contain("Sheet1!$B$1");
            chart1Text.Should().Contain("Sheet1!$A$2:$A$5");
            chart1Text.Should().Contain("Sheet1!$B$2:$B$5");
            chart1Text.Should().Contain("Sheet1!$C$1");
            chart1Text.Should().Contain("Sheet1!$C$2:$C$5");
            chart1Text.Should().Contain("Sheet1!$D$1");
            chart1Text.Should().Contain("Sheet1!$D$2:$D$5");
            chart1Xml.Root!.Element(ChartNs + "externalData")!.Attribute(RelsDocNs + "id")!.Value
                .Should().Be("rId1");
            var chart1Rels = LoadXml(archive, "ppt/charts/_rels/chart1.xml.rels");
            Relationship(chart1Rels, PackageRelType, "../embeddings/Microsoft_Excel_Worksheet.xlsx")
                .Should().NotBeNull();

            var chart2Xml = LoadXml(archive, "ppt/charts/chart2.xml");
            var chart2Text = chart2Xml.ToString(SaveOptions.DisableFormatting);
            chart2Text.Should().Contain("Edited Jan");
            chart2Text.Should().Contain("Edited Actual");
            chart2Text.Should().Contain("Edited Forecast");
            chart2Text.Should().Contain("ChartData!$A$2:$A$4");
            chart2Text.Should().Contain("ChartData!$B$1");
            chart2Text.Should().Contain("ChartData!$B$2:$B$4");
            chart2Text.Should().Contain("ChartData!$C$1");
            chart2Text.Should().Contain("ChartData!$C$2:$C$4");
            chart2Text.Should().NotContain("Sheet1!$B$1",
                "edited chart formulas should point at regenerated ChartData ranges");
            chart2Xml.Root!.Element(ChartNs + "externalData")!.Attribute(RelsDocNs + "id")!.Value
                .Should().Be("rIdWorkbook1");
            var chart2Rels = LoadXml(archive, "ppt/charts/_rels/chart2.xml.rels");
            Relationship(chart2Rels, PackageRelType, "../embeddings/chartWorkbook2.xlsx").Should().NotBeNull();

            var chart3Xml = LoadXml(archive, "ppt/charts/chart3.xml");
            var chart3Text = chart3Xml.ToString(SaveOptions.DisableFormatting);
            chart3Text.Should().Contain("Sheet1!$B$1");
            chart3Text.Should().Contain("Sheet1!$A$2:$A$5");
            chart3Text.Should().Contain("Sheet1!$B$2:$B$5");
            chart3Text.Should().Contain("Sheet1!$D$1");
            chart3Text.Should().Contain("Sheet1!$D$2:$D$5");
            chart3Text.Should().Contain("Sheet1!$C$1");
            chart3Text.Should().Contain("Sheet1!$C$2:$C$5");
            chart3Xml.Root!.Element(ChartNs + "externalData")!.Attribute(RelsDocNs + "id")!.Value
                .Should().Be("rId1");
            var chart3Rels = LoadXml(archive, "ppt/charts/_rels/chart3.xml.rels");
            Relationship(chart3Rels, PackageRelType, "../embeddings/Microsoft_Excel_Worksheet2.xlsx")
                .Should().NotBeNull();

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            Default(contentTypes, "xlsx", SpreadsheetWorkbookContentType).Should().NotBeNull();
            Override(contentTypes, "/ppt/embeddings/chartWorkbook2.xlsx", SpreadsheetWorkbookContentType)
                .Should().NotBeNull();

            using var regeneratedWorkbook = new ZipArchive(
                new MemoryStream(ReadBytes(archive, "ppt/embeddings/chartWorkbook2.xlsx")),
                ZipArchiveMode.Read);
            var regeneratedSheet = LoadXml(regeneratedWorkbook, "xl/worksheets/sheet1.xml")
                .ToString(SaveOptions.DisableFormatting);
            regeneratedSheet.Should().Contain("Edited Actual");
            regeneratedSheet.Should().Contain("Edited Forecast");
            regeneratedSheet.Should().Contain("Edited Jan");
            regeneratedSheet.Should().Contain("Edited Feb");
            regeneratedSheet.Should().Contain("Edited Mar");
            regeneratedSheet.Should().Contain("111");
            regeneratedSheet.Should().Contain("222");
            regeneratedSheet.Should().Contain("333");
            regeneratedSheet.Should().Contain("444");
            regeneratedSheet.Should().Contain("555");
            regeneratedSheet.Should().Contain("666");
        }

        var reloaded = PptxPackageReader.Read(new MemoryStream(savedBytes));
        var reloadedCharts = reloaded.Slides
            .SelectMany(slide => slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .Select(shape => shape.Chart!)
            .ToArray();
        reloadedCharts.Should().HaveCount(3);
        AssertChartFormulaReferences(
            reloadedCharts[0],
            ("Sheet1!$B$1", "Sheet1!$A$2:$A$5", "Sheet1!$B$2:$B$5"),
            ("Sheet1!$C$1", "Sheet1!$A$2:$A$5", "Sheet1!$C$2:$C$5"),
            ("Sheet1!$D$1", "Sheet1!$A$2:$A$5", "Sheet1!$D$2:$D$5"));
        AssertChartFormulaReferences(
            reloadedCharts[1],
            ("ChartData!$B$1", "ChartData!$A$2:$A$4", "ChartData!$B$2:$B$4"),
            ("ChartData!$C$1", "ChartData!$A$2:$A$4", "ChartData!$C$2:$C$4"));
        reloadedCharts[1].Categories.Should().Equal("Edited Jan", "Edited Feb", "Edited Mar");
        reloadedCharts[1].Series[0].Name.Should().Be("Edited Actual");
        reloadedCharts[1].Series[0].Values.Should().Equal(111, 222, 333);
        reloadedCharts[1].Series[1].Name.Should().Be("Edited Forecast");
        reloadedCharts[1].Series[1].Values.Should().Equal(444, 555, 666);
        AssertChartFormulaReferences(
            reloadedCharts[2],
            ("Sheet1!$B$1", "Sheet1!$A$2:$A$5", "Sheet1!$B$2:$B$5"),
            ("Sheet1!$D$1", "Sheet1!$A$2:$A$5", "Sheet1!$D$2:$D$5"),
            ("Sheet1!$C$1", "Sheet1!$A$2:$A$5", "Sheet1!$C$2:$C$5"));
    }

    [Fact]
    public void CoreProperties_RoundTripThroughPptxPackage()
    {
        var presentation = Presentation.CreateEmpty();
        var created = new DateTimeOffset(2026, 6, 29, 9, 30, 0, TimeSpan.Zero);
        var modified = created.AddMinutes(45);
        typeof(Presentation)
            .GetProperty(nameof(Presentation.Properties))!
            .PropertyType
            .Should()
            .Be(typeof(DocumentProperties));

        presentation.Properties.Title = "FreeP title";
        presentation.Properties.Author = "FreeP author";
        presentation.Properties.Subject = "FreeP subject";
        presentation.Properties.Keywords = "freep,pptx,opc";
        presentation.Properties.Comments = "FreeP comments";
        presentation.Properties.LastModifiedBy = "FreeP editor";
        presentation.Properties.Created = created;
        presentation.Properties.Modified = modified;
        presentation.Properties.Category = "FreeP category";
        presentation.Properties.ContentStatus = "Draft";
        presentation.Properties.Language = "en-US";
        presentation.Properties.Version = "2026.06";

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var coreProperties = OpcDocumentProperties.ReadCoreProperties(
                LoadXml(archive, OpcPackageProperties.CorePropertiesZipEntry));
            coreProperties.Should().Be(new CoreDocumentProperties(
                Title: "FreeP title",
                Author: "FreeP author",
                Subject: "FreeP subject",
                Keywords: "freep,pptx,opc",
                Comments: "FreeP comments",
                LastModifiedBy: "FreeP editor",
                Created: created,
                Modified: modified,
                Category: "FreeP category",
                ContentStatus: "Draft",
                Language: "en-US",
                Version: "2026.06"));
        }

        stream.Position = 0;
        var reloaded = PptxPackageReader.Read(stream);
        reloaded.Properties.Title.Should().Be("FreeP title");
        reloaded.Properties.Author.Should().Be("FreeP author");
        reloaded.Properties.Subject.Should().Be("FreeP subject");
        reloaded.Properties.Keywords.Should().Be("freep,pptx,opc");
        reloaded.Properties.Comments.Should().Be("FreeP comments");
        reloaded.Properties.LastModifiedBy.Should().Be("FreeP editor");
        reloaded.Properties.Created.Should().Be(created);
        reloaded.Properties.Modified.Should().Be(modified);
        reloaded.Properties.Category.Should().Be("FreeP category");
        reloaded.Properties.ContentStatus.Should().Be("Draft");
        reloaded.Properties.Language.Should().Be("en-US");
        reloaded.Properties.Version.Should().Be("2026.06");
    }

    [Fact]
    public void ReadWriteRead_RetainsUnmodeledPackagePartsRelationshipsAndContentTypes()
    {
        using var source = BuildPptxWithUnmodeledPackageData();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        loaded.Slides.Should().HaveCount(1);

        loaded.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 77,
            Name = "Modeled edit",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            var extendedProperties = OpcDocumentProperties.ReadExtendedProperties(
                LoadXml(archive, OpcPackageProperties.ExtendedPropertiesZipEntry));
            extendedProperties.Application.Should().Be("FreeP retention harness");
            var customProperties = OpcCustomDocumentProperties.FromDocument(
                LoadXml(archive, OpcPackageProperties.CustomPropertiesZipEntry));
            customProperties.GetString("RetentionMarker").Should().Be("retain-me");
            ReadText(archive, "customXml/item1.xml").Should().Contain("retain-me");
            ReadText(archive, "customXml/itemProps1.xml").Should().Contain("itemID");
            ReadText(archive, "customXml/payload.freex").Should().Contain("freex-payload");
            ReadBytes(archive, "ppt/customData/viewState.bin").Should().Equal(new byte[] { 0x46, 0x50, 0x52, 0x01 });

            var rootRels = LoadXml(archive, "_rels/.rels");
            Relationship(
                rootRels,
                OpcPackageProperties.ExtendedPropertiesRelationshipType,
                OpcPackageProperties.ExtendedPropertiesZipEntry).Should().NotBeNull();
            Relationship(
                rootRels,
                OpcPackageProperties.CustomPropertiesRelationshipType,
                OpcPackageProperties.CustomPropertiesZipEntry).Should().NotBeNull();
            Relationship(rootRels, CustomXmlRelType, "customXml/item1.xml").Should().NotBeNull();
            var externalReviewRel = Relationship(rootRels, ExternalReviewRelType, "https://example.com/freep-review");
            externalReviewRel.Should().NotBeNull();
            externalReviewRel!.Attribute("TargetMode")?.Value.Should().Be("External");
            Relationship(rootRels, UnknownSlideMirrorRelType, "ppt/slides/slide1.xml").Should().BeNull();

            var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
            Relationship(presRels, UnknownViewRelType, "customData/viewState.bin").Should().NotBeNull();

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            Override(
                contentTypes,
                OpcPackageProperties.ExtendedPropertiesPartName,
                OpcPackageProperties.ExtendedPropertiesContentType).Should().NotBeNull();
            Override(
                contentTypes,
                OpcPackageProperties.CustomPropertiesPartName,
                OpcPackageProperties.CustomPropertiesContentType).Should().NotBeNull();
            Override(contentTypes, "/customXml/itemProps1.xml",
                "application/vnd.openxmlformats-officedocument.customXmlProperties+xml").Should().NotBeNull();
            Override(contentTypes, "/ppt/customData/viewState.bin",
                "application/vnd.example.freep.viewstate").Should().NotBeNull();
            Default(contentTypes, "freex", "application/vnd.example.freep.payload").Should().NotBeNull();
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        reloaded.Slides.Should().HaveCount(1);
        reloaded.Slides[0].Shapes.Should().Contain(s => s.Name == "Modeled edit");
    }

    [Fact]
    public void ReadWriteRead_SemanticEditRetainsPresentationScopedCustomXmlPackageParts()
    {
        using var source = BuildPptxWithPresentationScopedCustomXml();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        loaded.Slides.Should().HaveCount(1);

        loaded.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 79,
            Name = "Modeled presentation custom XML edit",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 685800,
            OffsetYEmu = 685800,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            ReadText(archive, "customXml/item2.xml").Should().Contain("presentation-scoped-retain-me");
            ReadText(archive, "customXml/itemProps2.xml").Should().Contain("{22222222-2222-2222-2222-222222222222}");
            ReadText(archive, "customXml/item2.freexmeta").Should().Be("presentation custom xml payload");

            var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
            Relationship(presRels, CustomXmlRelType, "../customXml/item2.xml").Should().NotBeNull();

            var itemRels = LoadXml(archive, "customXml/_rels/item2.xml.rels");
            Relationship(
                itemRels,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps",
                "itemProps2.xml").Should().NotBeNull();
            Relationship(
                itemRels,
                "http://example.com/freep/relationships/customXmlPayload",
                "item2.freexmeta").Should().NotBeNull();

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            Override(contentTypes, "/customXml/itemProps2.xml",
                "application/vnd.openxmlformats-officedocument.customXmlProperties+xml").Should().NotBeNull();
            Default(contentTypes, "freexmeta", "application/vnd.example.freep.customxml-payload").Should().NotBeNull();
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        reloaded.Slides.Should().HaveCount(1);
        reloaded.Slides[0].Shapes.Should().Contain(s => s.Name == "Modeled presentation custom XML edit");
    }

    [Fact]
    public void ReadWriteRead_RetainsViewAndPrintSettingsPackageSemantics()
    {
        using var source = BuildPptxWithViewAndPrintSettings();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        loaded.Slides.Should().HaveCount(1);

        loaded.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 78,
            Name = "Modeled view print edit",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 457200,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            var presProps = ReadText(archive, "ppt/presProps.xml");
            presProps.Should().Contain("freep-print-retain");
            presProps.Should().Contain("prnWhat=\"handouts3\"");
            presProps.Should().Contain("frameSlides=\"1\"");

            var viewProps = ReadText(archive, "ppt/viewProps.xml");
            viewProps.Should().Contain("freep-view-retain");
            viewProps.Should().Contain("lastView=\"sldSorterView\"");
            viewProps.Should().Contain("cx=\"12700\"");

            ReadBytes(archive, "ppt/printerSettings/printerSettings1.bin")
                .Should()
                .Equal(new byte[] { 0x46, 0x50, 0x50, 0x01 });

            var presPropsRels = LoadXml(archive, "ppt/_rels/presProps.xml.rels");
            Relationship(
                presPropsRels,
                PrinterSettingsRelType,
                "printerSettings/printerSettings1.bin").Should().NotBeNull();

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            Override(
                contentTypes,
                "/ppt/printerSettings/printerSettings1.bin",
                "application/vnd.openxmlformats-officedocument.presentationml.printerSettings").Should().NotBeNull();
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        reloaded.Slides.Should().HaveCount(1);
        reloaded.Slides[0].Shapes.Should().Contain(s => s.Name == "Modeled view print edit");
    }

    [Fact]
    public void ReadWriteRead_ChartDataSemanticEdit_RegeneratesChartWorkbookAndKeepsUnrelatedPackageParts()
    {
        using var source = BuildPptxWithChartWorkbookAndUnrelatedPackageData();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        var chartShape = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart);
        chartShape.Chart.Should().NotBeNull();
        chartShape.Chart!.RegenerateWorkbookOnSave.Should().BeFalse();

        new ReplaceChartDataCommand(
            slideIndex: 0,
            shapeId: chartShape.Id,
            categories: ["East", "West"],
            seriesNames: ["Actual"],
            values: [new double?[] { 42, 51 }]).Apply(loaded);

        chartShape.Chart.RegenerateWorkbookOnSave.Should().BeTrue();

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            ReadText(archive, "customXml/chartWorkbookPayload.xml")
                .Should()
                .Contain("unrelated-retain-me");
            archive.GetEntry("ppt/embeddings/sourceWorkbook.xlsx").Should().BeNull(
                "a chart data edit must not carry forward the stale source workbook sidecar");

            var chartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            chartXml.ToString(SaveOptions.DisableFormatting).Should().Contain("East");
            chartXml.ToString(SaveOptions.DisableFormatting).Should().Contain("42");
            var externalData = chartXml.Root!.Element(ChartNs + "externalData");
            externalData.Should().NotBeNull("the edited chart should point at a regenerated workbook sidecar");
            externalData!.Attribute(RelsDocNs + "id")!.Value.Should().Be("rIdWorkbook1");

            var chartRels = LoadXml(archive, "ppt/charts/_rels/chart1.xml.rels");
            Relationship(chartRels, PackageRelType, "../embeddings/chartWorkbook1.xlsx").Should().NotBeNull();

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            Override(
                contentTypes,
                "/ppt/embeddings/chartWorkbook1.xlsx",
                SpreadsheetWorkbookContentType).Should().NotBeNull();
            Override(
                contentTypes,
                "/customXml/chartWorkbookPayload.xml",
                "application/vnd.example.freep.chart-workbook-payload+xml").Should().NotBeNull();

            using var workbookArchive = new ZipArchive(
                new MemoryStream(ReadBytes(archive, "ppt/embeddings/chartWorkbook1.xlsx")),
                ZipArchiveMode.Read);
            var sheetXml = LoadXml(workbookArchive, "xl/worksheets/sheet1.xml")
                .ToString(SaveOptions.DisableFormatting);
            sheetXml.Should().Contain("Actual");
            sheetXml.Should().Contain("East");
            sheetXml.Should().Contain("42");
            sheetXml.Should().Contain("51");
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        var reloadedChart = reloaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        reloadedChart.Categories.Should().Equal("East", "West");
        reloadedChart.Series.Should().ContainSingle();
        reloadedChart.Series[0].Name.Should().Be("Actual");
        reloadedChart.Series[0].Values.Should().Equal(42, 51);
    }

    [Fact]
    public void ReadWriteRead_UnrelatedEditToDeckWithStyledChart_PreservesChartStyleAndColorSidecars()
    {
        // Reproduces the confirmed finding: a regular (non-ChartEx) chart's PowerPoint
        // chart-style/chart-color-style sidecars must survive a save even when the chart
        // itself is never touched — only some other, unrelated shape is edited.
        using var source = BuildPptxWithStyledChartWorkbookAndUnrelatedPackageData();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        var chartShape = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart);
        chartShape.Chart.Should().NotBeNull();
        chartShape.Chart!.RegenerateWorkbookOnSave.Should().BeFalse(
            "this test never edits the chart's own data - only an unrelated shape is added");

        AddModeledShapeEdit(loaded, "styled-chart-unrelated-edit.pptx");

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read);

        archive.GetEntry("ppt/charts/style1.xml").Should().NotBeNull(
            "an untouched chart's PowerPoint chart-style sidecar must survive a save that never edited the chart");
        archive.GetEntry("ppt/charts/colors1.xml").Should().NotBeNull(
            "an untouched chart's PowerPoint chart-color-style sidecar must survive a save that never edited the chart");
        ReadText(archive, "ppt/charts/style1.xml").Should().Contain("must-survive-style");
        ReadText(archive, "ppt/charts/colors1.xml").Should().Contain("must-survive-colors");

        var chartRels = LoadXml(archive, "ppt/charts/_rels/chart1.xml.rels");
        Relationship(chartRels, ChartStyleRelType, "style1.xml").Should().NotBeNull(
            "the chart's own rels must still reference its style sidecar after an unrelated save");
        Relationship(chartRels, ChartColorStyleRelType, "colors1.xml").Should().NotBeNull(
            "the chart's own rels must still reference its color-style sidecar after an unrelated save");
        Relationship(chartRels, PackageRelType, "../embeddings/sourceStyledWorkbook.xlsx").Should().NotBeNull(
            "the pre-existing embedded-workbook relationship must still be preserved alongside the style sidecars");

        var contentTypes = LoadXml(archive, "[Content_Types].xml");
        Override(contentTypes, "/ppt/charts/style1.xml", ChartStyleContentType).Should().NotBeNull(
            "the saved package must declare a content type for the preserved chart-style part");
        Override(contentTypes, "/ppt/charts/colors1.xml", ChartColorStyleContentType).Should().NotBeNull(
            "the saved package must declare a content type for the preserved chart-color-style part");
    }

    [Fact]
    public void ReadWriteRead_StyledChartOwnDataEdit_PreservesChartStyleAndColorSidecars()
    {
        // Reproduces the R138 remediation finding: editing a REGULAR (non-ChartEx) chart's OWN
        // data through the real user-facing command (ReplaceChartDataCommand, the same command
        // the chart-data dialog dispatches) must regenerate the chart's embedded workbook
        // WITHOUT dropping its PowerPoint-2013+ chartStyle/chartColorStyle sidecars. The r137 fix
        // wave only preserved those sidecars for the "chart untouched, something else edited"
        // case (see ReadWriteRead_UnrelatedEditToDeckWithStyledChart_PreservesChartStyleAndColorSidecars
        // above) - it never touched WriteChartPart's RegenerateWorkbookOnSave branch, which
        // replaced the chart's entire .rels document with just the new workbook relationship.
        using var source = BuildPptxWithStyledChartWorkbookAndUnrelatedPackageData();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        var chartShape = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart);
        chartShape.Chart.Should().NotBeNull();
        chartShape.Chart!.IsChartEx.Should().BeFalse();
        chartShape.Chart.RegenerateWorkbookOnSave.Should().BeFalse(
            "this test edits the chart's own data via the command below, not before it");

        new ReplaceChartDataCommand(
            slideIndex: 0,
            shapeId: chartShape.Id,
            categories: ["New East", "New West"],
            seriesNames: ["New Actual"],
            values: [new double?[] { 77, 88 }]).Apply(loaded);

        chartShape.Chart.RegenerateWorkbookOnSave.Should().BeTrue();

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        using var archive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read);

        // The style/color sidecars are untouched by a data-only edit and must survive the save.
        archive.GetEntry("ppt/charts/style1.xml").Should().NotBeNull(
            "a chart-own data edit must not drop the chart's PowerPoint chart-style sidecar");
        archive.GetEntry("ppt/charts/colors1.xml").Should().NotBeNull(
            "a chart-own data edit must not drop the chart's PowerPoint chart-color-style sidecar");
        ReadText(archive, "ppt/charts/style1.xml").Should().Contain("must-survive-style");
        ReadText(archive, "ppt/charts/colors1.xml").Should().Contain("must-survive-colors");

        var chartRels = LoadXml(archive, "ppt/charts/_rels/chart1.xml.rels");
        Relationship(chartRels, ChartStyleRelType, "style1.xml").Should().NotBeNull(
            "the edited chart's own rels must still reference its style sidecar");
        Relationship(chartRels, ChartColorStyleRelType, "colors1.xml").Should().NotBeNull(
            "the edited chart's own rels must still reference its color-style sidecar");

        var workbookRelationship = Relationship(chartRels, PackageRelType, "../embeddings/chartWorkbook1.xlsx");
        workbookRelationship.Should().NotBeNull(
            "the edited chart must point its workbook relationship at a regenerated workbook");

        archive.GetEntry("ppt/embeddings/sourceStyledWorkbook.xlsx").Should().BeNull(
            "the pre-edit embedded workbook must not be carried forward once its data is stale");

        using var workbookArchive = new ZipArchive(
            new MemoryStream(ReadBytes(archive, "ppt/embeddings/chartWorkbook1.xlsx")),
            ZipArchiveMode.Read);
        var sheetXml = LoadXml(workbookArchive, "xl/worksheets/sheet1.xml").ToString(SaveOptions.DisableFormatting);
        sheetXml.Should().Contain("New Actual");
        sheetXml.Should().Contain("New East");
        sheetXml.Should().Contain("77");
        sheetXml.Should().Contain("88");

        var contentTypes = LoadXml(archive, "[Content_Types].xml");
        Override(contentTypes, "/ppt/charts/style1.xml", ChartStyleContentType).Should().NotBeNull(
            "the saved package must declare a content type for the preserved chart-style part");
        Override(contentTypes, "/ppt/charts/colors1.xml", ChartColorStyleContentType).Should().NotBeNull(
            "the saved package must declare a content type for the preserved chart-color-style part");
    }

    [Fact]
    public void ReadWriteRead_ChartWorkbookOnlyNoStyleSidecars_StillPreservedAfterUnrelatedEdit()
    {
        // Sibling no-regression test: a chart that has ONLY an embedded-workbook relationship
        // (the common case exercised by the pre-existing fixture/tests above, with no
        // chartStyle/chartColorStyle sidecars at all) must keep working exactly as before once
        // the preservation filter is broadened to also carry style/color sidecars forward.
        using var source = BuildPptxWithChartWorkbookAndUnrelatedPackageData();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        var chartShape = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart);
        chartShape.Chart!.RegenerateWorkbookOnSave.Should().BeFalse();

        AddModeledShapeEdit(loaded, "plain-chart-unrelated-edit.pptx");

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        using var archive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read);

        archive.GetEntry("ppt/embeddings/sourceWorkbook.xlsx").Should().NotBeNull(
            "a chart with only a workbook relationship (no style sidecars) must keep preserving that workbook");
        var chartRels = LoadXml(archive, "ppt/charts/_rels/chart1.xml.rels");
        Relationship(chartRels, PackageRelType, "../embeddings/sourceWorkbook.xlsx").Should().NotBeNull();
        // No style/color relationships existed in the source, so none should be invented.
        chartRels.Root!.Elements(RelsNs + "Relationship").Should().ContainSingle();
    }

    [Fact]
    public void InsertChart_PositionallyCollidesWithAnExistingChartsPartNumber_GetsItsOwnRegeneratedWorkbook()
    {
        // Reproduces the confirmed finding: EditingSession.InsertChart (the real production
        // "Insert > Chart" call site) never set RegenerateWorkbookOnSave on the brand-new
        // ChartShape it built. A new chart has no SourcePartPath, so on save
        // PptxPackageWriter.SourceChartPath falls back to a purely positional
        // "ppt/charts/chart{index}.xml" name. Inserting the new chart on a slide that is
        // written BEFORE an existing chart's slide makes the new chart claim chartIndex=1 -
        // exactly the pre-existing chart's own original part number - and, without the flag,
        // PptxChartWriter's non-regenerate branch would merge THAT unrelated chart's
        // <c:externalData>/workbook relationship onto the new chart, wiring its "Edit Data in
        // Excel" command to somebody else's workbook instead of writing its own.
        using var source = BuildPptxWithChartWorkbookAndUnrelatedPackageData();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        var existingChartShape = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart);
        existingChartShape.Chart!.SourcePartPath.Should().Be("ppt/charts/chart1.xml");
        existingChartShape.Chart.RegenerateWorkbookOnSave.Should().BeFalse();

        // Insert a fresh slide BEFORE the one carrying the existing chart, so the new chart
        // (positionally first) claims chartIndex=1 on save - the same number as the existing
        // chart's real, original part.
        loaded.Slides.Insert(0, new Slide());
        var bus = new PresentationCommandBus(loaded);
        var session = new FreeP.App.Compositor.EditingSession(loaded, bus);
        session.SelectSlide(0);
        var newChartShape = session.InsertChart();
        newChartShape.Chart.Should().NotBeNull();

        // This is the production call site under test: EditingSession.InsertChartCore (via
        // InsertChart) must mark the newly created chart for workbook regeneration.
        newChartShape.Chart!.RegenerateWorkbookOnSave.Should().BeTrue(
            "a brand-new chart has no preserved package data of its own to merge on save");

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        using var archive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read);

        // The new chart was written first (slide 0) and so occupies "chart1.xml" - the same
        // positional name the existing chart originally had.
        var newChartXml = LoadXml(archive, "ppt/charts/chart1.xml");
        var newChartExternalData = newChartXml.Root!.Element(ChartNs + "externalData");
        newChartExternalData.Should().NotBeNull("the new chart must still get its own Edit-Data wiring");
        var newChartRelId = newChartExternalData!.Attribute(RelsDocNs + "id")!.Value;

        var newChartRels = LoadXml(archive, "ppt/charts/_rels/chart1.xml.rels");
        var newChartWorkbookRelationship = newChartRels.Root!.Elements(RelsNs + "Relationship")
            .FirstOrDefault(r => r.Attribute("Id")?.Value == newChartRelId);
        newChartWorkbookRelationship.Should().NotBeNull();
        var newChartWorkbookTarget = newChartWorkbookRelationship!.Attribute("Target")!.Value;

        // Must NOT be wired to the pre-existing chart's stale embedded workbook.
        newChartWorkbookTarget.Should().NotBe("../embeddings/sourceWorkbook.xlsx",
            "the new chart must get its own freshly regenerated workbook, not the unrelated " +
            "chart's original one via the colliding positional part name");

        var newChartWorkbookPath = "ppt/" + newChartWorkbookTarget.Replace("../", "");
        var newChartWorkbookEntry = archive.GetEntry(newChartWorkbookPath);
        newChartWorkbookEntry.Should().NotBeNull("the new chart's regenerated workbook must actually be written");
        using (var workbookStream = newChartWorkbookEntry!.Open())
        using (var reader = new StreamReader(workbookStream))
        {
            reader.ReadToEnd().Should().NotContain("stale workbook bytes",
                "the new chart's workbook must be freshly generated, not a copy of the unrelated chart's data");
        }
    }

    [Fact]
    public void InsertChart_PositionallyCollidesWithAnExistingChartsPartNumber_ExistingChartKeepsItsOwnWorkbook()
    {
        // Sibling no-regression test for the fix above: the pre-existing chart that the new
        // chart's part-number collides with must be completely unaffected - it keeps
        // preserving its own original embedded workbook by SourcePartPath, regardless of
        // where in the slide order (and therefore under what new positional chartN.xml name)
        // it gets re-written.
        using var source = BuildPptxWithChartWorkbookAndUnrelatedPackageData();
        var loaded = PptxPackageReader.Read(source);
        var existingChartShape = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart);

        loaded.Slides.Insert(0, new Slide());
        var bus = new PresentationCommandBus(loaded);
        var session = new FreeP.App.Compositor.EditingSession(loaded, bus);
        session.SelectSlide(0);
        session.InsertChart();

        // The existing chart is now on slide index 1 and will be written under the new
        // positional name "chart2.xml" - but its SourcePartPath still says "chart1.xml", so
        // it must still find and preserve its own original workbook relationship.
        existingChartShape.Chart!.RegenerateWorkbookOnSave.Should().BeFalse();

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        using var archive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read);

        archive.GetEntry("ppt/embeddings/sourceWorkbook.xlsx").Should().NotBeNull(
            "the pre-existing chart's own original workbook must still be carried forward");
        var existingChartRels = LoadXml(archive, "ppt/charts/_rels/chart2.xml.rels");
        Relationship(existingChartRels, PackageRelType, "../embeddings/sourceWorkbook.xlsx").Should().NotBeNull(
            "the pre-existing chart must still be wired to its own original embedded workbook");
    }

    [Fact]
    public void ReadWriteRead_ChartExDataEdit_RegeneratesEmbeddedWorkbookSoItMatchesTheOnSlideCache()
    {
        // Reproduces the confirmed finding: editing a ChartEx chart's data through the real
        // user-facing command (ReplaceChartDataCommand, the same command a chart-data-dialog
        // edit dispatches) must refresh BOTH the on-slide cx:data cache AND the chart's own
        // embedded "Edit Data in Excel" workbook — not just the cache — so the next Excel
        // round trip doesn't start from stale numbers.
        using var source = BuildPptxWithChartExWorkbookAndStyleSidecars();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        var chartShape = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart);
        chartShape.Chart.Should().NotBeNull();
        chartShape.Chart!.IsChartEx.Should().BeTrue();
        chartShape.Chart.RegenerateWorkbookOnSave.Should().BeFalse();

        new ReplaceChartDataCommand(
            slideIndex: 0,
            shapeId: chartShape.Id,
            categories: ["New East", "New West"],
            seriesNames: ["New Actual"],
            values: [new double?[] { 77, 88 }]).Apply(loaded);

        chartShape.Chart.RegenerateWorkbookOnSave.Should().BeTrue();

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        using var archive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read);

        // The on-slide cache was already known to refresh correctly - confirm it still does.
        var chartExXml = LoadXml(archive, "ppt/charts/chartEx1.xml").ToString(SaveOptions.DisableFormatting);
        chartExXml.Should().Contain("New East");
        chartExXml.Should().Contain("77");

        // The chart's own embedded workbook - the one "Edit Data in Excel" opens - must be
        // regenerated with the SAME new numbers, not left holding the pre-edit data.
        var chartRels = LoadXml(archive, "ppt/charts/_rels/chartEx1.xml.rels");
        var workbookRelationship = Relationship(chartRels, PackageRelType, "../embeddings/chartWorkbook1.xlsx");
        workbookRelationship.Should().NotBeNull(
            "the edited ChartEx chart must point its workbook relationship at a regenerated workbook");

        archive.GetEntry("ppt/embeddings/sourceChartExWorkbook.xlsx").Should().BeNull(
            "the pre-edit embedded workbook must not be carried forward once its data is stale");

        using var workbookArchive = new ZipArchive(
            new MemoryStream(ReadBytes(archive, "ppt/embeddings/chartWorkbook1.xlsx")),
            ZipArchiveMode.Read);
        var sheetXml = LoadXml(workbookArchive, "xl/worksheets/sheet1.xml").ToString(SaveOptions.DisableFormatting);
        sheetXml.Should().Contain("New Actual");
        sheetXml.Should().Contain("New East");
        sheetXml.Should().Contain("77");
        sheetXml.Should().Contain("88");

        // The style/color sidecars are untouched by a data-only edit and must survive too.
        Relationship(chartRels, ChartStyleRelType, "style1.xml").Should().NotBeNull();
        Relationship(chartRels, ChartColorStyleRelType, "colors1.xml").Should().NotBeNull();
        archive.GetEntry("ppt/charts/style1.xml").Should().NotBeNull();
        archive.GetEntry("ppt/charts/colors1.xml").Should().NotBeNull();
    }

    [Fact]
    public void ReadWriteRead_ChartExUntouched_PreservesEmbeddedWorkbookAndStyleSidecarsVerbatim()
    {
        // Sibling no-regression test: when a ChartEx chart's data is NOT edited, its embedded
        // workbook and style/color sidecars must still be preserved byte-for-byte, exactly as
        // before the fix that teaches the writer to regenerate the workbook on data edits.
        using var source = BuildPptxWithChartExWorkbookAndStyleSidecars();
        var loaded = PptxPackageReader.Read(source);
        var chartShape = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart);
        chartShape.Chart!.RegenerateWorkbookOnSave.Should().BeFalse();

        AddModeledShapeEdit(loaded, "chartex-untouched-unrelated-edit.pptx");

        using var sourceArchive = new ZipArchive(source, ZipArchiveMode.Read);
        var originalWorkbookBytes = ReadBytes(sourceArchive, "ppt/embeddings/sourceChartExWorkbook.xlsx");
        var originalStyleBytes = ReadBytes(sourceArchive, "ppt/charts/style1.xml");
        var originalColorsBytes = ReadBytes(sourceArchive, "ppt/charts/colors1.xml");

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        using var savedArchive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read);

        ReadBytes(savedArchive, "ppt/embeddings/sourceChartExWorkbook.xlsx").Should().Equal(originalWorkbookBytes,
            "an untouched ChartEx chart's embedded workbook must be preserved byte-for-byte");
        ReadBytes(savedArchive, "ppt/charts/style1.xml").Should().Equal(originalStyleBytes);
        ReadBytes(savedArchive, "ppt/charts/colors1.xml").Should().Equal(originalColorsBytes);

        var chartRels = LoadXml(savedArchive, "ppt/charts/_rels/chartEx1.xml.rels");
        Relationship(chartRels, PackageRelType, "../embeddings/sourceChartExWorkbook.xlsx").Should().NotBeNull();
        Relationship(chartRels, ChartStyleRelType, "style1.xml").Should().NotBeNull();
        Relationship(chartRels, ChartColorStyleRelType, "colors1.xml").Should().NotBeNull();
    }

    [Fact]
    public void ReadWriteRead_MultiChartSemanticEdit_RegeneratesOnlyEditedChartWorkbookAndPreservesChartWorkbookMapping()
    {
        using var source = BuildPptxWithMultipleChartWorkbooksAndUnrelatedPackageData();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        var chartShapes = loaded.Slides[0].Shapes.Where(shape => shape.Kind == SlideShapeKind.Chart).ToArray();
        chartShapes.Should().HaveCount(3);
        chartShapes[0].Chart!.RegenerateWorkbookOnSave.Should().BeFalse();
        chartShapes[1].Chart!.RegenerateWorkbookOnSave.Should().BeFalse();
        chartShapes[2].Chart!.RegenerateWorkbookOnSave.Should().BeFalse();

        new ReplaceChartDataCommand(
            slideIndex: 0,
            shapeId: chartShapes[1].Id,
            categories: ["Edited Q1", "Edited Q2"],
            seriesNames: ["Edited Revenue"],
            values: [new double?[] { 123, 456 }]).Apply(loaded);

        chartShapes[0].Chart!.RegenerateWorkbookOnSave.Should().BeFalse();
        chartShapes[1].Chart!.RegenerateWorkbookOnSave.Should().BeTrue();
        chartShapes[2].Chart!.RegenerateWorkbookOnSave.Should().BeFalse();

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            ReadText(archive, "customXml/multiChartWorkbookPayload.xml")
                .Should()
                .Contain("multi-chart-retain-me");

            ReadText(archive, "ppt/embeddings/sourceWorkbookAlpha.xlsx")
                .Should()
                .Be("alpha workbook bytes");
            archive.GetEntry("ppt/embeddings/sourceWorkbookBeta.xlsx").Should().BeNull(
                "only the semantically edited chart should drop its stale source workbook sidecar");
            ReadText(archive, "ppt/embeddings/sourceWorkbookGamma.xlsx")
                .Should()
                .Be("gamma workbook bytes");
            archive.GetEntry("ppt/embeddings/chartWorkbook2.xlsx").Should().NotBeNull(
                "the edited second chart should regenerate its own workbook path without overwriting neighbors");

            var chart1Xml = LoadXml(archive, "ppt/charts/chart1.xml");
            chart1Xml.Root!.Element(ChartNs + "externalData")!.Attribute(RelsDocNs + "id")!.Value
                .Should().Be("rIdSourceWorkbookAlpha");
            var chart1Rels = LoadXml(archive, "ppt/charts/_rels/chart1.xml.rels");
            Relationship(chart1Rels, PackageRelType, "../embeddings/sourceWorkbookAlpha.xlsx").Should().NotBeNull();

            var chart2Xml = LoadXml(archive, "ppt/charts/chart2.xml");
            chart2Xml.ToString(SaveOptions.DisableFormatting).Should().Contain("Edited Q1");
            chart2Xml.Root!.Element(ChartNs + "externalData")!.Attribute(RelsDocNs + "id")!.Value
                .Should().Be("rIdWorkbook1");
            var chart2Rels = LoadXml(archive, "ppt/charts/_rels/chart2.xml.rels");
            Relationship(chart2Rels, PackageRelType, "../embeddings/chartWorkbook2.xlsx").Should().NotBeNull();

            var chart3Xml = LoadXml(archive, "ppt/charts/chart3.xml");
            chart3Xml.Root!.Element(ChartNs + "externalData")!.Attribute(RelsDocNs + "id")!.Value
                .Should().Be("rIdSourceWorkbookGamma");
            var chart3Rels = LoadXml(archive, "ppt/charts/_rels/chart3.xml.rels");
            Relationship(chart3Rels, PackageRelType, "../embeddings/sourceWorkbookGamma.xlsx").Should().NotBeNull();

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            Override(contentTypes, "/ppt/embeddings/sourceWorkbookAlpha.xlsx", SpreadsheetWorkbookContentType)
                .Should().NotBeNull();
            Override(contentTypes, "/ppt/embeddings/sourceWorkbookBeta.xlsx", SpreadsheetWorkbookContentType)
                .Should().BeNull("the stale workbook override for the edited chart should not survive");
            Override(contentTypes, "/ppt/embeddings/chartWorkbook2.xlsx", SpreadsheetWorkbookContentType)
                .Should().NotBeNull();
            Override(contentTypes, "/ppt/embeddings/sourceWorkbookGamma.xlsx", SpreadsheetWorkbookContentType)
                .Should().NotBeNull();
            Override(
                contentTypes,
                "/customXml/multiChartWorkbookPayload.xml",
                "application/vnd.example.freep.multi-chart-workbook-payload+xml").Should().NotBeNull();

            using var workbookArchive = new ZipArchive(
                new MemoryStream(ReadBytes(archive, "ppt/embeddings/chartWorkbook2.xlsx")),
                ZipArchiveMode.Read);
            var sheetXml = LoadXml(workbookArchive, "xl/worksheets/sheet1.xml")
                .ToString(SaveOptions.DisableFormatting);
            sheetXml.Should().Contain("Edited Revenue");
            sheetXml.Should().Contain("Edited Q1");
            sheetXml.Should().Contain("123");
            sheetXml.Should().Contain("456");
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        var reloadedCharts = reloaded.Slides[0].Shapes
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .Select(shape => shape.Chart!)
            .ToArray();
        reloadedCharts[0].Categories.Should().Equal("Alpha Old Q1", "Alpha Old Q2");
        reloadedCharts[0].Series[0].Name.Should().Be("Alpha Old Revenue");
        reloadedCharts[0].Series[0].Values.Should().Equal(10, 20);
        reloadedCharts[1].Categories.Should().Equal("Edited Q1", "Edited Q2");
        reloadedCharts[1].Series[0].Name.Should().Be("Edited Revenue");
        reloadedCharts[1].Series[0].Values.Should().Equal(123, 456);
        reloadedCharts[2].Categories.Should().Equal("Gamma Old Q1", "Gamma Old Q2");
        reloadedCharts[2].Series[0].Name.Should().Be("Gamma Old Revenue");
        reloadedCharts[2].Series[0].Values.Should().Equal(70, 80);
    }

    [Fact]
    public void ReadWriteRead_NonSequentialChartParts_PreservesSourceWorkbookMappingWhenOneChartChanges()
    {
        using var source = BuildPptxWithNonSequentialChartWorkbookParts();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        var chartShapes = loaded.Slides[0].Shapes.Where(shape => shape.Kind == SlideShapeKind.Chart).ToArray();
        chartShapes.Should().HaveCount(2);
        chartShapes[0].Chart!.SourcePartPath.Should().Be("ppt/charts/chart7.xml");
        chartShapes[1].Chart!.SourcePartPath.Should().Be("ppt/charts/chart3.xml");

        new ReplaceChartDataCommand(
            slideIndex: 0,
            shapeId: chartShapes[1].Id,
            categories: ["Edited Q1", "Edited Q2"],
            seriesNames: ["Edited Revenue"],
            values: [new double?[] { 321, 654 }]).Apply(loaded);

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            ReadText(archive, "ppt/embeddings/sourceWorkbookFirst.xlsx")
                .Should()
                .Be("first workbook bytes");
            archive.GetEntry("ppt/embeddings/sourceWorkbookSecond.xlsx").Should().BeNull(
                "the edited chart should drop its authored workbook even when the source chart part was not chart2.xml");
            archive.GetEntry("ppt/embeddings/chartWorkbook2.xlsx").Should().NotBeNull();
            archive.GetEntry("ppt/charts/chart7.xml").Should().BeNull("source chart part names are remapped to writer-owned chart indexes");
            archive.GetEntry("ppt/charts/chart3.xml").Should().BeNull("source chart part names are remapped to writer-owned chart indexes");

            var chart1Xml = LoadXml(archive, "ppt/charts/chart1.xml");
            chart1Xml.Root!.Element(ChartNs + "externalData")!.Attribute(RelsDocNs + "id")!.Value
                .Should().Be("rIdSourceWorkbookFirst");
            var chart1Rels = LoadXml(archive, "ppt/charts/_rels/chart1.xml.rels");
            Relationship(chart1Rels, PackageRelType, "../embeddings/sourceWorkbookFirst.xlsx").Should().NotBeNull();

            var chart2Xml = LoadXml(archive, "ppt/charts/chart2.xml");
            var chart2Text = chart2Xml.ToString(SaveOptions.DisableFormatting);
            chart2Text.Should().Contain("Edited Q1");
            chart2Text.Should().Contain("ChartData!$A$2:$A$3");
            chart2Text.Should().Contain("ChartData!$B$1");
            chart2Text.Should().Contain("ChartData!$B$2:$B$3");
            chart2Xml.Root!.Element(ChartNs + "externalData")!.Attribute(RelsDocNs + "id")!.Value
                .Should().Be("rIdWorkbook1");
            var chart2Rels = LoadXml(archive, "ppt/charts/_rels/chart2.xml.rels");
            Relationship(chart2Rels, PackageRelType, "../embeddings/chartWorkbook2.xlsx").Should().NotBeNull();

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            Override(contentTypes, "/ppt/embeddings/sourceWorkbookFirst.xlsx", SpreadsheetWorkbookContentType)
                .Should().NotBeNull();
            Override(contentTypes, "/ppt/embeddings/sourceWorkbookSecond.xlsx", SpreadsheetWorkbookContentType)
                .Should().BeNull();
            Override(contentTypes, "/ppt/embeddings/chartWorkbook2.xlsx", SpreadsheetWorkbookContentType)
                .Should().NotBeNull();

            using var workbookArchive = new ZipArchive(
                new MemoryStream(ReadBytes(archive, "ppt/embeddings/chartWorkbook2.xlsx")),
                ZipArchiveMode.Read);
            var sheetXml = LoadXml(workbookArchive, "xl/worksheets/sheet1.xml")
                .ToString(SaveOptions.DisableFormatting);
            sheetXml.Should().Contain("Edited Revenue");
            sheetXml.Should().Contain("Edited Q1");
            sheetXml.Should().Contain("321");
            sheetXml.Should().Contain("654");
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        var reloadedCharts = reloaded.Slides[0].Shapes
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .Select(shape => shape.Chart!)
            .ToArray();
        reloadedCharts[0].Categories.Should().Equal("First Old Q1", "First Old Q2");
        reloadedCharts[0].Series[0].Values.Should().Equal(11, 22);
        reloadedCharts[1].Categories.Should().Equal("Edited Q1", "Edited Q2");
        reloadedCharts[1].Series[0].Values.Should().Equal(321, 654);
    }

    [Fact]
    public void ReadWriteRead_AuthoredChartWorkbookFormulas_SemanticEditRewritesOnlyEditedChartRanges()
    {
        using var source = BuildPptxWithRichFormulaChartWorkbooksAndUnrelatedPackageData();
        var sourceBytes = source.ToArray();
        byte[] sourceFirstWorkbook;
        byte[] sourceSecondWorkbook;
        using (var sourceArchive = new ZipArchive(new MemoryStream(sourceBytes), ZipArchiveMode.Read))
        {
            sourceFirstWorkbook = ReadBytes(sourceArchive, "ppt/embeddings/authoredWorkbookFirst.xlsx");
            sourceSecondWorkbook = ReadBytes(sourceArchive, "ppt/embeddings/authoredWorkbookSecond.xlsx");
        }

        var loaded = PptxPackageReader.Read(new MemoryStream(sourceBytes));
        loaded.PackageSnapshot.Should().NotBeNull();
        var chartShapes = loaded.Slides[0].Shapes.Where(shape => shape.Kind == SlideShapeKind.Chart).ToArray();
        chartShapes.Should().HaveCount(2);

        var preservedChart = chartShapes[0].Chart!;
        preservedChart.RegenerateWorkbookOnSave.Should().BeFalse();
        preservedChart.Series.Should().HaveCount(2);
        preservedChart.Series[0].FormulaReferences.SeriesName.Should().Be("'Forecast Model 2026'!$C$1");
        preservedChart.Series[0].FormulaReferences.Category.Should().Be("'Forecast Model 2026'!$A$2:$A$4");
        preservedChart.Series[0].FormulaReferences.Values.Should().Be("'Forecast Model 2026'!Revenue_Actual");
        preservedChart.Series[1].FormulaReferences.SeriesName.Should().Be("'Forecast Model 2026'!$D$1");
        preservedChart.Series[1].FormulaReferences.Category.Should().Be("'Forecast Model 2026'!$A$2:$A$4");
        preservedChart.Series[1].FormulaReferences.Values.Should().Be("'Forecast Model 2026'!Local_Projection");

        var editedChart = chartShapes[1].Chart!;
        editedChart.RegenerateWorkbookOnSave.Should().BeFalse();
        editedChart.Series.Should().HaveCount(2);
        editedChart.Series[0].FormulaReferences.SeriesName.Should().Be("'Input Assumptions'!$C$1");
        editedChart.Series[0].FormulaReferences.Category.Should().Be("'Input Assumptions'!$A$2:$A$4");
        editedChart.Series[0].FormulaReferences.Values.Should().Be("'Input Assumptions'!Scenario_Source");
        editedChart.Series[1].FormulaReferences.SeriesName.Should().Be("'Input Assumptions'!$D$1");
        editedChart.Series[1].FormulaReferences.Category.Should().Be("'Input Assumptions'!$A$2:$A$4");
        editedChart.Series[1].FormulaReferences.Values.Should().Be("'Input Assumptions'!Scenario_Local");

        new ReplaceChartDataCommand(
            slideIndex: 0,
            shapeId: chartShapes[1].Id,
            categories: ["Edited Jan", "Edited Feb", "Edited Mar"],
            seriesNames: ["Edited Actual", "Edited Scenario"],
            values:
            [
                new double?[] { 111, 222, 333 },
                new double?[] { 444, 555, 666 },
            ]).Apply(loaded);

        preservedChart.RegenerateWorkbookOnSave.Should().BeFalse();
        editedChart.RegenerateWorkbookOnSave.Should().BeTrue();

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            ReadText(archive, "customXml/richFormulaPayload.xml")
                .Should()
                .Contain("rich-formula-retain-me");

            ReadBytes(archive, "ppt/embeddings/authoredWorkbookFirst.xlsx")
                .Should()
                .Equal(sourceFirstWorkbook, "the unedited chart should keep its PowerPoint-authored formula workbook byte-for-byte");
            using (var preservedWorkbook = new ZipArchive(
                new MemoryStream(ReadBytes(archive, "ppt/embeddings/authoredWorkbookFirst.xlsx")),
                ZipArchiveMode.Read))
            {
                var workbookXml = LoadXml(preservedWorkbook, "xl/workbook.xml")
                    .ToString(SaveOptions.DisableFormatting);
                workbookXml.Should().Contain("Revenue_Actual");
                workbookXml.Should().Contain("Local_Projection");
                workbookXml.Should().Contain("localSheetId=\"0\"");
                workbookXml.Should().Contain("'Forecast Model 2026'!$D$2:$D$4");

                var sheetXml = LoadXml(preservedWorkbook, "xl/worksheets/sheet1.xml")
                    .ToString(SaveOptions.DisableFormatting);
                sheetXml.Should().Contain("SUM(B2,Forecast_Assumption)");
                sheetXml.Should().Contain("C2+SUM($B$2:$B$4)/10");
            }

            archive.GetEntry("ppt/embeddings/authoredWorkbookSecond.xlsx").Should().BeNull(
                "the semantically edited chart should drop its stale authored formula workbook");
            archive.GetEntry("ppt/embeddings/chartWorkbook2.xlsx").Should().NotBeNull(
                "the edited chart should receive a regenerated workbook at its writer-owned chart index");

            var chart1Xml = LoadXml(archive, "ppt/charts/chart1.xml");
            var chart1Text = chart1Xml.ToString(SaveOptions.DisableFormatting);
            chart1Text.Should().Contain("'Forecast Model 2026'!$C$1");
            chart1Text.Should().Contain("'Forecast Model 2026'!$D$1");
            chart1Text.Should().Contain("'Forecast Model 2026'!$A$2:$A$4");
            chart1Text.Should().Contain("'Forecast Model 2026'!Revenue_Actual");
            chart1Text.Should().Contain("'Forecast Model 2026'!Local_Projection");
            chart1Xml.Root!.Element(ChartNs + "externalData")!.Attribute(RelsDocNs + "id")!.Value
                .Should().Be("rIdAuthoredWorkbookFirst");
            var chart1Rels = LoadXml(archive, "ppt/charts/_rels/chart1.xml.rels");
            Relationship(chart1Rels, PackageRelType, "../embeddings/authoredWorkbookFirst.xlsx").Should().NotBeNull();

            var chart2Xml = LoadXml(archive, "ppt/charts/chart2.xml");
            var chart2Text = chart2Xml.ToString(SaveOptions.DisableFormatting);
            chart2Text.Should().Contain("Edited Jan");
            chart2Text.Should().Contain("Edited Actual");
            chart2Text.Should().Contain("Edited Scenario");
            chart2Text.Should().Contain("ChartData!$A$2:$A$4");
            chart2Text.Should().Contain("ChartData!$B$1");
            chart2Text.Should().Contain("ChartData!$B$2:$B$4");
            chart2Text.Should().Contain("ChartData!$C$1");
            chart2Text.Should().Contain("ChartData!$C$2:$C$4");
            chart2Text.Should().NotContain("'Input Assumptions'!Scenario_Source",
                "edited charts should replace authored formulas with ranges into the regenerated workbook");
            chart2Text.Should().NotContain("'Input Assumptions'!Scenario_Local",
                "edited charts should replace authored formulas with ranges into the regenerated workbook");
            chart2Xml.Root!.Element(ChartNs + "externalData")!.Attribute(RelsDocNs + "id")!.Value
                .Should().Be("rIdWorkbook1");
            var chart2Rels = LoadXml(archive, "ppt/charts/_rels/chart2.xml.rels");
            Relationship(chart2Rels, PackageRelType, "../embeddings/chartWorkbook2.xlsx").Should().NotBeNull();

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            Override(contentTypes, "/ppt/embeddings/authoredWorkbookFirst.xlsx", SpreadsheetWorkbookContentType)
                .Should().NotBeNull();
            Override(contentTypes, "/ppt/embeddings/authoredWorkbookSecond.xlsx", SpreadsheetWorkbookContentType)
                .Should().BeNull("the stale workbook override for the edited chart should not survive");
            Override(contentTypes, "/ppt/embeddings/chartWorkbook2.xlsx", SpreadsheetWorkbookContentType)
                .Should().NotBeNull();
            Override(
                contentTypes,
                "/customXml/richFormulaPayload.xml",
                "application/vnd.example.freep.rich-formula-payload+xml").Should().NotBeNull();

            using var regeneratedWorkbook = new ZipArchive(
                new MemoryStream(ReadBytes(archive, "ppt/embeddings/chartWorkbook2.xlsx")),
                ZipArchiveMode.Read);
            var regeneratedSheet = LoadXml(regeneratedWorkbook, "xl/worksheets/sheet1.xml")
                .ToString(SaveOptions.DisableFormatting);
            regeneratedSheet.Should().Contain("Edited Actual");
            regeneratedSheet.Should().Contain("Edited Scenario");
            regeneratedSheet.Should().Contain("Edited Jan");
            regeneratedSheet.Should().Contain("111");
            regeneratedSheet.Should().Contain("222");
            regeneratedSheet.Should().Contain("333");
            regeneratedSheet.Should().Contain("444");
            regeneratedSheet.Should().Contain("555");
            regeneratedSheet.Should().Contain("666");
        }

        sourceSecondWorkbook.Should().NotBeEmpty("the source fixture should carry a real authored workbook for the edited chart");
        using (var staleEditedWorkbook = new ZipArchive(new MemoryStream(sourceSecondWorkbook), ZipArchiveMode.Read))
        {
            var workbookXml = LoadXml(staleEditedWorkbook, "xl/workbook.xml")
                .ToString(SaveOptions.DisableFormatting);
            workbookXml.Should().Contain("Scenario_Source");
            workbookXml.Should().Contain("Scenario_Local");
            workbookXml.Should().Contain("'Input Assumptions'!$D$2:$D$4");
        }

        var reloaded = PptxPackageReader.Read(new MemoryStream(savedBytes));
        var reloadedCharts = reloaded.Slides[0].Shapes
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .Select(shape => shape.Chart!)
            .ToArray();
        reloadedCharts[0].Series.Should().HaveCount(2);
        reloadedCharts[0].Series[0].FormulaReferences.SeriesName.Should().Be("'Forecast Model 2026'!$C$1");
        reloadedCharts[0].Series[0].FormulaReferences.Category.Should().Be("'Forecast Model 2026'!$A$2:$A$4");
        reloadedCharts[0].Series[0].FormulaReferences.Values.Should().Be("'Forecast Model 2026'!Revenue_Actual");
        reloadedCharts[0].Series[1].FormulaReferences.SeriesName.Should().Be("'Forecast Model 2026'!$D$1");
        reloadedCharts[0].Series[1].FormulaReferences.Category.Should().Be("'Forecast Model 2026'!$A$2:$A$4");
        reloadedCharts[0].Series[1].FormulaReferences.Values.Should().Be("'Forecast Model 2026'!Local_Projection");
        reloadedCharts[1].Series.Should().HaveCount(2);
        reloadedCharts[1].Series[0].FormulaReferences.SeriesName.Should().Be("ChartData!$B$1");
        reloadedCharts[1].Series[0].FormulaReferences.Category.Should().Be("ChartData!$A$2:$A$4");
        reloadedCharts[1].Series[0].FormulaReferences.Values.Should().Be("ChartData!$B$2:$B$4");
        reloadedCharts[1].Series[1].FormulaReferences.SeriesName.Should().Be("ChartData!$C$1");
        reloadedCharts[1].Series[1].FormulaReferences.Category.Should().Be("ChartData!$A$2:$A$4");
        reloadedCharts[1].Series[1].FormulaReferences.Values.Should().Be("ChartData!$C$2:$C$4");
        reloadedCharts[1].Categories.Should().Equal("Edited Jan", "Edited Feb", "Edited Mar");
        reloadedCharts[1].Series[0].Name.Should().Be("Edited Actual");
        reloadedCharts[1].Series[0].Values.Should().Equal(111, 222, 333);
        reloadedCharts[1].Series[1].Name.Should().Be("Edited Scenario");
        reloadedCharts[1].Series[1].Values.Should().Equal(444, 555, 666);
    }

    [Fact]
    public void ReadWriteRead_ScatterBubbleAuthoredFormulas_PreservesUneditedAndRegeneratesOnlyEditedBubbleRanges()
    {
        using var source = BuildPptxWithScatterBubbleFormulaWorkbooks();
        var sourceBytes = source.ToArray();
        byte[] sourceScatterWorkbook;
        byte[] sourceBubbleWorkbook;
        using (var sourceArchive = new ZipArchive(new MemoryStream(sourceBytes), ZipArchiveMode.Read))
        {
            sourceScatterWorkbook = ReadBytes(sourceArchive, "ppt/embeddings/scatterAuthoredWorkbook.xlsx");
            sourceBubbleWorkbook = ReadBytes(sourceArchive, "ppt/embeddings/bubbleAuthoredWorkbook.xlsx");
        }

        var loaded = PptxPackageReader.Read(new MemoryStream(sourceBytes));
        loaded.PackageSnapshot.Should().NotBeNull();
        var chartShapes = loaded.Slides[0].Shapes.Where(shape => shape.Kind == SlideShapeKind.Chart).ToArray();
        chartShapes.Should().HaveCount(2);

        var scatterChart = chartShapes[0].Chart!;
        scatterChart.ChartType.Should().Be(ChartType.Scatter);
        scatterChart.RegenerateWorkbookOnSave.Should().BeFalse();
        scatterChart.Series.Should().HaveCount(1);
        scatterChart.Series.Should().OnlyContain(series => !series.OnSecondaryAxis,
            "scatter uses independent X and Y value axes rather than a secondary series axis");
        scatterChart.Series[0].FormulaReferences.SeriesName.Should().Be("'Scatter Source'!$B$1");
        scatterChart.Series[0].FormulaReferences.XValues.Should().Be("'Scatter Source'!$A$2:$A$4");
        scatterChart.Series[0].FormulaReferences.YValues.Should().Be("'Scatter Source'!Scatter_Y");

        var bubbleChart = chartShapes[1].Chart!;
        bubbleChart.ChartType.Should().Be(ChartType.Bubble);
        bubbleChart.RegenerateWorkbookOnSave.Should().BeFalse();
        bubbleChart.Series.Should().HaveCount(1);
        bubbleChart.Series.Should().OnlyContain(series => !series.OnSecondaryAxis,
            "bubble uses independent X and Y value axes rather than a secondary series axis");
        bubbleChart.Series[0].FormulaReferences.SeriesName.Should().Be("'Bubble Source'!$B$1");
        bubbleChart.Series[0].FormulaReferences.XValues.Should().Be("'Bubble Source'!$A$2:$A$4");
        bubbleChart.Series[0].FormulaReferences.YValues.Should().Be("'Bubble Source'!Bubble_Y");
        bubbleChart.Series[0].FormulaReferences.BubbleSizes.Should().Be("'Bubble Source'!$D$2:$D$4");

        new SetChartCellValueCommand(
            slideIndex: 0,
            shapeId: chartShapes[1].Id,
            seriesIndex: 0,
            categoryIndex: 1,
            value: 2222).Apply(loaded);

        scatterChart.RegenerateWorkbookOnSave.Should().BeFalse();
        bubbleChart.RegenerateWorkbookOnSave.Should().BeTrue();

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            ReadBytes(archive, "ppt/embeddings/scatterAuthoredWorkbook.xlsx")
                .Should()
                .Equal(sourceScatterWorkbook, "the unedited scatter chart should keep its authored workbook byte-for-byte");
            archive.GetEntry("ppt/embeddings/bubbleAuthoredWorkbook.xlsx").Should().BeNull(
                "the edited bubble chart should drop its stale authored workbook sidecar");
            sourceBubbleWorkbook.Should().NotBeEmpty("the source bubble chart should carry an authored workbook");
            archive.GetEntry("ppt/embeddings/chartWorkbook2.xlsx").Should().NotBeNull(
                "the edited bubble chart should receive a regenerated workbook at its chart index");

            var scatterXml = LoadXml(archive, "ppt/charts/chart1.xml");
            var scatterText = scatterXml.ToString(SaveOptions.DisableFormatting);
            scatterText.Should().Contain("'Scatter Source'!$B$1");
            scatterText.Should().Contain("'Scatter Source'!$A$2:$A$4");
            scatterText.Should().Contain("'Scatter Source'!Scatter_Y");
            scatterXml.Root!.Element(ChartNs + "externalData")!.Attribute(RelsDocNs + "id")!.Value
                .Should().Be("rIdScatterWorkbook");
            var scatterRels = LoadXml(archive, "ppt/charts/_rels/chart1.xml.rels");
            Relationship(scatterRels, PackageRelType, "../embeddings/scatterAuthoredWorkbook.xlsx").Should().NotBeNull();

            var bubbleXml = LoadXml(archive, "ppt/charts/chart2.xml");
            var bubbleText = bubbleXml.ToString(SaveOptions.DisableFormatting);
            bubbleText.Should().Contain("ChartData!$B$1");
            bubbleText.Should().Contain("ChartData!$A$2:$A$4");
            bubbleText.Should().Contain("ChartData!$B$2:$B$4");
            bubbleText.Should().Contain("ChartData!$C$2:$C$4");
            bubbleText.Should().NotContain("'Bubble Source'!Bubble_Y",
                "edited bubble formulas should point at regenerated ChartData ranges");
            bubbleText.Should().NotContain("'Bubble Source'!$D$2:$D$4",
                "edited bubble-size formulas should point at regenerated ChartData ranges");
            bubbleXml.Root!.Element(ChartNs + "externalData")!.Attribute(RelsDocNs + "id")!.Value
                .Should().Be("rIdWorkbook1");
            var bubbleRels = LoadXml(archive, "ppt/charts/_rels/chart2.xml.rels");
            Relationship(bubbleRels, PackageRelType, "../embeddings/chartWorkbook2.xlsx").Should().NotBeNull();

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            Override(contentTypes, "/ppt/embeddings/scatterAuthoredWorkbook.xlsx", SpreadsheetWorkbookContentType)
                .Should().NotBeNull();
            Override(contentTypes, "/ppt/embeddings/bubbleAuthoredWorkbook.xlsx", SpreadsheetWorkbookContentType)
                .Should().BeNull("the stale bubble workbook override should not survive the edit");
            Override(contentTypes, "/ppt/embeddings/chartWorkbook2.xlsx", SpreadsheetWorkbookContentType)
                .Should().NotBeNull();

            using var regeneratedWorkbook = new ZipArchive(
                new MemoryStream(ReadBytes(archive, "ppt/embeddings/chartWorkbook2.xlsx")),
                ZipArchiveMode.Read);
            var regeneratedSheet = LoadXml(regeneratedWorkbook, "xl/worksheets/sheet1.xml")
                .ToString(SaveOptions.DisableFormatting);
            regeneratedSheet.Should().Contain("Bubble Growth");
            regeneratedSheet.Should().Contain("1.5");
            regeneratedSheet.Should().Contain("2222");
            regeneratedSheet.Should().Contain("9");
        }

        var reloaded = PptxPackageReader.Read(new MemoryStream(savedBytes));
        var reloadedCharts = reloaded.Slides[0].Shapes
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .Select(shape => shape.Chart!)
            .ToArray();
        reloadedCharts[0].Series[0].FormulaReferences.SeriesName.Should().Be("'Scatter Source'!$B$1");
        reloadedCharts[0].Series[0].FormulaReferences.XValues.Should().Be("'Scatter Source'!$A$2:$A$4");
        reloadedCharts[0].Series[0].FormulaReferences.YValues.Should().Be("'Scatter Source'!Scatter_Y");
        reloadedCharts[1].Series[0].FormulaReferences.SeriesName.Should().Be("ChartData!$B$1");
        reloadedCharts[1].Series[0].FormulaReferences.XValues.Should().Be("ChartData!$A$2:$A$4");
        reloadedCharts[1].Series[0].FormulaReferences.YValues.Should().Be("ChartData!$B$2:$B$4");
        reloadedCharts[1].Series[0].FormulaReferences.BubbleSizes.Should().Be("ChartData!$C$2:$C$4");
        reloadedCharts[1].Series[0].Values.Should().Equal(12, 2222, 36);
        reloadedCharts[1].Series[0].BubbleSizes.Should().Equal(9, 10, 11);
    }

    [Fact]
    public void ReadWriteRead_ChartDataTableSettings_RetainsModeledChartPackageSemantics()
    {
        using var source = BuildPptxWithChartWorkbookAndUnrelatedPackageData();
        var loaded = PptxPackageReader.Read(source);

        var chart = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        chart.DataTable.Should().NotBeNull("PowerPoint-authored c:dTable settings should import into the shared model");
        chart.DataTable!.ShowHorizontalBorder.Should().BeTrue();
        chart.DataTable.ShowVerticalBorder.Should().BeFalse();
        chart.DataTable.ShowOutlineBorder.Should().BeTrue();
        chart.DataTable.ShowLegendKeys.Should().BeTrue();
        chart.DataTable.BackgroundFill.Should().BeOfType<ShapeFill.Solid>()
            .Which.Color.Resolved.Should().Be(new SrgbColor(0xFA, 0xF1, 0xD2));
        chart.DataTable.BorderOutline.Should().BeOfType<ShapeOutline.Visible>()
            .Which.Color.Resolved.Should().Be(new SrgbColor(0x12, 0x34, 0x56));
        ((ShapeOutline.Visible)chart.DataTable.BorderOutline!).WidthPt.Should().BeApproximately(1.25, 0.001);
        ((ShapeOutline.Visible)chart.DataTable.BorderOutline!).Dash.Should().Be(OutlineDash.DashDot);
        chart.DataTable.TextStyle.Should().NotBeNull();
        chart.DataTable.TextStyle!.FontSizePt.Should().Be(8.75);
        chart.DataTable.TextStyle.Bold.Should().BeTrue();
        chart.DataTable.TextStyle.Italic.Should().BeTrue();
        chart.DataTable.TextStyle.Color.Should().NotBeNull();
        chart.DataTable.TextStyle.Color!.Resolved.Should().Be(new SrgbColor(0x22, 0x44, 0x66));

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            ReadText(archive, "customXml/chartWorkbookPayload.xml")
                .Should()
                .Contain("unrelated-retain-me");

            var savedChartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            var savedPlotArea = savedChartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!;
            var savedDataTable = savedPlotArea.Element(ChartNs + "dTable");
            savedDataTable.Should().NotBeNull("saving should write c:dTable back into the chart package part");
            savedDataTable!.Element(ChartNs + "showHorzBorder")!.Attribute("val")!.Value.Should().Be("1");
            savedDataTable.Element(ChartNs + "showVertBorder")!.Attribute("val")!.Value.Should().Be("0");
            savedDataTable.Element(ChartNs + "showOutline")!.Attribute("val")!.Value.Should().Be("1");
            savedDataTable.Element(ChartNs + "showKeys")!.Attribute("val")!.Value.Should().Be("1");
            var savedSpPr = savedDataTable.Element(ChartNs + "spPr")!;
            savedSpPr.Element(DrawingNs + "solidFill")!
                .Element(DrawingNs + "srgbClr")!
                .Attribute("val")!
                .Value.Should().Be("FAF1D2");
            var savedLine = savedSpPr.Element(DrawingNs + "ln")!;
            savedLine.Attribute("w")!.Value.Should().Be(DrawingMlCoordinateUnits.PointsToEmu(1.25).ToString());
            savedLine.Element(DrawingNs + "prstDash")!.Attribute("val")!.Value.Should().Be("dashDot");
            savedLine.Element(DrawingNs + "solidFill")!
                .Element(DrawingNs + "srgbClr")!
                .Attribute("val")!
                .Value.Should().Be("123456");
            var savedDefRPr = savedDataTable.Element(ChartNs + "txPr")!
                .Element(DrawingNs + "p")!
                .Element(DrawingNs + "pPr")!
                .Element(DrawingNs + "defRPr")!;
            savedDefRPr.Attribute("sz")!.Value.Should().Be("875");
            savedDefRPr.Attribute("b")!.Value.Should().Be("1");
            savedDefRPr.Attribute("i")!.Value.Should().Be("1");
            savedDefRPr.Element(DrawingNs + "solidFill")!
                .Element(DrawingNs + "srgbClr")!
                .Attribute("val")!
                .Value.Should().Be("224466");
            savedPlotArea.Elements().Last(element => element.Name == ChartNs + "valAx" || element.Name == ChartNs + "dTable")
                .Name.Should().Be(ChartNs + "dTable", "c:dTable should remain after chart axes in the package chart part");
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        var reloadedDataTable = reloaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!.DataTable;
        reloadedDataTable.Should().NotBeNull();
        reloadedDataTable!.ShowHorizontalBorder.Should().BeTrue();
        reloadedDataTable.ShowVerticalBorder.Should().BeFalse();
        reloadedDataTable.ShowOutlineBorder.Should().BeTrue();
        reloadedDataTable.ShowLegendKeys.Should().BeTrue();
        reloadedDataTable.BackgroundFill.Should().BeOfType<ShapeFill.Solid>()
            .Which.Color.Resolved.Should().Be(new SrgbColor(0xFA, 0xF1, 0xD2));
        reloadedDataTable.BorderOutline.Should().BeOfType<ShapeOutline.Visible>()
            .Which.Color.Resolved.Should().Be(new SrgbColor(0x12, 0x34, 0x56));
        ((ShapeOutline.Visible)reloadedDataTable.BorderOutline!).WidthPt.Should().BeApproximately(1.25, 0.001);
        ((ShapeOutline.Visible)reloadedDataTable.BorderOutline!).Dash.Should().Be(OutlineDash.DashDot);
        reloadedDataTable.TextStyle.Should().NotBeNull();
        reloadedDataTable.TextStyle!.FontSizePt.Should().Be(8.75);
        reloadedDataTable.TextStyle.Bold.Should().BeTrue();
        reloadedDataTable.TextStyle.Italic.Should().BeTrue();
        reloadedDataTable.TextStyle.Color.Should().NotBeNull();
        reloadedDataTable.TextStyle.Color!.Resolved.Should().Be(new SrgbColor(0x22, 0x44, 0x66));
    }

    [Fact]
    public void ReadWriteRead_ChartManualLayoutAndLegendOverlay_RetainsModeledChartPackageSemantics()
    {
        using var source = BuildPptxWithChartManualLayoutAndLegendOverlay();
        var loaded = PptxPackageReader.Read(source);

        var chart = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        chart.PlotAreaManualLayout.Should().NotBeNull();
        chart.PlotAreaManualLayout!.LayoutTarget.Should().Be("inner");
        chart.PlotAreaManualLayout.XMode.Should().Be(ChartManualLayoutMode.Factor);
        chart.PlotAreaManualLayout.YMode.Should().Be(ChartManualLayoutMode.Factor);
        chart.PlotAreaManualLayout.WidthMode.Should().Be(ChartManualLayoutMode.Factor);
        chart.PlotAreaManualLayout.HeightMode.Should().Be(ChartManualLayoutMode.Factor);
        chart.PlotAreaManualLayout.X.Should().BeApproximately(0.12, 0.0001);
        chart.PlotAreaManualLayout.Y.Should().BeApproximately(0.18, 0.0001);
        chart.PlotAreaManualLayout.Width.Should().BeApproximately(0.68, 0.0001);
        chart.PlotAreaManualLayout.Height.Should().BeApproximately(0.62, 0.0001);
        chart.Legend.Should().Be(LegendPosition.Right);
        chart.LegendOverlay.Should().BeTrue();
        chart.LegendManualLayout.Should().NotBeNull();
        chart.LegendManualLayout!.X.Should().BeApproximately(0.72, 0.0001);
        chart.LegendManualLayout.Y.Should().BeApproximately(0.20, 0.0001);
        chart.LegendManualLayout.Width.Should().BeApproximately(0.20, 0.0001);
        chart.LegendManualLayout.Height.Should().BeApproximately(0.25, 0.0001);

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            ReadText(archive, "customXml/chartWorkbookPayload.xml")
                .Should()
                .Contain("unrelated-retain-me");

            var savedChartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            var savedChart = savedChartXml.Root!.Element(ChartNs + "chart")!;
            var savedPlotManualLayout = savedChart
                .Element(ChartNs + "plotArea")!
                .Element(ChartNs + "layout")!
                .Element(ChartNs + "manualLayout")!;
            savedPlotManualLayout.Element(ChartNs + "layoutTarget")!.Attribute("val")!.Value.Should().Be("inner");
            savedPlotManualLayout.Element(ChartNs + "xMode")!.Attribute("val")!.Value.Should().Be("factor");
            savedPlotManualLayout.Element(ChartNs + "yMode")!.Attribute("val")!.Value.Should().Be("factor");
            savedPlotManualLayout.Element(ChartNs + "wMode")!.Attribute("val")!.Value.Should().Be("factor");
            savedPlotManualLayout.Element(ChartNs + "hMode")!.Attribute("val")!.Value.Should().Be("factor");
            savedPlotManualLayout.Element(ChartNs + "x")!.Attribute("val")!.Value.Should().Be("0.12");
            savedPlotManualLayout.Element(ChartNs + "y")!.Attribute("val")!.Value.Should().Be("0.18");
            savedPlotManualLayout.Element(ChartNs + "w")!.Attribute("val")!.Value.Should().Be("0.68");
            savedPlotManualLayout.Element(ChartNs + "h")!.Attribute("val")!.Value.Should().Be("0.62");

            var savedLegend = savedChart.Element(ChartNs + "legend")!;
            savedLegend.Element(ChartNs + "overlay")!.Attribute("val")!.Value.Should().Be("1");
            var savedLegendManualLayout = savedLegend
                .Element(ChartNs + "layout")!
                .Element(ChartNs + "manualLayout")!;
            savedLegendManualLayout.Element(ChartNs + "x")!.Attribute("val")!.Value.Should().Be("0.72");
            savedLegendManualLayout.Element(ChartNs + "y")!.Attribute("val")!.Value.Should().Be("0.2");
            savedLegendManualLayout.Element(ChartNs + "w")!.Attribute("val")!.Value.Should().Be("0.2");
            savedLegendManualLayout.Element(ChartNs + "h")!.Attribute("val")!.Value.Should().Be("0.25");
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        var reloadedChart = reloaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        reloadedChart.PlotAreaManualLayout.Should().NotBeNull();
        reloadedChart.PlotAreaManualLayout!.X.Should().BeApproximately(0.12, 0.0001);
        reloadedChart.PlotAreaManualLayout.Y.Should().BeApproximately(0.18, 0.0001);
        reloadedChart.PlotAreaManualLayout.Width.Should().BeApproximately(0.68, 0.0001);
        reloadedChart.PlotAreaManualLayout.Height.Should().BeApproximately(0.62, 0.0001);
        reloadedChart.LegendOverlay.Should().BeTrue();
        reloadedChart.LegendManualLayout.Should().NotBeNull();
        reloadedChart.LegendManualLayout!.X.Should().BeApproximately(0.72, 0.0001);
        reloadedChart.LegendManualLayout.Y.Should().BeApproximately(0.20, 0.0001);
        reloadedChart.LegendManualLayout.Width.Should().BeApproximately(0.20, 0.0001);
        reloadedChart.LegendManualLayout.Height.Should().BeApproximately(0.25, 0.0001);
    }

    [Fact]
    public void ReadWriteRead_UnknownChartManualLayoutMode_PreservesTokenAndCoordinates()
    {
        using var source = BuildPptxWithChartManualLayoutAndLegendOverlay();
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            var manualLayout = chartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!
                .Element(ChartNs + "layout")!
                .Element(ChartNs + "manualLayout")!;
            manualLayout.Element(ChartNs + "xMode")!.SetAttributeValue("val", "futureMode");
            WriteXml(archive, "ppt/charts/chart1.xml", chartXml);
        }

        source.Position = 0;
        var loaded = PptxPackageReader.Read(source);
        var layout = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart)
            .Chart!.PlotAreaManualLayout!;

        layout.XMode.Should().Be(ChartManualLayoutMode.Unsupported);
        layout.RawXModeToken.Should().Be("futureMode");
        layout.YMode.Should().Be(ChartManualLayoutMode.Factor);
        layout.X.Should().BeApproximately(0.12, 0.0001);
        layout.Y.Should().BeApproximately(0.18, 0.0001);
        layout.Width.Should().BeApproximately(0.68, 0.0001);
        layout.Height.Should().BeApproximately(0.62, 0.0001);

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        using (var savedArchive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read))
        {
            var savedManualLayout = LoadXml(savedArchive, "ppt/charts/chart1.xml")
                .Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!
                .Element(ChartNs + "layout")!
                .Element(ChartNs + "manualLayout")!;
            savedManualLayout.Element(ChartNs + "xMode")!.Attribute("val")!.Value.Should().Be("futureMode");
            savedManualLayout.Element(ChartNs + "yMode")!.Attribute("val")!.Value.Should().Be("factor");
            savedManualLayout.Element(ChartNs + "x")!.Attribute("val")!.Value.Should().Be("0.12");
            savedManualLayout.Element(ChartNs + "h")!.Attribute("val")!.Value.Should().Be("0.62");
        }

        saved.Position = 0;
        var reloaded = PptxPackageReader.Read(saved);
        var reloadedLayout = reloaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart)
            .Chart!.PlotAreaManualLayout!;
        reloadedLayout.XMode.Should().Be(ChartManualLayoutMode.Unsupported);
        reloadedLayout.RawXModeToken.Should().Be("futureMode");
        reloadedLayout.X.Should().BeApproximately(0.12, 0.0001);
    }

    [Fact]
    public void ReadWriteRead_LineChartSmoothSeriesDecision_RoundTrips()
    {
        using var source = BuildPptxWithLineChartSmoothDecision(smooth: true);
        var loaded = PptxPackageReader.Read(source);

        var chart = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        chart.ChartType.Should().Be(ChartType.Line);
        chart.Series.Should().ContainSingle();
        chart.Series[0].SmoothLine.Should().BeTrue(
            "PowerPoint-authored c:smooth should be available to the shared WPF/Avalonia render planner");

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            var savedChartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            var savedSmooth = savedChartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!
                .Element(ChartNs + "lineChart")!
                .Element(ChartNs + "ser")!
                .Element(ChartNs + "smooth");
            savedSmooth.Should().NotBeNull("the writer should preserve the modeled smooth-line decision");
            savedSmooth!.Attribute("val")!.Value.Should().Be("1");
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        var reloadedChart = reloaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        reloadedChart.Series[0].SmoothLine.Should().BeTrue();
    }

    [Fact]
    public void ReadWriteRead_ChartDataTableTextStyleFontFamily_RoundTripsAndIsNotDroppedToCalibri()
    {
        // KA1: c:dTable/c:txPr/a:defRPr/a:latin typeface="Georgia" must be captured into
        // ChartTextStyle.FontFamily and re-emitted on save, instead of being silently
        // dropped (which previously caused the data table to always render/save in the
        // renderer's hardcoded "Calibri" default).
        using var source = BuildPptxWithChartDataTableFontFamily("Georgia");
        var loaded = PptxPackageReader.Read(source);

        var chart = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        chart.DataTable.Should().NotBeNull();
        chart.DataTable!.TextStyle.Should().NotBeNull();
        chart.DataTable.TextStyle!.FontFamily.Should().Be("Georgia");

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            var savedChartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            var savedDefRPr = savedChartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!
                .Element(ChartNs + "dTable")!
                .Element(ChartNs + "txPr")!
                .Element(DrawingNs + "p")!
                .Element(DrawingNs + "pPr")!
                .Element(DrawingNs + "defRPr")!;
            savedDefRPr.Element(DrawingNs + "latin").Should().NotBeNull(
                "the data-table font family must round-trip as a:latin, not be dropped on save");
            savedDefRPr.Element(DrawingNs + "latin")!.Attribute("typeface")!.Value.Should().Be("Georgia");
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        var reloadedDataTable = reloaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!.DataTable;
        reloadedDataTable.Should().NotBeNull();
        reloadedDataTable!.TextStyle.Should().NotBeNull();
        reloadedDataTable.TextStyle!.FontFamily.Should().Be("Georgia");
    }

    [Fact]
    public void ReadWriteRead_ChartDataTableTextStyleWithoutLatin_FontFamilyIsNullNotDefaulted()
    {
        // No a:latin present on the source defRPr → FontFamily should stay null (unset),
        // not be defaulted to anything — the renderer default only applies at draw time.
        using var source = BuildPptxWithChartDataTableFontFamily(fontFamily: null);
        var loaded = PptxPackageReader.Read(source);

        var chart = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        chart.DataTable.Should().NotBeNull();
        chart.DataTable!.TextStyle.Should().NotBeNull();
        chart.DataTable.TextStyle!.FontFamily.Should().BeNull();

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);

        using var savedRead = new MemoryStream(saved.ToArray());
        var reloaded = PptxPackageReader.Read(savedRead);
        var reloadedDataTable = reloaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!.DataTable;
        reloadedDataTable.Should().NotBeNull();
        reloadedDataTable!.TextStyle.Should().NotBeNull();
        reloadedDataTable.TextStyle!.FontFamily.Should().BeNull();
    }

    [Fact]
    public void ReadWrite_ChartWithoutTextProperties_DoesNotSerializeSyntheticOfficeDefault()
    {
        using var source = BuildPptxWithLineChartSmoothDecision(smooth: false);
        var loaded = PptxPackageReader.Read(source);
        var chart = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;

        chart.TextStyle.Should().NotBeNull();
        chart.TextStyle!.IsImplicitDefault.Should().BeTrue();
        chart.TextStyle.FontSizePt.Should().Be(18.0);

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);

        using var archive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read);
        var chartXml = LoadXml(archive, "ppt/charts/chart1.xml");
        chartXml.Root!.Element(ChartNs + "txPr").Should().BeNull(
            "an inherited Office chart-title default is not an authored c:chartSpace/c:txPr node");
    }

    private static MemoryStream BuildPptxWithLineChartSmoothDecision(bool smooth)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape { ChartType = ChartType.Line };
        chart.Categories.AddRange(["Q1", "Q2", "Q3"]);
        var series = new ChartSeries
        {
            Name = "Smoothed trend",
            MarkerStyle = new ChartMarkerStyle { Symbol = ChartMarkerSymbol.None }
        };
        series.Values.AddRange([10, 24, 18]);
        chart.Series.Add(series);
        slide.Shapes.Add(new SlideShape
        {
            Id = 101,
            Name = "Smooth line chart",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 3657600,
            ExtentCyEmu = 2743200,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            var seriesEl = chartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!
                .Element(ChartNs + "lineChart")!
                .Element(ChartNs + "ser")!;
            seriesEl.Element(ChartNs + "smooth")?.Remove();
            seriesEl.Add(new XElement(ChartNs + "smooth", new XAttribute("val", smooth ? "1" : "0")));
            WriteXml(archive, "ppt/charts/chart1.xml", chartXml);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream BuildPptxWithChartDataTableFontFamily(string? fontFamily)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(["East", "West"]);
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange([10, 20]);
        chart.Series.Add(series);
        slide.Shapes.Add(new SlideShape
        {
            Id = 101,
            Name = "Font family chart",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 3657600,
            ExtentCyEmu = 2743200,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            var defRPr = new XElement(DrawingNs + "defRPr",
                new XAttribute("sz", "875"));
            if (fontFamily is not null)
                defRPr.Add(new XElement(DrawingNs + "latin", new XAttribute("typeface", fontFamily)));

            chartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!
                .Add(new XElement(ChartNs + "dTable",
                    new XElement(ChartNs + "showHorzBorder", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showVertBorder", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showOutline", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showKeys", new XAttribute("val", "0")),
                    new XElement(ChartNs + "txPr",
                        new XElement(DrawingNs + "bodyPr"),
                        new XElement(DrawingNs + "lstStyle"),
                        new XElement(DrawingNs + "p",
                            new XElement(DrawingNs + "pPr", defRPr),
                            new XElement(DrawingNs + "endParaRPr")))));
            WriteXml(archive, "ppt/charts/chart1.xml", chartXml);
        }

        package.Position = 0;
        return package;
    }

    [Fact]
    public void ReadWriteRead_ChartDataTableGradientBorderOutline_IsPreservedNotReplacedByDefaultGray()
    {
        // JA1: a c:dTable/c:spPr/a:ln with an a:gradFill child (gradient border) must survive
        // read -> write -> read as ShapeOutline.GradientVisible, not be discarded and replaced
        // by the renderer/writer default gray solid outline.
        using var source = BuildPptxWithGradientChartDataTableBorder();
        var loaded = PptxPackageReader.Read(source);

        var chart = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        chart.DataTable.Should().NotBeNull();
        var gradientOutline = chart.DataTable!.BorderOutline.Should().BeOfType<ShapeOutline.GradientVisible>().Subject;
        gradientOutline.WidthPt.Should().BeApproximately(1.0, 0.001);
        gradientOutline.Gradient.Stops.Select(s => s.Color.Resolved).Should()
            .Equal(new SrgbColor(0xFF, 0x00, 0x00), new SrgbColor(0x00, 0x00, 0xFF));

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            var savedChartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            var savedDataTable = savedChartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!
                .Element(ChartNs + "dTable");
            savedDataTable.Should().NotBeNull();
            var savedLine = savedDataTable!.Element(ChartNs + "spPr")!.Element(DrawingNs + "ln")!;
            savedLine.Element(DrawingNs + "gradFill").Should().NotBeNull(
                "the gradient border must round-trip as a:gradFill, not be collapsed to a:solidFill");
            savedLine.Element(DrawingNs + "solidFill").Should().BeNull(
                "a gradient border must not be silently replaced by a solid default gray fill");
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        var reloadedDataTable = reloaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!.DataTable;
        reloadedDataTable.Should().NotBeNull();
        var reloadedGradient = reloadedDataTable!.BorderOutline.Should().BeOfType<ShapeOutline.GradientVisible>().Subject;
        reloadedGradient.WidthPt.Should().BeApproximately(1.0, 0.001);
        reloadedGradient.Gradient.Stops.Select(s => s.Color.Resolved).Should()
            .Equal(new SrgbColor(0xFF, 0x00, 0x00), new SrgbColor(0x00, 0x00, 0xFF));
    }

    private static MemoryStream BuildPptxWithGradientChartDataTableBorder()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(["East", "West"]);
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange([10, 20]);
        chart.Series.Add(series);
        slide.Shapes.Add(new SlideShape
        {
            Id = 101,
            Name = "Gradient border chart",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 3657600,
            ExtentCyEmu = 2743200,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            chartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!
                .Add(new XElement(ChartNs + "dTable",
                    new XElement(ChartNs + "showHorzBorder", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showVertBorder", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showOutline", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showKeys", new XAttribute("val", "0")),
                    new XElement(ChartNs + "spPr",
                        new XElement(DrawingNs + "ln",
                            new XAttribute("w", DrawingMlCoordinateUnits.PointsToEmu(1.0)),
                            new XElement(DrawingNs + "gradFill",
                                new XElement(DrawingNs + "gsLst",
                                    new XElement(DrawingNs + "gs",
                                        new XAttribute("pos", "0"),
                                        new XElement(DrawingNs + "srgbClr", new XAttribute("val", "FF0000"))),
                                    new XElement(DrawingNs + "gs",
                                        new XAttribute("pos", "100000"),
                                        new XElement(DrawingNs + "srgbClr", new XAttribute("val", "0000FF")))),
                                new XElement(DrawingNs + "lin",
                                    new XAttribute("ang", "5400000"),
                                    new XAttribute("scaled", "0")))))));
            WriteXml(archive, "ppt/charts/chart1.xml", chartXml);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream BuildPptxWithUnmodeledPackageData()
    {
        var presentation = Presentation.CreateEmpty();
        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            WriteXml(
                archive,
                OpcPackageProperties.ExtendedPropertiesZipEntry,
                OpcDocumentProperties.BuildExtendedPropertiesDocument(
                    new ExtendedDocumentProperties(Application: "FreeP retention harness"),
                    includeXmlDeclaration: true));

            var customProperties = OpcCustomDocumentProperties.Create();
            customProperties.SetString("RetentionMarker", "retain-me");
            WriteXml(
                archive,
                OpcPackageProperties.CustomPropertiesZipEntry,
                customProperties.ToXDocument(includeXmlDeclaration: true));
            WriteText(archive, "customXml/item1.xml", """<bag xmlns="urn:freep:test">retain-me</bag>""");
            WriteText(archive, "customXml/itemProps1.xml",
                """<ds:datastoreItem ds:itemID="{11111111-1111-1111-1111-111111111111}" xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>""");
            WriteText(archive, "customXml/payload.freex", "freex-payload");
            WriteBytes(archive, "ppt/customData/viewState.bin", new byte[] { 0x46, 0x50, 0x52, 0x01 });

            var rootRels = LoadXml(archive, "_rels/.rels");
            AddRelationship(
                rootRels,
                "rIdAppProps",
                OpcPackageProperties.ExtendedPropertiesRelationshipType,
                OpcPackageProperties.ExtendedPropertiesZipEntry);
            AddRelationship(
                rootRels,
                "rIdCustomProps",
                OpcPackageProperties.CustomPropertiesRelationshipType,
                OpcPackageProperties.CustomPropertiesZipEntry);
            AddRelationship(rootRels, "rIdCustomXml", CustomXmlRelType, "customXml/item1.xml");
            AddRelationship(rootRels, "rIdExternalReview", ExternalReviewRelType, "https://example.com/freep-review", external: true);
            AddRelationship(rootRels, "rIdSlideMirror", UnknownSlideMirrorRelType, "ppt/slides/slide1.xml");
            WriteXml(archive, "_rels/.rels", rootRels);

            var itemRels = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(RelsNs + "Relationships",
                    new XElement(RelsNs + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"),
                        new XAttribute("Target", "itemProps1.xml"))));
            WriteXml(archive, "customXml/_rels/item1.xml.rels", itemRels);

            var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
            AddRelationship(presRels, "rIdUnknownView", UnknownViewRelType, "customData/viewState.bin");
            WriteXml(archive, "ppt/_rels/presentation.xml.rels", presRels);

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            AddOverride(
                contentTypes,
                OpcPackageProperties.ExtendedPropertiesPartName,
                OpcPackageProperties.ExtendedPropertiesContentType);
            AddOverride(
                contentTypes,
                OpcPackageProperties.CustomPropertiesPartName,
                OpcPackageProperties.CustomPropertiesContentType);
            AddOverride(contentTypes, "/customXml/itemProps1.xml",
                "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
            AddOverride(contentTypes, "/ppt/customData/viewState.bin",
                "application/vnd.example.freep.viewstate");
            AddDefault(contentTypes, "freex", "application/vnd.example.freep.payload");
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream BuildPptxWithChartWorkbookAndUnrelatedPackageData()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(["Old East", "Old West"]);
        var series = new ChartSeries { Name = "Old Actual" };
        series.Values.AddRange([10, 20]);
        chart.Series.Add(series);
        slide.Shapes.Add(new SlideShape
        {
            Id = 101,
            Name = "Workbook chart",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 3657600,
            ExtentCyEmu = 2743200,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            chartXml.Root!.Element(ChartNs + "externalData")?.Remove();
            chartXml.Root.Add(new XElement(ChartNs + "externalData",
                new XAttribute(RelsDocNs + "id", "rIdSourceWorkbook"),
                new XElement(ChartNs + "autoUpdate", new XAttribute("val", "0"))));
            chartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!
                .Add(new XElement(ChartNs + "dTable",
                    new XElement(ChartNs + "showHorzBorder", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showVertBorder", new XAttribute("val", "0")),
                    new XElement(ChartNs + "showOutline", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showKeys", new XAttribute("val", "1")),
                    new XElement(ChartNs + "spPr",
                        new XElement(DrawingNs + "solidFill",
                            new XElement(DrawingNs + "srgbClr", new XAttribute("val", "FAF1D2"))),
                        new XElement(DrawingNs + "ln",
                            new XAttribute("w", DrawingMlCoordinateUnits.PointsToEmu(1.25)),
                            new XElement(DrawingNs + "solidFill",
                                new XElement(DrawingNs + "srgbClr", new XAttribute("val", "123456"))),
                            new XElement(DrawingNs + "prstDash", new XAttribute("val", "dashDot")))),
                    new XElement(ChartNs + "txPr",
                        new XElement(DrawingNs + "bodyPr"),
                        new XElement(DrawingNs + "lstStyle"),
                        new XElement(DrawingNs + "p",
                            new XElement(DrawingNs + "pPr",
                                new XElement(DrawingNs + "defRPr",
                                    new XAttribute("sz", "875"),
                                    new XAttribute("b", "1"),
                                    new XAttribute("i", "1"),
                                    new XElement(DrawingNs + "solidFill",
                                        new XElement(DrawingNs + "srgbClr", new XAttribute("val", "224466"))))),
                            new XElement(DrawingNs + "endParaRPr")))));
            WriteXml(archive, "ppt/charts/chart1.xml", chartXml);

            var chartRels = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(RelsNs + "Relationships"));
            AddRelationship(
                chartRels,
                "rIdSourceWorkbook",
                PackageRelType,
                "../embeddings/sourceWorkbook.xlsx");
            WriteXml(archive, "ppt/charts/_rels/chart1.xml.rels", chartRels);
            WriteBytes(archive, "ppt/embeddings/sourceWorkbook.xlsx", Encoding.UTF8.GetBytes("stale workbook bytes"));

            WriteText(
                archive,
                "customXml/chartWorkbookPayload.xml",
                """<payload xmlns="urn:freep:test">unrelated-retain-me</payload>""");
            var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
            AddRelationship(
                presRels,
                "rIdChartWorkbookPayload",
                CustomXmlRelType,
                "../customXml/chartWorkbookPayload.xml");
            WriteXml(archive, "ppt/_rels/presentation.xml.rels", presRels);

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            AddOverride(
                contentTypes,
                "/ppt/embeddings/sourceWorkbook.xlsx",
                SpreadsheetWorkbookContentType);
            AddOverride(
                contentTypes,
                "/customXml/chartWorkbookPayload.xml",
                "application/vnd.example.freep.chart-workbook-payload+xml");
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    /// <summary>
    /// A regular (non-ChartEx) chart whose own rels carry THREE relationships: its embedded
    /// workbook plus a PowerPoint-2013+ chartStyle and chartColorStyle sidecar (the parts
    /// PowerPoint writes for "Chart Styles"/"Chart Colors" gallery choices). Mirrors
    /// <see cref="BuildPptxWithChartWorkbookAndUnrelatedPackageData"/> but adds the style
    /// sidecars the confirmed finding says get silently dropped.
    /// </summary>
    private static MemoryStream BuildPptxWithStyledChartWorkbookAndUnrelatedPackageData()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(["Old East", "Old West"]);
        var series = new ChartSeries { Name = "Old Actual" };
        series.Values.AddRange([10, 20]);
        chart.Series.Add(series);
        slide.Shapes.Add(new SlideShape
        {
            Id = 101,
            Name = "Styled workbook chart",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 3657600,
            ExtentCyEmu = 2743200,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            chartXml.Root!.Element(ChartNs + "externalData")?.Remove();
            chartXml.Root.Add(new XElement(ChartNs + "externalData",
                new XAttribute(RelsDocNs + "id", "rIdSourceWorkbook"),
                new XElement(ChartNs + "autoUpdate", new XAttribute("val", "0"))));
            WriteXml(archive, "ppt/charts/chart1.xml", chartXml);

            var chartRels = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(RelsNs + "Relationships"));
            AddRelationship(chartRels, "rIdSourceWorkbook", PackageRelType, "../embeddings/sourceStyledWorkbook.xlsx");
            AddRelationship(chartRels, "rIdStyle", ChartStyleRelType, "style1.xml");
            AddRelationship(chartRels, "rIdColors", ChartColorStyleRelType, "colors1.xml");
            WriteXml(archive, "ppt/charts/_rels/chart1.xml.rels", chartRels);

            WriteBytes(archive, "ppt/embeddings/sourceStyledWorkbook.xlsx", Encoding.UTF8.GetBytes("stale styled workbook bytes"));
            WriteText(
                archive,
                "ppt/charts/style1.xml",
                """<cs:chartStyle xmlns:cs="http://schemas.microsoft.com/office/drawing/2012/chartStyle">must-survive-style</cs:chartStyle>""");
            WriteText(
                archive,
                "ppt/charts/colors1.xml",
                """<cs:colorStyle xmlns:cs="http://schemas.microsoft.com/office/drawing/2012/chartStyle">must-survive-colors</cs:colorStyle>""");

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            AddOverride(contentTypes, "/ppt/embeddings/sourceStyledWorkbook.xlsx", SpreadsheetWorkbookContentType);
            AddOverride(contentTypes, "/ppt/charts/style1.xml", ChartStyleContentType);
            AddOverride(contentTypes, "/ppt/charts/colors1.xml", ChartColorStyleContentType);
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    /// <summary>
    /// A ChartEx (histogram) chart whose embedded "Edit Data in Excel" workbook and
    /// chartStyle/chartColorStyle sidecars are all present, with the on-slide cx:data cache
    /// deliberately holding different numbers than the (placeholder) embedded workbook bytes -
    /// mirroring a real PowerPoint-authored ChartEx part. A data edit is expected to refresh
    /// both the cache and a freshly regenerated workbook; an untouched save must preserve the
    /// workbook and sidecars verbatim.
    /// </summary>
    private static MemoryStream BuildPptxWithChartExWorkbookAndStyleSidecars()
    {
        const string chartExUri = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        XNamespace cx = chartExUri;
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            IsChartEx = true,
            ChartExLayoutId = "histogram",
            PreservedChartExXml = new XDocument(
                new XElement(cx + "chartSpace",
                    new XAttribute(XNamespace.Xmlns + "cx", chartExUri),
                    new XAttribute(XNamespace.Xmlns + "r", RelsDocNs.NamespaceName),
                    new XElement(cx + "chartData",
                        new XElement(cx + "data",
                            new XAttribute("id", 0),
                            new XElement(cx + "strDim",
                                new XAttribute("type", "cat"),
                                new XElement(cx + "lvl",
                                    new XAttribute("ptCount", 2),
                                    new XElement(cx + "pt", new XAttribute("idx", 0), "Old East"),
                                    new XElement(cx + "pt", new XAttribute("idx", 1), "Old West"))),
                            new XElement(cx + "numDim",
                                new XAttribute("type", "val"),
                                new XElement(cx + "lvl",
                                    new XAttribute("ptCount", 2),
                                    new XElement(cx + "pt", new XAttribute("idx", 0), "10"),
                                    new XElement(cx + "pt", new XAttribute("idx", 1), "20"))))),
                    new XElement(cx + "chart",
                        new XElement(cx + "plotArea",
                            new XElement(cx + "plotAreaRegion",
                                new XElement(cx + "series",
                                    new XAttribute("layoutId", "histogram"),
                                    new XElement(cx + "tx",
                                        new XElement(cx + "txData",
                                            new XElement(cx + "v", "Old Actual"))),
                                    new XElement(cx + "dataId", new XAttribute("val", 0)))))),
                    new XElement(cx + "externalData", new XAttribute(RelsDocNs + "id", "rIdWorkbook"))))
                .ToString(SaveOptions.DisableFormatting),
        };
        chart.Categories.AddRange(["Old East", "Old West"]);
        var series = new ChartSeries { Name = "Old Actual" };
        series.Values.AddRange([10, 20]);
        chart.Series.Add(series);

        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 201,
            Name = "ChartEx workbook+style chart",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 3657600,
            ExtentCyEmu = 2743200,
            Chart = chart,
        });
        var presentation = new Presentation();
        presentation.Slides.Add(slide);

        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartRels = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(RelsNs + "Relationships"));
            AddRelationship(chartRels, "rIdWorkbook", PackageRelType, "../embeddings/sourceChartExWorkbook.xlsx");
            AddRelationship(chartRels, "rIdStyle", ChartStyleRelType, "style1.xml");
            AddRelationship(chartRels, "rIdColors", ChartColorStyleRelType, "colors1.xml");
            WriteXml(archive, "ppt/charts/_rels/chartEx1.xml.rels", chartRels);

            WriteBytes(
                archive,
                "ppt/embeddings/sourceChartExWorkbook.xlsx",
                Encoding.UTF8.GetBytes("stale chartex workbook bytes"));
            WriteText(
                archive,
                "ppt/charts/style1.xml",
                """<cs:chartStyle xmlns:cs="http://schemas.microsoft.com/office/drawing/2012/chartStyle">must-survive-style</cs:chartStyle>""");
            WriteText(
                archive,
                "ppt/charts/colors1.xml",
                """<cs:colorStyle xmlns:cs="http://schemas.microsoft.com/office/drawing/2012/chartStyle">must-survive-colors</cs:colorStyle>""");

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            AddOverride(contentTypes, "/ppt/embeddings/sourceChartExWorkbook.xlsx", SpreadsheetWorkbookContentType);
            AddOverride(contentTypes, "/ppt/charts/style1.xml", ChartStyleContentType);
            AddOverride(contentTypes, "/ppt/charts/colors1.xml", ChartColorStyleContentType);
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream BuildPptxWithChartManualLayoutAndLegendOverlay()
    {
        using var source = BuildPptxWithChartWorkbookAndUnrelatedPackageData();
        var package = new MemoryStream();
        package.Write(source.ToArray());
        package.Position = 0;

        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            var chartEl = chartXml.Root!.Element(ChartNs + "chart")!;
            var plotArea = chartEl.Element(ChartNs + "plotArea")!;
            plotArea.AddFirst(BuildChartManualLayoutXml(
                layoutTarget: "inner",
                x: "0.12",
                y: "0.18",
                width: "0.68",
                height: "0.62"));

            chartEl.Element(ChartNs + "legend")?.Remove();
            chartEl.Element(ChartNs + "plotVisOnly")!.AddBeforeSelf(
                new XElement(ChartNs + "legend",
                    new XElement(ChartNs + "legendPos", new XAttribute("val", "r")),
                    BuildChartManualLayoutXml(
                        layoutTarget: null,
                        x: "0.72",
                        y: "0.20",
                        width: "0.20",
                        height: "0.25"),
                    new XElement(ChartNs + "overlay", new XAttribute("val", "1"))));

            WriteXml(archive, "ppt/charts/chart1.xml", chartXml);
        }

        package.Position = 0;
        return package;
    }

    private static XElement BuildChartManualLayoutXml(
        string? layoutTarget,
        string x,
        string y,
        string width,
        string height) =>
        new(ChartNs + "layout",
            new XElement(ChartNs + "manualLayout",
                layoutTarget is not null
                    ? new XElement(ChartNs + "layoutTarget", new XAttribute("val", layoutTarget))
                    : null,
                new XElement(ChartNs + "xMode", new XAttribute("val", "factor")),
                new XElement(ChartNs + "yMode", new XAttribute("val", "factor")),
                new XElement(ChartNs + "wMode", new XAttribute("val", "factor")),
                new XElement(ChartNs + "hMode", new XAttribute("val", "factor")),
                new XElement(ChartNs + "x", new XAttribute("val", x)),
                new XElement(ChartNs + "y", new XAttribute("val", y)),
                new XElement(ChartNs + "w", new XAttribute("val", width)),
                new XElement(ChartNs + "h", new XAttribute("val", height))));

    private static MemoryStream BuildPptxWithMultipleChartWorkbooksAndUnrelatedPackageData()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(CreateWorkbookChartShape(101, "Alpha", 10, 20, 914400));
        slide.Shapes.Add(CreateWorkbookChartShape(102, "Beta", 30, 40, 4572000));
        slide.Shapes.Add(CreateWorkbookChartShape(103, "Gamma", 70, 80, 8229600));
        presentation.Slides.Add(slide);

        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            AddSourceWorkbookSidecar(
                archive,
                chartIndex: 1,
                workbookName: "sourceWorkbookAlpha.xlsx",
                relId: "rIdSourceWorkbookAlpha",
                workbookPayload: "alpha workbook bytes");
            AddSourceWorkbookSidecar(
                archive,
                chartIndex: 2,
                workbookName: "sourceWorkbookBeta.xlsx",
                relId: "rIdSourceWorkbookBeta",
                workbookPayload: "beta workbook bytes");
            AddSourceWorkbookSidecar(
                archive,
                chartIndex: 3,
                workbookName: "sourceWorkbookGamma.xlsx",
                relId: "rIdSourceWorkbookGamma",
                workbookPayload: "gamma workbook bytes");

            WriteText(
                archive,
                "customXml/multiChartWorkbookPayload.xml",
                """<payload xmlns="urn:freep:test">multi-chart-retain-me</payload>""");
            var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
            AddRelationship(
                presRels,
                "rIdMultiChartWorkbookPayload",
                CustomXmlRelType,
                "../customXml/multiChartWorkbookPayload.xml");
            WriteXml(archive, "ppt/_rels/presentation.xml.rels", presRels);

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            AddOverride(contentTypes, "/ppt/embeddings/sourceWorkbookAlpha.xlsx", SpreadsheetWorkbookContentType);
            AddOverride(contentTypes, "/ppt/embeddings/sourceWorkbookBeta.xlsx", SpreadsheetWorkbookContentType);
            AddOverride(contentTypes, "/ppt/embeddings/sourceWorkbookGamma.xlsx", SpreadsheetWorkbookContentType);
            AddOverride(contentTypes, "/customXml/multiChartWorkbookPayload.xml",
                "application/vnd.example.freep.multi-chart-workbook-payload+xml");
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream BuildPptxWithNonSequentialChartWorkbookParts()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(CreateWorkbookChartShape(201, "First", 11, 22, 914400));
        slide.Shapes.Add(CreateWorkbookChartShape(202, "Second", 33, 44, 4572000));
        presentation.Slides.Add(slide);

        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            MoveEntry(archive, "ppt/charts/chart1.xml", "ppt/charts/chart7.xml");
            MoveEntry(archive, "ppt/charts/chart2.xml", "ppt/charts/chart3.xml");

            var slideRels = LoadXml(archive, "ppt/slides/_rels/slide1.xml.rels");
            SetRelationshipTarget(slideRels, "rIdChart1", "../charts/chart7.xml");
            SetRelationshipTarget(slideRels, "rIdChart2", "../charts/chart3.xml");
            WriteXml(archive, "ppt/slides/_rels/slide1.xml.rels", slideRels);

            AddSourceWorkbookSidecar(
                archive,
                chartPath: "ppt/charts/chart7.xml",
                workbookName: "sourceWorkbookFirst.xlsx",
                relId: "rIdSourceWorkbookFirst",
                workbookPayload: "first workbook bytes");
            AddSourceWorkbookSidecar(
                archive,
                chartPath: "ppt/charts/chart3.xml",
                workbookName: "sourceWorkbookSecond.xlsx",
                relId: "rIdSourceWorkbookSecond",
                workbookPayload: "second workbook bytes");

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            RemoveOverride(contentTypes, "/ppt/charts/chart1.xml");
            RemoveOverride(contentTypes, "/ppt/charts/chart2.xml");
            AddOverride(contentTypes, "/ppt/charts/chart7.xml", ChartContentType);
            AddOverride(contentTypes, "/ppt/charts/chart3.xml", ChartContentType);
            AddOverride(contentTypes, "/ppt/embeddings/sourceWorkbookFirst.xlsx", SpreadsheetWorkbookContentType);
            AddOverride(contentTypes, "/ppt/embeddings/sourceWorkbookSecond.xlsx", SpreadsheetWorkbookContentType);
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream BuildPptxWithRichFormulaChartWorkbooksAndUnrelatedPackageData()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var firstChartShape = CreateWorkbookChartShape(301, "Formula first", 11, 22, 914400);
        firstChartShape.Chart!.Series.Add(new ChartSeries
        {
            Name = "Formula first Old Projection",
            Values = { 12, 24 },
        });
        slide.Shapes.Add(firstChartShape);

        var secondChartShape = CreateWorkbookChartShape(302, "Formula second", 33, 44, 4572000);
        secondChartShape.Chart!.Series.Add(new ChartSeries
        {
            Name = "Formula second Old Scenario",
            Values = { 35, 47 },
        });
        slide.Shapes.Add(secondChartShape);
        presentation.Slides.Add(slide);

        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            AddSourceWorkbookSidecar(
                archive,
                chartIndex: 1,
                workbookName: "authoredWorkbookFirst.xlsx",
                relId: "rIdAuthoredWorkbookFirst",
                workbookBytes: BuildRichWorkbookBytes(
                    "Forecast Model 2026",
                    "first-rich-formula-workbook",
                    "Revenue_Actual",
                    "Local_Projection",
                    "Actual",
                    "Projection",
                    "SUM(B2,Forecast_Assumption)",
                    "SUM(B3,Forecast_Assumption)",
                    "SUM(B4,Forecast_Assumption)",
                    "C2+SUM($B$2:$B$4)/10",
                    "C3+SUM($B$2:$B$4)/10",
                    "C4+SUM($B$2:$B$4)/10"));
            SetChartFormulaReferences(
                archive,
                "ppt/charts/chart1.xml",
                seriesIndex: 0,
                "'Forecast Model 2026'!$C$1",
                "'Forecast Model 2026'!$A$2:$A$4",
                "'Forecast Model 2026'!Revenue_Actual");
            SetChartFormulaReferences(
                archive,
                "ppt/charts/chart1.xml",
                seriesIndex: 1,
                "'Forecast Model 2026'!$D$1",
                "'Forecast Model 2026'!$A$2:$A$4",
                "'Forecast Model 2026'!Local_Projection");

            AddSourceWorkbookSidecar(
                archive,
                chartIndex: 2,
                workbookName: "authoredWorkbookSecond.xlsx",
                relId: "rIdAuthoredWorkbookSecond",
                workbookBytes: BuildRichWorkbookBytes(
                    "Input Assumptions",
                    "second-rich-formula-workbook",
                    "Scenario_Source",
                    "Scenario_Local",
                    "Source",
                    "Local",
                    "SUM(B2:C2)",
                    "SUM(B3:C3)",
                    "SUM(B4:C4)",
                    "C2*1.15+SUM($B$2:$B$4)",
                    "C3*1.15+SUM($B$2:$B$4)",
                    "C4*1.15+SUM($B$2:$B$4)"));
            SetChartFormulaReferences(
                archive,
                "ppt/charts/chart2.xml",
                seriesIndex: 0,
                "'Input Assumptions'!$C$1",
                "'Input Assumptions'!$A$2:$A$4",
                "'Input Assumptions'!Scenario_Source");
            SetChartFormulaReferences(
                archive,
                "ppt/charts/chart2.xml",
                seriesIndex: 1,
                "'Input Assumptions'!$D$1",
                "'Input Assumptions'!$A$2:$A$4",
                "'Input Assumptions'!Scenario_Local");

            WriteText(
                archive,
                "customXml/richFormulaPayload.xml",
                """<payload xmlns="urn:freep:test">rich-formula-retain-me</payload>""");
            var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
            AddRelationship(
                presRels,
                "rIdRichFormulaPayload",
                CustomXmlRelType,
                "../customXml/richFormulaPayload.xml");
            WriteXml(archive, "ppt/_rels/presentation.xml.rels", presRels);

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            AddOverride(contentTypes, "/ppt/embeddings/authoredWorkbookFirst.xlsx", SpreadsheetWorkbookContentType);
            AddOverride(contentTypes, "/ppt/embeddings/authoredWorkbookSecond.xlsx", SpreadsheetWorkbookContentType);
            AddOverride(contentTypes, "/customXml/richFormulaPayload.xml",
                "application/vnd.example.freep.rich-formula-payload+xml");
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream BuildPptxWithScatterBubbleFormulaWorkbooks()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(CreateScatterFormulaChartShape(401, 914400));
        slide.Shapes.Add(CreateBubbleFormulaChartShape(402, 4572000));
        presentation.Slides.Add(slide);

        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            AddSourceWorkbookSidecar(
                archive,
                chartIndex: 1,
                workbookName: "scatterAuthoredWorkbook.xlsx",
                relId: "rIdScatterWorkbook",
                workbookBytes: BuildRichWorkbookBytes(
                    "Scatter Source",
                    "scatter-formula-workbook",
                    "Scatter_Y",
                    "Scatter_Local_Y",
                    "Scatter Growth",
                    "Scatter Projection",
                    "A2*10",
                    "A3*10",
                    "A4*10",
                    "C2+1",
                    "C3+1",
                    "C4+1"));
            SetScatterFormulaReferences(
                archive,
                "ppt/charts/chart1.xml",
                seriesIndex: 0,
                seriesNameFormula: "'Scatter Source'!$B$1",
                xValuesFormula: "'Scatter Source'!$A$2:$A$4",
                yValuesFormula: "'Scatter Source'!Scatter_Y");

            AddSourceWorkbookSidecar(
                archive,
                chartIndex: 2,
                workbookName: "bubbleAuthoredWorkbook.xlsx",
                relId: "rIdBubbleWorkbook",
                workbookBytes: BuildRichWorkbookBytes(
                    "Bubble Source",
                    "bubble-formula-workbook",
                    "Bubble_Y",
                    "Bubble_Size",
                    "Bubble Growth",
                    "Bubble Size",
                    "A2*8",
                    "A3*8",
                    "A4*8",
                    "B2/2",
                    "B3/2",
                    "B4/2"));
            SetBubbleFormulaReferences(
                archive,
                "ppt/charts/chart2.xml",
                seriesIndex: 0,
                seriesNameFormula: "'Bubble Source'!$B$1",
                xValuesFormula: "'Bubble Source'!$A$2:$A$4",
                yValuesFormula: "'Bubble Source'!Bubble_Y",
                bubbleSizeFormula: "'Bubble Source'!$D$2:$D$4");

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            AddOverride(contentTypes, "/ppt/embeddings/scatterAuthoredWorkbook.xlsx", SpreadsheetWorkbookContentType);
            AddOverride(contentTypes, "/ppt/embeddings/bubbleAuthoredWorkbook.xlsx", SpreadsheetWorkbookContentType);
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    private static SlideShape CreateScatterFormulaChartShape(uint shapeId, long offsetXEmu)
    {
        var chart = new ChartShape { ChartType = ChartType.Scatter, ScatterStyle = ScatterStyle.Marker };
        var series = new ChartSeries { Name = "Scatter Growth" };
        series.XValues.AddRange([1.5, 2.5, 3.5]);
        series.Values.AddRange([15, 25, 35]);
        chart.Series.Add(series);
        return CreateChartShape(shapeId, "Scatter formula chart", chart, offsetXEmu);
    }

    private static SlideShape CreateBubbleFormulaChartShape(uint shapeId, long offsetXEmu)
    {
        var chart = new ChartShape { ChartType = ChartType.Bubble };
        var series = new ChartSeries { Name = "Bubble Growth" };
        series.XValues.AddRange([1.5, 2.5, 3.5]);
        series.Values.AddRange([12, 24, 36]);
        series.BubbleSizes.AddRange([9, 10, 11]);
        chart.Series.Add(series);
        return CreateChartShape(shapeId, "Bubble formula chart", chart, offsetXEmu);
    }

    private static SlideShape CreateChartShape(uint shapeId, string name, ChartShape chart, long offsetXEmu) =>
        new()
        {
            Id = shapeId,
            Name = name,
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = offsetXEmu,
            OffsetYEmu = 914400,
            ExtentCxEmu = 3200400,
            ExtentCyEmu = 2743200,
            Chart = chart,
        };

    private static SlideShape CreateWorkbookChartShape(
        uint shapeId,
        string name,
        double firstValue,
        double secondValue,
        long offsetXEmu)
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange([$"{name} Old Q1", $"{name} Old Q2"]);
        var series = new ChartSeries { Name = $"{name} Old Revenue" };
        series.Values.AddRange([firstValue, secondValue]);
        chart.Series.Add(series);
        return new SlideShape
        {
            Id = shapeId,
            Name = $"{name} workbook chart",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = offsetXEmu,
            OffsetYEmu = 914400,
            ExtentCxEmu = 3200400,
            ExtentCyEmu = 2743200,
            Chart = chart,
        };
    }

    private static void AddSourceWorkbookSidecar(
        ZipArchive archive,
        int chartIndex,
        string workbookName,
        string relId,
        string workbookPayload)
    {
        var chartPath = $"ppt/charts/chart{chartIndex}.xml";
        var chartXml = LoadXml(archive, chartPath);
        chartXml.Root!.Element(ChartNs + "externalData")?.Remove();
        chartXml.Root.Add(new XElement(ChartNs + "externalData",
            new XAttribute(RelsDocNs + "id", relId),
            new XElement(ChartNs + "autoUpdate", new XAttribute("val", "0"))));
        WriteXml(archive, chartPath, chartXml);

        var chartRels = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(RelsNs + "Relationships"));
        AddRelationship(chartRels, relId, PackageRelType, $"../embeddings/{workbookName}");
        WriteXml(archive, $"ppt/charts/_rels/chart{chartIndex}.xml.rels", chartRels);
        WriteBytes(archive, $"ppt/embeddings/{workbookName}", Encoding.UTF8.GetBytes(workbookPayload));
    }

    private static void AddSourceWorkbookSidecar(
        ZipArchive archive,
        int chartIndex,
        string workbookName,
        string relId,
        byte[] workbookBytes)
    {
        var chartPath = $"ppt/charts/chart{chartIndex}.xml";
        var chartXml = LoadXml(archive, chartPath);
        chartXml.Root!.Element(ChartNs + "externalData")?.Remove();
        chartXml.Root.Add(new XElement(ChartNs + "externalData",
            new XAttribute(RelsDocNs + "id", relId),
            new XElement(ChartNs + "autoUpdate", new XAttribute("val", "0"))));
        WriteXml(archive, chartPath, chartXml);

        var chartRels = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(RelsNs + "Relationships"));
        AddRelationship(chartRels, relId, PackageRelType, $"../embeddings/{workbookName}");
        WriteXml(archive, $"ppt/charts/_rels/chart{chartIndex}.xml.rels", chartRels);
        WriteBytes(archive, $"ppt/embeddings/{workbookName}", workbookBytes);
    }

    private static void AddSourceWorkbookSidecar(
        ZipArchive archive,
        string chartPath,
        string workbookName,
        string relId,
        string workbookPayload)
    {
        var chartXml = LoadXml(archive, chartPath);
        chartXml.Root!.Element(ChartNs + "externalData")?.Remove();
        chartXml.Root.Add(new XElement(ChartNs + "externalData",
            new XAttribute(RelsDocNs + "id", relId),
            new XElement(ChartNs + "autoUpdate", new XAttribute("val", "0"))));
        WriteXml(archive, chartPath, chartXml);

        var chartRels = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(RelsNs + "Relationships"));
        AddRelationship(chartRels, relId, PackageRelType, $"../embeddings/{workbookName}");
        WriteXml(archive, OpcPathHelper.GetRelationshipPartPath(chartPath), chartRels);
        WriteBytes(archive, $"ppt/embeddings/{workbookName}", Encoding.UTF8.GetBytes(workbookPayload));
    }

    private static void SetChartFormulaReferences(
        ZipArchive archive,
        string chartPath,
        int seriesIndex,
        string seriesNameFormula,
        string categoryFormula,
        string valuesFormula)
    {
        var chartXml = LoadXml(archive, chartPath);
        var series = chartXml.Descendants(ChartNs + "ser").ElementAt(seriesIndex);
        SetSeriesNameFormula(series, seriesNameFormula);
        SetFormula(series.Element(ChartNs + "cat")!, categoryFormula);
        SetFormula(series.Element(ChartNs + "val")!, valuesFormula);
        WriteXml(archive, chartPath, chartXml);
    }

    private static void SetScatterFormulaReferences(
        ZipArchive archive,
        string chartPath,
        int seriesIndex,
        string seriesNameFormula,
        string xValuesFormula,
        string yValuesFormula)
    {
        var chartXml = LoadXml(archive, chartPath);
        var series = chartXml.Descendants(ChartNs + "ser").ElementAt(seriesIndex);
        SetSeriesNameFormula(series, seriesNameFormula);
        SetFormula(series.Element(ChartNs + "xVal")!, xValuesFormula);
        SetFormula(series.Element(ChartNs + "yVal")!, yValuesFormula);
        WriteXml(archive, chartPath, chartXml);
    }

    private static void SetBubbleFormulaReferences(
        ZipArchive archive,
        string chartPath,
        int seriesIndex,
        string seriesNameFormula,
        string xValuesFormula,
        string yValuesFormula,
        string bubbleSizeFormula)
    {
        var chartXml = LoadXml(archive, chartPath);
        var series = chartXml.Descendants(ChartNs + "ser").ElementAt(seriesIndex);
        SetSeriesNameFormula(series, seriesNameFormula);
        SetFormula(series.Element(ChartNs + "xVal")!, xValuesFormula);
        SetFormula(series.Element(ChartNs + "yVal")!, yValuesFormula);
        SetFormula(series.Element(ChartNs + "bubbleSize")!, bubbleSizeFormula);
        WriteXml(archive, chartPath, chartXml);
    }

    /// <summary>
    /// Points a c:cat/c:val/c:xVal/c:yVal/c:bubbleSize wrapper at a workbook range, simulating a
    /// PowerPoint-authored chart. The writer emits the literal form (c:strLit/c:numLit) when it has
    /// no range to reference, so a literal is promoted to the matching *Ref, keeping its points as
    /// the cache; an existing *Ref just has its c:f rewritten.
    /// </summary>
    private static void SetFormula(XElement wrapper, string formula)
    {
        var numeric = wrapper.Name != ChartNs + "cat";
        var refName = numeric ? "numRef" : "strRef";
        var litName = numeric ? "numLit" : "strLit";
        var cacheName = numeric ? "numCache" : "strCache";

        var referenceElement = wrapper.Element(ChartNs + refName);
        if (referenceElement is null)
        {
            var literal = wrapper.Element(ChartNs + litName)!;
            literal.Remove();
            referenceElement = new XElement(ChartNs + refName,
                new XElement(ChartNs + cacheName, literal.Nodes()));
            wrapper.Add(referenceElement);
        }

        var formulaElement = referenceElement.Element(ChartNs + "f");
        if (formulaElement is null)
            referenceElement.AddFirst(new XElement(ChartNs + "f", formula));
        else
            formulaElement.Value = formula;
    }

    /// <summary>Same promotion for c:tx, whose literal form is a bare c:v rather than a c:strLit.</summary>
    private static void SetSeriesNameFormula(XElement series, string formula)
    {
        var tx = series.Element(ChartNs + "tx")!;
        if (tx.Element(ChartNs + "strRef") is null)
        {
            var name = tx.Element(ChartNs + "v")?.Value ?? string.Empty;
            tx.RemoveNodes();
            tx.Add(new XElement(ChartNs + "strRef",
                new XElement(ChartNs + "strCache",
                    new XElement(ChartNs + "ptCount", new XAttribute("val", "1")),
                    new XElement(ChartNs + "pt",
                        new XAttribute("idx", "0"),
                        new XElement(ChartNs + "v", name)))));
        }

        var referenceElement = tx.Element(ChartNs + "strRef")!;
        var formulaElement = referenceElement.Element(ChartNs + "f");
        if (formulaElement is null)
            referenceElement.AddFirst(new XElement(ChartNs + "f", formula));
        else
            formulaElement.Value = formula;
    }

    private static byte[] BuildRichWorkbookBytes(
        string sheetName,
        string marker,
        string firstDefinedName,
        string secondDefinedName,
        string firstSeriesHeader,
        string secondSeriesHeader,
        string formula1,
        string formula2,
        string formula3,
        string formula4,
        string formula5,
        string formula6)
    {
        using var workbook = new MemoryStream();
        using (var archive = new ZipArchive(workbook, ZipArchiveMode.Update, leaveOpen: true))
        {
            WriteXml(archive, "[Content_Types].xml", new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(ContentTypesNs + "Types",
                    new XElement(ContentTypesNs + "Default",
                        new XAttribute("Extension", "rels"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(ContentTypesNs + "Default",
                        new XAttribute("Extension", "xml"),
                        new XAttribute("ContentType", "application/xml")),
                    new XElement(ContentTypesNs + "Override",
                        new XAttribute("PartName", "/xl/workbook.xml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                    new XElement(ContentTypesNs + "Override",
                        new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                    new XElement(ContentTypesNs + "Override",
                        new XAttribute("PartName", "/xl/styles.xml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")),
                    new XElement(ContentTypesNs + "Override",
                        new XAttribute("PartName", "/xl/calcChain.xml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml")))));

            WriteXml(archive, "_rels/.rels", new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(RelsNs + "Relationships",
                    new XElement(RelsNs + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                        new XAttribute("Target", "xl/workbook.xml")))));

            var spreadsheetNs = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            WriteXml(archive, "xl/workbook.xml", new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(spreadsheetNs + "workbook",
                    new XAttribute(XNamespace.Xmlns + "r", RelsDocNs.NamespaceName),
                    new XElement(spreadsheetNs + "sheets",
                        new XElement(spreadsheetNs + "sheet",
                            new XAttribute("name", sheetName),
                            new XAttribute("sheetId", "1"),
                            new XAttribute(RelsDocNs + "id", "rId1"))),
                    new XElement(spreadsheetNs + "definedNames",
                        new XElement(spreadsheetNs + "definedName",
                            new XAttribute("name", firstDefinedName),
                            $"'{sheetName}'!$C$2:$C$4"),
                        new XElement(spreadsheetNs + "definedName",
                            new XAttribute("name", "Forecast_Assumption"),
                            new XAttribute("localSheetId", "0"),
                            $"'{sheetName}'!$B$2"),
                        new XElement(spreadsheetNs + "definedName",
                            new XAttribute("name", secondDefinedName),
                            new XAttribute("localSheetId", "0"),
                            $"'{sheetName}'!$D$2:$D$4")),
                    new XElement(spreadsheetNs + "calcPr",
                        new XAttribute("calcId", "191029"),
                        new XAttribute("fullCalcOnLoad", "1")))));

            WriteXml(archive, "xl/_rels/workbook.xml.rels", new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(RelsNs + "Relationships",
                    new XElement(RelsNs + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                        new XAttribute("Target", "worksheets/sheet1.xml")),
                    new XElement(RelsNs + "Relationship",
                        new XAttribute("Id", "rId2"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                        new XAttribute("Target", "styles.xml")),
                    new XElement(RelsNs + "Relationship",
                        new XAttribute("Id", "rId3"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain"),
                        new XAttribute("Target", "calcChain.xml")))));

            WriteXml(archive, "xl/styles.xml", new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(spreadsheetNs + "styleSheet",
                    new XElement(spreadsheetNs + "fonts",
                        new XAttribute("count", "1"),
                        new XElement(spreadsheetNs + "font")),
                    new XElement(spreadsheetNs + "fills",
                        new XAttribute("count", "1"),
                        new XElement(spreadsheetNs + "fill")),
                    new XElement(spreadsheetNs + "borders",
                        new XAttribute("count", "1"),
                        new XElement(spreadsheetNs + "border")),
                    new XElement(spreadsheetNs + "cellStyleXfs",
                        new XAttribute("count", "1"),
                        new XElement(spreadsheetNs + "xf",
                            new XAttribute("numFmtId", "0"),
                            new XAttribute("fontId", "0"),
                            new XAttribute("fillId", "0"),
                            new XAttribute("borderId", "0"))),
                    new XElement(spreadsheetNs + "cellXfs",
                        new XAttribute("count", "1"),
                        new XElement(spreadsheetNs + "xf",
                            new XAttribute("numFmtId", "0"),
                            new XAttribute("fontId", "0"),
                            new XAttribute("fillId", "0"),
                            new XAttribute("borderId", "0"),
                            new XAttribute("xfId", "0"))))));

            WriteXml(archive, "xl/worksheets/sheet1.xml", new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(spreadsheetNs + "worksheet",
                    new XElement(spreadsheetNs + "sheetData",
                        new XElement(spreadsheetNs + "row",
                            new XAttribute("r", "1"),
                            InlineStringCell(spreadsheetNs, "A1", "Quarter"),
                            InlineStringCell(spreadsheetNs, "B1", "Base"),
                            InlineStringCell(spreadsheetNs, "C1", firstSeriesHeader),
                            InlineStringCell(spreadsheetNs, "D1", secondSeriesHeader)),
                        RichWorkbookRow(spreadsheetNs, 2, "Jan", 10, formula1, 11, formula4, 12),
                        RichWorkbookRow(spreadsheetNs, 3, "Feb", 20, formula2, 22, formula5, 24),
                        RichWorkbookRow(spreadsheetNs, 4, "Mar", 30, formula3, 33, formula6, 36)),
                    new XElement(spreadsheetNs + "customProperties",
                        new XElement(spreadsheetNs + "customPr",
                            new XAttribute("name", "FreePMarker"),
                            new XAttribute("val", marker))))));

            WriteXml(archive, "xl/calcChain.xml", new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(spreadsheetNs + "calcChain",
                    new XElement(spreadsheetNs + "c", new XAttribute("r", "C2"), new XAttribute("i", "1")),
                    new XElement(spreadsheetNs + "c", new XAttribute("r", "D2"), new XAttribute("i", "1")),
                    new XElement(spreadsheetNs + "c", new XAttribute("r", "C3"), new XAttribute("i", "1")),
                    new XElement(spreadsheetNs + "c", new XAttribute("r", "D3"), new XAttribute("i", "1")),
                    new XElement(spreadsheetNs + "c", new XAttribute("r", "C4"), new XAttribute("i", "1")),
                    new XElement(spreadsheetNs + "c", new XAttribute("r", "D4"), new XAttribute("i", "1")))));
        }

        return workbook.ToArray();
    }

    private static XElement RichWorkbookRow(
        XNamespace spreadsheetNs,
        int row,
        string quarter,
        int baseValue,
        string firstFormula,
        int firstCachedValue,
        string secondFormula,
        int secondCachedValue) =>
        new(spreadsheetNs + "row",
            new XAttribute("r", row.ToString()),
            InlineStringCell(spreadsheetNs, $"A{row}", quarter),
            NumberCell(spreadsheetNs, $"B{row}", baseValue),
            FormulaCell(spreadsheetNs, $"C{row}", firstFormula, firstCachedValue),
            FormulaCell(spreadsheetNs, $"D{row}", secondFormula, secondCachedValue));

    private static XElement InlineStringCell(XNamespace spreadsheetNs, string reference, string value) =>
        new(spreadsheetNs + "c",
            new XAttribute("r", reference),
            new XAttribute("t", "inlineStr"),
            new XElement(spreadsheetNs + "is",
                new XElement(spreadsheetNs + "t", value)));

    private static XElement NumberCell(XNamespace spreadsheetNs, string reference, int value) =>
        new(spreadsheetNs + "c",
            new XAttribute("r", reference),
            new XElement(spreadsheetNs + "v", value.ToString()));

    private static XElement FormulaCell(XNamespace spreadsheetNs, string reference, string formula, int cachedValue) =>
        new(spreadsheetNs + "c",
            new XAttribute("r", reference),
            new XElement(spreadsheetNs + "f", formula),
            new XElement(spreadsheetNs + "v", cachedValue.ToString()));

    private static MemoryStream BuildPptxWithPresentationScopedCustomXml()
    {
        var presentation = Presentation.CreateEmpty();
        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            WriteText(archive, "customXml/item2.xml",
                """<bag xmlns="urn:freep:test">presentation-scoped-retain-me</bag>""");
            WriteText(archive, "customXml/itemProps2.xml",
                """<ds:datastoreItem ds:itemID="{22222222-2222-2222-2222-222222222222}" xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>""");
            WriteText(archive, "customXml/item2.freexmeta", "presentation custom xml payload");

            var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
            AddRelationship(presRels, "rIdPresentationCustomXml", CustomXmlRelType, "../customXml/item2.xml");
            WriteXml(archive, "ppt/_rels/presentation.xml.rels", presRels);

            var itemRels = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(RelsNs + "Relationships",
                    new XElement(RelsNs + "Relationship",
                        new XAttribute("Id", "rIdProps"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"),
                        new XAttribute("Target", "itemProps2.xml")),
                    new XElement(RelsNs + "Relationship",
                        new XAttribute("Id", "rIdPayload"),
                        new XAttribute("Type", "http://example.com/freep/relationships/customXmlPayload"),
                        new XAttribute("Target", "item2.freexmeta"))));
            WriteXml(archive, "customXml/_rels/item2.xml.rels", itemRels);

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            AddOverride(contentTypes, "/customXml/itemProps2.xml",
                "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
            AddDefault(contentTypes, "freexmeta", "application/vnd.example.freep.customxml-payload");
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream BuildPptxWithViewAndPrintSettings()
    {
        var presentation = Presentation.CreateEmpty();
        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var presProps = XDocument.Parse("""
                <p:presentationPr xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:prnPr prnWhat="handouts3" clrMode="clr" hiddenSlides="0" frameSlides="1"/>
                  <p:extLst>
                    <p:ext uri="{11111111-2222-3333-4444-555555555555}">
                      <freep:retention xmlns:freep="urn:freep:test">freep-print-retain</freep:retention>
                    </p:ext>
                  </p:extLst>
                </p:presentationPr>
                """);
            WriteXml(archive, "ppt/presProps.xml", presProps);

            var viewProps = XDocument.Parse("""
                <p:viewPr xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" lastView="sldSorterView">
                  <p:normalViewPr showOutlineIcons="1"/>
                  <p:gridSpacing cx="12700" cy="12700"/>
                  <p:extLst>
                    <p:ext uri="{66666666-7777-8888-9999-AAAAAAAAAAAA}">
                      <freep:retention xmlns:freep="urn:freep:test">freep-view-retain</freep:retention>
                    </p:ext>
                  </p:extLst>
                </p:viewPr>
                """);
            WriteXml(archive, "ppt/viewProps.xml", viewProps);

            WriteBytes(archive, "ppt/printerSettings/printerSettings1.bin", new byte[] { 0x46, 0x50, 0x50, 0x01 });

            var presPropsRels = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(RelsNs + "Relationships"));
            AddRelationship(
                presPropsRels,
                "rIdPrinterSettings",
                PrinterSettingsRelType,
                "printerSettings/printerSettings1.bin");
            WriteXml(archive, "ppt/_rels/presProps.xml.rels", presPropsRels);

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            AddOverride(
                contentTypes,
                "/ppt/printerSettings/printerSettings1.bin",
                "application/vnd.openxmlformats-officedocument.presentationml.printerSettings");
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    private static void AssertCommentsNotesPackageParts(ZipArchive archive)
    {
        archive.GetEntry("ppt/notesSlides/notesSlide1.xml").Should().NotBeNull();
        archive.GetEntry("ppt/notesSlides/notesSlide2.xml").Should().NotBeNull();
        archive.GetEntry("ppt/notesMasters/notesMaster1.xml").Should().NotBeNull();
        archive.GetEntry("ppt/comments/comment1.xml").Should().NotBeNull();
        archive.GetEntry("ppt/comments/comment2.xml").Should().NotBeNull();
        archive.GetEntry("ppt/commentAuthors.xml").Should().NotBeNull();

        ReadText(archive, "ppt/notesSlides/notesSlide1.xml")
            .Should().Contain("Speaker note: introduce the review workflow.")
            .And.Contain("package save");
        ReadText(archive, "ppt/notesSlides/notesSlide2.xml")
            .Should().Contain("Speaker note: summarize the comment decisions.");
        ReadText(archive, "ppt/comments/comment1.xml")
            .Should().Contain("Confirm the title before publishing.");
        ReadText(archive, "ppt/comments/comment2.xml")
            .Should().Contain("Add a data source footnote.")
            .And.Contain("Keep this callout for presenter notes.");
        ReadText(archive, "ppt/commentAuthors.xml")
            .Should().Contain("Alice Reviewer")
            .And.Contain("Bob Reviewer");

        var contentTypes = LoadXml(archive, "[Content_Types].xml");
        Override(contentTypes, "/ppt/notesSlides/notesSlide1.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.notesSlide+xml").Should().NotBeNull();
        Override(contentTypes, "/ppt/notesSlides/notesSlide2.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.notesSlide+xml").Should().NotBeNull();
        Override(contentTypes, "/ppt/notesMasters/notesMaster1.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.notesMaster+xml").Should().NotBeNull();
        Override(contentTypes, "/ppt/comments/comment1.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.comments+xml").Should().NotBeNull();
        Override(contentTypes, "/ppt/comments/comment2.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.comments+xml").Should().NotBeNull();
        Override(contentTypes, "/ppt/commentAuthors.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.commentAuthors+xml").Should().NotBeNull();

        var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
        Relationship(presRels,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesMaster",
            "notesMasters/notesMaster1.xml").Should().NotBeNull();
        Relationship(presRels,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/commentAuthors",
            "commentAuthors.xml").Should().NotBeNull();

        AssertSlideCommentsNotesRelationships(archive, slideIndex: 1);
        AssertSlideCommentsNotesRelationships(archive, slideIndex: 2);
    }

    private static void AssertSlideCommentsNotesRelationships(ZipArchive archive, int slideIndex)
    {
        var slideRels = LoadXml(archive, $"ppt/slides/_rels/slide{slideIndex}.xml.rels");
        Relationship(slideRels,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesSlide",
            $"../notesSlides/notesSlide{slideIndex}.xml").Should().NotBeNull();
        Relationship(slideRels,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments",
            $"../comments/comment{slideIndex}.xml").Should().NotBeNull();
    }

    private static string TextBodyText(TextBody? body) =>
        body is null
            ? string.Empty
            : string.Concat(body.Paragraphs.SelectMany(paragraph => paragraph.Runs.Select(run => run.Text)));

    private static string AddModeledShapeEdit(Presentation presentation, string deckName)
    {
        var slide = presentation.Slides[0];
        var shapeId = slide.Shapes.Select(shape => shape.Id).DefaultIfEmpty(0u).Max() + 1u;
        var shapeName = $"Semantic corpus edit - {Path.GetFileNameWithoutExtension(deckName)}";
        slide.Shapes.Add(new SlideShape
        {
            Id = shapeId,
            Name = shapeName,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 457200,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 457200,
        });

        return shapeName;
    }

    private static void AssertPreservedPackageEntries(
        ZipArchive sourceArchive,
        ZipArchive savedArchive,
        string deckName)
    {
        foreach (var sourceEntry in sourceArchive.Entries
                     .Where(entry => !string.IsNullOrWhiteSpace(entry.FullName) && !entry.FullName.EndsWith('/'))
                     .Where(entry => !IsWriterOwnedPart(entry.FullName)))
        {
            var savedEntry = savedArchive.GetEntry(sourceEntry.FullName);
            savedEntry.Should().NotBeNull($"{deckName} should retain package entry {sourceEntry.FullName}");
            ReadBytes(savedArchive, sourceEntry.FullName).Should().Equal(
                ReadBytes(sourceArchive, sourceEntry.FullName),
                $"{deckName} should byte-preserve non-writer-owned package entry {sourceEntry.FullName}");
        }
    }

    private static void AssertPreservedContentTypes(
        ZipArchive sourceArchive,
        ZipArchive savedArchive,
        string deckName)
    {
        var sourceTypes = LoadXml(sourceArchive, "[Content_Types].xml");
        var savedTypes = LoadXml(savedArchive, "[Content_Types].xml");

        var savedDefaults = savedTypes.Root!
            .Elements(ContentTypesNs + "Default")
            .ToDictionary(
                element => element.Attribute("Extension")!.Value,
                element => element.Attribute("ContentType")!.Value,
                StringComparer.OrdinalIgnoreCase);
        foreach (var sourceDefault in sourceTypes.Root!.Elements(ContentTypesNs + "Default"))
        {
            var extension = sourceDefault.Attribute("Extension")?.Value;
            var contentType = sourceDefault.Attribute("ContentType")?.Value;
            if (string.IsNullOrWhiteSpace(extension) || string.IsNullOrWhiteSpace(contentType))
                continue;

            savedDefaults.Should().ContainKey(extension, $"{deckName} should retain Default content type for .{extension}");
            savedDefaults[extension].Should().Be(contentType, $"{deckName} should retain Default content type for .{extension}");
        }

        var savedOverrides = savedTypes.Root!
            .Elements(ContentTypesNs + "Override")
            .ToDictionary(
                element => NormalizePartName(element.Attribute("PartName")!.Value),
                element => element.Attribute("ContentType")!.Value,
                StringComparer.OrdinalIgnoreCase);
        foreach (var sourceOverride in sourceTypes.Root!.Elements(ContentTypesNs + "Override"))
        {
            var partName = sourceOverride.Attribute("PartName")?.Value;
            var contentType = sourceOverride.Attribute("ContentType")?.Value;
            if (string.IsNullOrWhiteSpace(partName) ||
                string.IsNullOrWhiteSpace(contentType) ||
                IsWriterOwnedPart(partName))
            {
                continue;
            }

            var normalizedPartName = NormalizePartName(partName);
            savedOverrides.Should().ContainKey(normalizedPartName,
                $"{deckName} should retain Override content type for {normalizedPartName}");
            savedOverrides[normalizedPartName].Should().Be(contentType,
                $"{deckName} should retain Override content type for {normalizedPartName}");
        }
    }

    private static void AssertPreservedRelationships(
        ZipArchive sourceArchive,
        ZipArchive savedArchive,
        string deckName)
    {
        foreach (var sourceRelsEntry in sourceArchive.Entries
                     .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var sourcePartPath = SourcePartPathFromRelationshipPath(sourceRelsEntry.FullName);
            var retainedRelationships = OpcRelationships.Load(sourceArchive, sourceRelsEntry.FullName)
                .Where(relationship => !IsWriterOwnedRelationship(
                    sourcePartPath,
                    relationship.Type,
                    relationship.Target,
                    relationship.IsExternal))
                .ToArray();
            if (retainedRelationships.Length == 0)
                continue;

            savedArchive.GetEntry(sourceRelsEntry.FullName).Should().NotBeNull(
                $"{deckName} should retain relationship part {sourceRelsEntry.FullName}");
            var savedRelationships = OpcRelationships.Load(savedArchive, sourceRelsEntry.FullName);
            foreach (var relationship in retainedRelationships)
            {
                savedRelationships.Should().Contain(saved => RelationshipMatches(saved, relationship),
                    $"{deckName} should retain {sourceRelsEntry.FullName} relationship {relationship.Type} -> {relationship.Target}");
            }
        }
    }

    private static void AssertFeaturePackageEntriesStillPresent(
        ZipArchive sourceArchive,
        ZipArchive savedArchive,
        string deckName,
        string[] featurePartPrefixes)
    {
        var sourceFeatureEntries = FeatureDataEntries(sourceArchive, featurePartPrefixes)
            .Select(entry => entry.FullName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        sourceFeatureEntries.Should().NotBeEmpty($"{deckName} should exercise the requested feature package parts");

        var savedFeatureEntries = FeatureDataEntries(savedArchive, featurePartPrefixes)
            .Select(entry => entry.FullName)
            .ToArray();
        savedFeatureEntries.Should().HaveCountGreaterThanOrEqualTo(sourceFeatureEntries.Length,
            $"{deckName} should keep the requested feature package part family after a modeled edit");

        var sourceTypes = LoadXml(sourceArchive, "[Content_Types].xml");
        var savedTypes = LoadXml(savedArchive, "[Content_Types].xml");
        var savedFeatureContentTypes = savedFeatureEntries
            .Select(entry => ContentTypeForPart(savedTypes, entry))
            .Where(contentType => !string.IsNullOrWhiteSpace(contentType))
            .ToLookup(contentType => contentType!, StringComparer.OrdinalIgnoreCase);
        var sourceFeatureContentTypes = sourceFeatureEntries
            .Select(entry => ContentTypeForPart(sourceTypes, entry))
            .Where(contentType => !string.IsNullOrWhiteSpace(contentType))
            .GroupBy(contentType => contentType!, StringComparer.OrdinalIgnoreCase);

        foreach (var sourceContentType in sourceFeatureContentTypes)
        {
            savedFeatureContentTypes[sourceContentType.Key].Should().HaveCountGreaterThanOrEqualTo(
                sourceContentType.Count(),
                $"{deckName} should keep {sourceContentType.Count()} high-risk feature part(s) with content type {sourceContentType.Key}");
        }
    }

    private static IEnumerable<ZipArchiveEntry> FeatureDataEntries(
        ZipArchive archive,
        string[] featurePartPrefixes) =>
        archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.FullName) && !entry.FullName.EndsWith('/'))
            .Where(entry => IsFeaturePart(entry.FullName, featurePartPrefixes))
            .Where(entry => !entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            .Where(entry => !NormalizePartName(entry.FullName).Contains("/_rels/", StringComparison.OrdinalIgnoreCase));

    private static void AssertFeatureContentTypesStillPresent(
        ZipArchive sourceArchive,
        ZipArchive savedArchive,
        string deckName,
        string[] featurePartPrefixes)
    {
        var sourceTypes = LoadXml(sourceArchive, "[Content_Types].xml");
        var savedTypes = LoadXml(savedArchive, "[Content_Types].xml");
        var sourceFeatureEntries = FeatureDataEntries(sourceArchive, featurePartPrefixes)
            .Select(entry => entry.FullName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var savedContentTypesByFamily = FeatureDataEntries(savedArchive, featurePartPrefixes)
            .Select(entry => ContentTypeForPart(savedTypes, entry.FullName))
            .Where(contentType => !string.IsNullOrWhiteSpace(contentType))
            .ToLookup(contentType => contentType!, StringComparer.OrdinalIgnoreCase);

        foreach (var sourceEntry in sourceFeatureEntries)
        {
            var sourceContentType = ContentTypeForPart(sourceTypes, sourceEntry);
            sourceContentType.Should().NotBeNull($"{deckName} should have a source content type for {sourceEntry}");
            savedContentTypesByFamily[sourceContentType!].Should().NotBeEmpty(
                $"{deckName} should keep content type {sourceContentType} for the {FeatureFamilyForPart(sourceEntry, featurePartPrefixes)} package family after a modeled edit");
        }
    }

    private static void AssertFeatureRelationshipsStillPresent(
        ZipArchive sourceArchive,
        ZipArchive savedArchive,
        string deckName,
        string[] featurePartPrefixes)
    {
        foreach (var sourceRelsEntry in sourceArchive.Entries
                     .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var sourcePartPath = SourcePartPathFromRelationshipPath(sourceRelsEntry.FullName);
            var featureRelationships = OpcRelationships.Load(sourceArchive, sourceRelsEntry.FullName)
                .Where(relationship => IsFeatureRelationship(sourcePartPath, relationship, featurePartPrefixes))
                .ToArray();
            if (featureRelationships.Length == 0)
                continue;

            savedArchive.GetEntry(sourceRelsEntry.FullName).Should().NotBeNull(
                $"{deckName} should keep relationship part {sourceRelsEntry.FullName} after a modeled edit");
            var savedFeatureRelationships = OpcRelationships.Load(savedArchive, sourceRelsEntry.FullName)
                .Where(relationship => IsFeatureRelationship(sourcePartPath, relationship, featurePartPrefixes))
                .GroupBy(relationship => relationship.Type, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var sourceGroup in featureRelationships.GroupBy(relationship => relationship.Type, StringComparer.OrdinalIgnoreCase))
            {
                savedFeatureRelationships.Should().ContainKey(sourceGroup.Key,
                    $"{deckName} should keep high-risk feature relationship type {sourceGroup.Key} in {sourceRelsEntry.FullName}");
                savedFeatureRelationships[sourceGroup.Key].Should().BeGreaterThanOrEqualTo(sourceGroup.Count(),
                    $"{deckName} should keep high-risk feature relationship count for {sourceGroup.Key} in {sourceRelsEntry.FullName}");
            }
        }
    }

    private static string FindCorpusDirectory() =>
        TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory(
            "tools", "FreeP.RenderCompare", "corpus");

    private static bool RelationshipMatches(OpcRelationship actual, OpcRelationship expected) =>
        string.Equals(actual.Type, expected.Type, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(actual.Target, expected.Target, StringComparison.Ordinal) &&
        actual.IsExternal == expected.IsExternal;

    private static bool IsWriterOwnedRelationship(string sourcePartPath, string type, string target, bool external)
    {
        if (WriterOwnedRelationshipTypes.Contains(type))
            return true;

        if (external || string.IsNullOrWhiteSpace(target))
            return false;

        var sourceDirectory = string.IsNullOrWhiteSpace(sourcePartPath)
            ? string.Empty
            : OpcPathHelper.GetDirectoryName(sourcePartPath);
        var targetPath = OpcPathHelper.ResolveRelativeZipPath(sourceDirectory, target);
        return IsWriterOwnedPart(targetPath);
    }

    private static bool IsWriterOwnedPart(string partName)
    {
        var normalized = NormalizePartName(partName);
        return WriterOwnedPackagePartPaths.Any(path => string.Equals(
                   NormalizePartName(path),
                   normalized,
                   StringComparison.OrdinalIgnoreCase)) ||
               WriterOwnedPackagePartPrefixes.Any(prefix =>
                   normalized.StartsWith(NormalizePartName(prefix), StringComparison.OrdinalIgnoreCase));
    }

    private static string SourcePartPathFromRelationshipPath(string relsPath)
    {
        var normalized = relsPath.Replace('\\', '/').TrimStart('/');
        if (string.Equals(normalized, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        const string marker = "/_rels/";
        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || !normalized.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return normalized[..markerIndex] + "/" + normalized[(markerIndex + marker.Length)..^".rels".Length];
    }

    private static string NormalizePartName(string partName) =>
        OpcPathHelper.ToZipEntryPath(partName);

    private static bool IsFeatureRelationship(
        string sourcePartPath,
        OpcRelationship relationship,
        string[] featurePartPrefixes)
    {
        if (relationship.IsExternal || string.IsNullOrWhiteSpace(relationship.Target))
            return false;

        var sourceDirectory = string.IsNullOrWhiteSpace(sourcePartPath)
            ? string.Empty
            : OpcPathHelper.GetDirectoryName(sourcePartPath);
        var targetPath = OpcPathHelper.ResolveRelativeZipPath(sourceDirectory, relationship.Target);
        return IsFeaturePart(targetPath, featurePartPrefixes);
    }

    private static bool IsFeaturePart(string partName, string[] featurePartPrefixes)
    {
        var normalized = NormalizePartName(partName);
        return featurePartPrefixes.Any(prefix =>
            normalized.StartsWith(NormalizePartName(prefix), StringComparison.OrdinalIgnoreCase));
    }

    private static string FeatureFamilyForPart(string partName, string[] featurePartPrefixes)
    {
        var normalized = NormalizePartName(partName);
        return featurePartPrefixes.First(prefix =>
            normalized.StartsWith(NormalizePartName(prefix), StringComparison.OrdinalIgnoreCase));
    }

    private static string? ContentTypeForPart(XDocument contentTypes, string partName)
    {
        var normalizedPartName = "/" + NormalizePartName(partName);
        var overrideType = contentTypes.Root?
            .Elements(ContentTypesNs + "Override")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("PartName")?.Value,
                normalizedPartName,
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")
            ?.Value;
        if (!string.IsNullOrWhiteSpace(overrideType))
            return overrideType;

        var extension = Path.GetExtension(partName).TrimStart('.');
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        return contentTypes.Root?
            .Elements(ContentTypesNs + "Default")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("Extension")?.Value,
                extension,
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")
            ?.Value;
    }

    private static readonly XNamespace RelsNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace RelsDocNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace ChartNs =
        "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs =
        "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ContentTypesNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private static XElement? Relationship(XDocument doc, string type, string target) =>
        doc.Root?.Elements(RelsNs + "Relationship").FirstOrDefault(r =>
            r.Attribute("Type")?.Value == type &&
            r.Attribute("Target")?.Value == target);

    private static XElement? Override(XDocument doc, string partName, string contentType) =>
        doc.Root?.Elements(ContentTypesNs + "Override").FirstOrDefault(o =>
            o.Attribute("PartName")?.Value == partName &&
            o.Attribute("ContentType")?.Value == contentType);

    private static XElement? Default(XDocument doc, string extension, string contentType) =>
        doc.Root?.Elements(ContentTypesNs + "Default").FirstOrDefault(o =>
            o.Attribute("Extension")?.Value == extension &&
            o.Attribute("ContentType")?.Value == contentType);

    private static void AssertChartFormulaReferences(
        ChartShape chart,
        params (string SeriesName, string Category, string Values)[] expected)
    {
        chart.Series.Should().HaveCount(expected.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            chart.Series[i].FormulaReferences.SeriesName.Should().Be(expected[i].SeriesName);
            chart.Series[i].FormulaReferences.Category.Should().Be(expected[i].Category);
            chart.Series[i].FormulaReferences.Values.Should().Be(expected[i].Values);
        }
    }

    private static void AddRelationship(XDocument doc, string id, string type, string target, bool external = false)
    {
        var relationship = new XElement(RelsNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", type),
            new XAttribute("Target", target));
        if (external)
            relationship.Add(new XAttribute("TargetMode", "External"));

        doc.Root!.Add(relationship);
    }

    private static void AddOverride(XDocument doc, string partName, string contentType)
    {
        doc.Root!.Add(new XElement(ContentTypesNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));
    }

    private static void RemoveOverride(XDocument doc, string partName)
    {
        doc.Root!
            .Elements(ContentTypesNs + "Override")
            .Where(element => string.Equals(
                element.Attribute("PartName")?.Value,
                partName,
                StringComparison.OrdinalIgnoreCase))
            .Remove();
    }

    private static void SetRelationshipTarget(XDocument doc, string id, string target)
    {
        var relationship = doc.Root!
            .Elements(RelsNs + "Relationship")
            .Single(element => element.Attribute("Id")?.Value == id);
        relationship.SetAttributeValue("Target", target);
    }

    private static void AddDefault(XDocument doc, string extension, string contentType)
    {
        doc.Root!.Add(new XElement(ContentTypesNs + "Default",
            new XAttribute("Extension", extension),
            new XAttribute("ContentType", contentType)));
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new FileNotFoundException(path);
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string ReadText(ZipArchive archive, string path) =>
        Encoding.UTF8.GetString(ReadBytes(archive, path));

    private static byte[] ReadBytes(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new FileNotFoundException(path);
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static void WriteText(ZipArchive archive, string path, string text) =>
        WriteBytes(archive, path, Encoding.UTF8.GetBytes(text));

    private static void WriteXml(ZipArchive archive, string path, XDocument doc)
    {
        var entry = archive.GetEntry(path);
        entry?.Delete();
        entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        doc.Save(stream);
    }

    private static void WriteBytes(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.GetEntry(path);
        entry?.Delete();
        entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void MoveEntry(ZipArchive archive, string sourcePath, string destinationPath)
    {
        var bytes = ReadBytes(archive, sourcePath);
        archive.GetEntry(sourcePath)!.Delete();
        WriteBytes(archive, destinationPath, bytes);
    }
}
