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

namespace FreeX.App.Host.Tests;

public partial class ExportPlannerTests
{
    [Fact]
    public void PdfLinkOverlayExtractor_IncludesRenderTranslationTransformsButNotLayoutTranslation()
    {
        StaTestRunner.Run(() =>
        {
            var page = new FixedPage { Width = 180, Height = 120 };
            var container = new Canvas
            {
                LayoutTransform = new TranslateTransform(100, 200),
                RenderTransform = new TranslateTransform(3, 4)
            };
            Canvas.SetLeft(container, 10);
            Canvas.SetTop(container, 20);

            var linkTransform = new TransformGroup();
            linkTransform.Children.Add(new TranslateTransform(7, 8));
            linkTransform.Children.Add(new MatrixTransform(new Matrix(1, 0, 0, 1, 11, 13)));
            var panel = new Canvas
            {
                Margin = new System.Windows.Thickness(5, 6, 0, 0),
                RenderTransform = linkTransform
            };
            var host = new VisualHost
            {
                LinkOverlays =
                [
                    new PdfLinkOverlay(
                        "https://example.com/translated",
                        HyperlinkTargetKind.ExistingFileOrWebPage,
                        X: 2,
                        Y: 3,
                        Width: 20,
                        Height: 10)
                ]
            };

            panel.Children.Add(host);
            container.Children.Add(panel);
            page.Children.Add(container);

            var overlay = PdfLinkOverlayExtractor.Extract(page).Should().ContainSingle().Subject;
            overlay.Target.Should().Be("https://example.com/translated");
            overlay.X.Should().Be(38);
            overlay.Y.Should().Be(54);
            overlay.Width.Should().Be(20);
            overlay.Height.Should().Be(10);
        });
    }

