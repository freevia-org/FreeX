using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FreeX.App.Host.Tests;

public partial class ExportPlannerTests
{
    [Fact]
    public void PdfDocumentExporter_MapsQualityToRasterDpi()
    {
        PdfDocumentExporter.ResolveRasterDpi(ExportQuality.Standard).Should().Be(96.0);
        PdfDocumentExporter.ResolveRasterDpi(ExportQuality.MinimumSize).Should().Be(72.0);
        PdfDocumentExporter.ResolveRasterDpi((ExportQuality)99).Should().Be(96.0);
    }

    [Theory]
    [InlineData(@"C:\temp\report.pdf", @"C:\temp\report.xps")]
    [InlineData(@"C:\temp\report", @"C:\temp\report.xps")]
    [InlineData(@"C:\temp\report.output", @"C:\temp\report.xps")]
    public void GetFallbackXpsPath_ChangesRequestedPathToXps(string requestedPath, string expected)
    {
        ExportPlanner.GetFallbackXpsPath(requestedPath).Should().Be(expected);
    }

    [Fact]
    public void PdfFallbackMessage_ExplainsWindowsPrintPipelineAndXpsConversion()
    {
        ExportPlanner.PdfFallbackMessage.Should().Be(
            UiText.Get("Export_PdfFallbackMessage"));
    }

    [Fact]
    public void PdfDocumentExporter_WritesPdfFileFromFixedDocument()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateOnePageDocument();

            try
            {
                PdfDocumentExporter.Save(document, path);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes[..Math.Min(bytes.Length, 8)]).Should().StartWith("%PDF-");
                Encoding.ASCII.GetString(bytes).Should().Contain("%%EOF");

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.PageCount.Should().Be(1);
                pdf.Pages[0].Width.Point.Should().BeApproximately(120, 0.01);
                pdf.Pages[0].Height.Point.Should().BeApproximately(90, 0.01);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesRequestedDocumentProperties()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateOnePageDocument();
            var properties = new PdfDocumentProperties(
                Title: "Quarterly Review",
                Author: "Finance Team",
                Subject: "Workbook export",
                Keywords: "FreeX, spreadsheet");

            try
            {
                PdfDocumentExporter.Save(document, path, properties);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.Info.Title.Should().Be("Quarterly Review");
                pdf.Info.Author.Should().Be("Finance Team");
                pdf.Info.Subject.Should().Be("Workbook export");
                pdf.Info.Keywords.Should().Be("FreeX, spreadsheet");
                pdf.Info.Creator.Should().Be("FreeX");
                ReadDisplayDocTitle(pdf).Should().BeTrue();
                ReadPrintScaling(pdf).Should().Be("/None");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_DoesNotRequestTitleDisplayWithoutTitle()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateOnePageDocument();
            var properties = new PdfDocumentProperties(
                Title: "   ",
                Author: "Finance Team",
                Subject: "Workbook export",
                Keywords: "FreeX, spreadsheet");

            try
            {
                PdfDocumentExporter.Save(document, path, properties);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.Info.Title.Should().BeEmpty();
                ReadDisplayDocTitle(pdf).Should().BeFalse();
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_DisablesViewerPrintScalingByDefault()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateOnePageDocument();

            try
            {
                PdfDocumentExporter.Save(document, path);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                ReadPrintScaling(pdf).Should().Be("/None");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_RequestsSinglePageInitialLayout()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateDocument(pageCount: 2);

            try
            {
                PdfDocumentExporter.Save(document, path);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                ReadPageLayout(pdf).Should().Be("/SinglePage");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_SetsDefaultWindowViewerPreferences()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateOnePageDocument();

            try
            {
                PdfDocumentExporter.Save(document, path);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                ReadViewerPreference(pdf, "/FitWindow").Should().BeTrue();
                ReadViewerPreference(pdf, "/CenterWindow").Should().BeTrue();
                ReadViewerPreference(pdf, "/PickTrayByPDFSize").Should().BeTrue();
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_SetsDefaultCatalogLanguage()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateOnePageDocument();

            try
            {
                PdfDocumentExporter.Save(document, path);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.Internals.Catalog.Elements.GetString("/Lang").Should().Be("en-US");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesRequestedCatalogLanguage()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateOnePageDocument();

            try
            {
                PdfDocumentExporter.Save(document, path, pdfLanguage: "uk-UA");

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.Internals.Catalog.Elements.GetString("/Lang").Should().Be("uk-UA");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesRequestedPageRange()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateDocument(pageCount: 3);

            try
            {
                PdfDocumentExporter.Save(document, path, null, new ExportPageRange(2, 2));

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.PageCount.Should().Be(1);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesRequestedBookmarksAndFiltersThemToPageRange()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateDocument(pageCount: 3);
            var bookmarks = new[]
            {
                new PdfBookmark("Summary", PageIndex: 0),
                new PdfBookmark("Details", PageIndex: 1),
                new PdfBookmark("Hidden", PageIndex: 2)
            };

            try
            {
                PdfDocumentExporter.Save(document, path, null, new ExportPageRange(2, 2), bookmarks: bookmarks);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.PageCount.Should().Be(1);
                pdf.Outlines.Count.Should().Be(1);
                pdf.Outlines[0].Title.Should().Be("Details");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_BookmarksRequestOutlineViewerMode()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateOnePageDocument();
            var bookmarks = new[] { new PdfBookmark("Summary", PageIndex: 0) };

            try
            {
                PdfDocumentExporter.Save(document, path, null, null, bookmarks: bookmarks);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.PageMode.Should().Be(PdfPageMode.UseOutlines);
                pdf.Internals.Catalog.Elements.GetName("/NonFullScreenPageMode")
                    .Should().Be("/UseOutlines");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_AppliesRequestedInitialViewAndOpenMode()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateOnePageDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    initialView: PdfInitialView.OneColumn,
                    openMode: PdfOpenMode.FullScreen);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                ReadPageLayout(pdf).Should().Be("/OneColumn");
                pdf.PageMode.Should().Be(PdfPageMode.FullScreen);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayWhenRequested()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateOnePageDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("FreeX PDF 1");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesVisualHostGeometryAsVectorContent()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateVectorGeometryDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                var content = ReadDecodedPageContent(pdf.Pages[0]);
                content.Should().Contain("15 72 m");
                content.Should().Contain("45 57 l");
                content.Should().Contain("B*");
                content.Should().Contain("1 0 0 rg");
                content.Should().Contain("0 0 1 RG");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_AppliesVisualHostRenderTransformsToVectorContent()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateTransformedVectorGeometryDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                var content = ReadDecodedPageContent(pdf.Pages[0]);
                content.Should().Contain("18 63 m");
                content.Should().Contain("48 40.5 l");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesLinearGradientGeometryAsPdfShading()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateGradientVectorGeometryDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var pdfBytes = Encoding.ASCII.GetString(File.ReadAllBytes(path));
                pdfBytes.Should().Contain("/ShadingType 2");
                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                var content = ReadDecodedPageContent(pdf.Pages[0]);
                content.Should().Contain("/Pattern cs");
                content.Should().Contain(" scn");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesLinkAnnotationsForPrintedWorksheetHyperlinks()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var workbook = new Workbook("Hyperlink annotation export");
            var sheet = workbook.AddSheet("Sheet1");
            var webAddress = new CellAddress(sheet.Id, 1, 1);
            var mailAddress = new CellAddress(sheet.Id, 2, 1);
            var bareMailAddress = new CellAddress(sheet.Id, 3, 1);
            var fileAddress = new CellAddress(sheet.Id, 4, 1);
            var uncAddress = new CellAddress(sheet.Id, 5, 1);
            sheet.SetCell(webAddress, new TextValue("Docs"));
            sheet.SetCell(mailAddress, new TextValue("Mail"));
            sheet.SetCell(bareMailAddress, new TextValue("Bare mail"));
            sheet.SetCell(fileAddress, new TextValue("File"));
            sheet.SetCell(uncAddress, new TextValue("Share"));
            sheet.Hyperlinks[webAddress] = "https://example.com/freex";
            sheet.Hyperlinks[mailAddress] = "mailto:review@example.com";
            sheet.Hyperlinks[bareMailAddress] = "bare@example.com";
            sheet.HyperlinkMetadata[bareMailAddress] = new HyperlinkMetadata(
                HyperlinkTargetKind.EmailAddress);
            sheet.Hyperlinks[fileAddress] = @"C:\Reports\Book 1.xlsx";
            sheet.Hyperlinks[uncAddress] = @"\\server\share\book.xlsx";
            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: false);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                ReadLinkAnnotationUris(pdf.Pages[0])
                    .Should()
                    .BeEquivalentTo(
                        "https://example.com/freex",
                        "mailto:review@example.com",
                        "mailto:bare@example.com",
                        "file:///C:/Reports/Book 1.xlsx",
                        "file://server/share/book.xlsx");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_FiltersLinkAnnotationsToRequestedPageRange()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var workbook = new Workbook("Hyperlink page range export");
            var sheet = workbook.AddSheet("Sheet1");
            var firstPageAddress = new CellAddress(sheet.Id, 1, 1);
            var secondPageAddress = new CellAddress(sheet.Id, 1, 25);
            sheet.SetCell(firstPageAddress, new TextValue("First"));
            sheet.SetCell(secondPageAddress, new TextValue("Second"));
            sheet.Hyperlinks[firstPageAddress] = "https://example.com/first";
            sheet.Hyperlinks[secondPageAddress] = "https://example.com/second";
            sheet.PrintArea = new GridRange(firstPageAddress, secondPageAddress);
            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            try
            {
                PdfDocumentExporter.Save(document, path, null, new ExportPageRange(2, 2));

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.PageCount.Should().Be(1);
                ReadLinkAnnotationUris(pdf.Pages[0])
                    .Should()
                    .Equal("https://example.com/second");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesLinkAnnotationRectInPdfCoordinates()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = new FixedDocument();
            var page = new FixedPage { Width = 200, Height = 100 };
            page.Children.Add(new VisualHost
            {
                LinkOverlays =
                [
                    new PdfLinkOverlay(
                        "https://example.com/rect",
                        HyperlinkTargetKind.ExistingFileOrWebPage,
                        X: 96,
                        Y: 24,
                        Width: 48,
                        Height: 12)
                ]
            });
            var content = new PageContent();
            ((System.Windows.Markup.IAddChild)content).AddChild(page);
            document.Pages.Add(content);
            document.DocumentPaginator.PageSize = new Size(200, 100);

            try
            {
                PdfDocumentExporter.Save(document, path);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                var rect = ReadLinkAnnotationRects(pdf.Pages[0]).Should().ContainSingle().Subject;
                rect.Should().Equal(72, 48, 108, 57);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesPrintableInvisibleLinkAnnotationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = new FixedDocument();
            var page = new FixedPage { Width = 200, Height = 100 };
            page.Children.Add(new VisualHost
            {
                LinkOverlays =
                [
                    new PdfLinkOverlay(
                        "https://example.com/metadata",
                        HyperlinkTargetKind.ExistingFileOrWebPage,
                        X: 12,
                        Y: 24,
                        Width: 48,
                        Height: 12)
                ]
            });
            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            document.Pages.Add(content);
            document.DocumentPaginator.PageSize = new Size(200, 100);

            try
            {
                PdfDocumentExporter.Save(document, path);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                var annotation = ReadLinkAnnotations(pdf.Pages[0]).Should().ContainSingle().Subject;
                annotation.Elements.GetName("/H").Should().Be("/I");
                annotation.Elements.GetInteger("/F").Should().Be(4);
                annotation.Elements.GetString("/Contents").Should().Be("https://example.com/metadata");
                ReadLinkAnnotationBorder(annotation).Should().Equal(0, 0, 0);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_ClampsLinkAnnotationRectToPdfPage()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = new FixedDocument();
            var page = new FixedPage { Width = 200, Height = 100 };
            page.Children.Add(new VisualHost
            {
                LinkOverlays =
                [
                    new PdfLinkOverlay(
                        "https://example.com/clamped",
                        HyperlinkTargetKind.ExistingFileOrWebPage,
                        X: -24,
                        Y: -12,
                        Width: 260,
                        Height: 140)
                ]
            });
            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            document.Pages.Add(content);
            document.DocumentPaginator.PageSize = new Size(200, 100);

            try
            {
                PdfDocumentExporter.Save(document, path);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                var rect = ReadLinkAnnotationRects(pdf.Pages[0]).Should().ContainSingle().Subject;
                rect.Should().Equal(0, 0, 150, 75);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_SkipsInternalWorkbookLinkAnnotations()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = new FixedDocument();
            var page = new FixedPage { Width = 200, Height = 100 };
            page.Children.Add(new VisualHost
            {
                LinkOverlays =
                [
                    new PdfLinkOverlay(
                        "Sheet2!A1",
                        HyperlinkTargetKind.PlaceInThisDocument,
                        X: 12,
                        Y: 24,
                        Width: 48,
                        Height: 12)
                ]
            });
            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            document.Pages.Add(content);
            document.DocumentPaginator.PageSize = new Size(200, 100);

            try
            {
                PdfDocumentExporter.Save(document, path);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                ReadLinkAnnotations(pdf.Pages[0]).Should().BeEmpty();
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_RejectsOutOfRangePageRangeWithoutCreatingFile()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateDocument(pageCount: 2);

            try
            {
                var action = () => PdfDocumentExporter.Save(document, path, null, new ExportPageRange(3, 3));

                action.Should().Throw<InvalidOperationException>()
                    .WithMessage(UiText.Format("Export_PageRangeStartsAfterLastPage", 2));
                File.Exists(path).Should().BeFalse();
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_RejectsPageRangeEndingAfterDocumentWithoutCreatingFile()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateDocument(pageCount: 2);

            try
            {
                var action = () => PdfDocumentExporter.Save(document, path, null, new ExportPageRange(1, 3));

                action.Should().Throw<InvalidOperationException>()
                    .WithMessage(UiText.Format("Export_PageRangeEndsAfterLastPage", 2));
                File.Exists(path).Should().BeFalse();
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WithoutRequestedPropertiesOnlyWritesProducerMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateOnePageDocument();

            try
            {
                PdfDocumentExporter.Save(document, path);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.Info.Title.Should().BeEmpty();
                pdf.Info.Author.Should().BeEmpty();
                pdf.Info.Subject.Should().BeEmpty();
                pdf.Info.Keywords.Should().BeEmpty();
                pdf.Info.Creator.Should().Be("FreeX");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_IgnoresBlankDocumentProperties()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateOnePageDocument();
            var properties = new PdfDocumentProperties(
                Title: " ",
                Author: null,
                Subject: "",
                Keywords: "\t");

            try
            {
                PdfDocumentExporter.Save(document, path, properties);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.Info.Title.Should().BeEmpty();
                pdf.Info.Author.Should().BeEmpty();
                pdf.Info.Subject.Should().BeEmpty();
                pdf.Info.Keywords.Should().BeEmpty();
                pdf.Info.Creator.Should().Be("FreeX");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_TrimsDocumentPropertiesBeforeWriting()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateOnePageDocument();
            var properties = new PdfDocumentProperties(
                Title: "  Quarterly Review  ",
                Author: "\tFinance Team\t",
                Subject: "  Workbook export",
                Keywords: "FreeX, spreadsheet  ");

            try
            {
                PdfDocumentExporter.Save(document, path, properties);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.Info.Title.Should().Be("Quarterly Review");
                pdf.Info.Author.Should().Be("Finance Team");
                pdf.Info.Subject.Should().Be("Workbook export");
                pdf.Info.Keywords.Should().Be("FreeX, spreadsheet");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentProperties_FromWorkbook_ReturnsNullUnlessOptionIsRequested()
    {
        var workbook = new Workbook("Budget Model");

        PdfDocumentProperties.FromWorkbook(workbook, ExportOptions.ExcelLikeDefault)
            .Should().BeNull();

        PdfDocumentProperties.FromWorkbook(
                workbook,
                new ExportOptions(
                    ExportContentScope.ActiveSheet,
                    IncludeDocumentProperties: true,
                    OpenAfterPublish: false))
            .Should().Be(new PdfDocumentProperties(
                Title: "Budget Model",
                Author: "FreeX",
                Subject: "FreeX workbook export",
                Keywords: "FreeX, spreadsheet"));
    }

    [Fact]
    public void PdfDocumentProperties_FromWorkbook_UsesWorkbookUserNameWhenAvailable()
    {
        var workbook = new Workbook("Budget Model")
        {
            FileSharing = new WorkbookFileSharingModel
            {
                UserName = "  Analyst  "
            }
        };

        PdfDocumentProperties.FromWorkbook(
                workbook,
                new ExportOptions(
                    ExportContentScope.ActiveSheet,
                    IncludeDocumentProperties: true,
                    OpenAfterPublish: false))
            .Should().Be(new PdfDocumentProperties(
                Title: "Budget Model",
                Author: "Analyst",
                Subject: "FreeX workbook export",
                Keywords: "FreeX, spreadsheet"));
    }

    [Fact]
    public void XpsDocumentProperties_ApplyToPackageProperties_WhenOptionIsRequested()
    {
        var workbook = new Workbook("Budget Model");
        using var stream = new MemoryStream();
        using var package = System.IO.Packaging.Package.Open(stream, FileMode.Create, FileAccess.ReadWrite);

        XpsDocumentProperties.ApplyToPackage(
            package,
            XpsDocumentProperties.FromWorkbook(
                workbook,
                new ExportOptions(
                    ExportContentScope.ActiveSheet,
                    IncludeDocumentProperties: true,
                    OpenAfterPublish: false)));

        package.PackageProperties.Title.Should().Be("Budget Model");
        package.PackageProperties.Creator.Should().Be("FreeX");
        package.PackageProperties.Subject.Should().Be("FreeX workbook export");
        package.PackageProperties.Keywords.Should().Be("FreeX, spreadsheet");
    }

    [Fact]
    public void XpsDocumentProperties_FromWorkbook_UsesWorkbookUserNameWhenAvailable()
    {
        var workbook = new Workbook("Budget Model")
        {
            FileSharing = new WorkbookFileSharingModel
            {
                UserName = "  Analyst  "
            }
        };
        using var stream = new MemoryStream();
        using var package = System.IO.Packaging.Package.Open(stream, FileMode.Create, FileAccess.ReadWrite);

        XpsDocumentProperties.ApplyToPackage(
            package,
            XpsDocumentProperties.FromWorkbook(
                workbook,
                new ExportOptions(
                    ExportContentScope.ActiveSheet,
                    IncludeDocumentProperties: true,
                    OpenAfterPublish: false)));

        package.PackageProperties.Creator.Should().Be("Analyst");
    }

    [Fact]
    public void XpsDocumentProperties_TrimsAndSkipsBlankPackageProperties()
    {
        using var stream = new MemoryStream();
        using var package = System.IO.Packaging.Package.Open(stream, FileMode.Create, FileAccess.ReadWrite);

        XpsDocumentProperties.ApplyToPackage(
            package,
            new XpsDocumentProperties(
                Title: "  Quarterly Review  ",
                Creator: "\tFinance Team\t",
                Subject: "   ",
                Keywords: "FreeX, spreadsheet  "));

        package.PackageProperties.Title.Should().Be("Quarterly Review");
        package.PackageProperties.Creator.Should().Be("Finance Team");
        package.PackageProperties.Subject.Should().BeNull();
        package.PackageProperties.Keywords.Should().Be("FreeX, spreadsheet");
    }

    private static FixedDocument CreateOnePageDocument()
        => CreateDocument(pageCount: 1);

    private static IReadOnlyList<string> ReadLinkAnnotationUris(PdfPage page)
    {
        var uris = new List<string>();
        foreach (var annotation in ReadLinkAnnotations(page))
        {
            var action = annotation.Elements.GetDictionary("/A");
            action.Should().NotBeNull();
            action!.Elements.GetName("/S").Should().Be("/URI");
            uris.Add(action.Elements.GetString("/URI"));
        }

        return uris;
    }

    private static IReadOnlyList<double[]> ReadLinkAnnotationRects(PdfPage page)
    {
        var rects = new List<double[]>();
        foreach (var annotation in ReadLinkAnnotations(page))
        {
            var rect = annotation.Elements.GetArray("/Rect");
            rect.Should().NotBeNull();
            rects.Add([
                rect!.Elements.GetReal(0),
                rect.Elements.GetReal(1),
                rect.Elements.GetReal(2),
                rect.Elements.GetReal(3)
            ]);
        }

        return rects;
    }

    private static IReadOnlyList<PdfDictionary> ReadLinkAnnotations(PdfPage page)
    {
        var annotations = page.Elements.GetArray("/Annots");
        if (annotations is null)
            return [];

        var result = new List<PdfDictionary>();
        foreach (var item in annotations.Elements)
        {
            var annotation = ResolveDictionary(item);
            if (annotation is not null && annotation.Elements.GetName("/Subtype") == "/Link")
                result.Add(annotation);
        }

        return result;
    }

    private static IReadOnlyList<int> ReadLinkAnnotationBorder(PdfDictionary annotation)
    {
        var border = annotation.Elements.GetArray("/Border");
        border.Should().NotBeNull();
        return [
            border!.Elements.GetInteger(0),
            border.Elements.GetInteger(1),
            border.Elements.GetInteger(2)
        ];
    }

    private static string ReadDecodedPageContent(PdfPage page)
    {
        var builder = new StringBuilder();
        foreach (var content in page.Contents)
        {
            if (content.Stream?.Value is { } bytes)
                builder.Append(Encoding.ASCII.GetString(bytes));
        }

        return builder.ToString();
    }

    private static PdfDictionary? ResolveDictionary(PdfItem item)
    {
        return item switch
        {
            PdfDictionary dictionary => dictionary,
            PdfReference reference => reference.Value as PdfDictionary,
            _ => null
        };
    }

    private static FixedDocument CreateVectorGeometryDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(160, 120);
        var page = new FixedPage
        {
            Width = 160,
            Height = 120,
            Background = Brushes.White
        };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawGeometry(
                Brushes.Red,
                new System.Windows.Media.Pen(Brushes.Blue, 2),
                new RectangleGeometry(new Rect(8, 10, 40, 20)));
        }

        var host = new VisualHost { Visual = visual };
        Canvas.SetLeft(host, 12);
        Canvas.SetTop(host, 14);
        page.Children.Add(host);

        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateTransformedVectorGeometryDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(160, 120);
        var page = new FixedPage
        {
            Width = 160,
            Height = 120,
            Background = Brushes.White
        };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawGeometry(
                Brushes.Green,
                null,
                new RectangleGeometry(new Rect(10, 10, 20, 10)));
        }

        var host = new VisualHost
        {
            Visual = visual,
            RenderTransform = new ScaleTransform(2, 3)
        };
        Canvas.SetLeft(host, 4);
        Canvas.SetTop(host, 6);
        page.Children.Add(host);

        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateGradientVectorGeometryDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(160, 120);
        var page = new FixedPage
        {
            Width = 160,
            Height = 120,
            Background = Brushes.White
        };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new LinearGradientBrush(
                Colors.Red,
                Colors.Blue,
                new Point(0, 0),
                new Point(1, 0));
            dc.DrawGeometry(
                brush,
                null,
                new RectangleGeometry(new Rect(8, 10, 40, 20)));
        }

        var host = new VisualHost { Visual = visual };
        Canvas.SetLeft(host, 12);
        Canvas.SetTop(host, 14);
        page.Children.Add(host);

        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateDocument(int pageCount)
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(160, 120);
        for (var i = 0; i < pageCount; i++)
        {
            var page = new FixedPage
            {
                Width = 160,
                Height = 120,
                Background = Brushes.White
            };
            page.Children.Add(new TextBlock { Text = $"FreeX PDF {i + 1}", Margin = new System.Windows.Thickness(12) });
            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            document.Pages.Add(content);
        }

        return document;
    }

    private static bool ReadDisplayDocTitle(PdfDocument pdf) =>
        pdf.Internals.Catalog.Elements
            .GetDictionary("/ViewerPreferences")
            ?.Elements.GetBoolean("/DisplayDocTitle", false) == true;

    private static string? ReadPrintScaling(PdfDocument pdf) =>
        pdf.Internals.Catalog.Elements
            .GetDictionary("/ViewerPreferences")
            ?.Elements.GetName("/PrintScaling");

    private static bool ReadViewerPreference(PdfDocument pdf, string key) =>
        pdf.Internals.Catalog.Elements
            .GetDictionary("/ViewerPreferences")
            ?.Elements.GetBoolean(key, false) == true;

    private static string? ReadPageLayout(PdfDocument pdf) =>
        pdf.Internals.Catalog.Elements.GetName("/PageLayout");
}