    [Fact]
    public void PdfTextOverlayExtractor_IncludesRenderTranslationTransformsButNotLayoutTranslation()
    {
        StaTestRunner.Run(() =>
        {
            var page = new FixedPage { Width = 180, Height = 120 };
            var container = new Canvas
            {
                LayoutTransform = new TranslateTransform(100, 200),
                RenderTransform = new TranslateTransform(3, 4)
            };
            Canvas.SetLeft(container, 10);
            Canvas.SetTop(container, 20);

            var textTransform = new TransformGroup();
            textTransform.Children.Add(new TranslateTransform(7, 8));
            textTransform.Children.Add(new MatrixTransform(new Matrix(1, 0, 0, 1, 11, 13)));
            var text = new TextBlock
            {
                Text = "Translated PDF Text",
                Margin = new System.Windows.Thickness(5, 6, 0, 0),
                RenderTransform = textTransform
            };

            container.Children.Add(text);
            page.Children.Add(container);

            var overlay = PdfTextOverlayExtractor.Extract(page).Should().ContainSingle().Subject;
            overlay.Text.Should().Be("Translated PDF Text");
            overlay.X.Should().Be(36);
            overlay.Y.Should().Be(51);
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForPrintedWorksheetCells()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var workbook = new Workbook("Selectable worksheet export");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Worksheet Cell PDF Text"));
            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Worksheet Cell PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForPrintedCharts()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var workbook = new Workbook("Selectable chart export");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 30, 30), new TextValue("Month"));
            sheet.SetCell(new CellAddress(sheet.Id, 30, 31), new TextValue("PDF Rev"));
            sheet.SetCell(new CellAddress(sheet.Id, 31, 30), new TextValue("PDF tick Jan"));
            sheet.SetCell(new CellAddress(sheet.Id, 31, 31), new NumberValue(8));
            sheet.SetCell(new CellAddress(sheet.Id, 32, 30), new TextValue("PDF tick Feb"));
            sheet.SetCell(new CellAddress(sheet.Id, 32, 31), new NumberValue(14));
            sheet.SetCell(new CellAddress(sheet.Id, 33, 30), new TextValue("PDF tick Mar"));
            sheet.SetCell(new CellAddress(sheet.Id, 33, 31), new NumberValue(11));
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 20, 8));
            sheet.Charts.Add(new ChartModel
            {
                Type = ChartType.Column,
                DataRange = new GridRange(
                    new CellAddress(sheet.Id, 30, 30),
                    new CellAddress(sheet.Id, 33, 31)),
                Title = "Chart Title PDF Text",
                XAxisTitle = "Month Axis PDF Text",
                YAxisTitle = "Sales Axis PDF Text",
                Left = 24,
                Top = 24,
                Width = 380,
                Height = 210,
                ShowLegend = true,
                LegendPosition = ChartLegendPosition.Right,
                YAxisMinimum = 0,
                YAxisMaximum = 20,
                YAxisMajorUnit = 10,
                YAxisNumberFormat = ChartDataLabelNumberFormat.Currency,
                ShowDataLabels = true,
                ShowDataLabelCategoryName = true,
                ShowDataLabelValue = true
            });
            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var pdfText = Encoding.ASCII.GetString(File.ReadAllBytes(path));
                pdfText.Should().Contain("Chart Title PDF Text");
                pdfText.Should().Contain("Month Axis PDF Text");
                pdfText.Should().Contain("Sales Axis PDF Text");
                pdfText.Should().Contain("PDF Rev");
                pdfText.Should().Contain("PDF tick Jan");
                pdfText.Should().Contain("$10.00");
                pdfText.Should().Contain("PDF tick Jan, 8");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForPrintedHeaderFooter()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var workbook = new Workbook("HeaderFooterExport.xlsx");
            var sheet = workbook.AddSheet("Summary");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Worksheet cell"));
            sheet.PageHeader = new WorksheetHeaderFooter(
                "Header left &[Page]",
                "Header center &[Pages]",
                "Header right &[File] &[Picture]");
            sheet.PageFooter = new WorksheetHeaderFooter(
                "Footer left &[Tab]",
                "Footer center",
                $"{new string('x', 300)} hidden-tail-token");
            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var pdfText = Encoding.ASCII.GetString(File.ReadAllBytes(path));
                pdfText.Should().Contain("Header left 1");
                pdfText.Should().Contain("Header center 1");
                pdfText.Should().Contain("Header right HeaderFooterExport.xlsx");
                pdfText.Should().Contain("Footer left Summary");
                pdfText.Should().Contain("Footer center");
                pdfText.Should().Contain(new string('x', 10));
                pdfText.Should().NotContain("hidden-tail-token");
                pdfText.Should().NotContain("&[Picture]");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_DoesNotWriteHiddenClippedWorksheetCellText()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var workbook = new Workbook("Clipped worksheet export");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(
                new CellAddress(sheet.Id, 1, 1),
                new TextValue("visible prefix worksheet text hidden-tail-token"));
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 12));
            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var pdfText = Encoding.ASCII.GetString(File.ReadAllBytes(path));
                pdfText.Should().Contain("visible");
                pdfText.Should().NotContain("hidden-tail-token");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForPrintedWorkbookCells()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var workbook = new Workbook("Selectable workbook export");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Workbook Cell PDF Text"));
            var document = PrintRenderer.RenderWorkbook(workbook, new ViewportService());

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Workbook Cell PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForDisplayedComments()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var workbook = new Workbook("Selectable displayed comments export");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new TextValue("Anchor"));
            sheet.Comments[a1] = "Displayed Comment PDF Text";
            sheet.PrintComments = WorksheetPrintComments.AsDisplayed;
            var document = PrintRenderer.RenderWorkbook(workbook, new ViewportService());

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Displayed Comment PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForNestedTextBlocks()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateNestedTextDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Nested PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForInlineTextBlocks()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateInlineTextDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Inline PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForNestedInlineTextBlocks()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateNestedInlineTextDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Nested Inline PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForInlineUiContainerText()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateInlineUiContainerTextDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Inline UI PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForNestedInlineUiContainerText()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateNestedInlineUiContainerTextDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var pdfText = Encoding.ASCII.GetString(File.ReadAllBytes(path));
                pdfText.Should().Contain("Nested Inline UI PDF Text");
                pdfText.Should().Contain(@"Inline Header\nInline Body");
                pdfText.Should().Contain(@"First Item\nSecond Item");
                pdfText.Should().NotContain("Hidden Inline UI Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForAccessText()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateAccessTextDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Publish PDF");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForTextBoxes()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateTextBoxDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Textbox PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForStringContentControls()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateStringContentControlDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Label PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForObjectContentControls()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateObjectContentControlDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("12345");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForHeaderedContentControls()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateHeaderedContentControlDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Header PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForHeaderedContentControlObjectHeaders()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateObjectHeaderedContentControlDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("67890");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForHeaderedContentControlHeaderElements()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateHeaderElementContentControlDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Element Header PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForItemsControlStringItems()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateStringItemsControlDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Item PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForItemsControlObjectItems()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateObjectItemsControlDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("24680");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForComboBoxSelectedItem()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateComboBoxDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                var pdfText = Encoding.ASCII.GetString(bytes);
                pdfText.Should().Contain("Selected PDF Text");
                pdfText.Should().NotContain("Unselected PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_DoesNotWriteSelectableTextOverlayForClosedComboBoxUnselectedItems()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateUnselectedComboBoxDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().NotContain("Hidden Dropdown PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForGlyphs()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateGlyphsDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    null,
                    null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Glyph PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_DoesNotWriteSelectableTextOverlayForHiddenText()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateHiddenTextDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    properties: null,
                    pageRange: null,
                    includeSelectableText: true);

                var pdfText = Encoding.ASCII.GetString(File.ReadAllBytes(path));
                pdfText.Should().Contain("Visible PDF Text");
                pdfText.Should().NotContain("Hidden PDF Text");
                pdfText.Should().NotContain("Collapsed PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForHeaderedContentControlHeaderAndContent()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateHeaderedContentControlHeaderAndContentDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    properties: null,
                    pageRange: null,
                    includeSelectableText: true);

                var pdfText = Encoding.ASCII.GetString(File.ReadAllBytes(path));
                pdfText.Should().Contain("Header Body PDF Text");
                pdfText.Should().Contain("Header Title PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForItemsControlElementItems()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateElementItemsControlDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    properties: null,
                    pageRange: null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Element Item PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForRichTextBoxes()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateRichTextBoxDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    properties: null,
                    pageRange: null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Rich PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PdfDocumentExporter_WritesSelectableTextOverlayForFlowDocumentViewers()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateFlowDocumentViewerDocument();

            try
            {
                PdfDocumentExporter.Save(
                    document,
                    path,
                    properties: null,
                    pageRange: null,
                    includeSelectableText: true);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes).Should().Contain("Flow PDF Text");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    private static FixedDocument CreateNestedTextDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(160, 120);
        var page = new FixedPage
        {
            Width = 160,
            Height = 120,
            Background = Brushes.White
        };
        page.Children.Add(new Border
        {
            Margin = new System.Windows.Thickness(12),
            Child = new TextBlock { Text = "Nested PDF Text" }
        });
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateInlineTextDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(160, 120);
        var page = new FixedPage
        {
            Width = 160,
            Height = 120,
            Background = Brushes.White
        };
        var text = new TextBlock { Margin = new System.Windows.Thickness(12) };
        text.Inlines.Add(new Run("Inline "));
        text.Inlines.Add(new Run("PDF Text"));
        page.Children.Add(text);
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateNestedInlineTextDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(180, 120);
        var page = new FixedPage
        {
            Width = 180,
            Height = 120,
            Background = Brushes.White
        };
        var text = new TextBlock { Margin = new System.Windows.Thickness(12) };
        text.Inlines.Add(new Run("Nested "));
        text.Inlines.Add(new Bold(new Run("Inline ")));
        text.Inlines.Add(new Italic(new Run("PDF Text")));
        page.Children.Add(text);
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateInlineUiContainerTextDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(190, 120);
        var page = new FixedPage
        {
            Width = 190,
            Height = 120,
            Background = Brushes.White
        };
        var text = new TextBlock { Margin = new System.Windows.Thickness(12) };
        text.Inlines.Add(new Run("Inline "));
        text.Inlines.Add(new InlineUIContainer(new TextBlock { Text = "UI " }));
        text.Inlines.Add(new Run("PDF Text"));
        page.Children.Add(text);
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateNestedInlineUiContainerTextDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(230, 120);
        var page = new FixedPage
        {
            Width = 230,
            Height = 120,
            Background = Brushes.White
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Nested " });
        panel.Children.Add(new Border { Child = new TextBlock { Text = "Inline UI PDF Text" } });
        panel.Children.Add(new HeaderedContentControl
        {
            Header = "Inline Header",
            Content = new TextBlock { Text = "Inline Body" }
        });
        panel.Children.Add(new ListBox
        {
            Items =
            {
                new TextBlock { Text = "First Item" },
                new TextBlock { Text = "Second Item" }
            }
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Hidden Inline UI Text",
            Visibility = System.Windows.Visibility.Collapsed
        });
        var text = new TextBlock { Margin = new System.Windows.Thickness(12) };
        text.Inlines.Add(new InlineUIContainer(new Border { Child = panel }));
        page.Children.Add(text);
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateAccessTextDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(160, 120);
        var page = new FixedPage
        {
            Width = 160,
            Height = 120,
            Background = Brushes.White
        };
        page.Children.Add(new AccessText { Text = "_Publish PDF", Margin = new System.Windows.Thickness(12) });
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateTextBoxDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(160, 120);
        var page = new FixedPage
        {
            Width = 160,
            Height = 120,
            Background = Brushes.White
        };
        page.Children.Add(new TextBox
        {
            Text = "Textbox PDF Text",
            Margin = new System.Windows.Thickness(12),
            BorderThickness = new System.Windows.Thickness(0)
        });
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateStringContentControlDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(160, 120);
        var page = new FixedPage
        {
            Width = 160,
            Height = 120,
            Background = Brushes.White
        };
        page.Children.Add(new Label
        {
            Content = "Label PDF Text",
            Margin = new System.Windows.Thickness(12),
            Padding = new System.Windows.Thickness(0)
        });
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateObjectContentControlDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(160, 120);
        var page = new FixedPage
        {
            Width = 160,
            Height = 120,
            Background = Brushes.White
        };
        page.Children.Add(new Label
        {
            Content = 12345,
            Margin = new System.Windows.Thickness(12),
            Padding = new System.Windows.Thickness(0)
        });
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateHeaderedContentControlDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(180, 120);
        var page = new FixedPage
        {
            Width = 180,
            Height = 120,
            Background = Brushes.White
        };
        page.Children.Add(new GroupBox
        {
            Header = "Header PDF Text",
            Content = "",
            Margin = new System.Windows.Thickness(12),
            Padding = new System.Windows.Thickness(0)
        });
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateObjectHeaderedContentControlDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(180, 120);
        var page = new FixedPage
        {
            Width = 180,
            Height = 120,
            Background = Brushes.White
        };
        page.Children.Add(new GroupBox
        {
            Header = 67890,
            Content = "",
            Margin = new System.Windows.Thickness(12),
            Padding = new System.Windows.Thickness(0)
        });
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateHeaderElementContentControlDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(220, 120);
        var page = new FixedPage
        {
            Width = 220,
            Height = 120,
            Background = Brushes.White
        };
        page.Children.Add(new GroupBox
        {
            Header = new TextBlock { Text = "Element Header PDF Text" },
            Content = "",
            Margin = new System.Windows.Thickness(12),
            Padding = new System.Windows.Thickness(0)
        });
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateStringItemsControlDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(180, 120);
        var page = new FixedPage
        {
            Width = 180,
            Height = 120,
            Background = Brushes.White
        };
        var items = new ListBox
        {
            Margin = new System.Windows.Thickness(12),
            BorderThickness = new System.Windows.Thickness(0)
        };
        items.Items.Add("Item PDF Text");
        page.Children.Add(items);
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateObjectItemsControlDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(180, 120);
        var page = new FixedPage
        {
            Width = 180,
            Height = 120,
            Background = Brushes.White
        };
        var items = new ListBox
        {
            Margin = new System.Windows.Thickness(12),
            BorderThickness = new System.Windows.Thickness(0)
        };
        items.Items.Add(24680);
        page.Children.Add(items);
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateComboBoxDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(180, 120);
        var page = new FixedPage
        {
            Width = 180,
            Height = 120,
            Background = Brushes.White
        };
        var comboBox = new ComboBox
        {
            Margin = new System.Windows.Thickness(12),
            BorderThickness = new System.Windows.Thickness(0),
            SelectedIndex = 1
        };
        comboBox.Items.Add("Unselected PDF Text");
        comboBox.Items.Add("Selected PDF Text");
        page.Children.Add(comboBox);
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateUnselectedComboBoxDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(180, 120);
        var page = new FixedPage
        {
            Width = 180,
            Height = 120,
            Background = Brushes.White
        };
        var comboBox = new ComboBox
        {
            Margin = new System.Windows.Thickness(12),
            BorderThickness = new System.Windows.Thickness(0),
            SelectedIndex = -1
        };
        comboBox.Items.Add("Hidden Dropdown PDF Text");
        page.Children.Add(comboBox);
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateGlyphsDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(160, 120);
        var page = new FixedPage
        {
            Width = 160,
            Height = 120,
            Background = Brushes.White
        };
        page.Children.Add(new Glyphs
        {
            UnicodeString = "Glyph PDF Text",
            FontRenderingEmSize = 12,
            Fill = Brushes.Black,
            Margin = new System.Windows.Thickness(12)
        });
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateHiddenTextDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(200, 120);
        var page = new FixedPage
        {
            Width = 200,
            Height = 120,
            Background = Brushes.White
        };
        var stack = new StackPanel { Margin = new System.Windows.Thickness(12) };
        stack.Children.Add(new TextBlock { Text = "Visible PDF Text" });
        stack.Children.Add(new TextBlock { Text = "Hidden PDF Text", Visibility = System.Windows.Visibility.Hidden });
        stack.Children.Add(new TextBlock { Text = "Collapsed PDF Text", Visibility = System.Windows.Visibility.Collapsed });
        page.Children.Add(stack);
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateHeaderedContentControlHeaderAndContentDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(220, 120);
        var page = new FixedPage
        {
            Width = 220,
            Height = 120,
            Background = Brushes.White
        };
        page.Children.Add(new GroupBox
        {
            Header = "Header Title PDF Text",
            Content = "Header Body PDF Text",
            Margin = new System.Windows.Thickness(12),
            Padding = new System.Windows.Thickness(0)
        });
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateElementItemsControlDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(220, 120);
        var page = new FixedPage
        {
            Width = 220,
            Height = 120,
            Background = Brushes.White
        };
        var items = new ListBox
        {
            Margin = new System.Windows.Thickness(12),
            BorderThickness = new System.Windows.Thickness(0)
        };
        items.Items.Add(new TextBlock { Text = "Element Item PDF Text" });
        page.Children.Add(items);
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateRichTextBoxDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(180, 120);
        var page = new FixedPage
        {
            Width = 180,
            Height = 120,
            Background = Brushes.White
        };
        var richText = new RichTextBox
        {
            Document = new FlowDocument(new Paragraph(new Run("Rich PDF Text"))),
            Margin = new System.Windows.Thickness(12),
            BorderThickness = new System.Windows.Thickness(0),
            Padding = new System.Windows.Thickness(0)
        };
        page.Children.Add(richText);
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    private static FixedDocument CreateFlowDocumentViewerDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(180, 120);
        var page = new FixedPage
        {
            Width = 180,
            Height = 120,
            Background = Brushes.White
        };
        page.Children.Add(new FlowDocumentScrollViewer
        {
            Document = new FlowDocument(new Paragraph(new Run("Flow PDF Text"))),
            Margin = new System.Windows.Thickness(12),
            Padding = new System.Windows.Thickness(0)
        });
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }
}
