using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using FluentAssertions;
using Freexcel.Core.Model;
using PdfSharp.Pdf.IO;

namespace Freexcel.App.Host.Tests;

public class ExportPlannerTests
{
    [Theory]
    [InlineData(@"C:\temp\book.xps", "xps")]
    [InlineData(@"C:\temp\book.XPS", "xps")]
    [InlineData(@"C:\temp\book.pdf", "pdf")]
    [InlineData(@"C:\temp\book", "pdf")]
    [InlineData(@"C:\temp\book.export", "pdf")]
    public void InferExportFormat_UsesXpsOnlyForXpsExtension(string path, string expectedFormat)
    {
        var expected = expectedFormat == "xps"
            ? ExportFormat.Xps
            : ExportFormat.Pdf;

        ExportPlanner.InferExportFormat(path).Should().Be(expected);
    }

    [Fact]
    public void PlanExport_CarriesInferredFormatWithRequestedPath()
    {
        var request = ExportPlanner.PlanExport(@"C:\temp\report.pdf");

        request.Should().Be(new ExportRequest(
            @"C:\temp\report.pdf",
            ExportFormat.Pdf,
            ExportOptions.ExcelLikeDefault,
            null));
        request.UsesXpsFallback.Should().BeFalse();
        request.ActualPath.Should().Be(@"C:\temp\report.pdf");
    }

    [Fact]
    public void PlanExport_AppendsPdfExtensionForExtensionlessPdfRequests()
    {
        var request = ExportPlanner.PlanExport(@"C:\temp\report");

        request.Should().Be(new ExportRequest(
            @"C:\temp\report.pdf",
            ExportFormat.Pdf,
            ExportOptions.ExcelLikeDefault,
            null));
        request.ActualPath.Should().Be(@"C:\temp\report.pdf");
    }

    [Fact]
    public void PlanExport_XpsRequestKeepsRequestedPathAndDoesNotUseFallback()
    {
        var request = ExportPlanner.PlanExport(@"C:\temp\report.xps");

        request.Should().Be(new ExportRequest(
            @"C:\temp\report.xps",
            ExportFormat.Xps,
            ExportOptions.ExcelLikeDefault,
            null));
        request.UsesXpsFallback.Should().BeFalse();
        request.ActualPath.Should().Be(@"C:\temp\report.xps");
    }

    [Fact]
    public void ExportOptions_DefaultsToActiveSheetWithoutDocumentProperties()
    {
        ExportOptions.ExcelLikeDefault.Should().Be(new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false));

        ExportPlanner.DescribeOptions(ExportOptions.ExcelLikeDefault)
            .Should().Be("Active sheet only; document properties are not included.");
    }

    [Fact]
    public void ExportOptions_DescribeSelectionAndOpenAfterPublish()
    {
        var options = new ExportOptions(
            ExportContentScope.Selection,
            IncludeDocumentProperties: true,
            OpenAfterPublish: true,
            PageRange: new ExportPageRange(2, 4));

        ExportPlanner.DescribeOptions(options)
            .Should().Be("Selection; pages 2-4; document properties are included; open after publishing.");
    }

    [Fact]
    public void ExportOptions_DescribeWithXpsFormatIncludesDocumentProperties()
    {
        var options = new ExportOptions(
            ExportContentScope.Selection,
            IncludeDocumentProperties: true,
            OpenAfterPublish: true);

        ExportPlanner.DescribeOptions(options, ExportFormat.Xps)
            .Should().Be("Selection; document properties are included; open after publishing.");
    }

    [Fact]
    public void ExportOptions_DescribeEntireWorkbook()
    {
        var options = new ExportOptions(
            ExportContentScope.EntireWorkbook,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false);

        ExportPlanner.DescribeOptions(options)
            .Should().Be("Entire workbook; document properties are not included.");
    }

    [Fact]
    public void ExportOptionsDialog_CreateResult_NormalizesExcelOptions()
    {
        ExportOptionsDialog.CreateResult(
                ExportContentScope.EntireWorkbook,
                includeDocumentProperties: true,
                openAfterPublish: true,
                pageRange: new ExportPageRange(3, 3))
            .Should()
            .Be(new ExportOptions(
                ExportContentScope.EntireWorkbook,
                IncludeDocumentProperties: true,
                OpenAfterPublish: true,
                PageRange: new ExportPageRange(3, 3)));
    }

    [Fact]
    public void ExportOptionsDialog_ExposesKeyboardAccessKeys()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ExportOptionsDialog.cs"));

        foreach (var expected in new[]
        {
            "Content = \"Active _sheet(s)\"",
            "Content = \"Selected _range\"",
            "Content = \"_Workbook\"",
            "Content = \"_Include document properties\"",
            "Content = \"_Open after publishing\"",
            "Content = \"_Pages from\"",
            "Target = _fromPageBox",
            "Content = \"t_o\"",
            "Target = _toPageBox",
            "Content = \"_OK\"",
            "Content = \"_Cancel\""
        })
            source.Should().Contain(expected);

        source.Should().NotContain("_Ignore print areas");
        source.Should().NotContain("_Create bookmarks using sheet names");
        source.Should().NotContain("CSV _delimiter:");
    }

    [Theory]
    [InlineData("", "", true, null, null)]
    [InlineData("2", "4", true, 2, 4)]
    [InlineData("3", "3", true, 3, 3)]
    [InlineData("0", "3", false, null, null)]
    [InlineData("4", "2", false, null, null)]
    [InlineData("x", "2", false, null, null)]
    [InlineData("2", "", false, null, null)]
    public void TryCreatePageRange_ValidatesOptionalOneBasedPageRange(
        string fromText,
        string toText,
        bool expectedSuccess,
        int? expectedFrom,
        int? expectedTo)
    {
        var success = ExportPlanner.TryCreatePageRange(fromText, toText, out var range, out var error);

        success.Should().Be(expectedSuccess);
        if (expectedSuccess && expectedFrom is not null && expectedTo is not null)
        {
            range.Should().Be(new ExportPageRange(expectedFrom.Value, expectedTo.Value));
            error.Should().BeNull();
        }
        else if (expectedSuccess)
        {
            range.Should().BeNull();
            error.Should().BeNull();
        }
        else
        {
            range.Should().BeNull();
            error.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [InlineData(null, 3, true, null)]
    [InlineData(1, 3, true, null)]
    [InlineData(3, 3, true, null)]
    [InlineData(4, 3, false, "Page range starts after the last exportable page (3).")]
    [InlineData(1, 0, false, "There are no exportable pages.")]
    public void TryValidatePageRange_ChecksRenderedPageCount(
        int? fromPage,
        int pageCount,
        bool expectedSuccess,
        string? expectedError)
    {
        var pageRange = fromPage is null ? null : new ExportPageRange(fromPage.Value, fromPage.Value);

        var success = ExportPlanner.TryValidatePageRange(pageRange, pageCount, out var error);

        success.Should().Be(expectedSuccess);
        error.Should().Be(expectedError);
    }

    [Fact]
    public void DescribeRequest_ExplainsPdfFallbackAndSupportedOptions()
    {
        var request = ExportPlanner.PlanExport(@"C:\temp\report.pdf");

        ExportPlanner.DescribeRequest(request).Should().Be(
            "Options: Active sheet only; document properties are not included.");
    }

    [Fact]
    public void DescribeRequest_ForXpsIncludesDocumentProperties()
    {
        var request = ExportPlanner.PlanExport(
            @"C:\temp\report.xps",
            new ExportOptions(
                ExportContentScope.ActiveSheet,
                IncludeDocumentProperties: true,
                OpenAfterPublish: false));

        ExportPlanner.DescribeRequest(request).Should().Be(
            "Options: Active sheet only; document properties are included.");
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
            "PDF export uses Freexcel's print renderer. XPS export remains available for Windows print-pipeline workflows.");
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
                Keywords: "Freexcel, spreadsheet");

            try
            {
                PdfDocumentExporter.Save(document, path, properties);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.Info.Title.Should().Be("Quarterly Review");
                pdf.Info.Author.Should().Be("Finance Team");
                pdf.Info.Subject.Should().Be("Workbook export");
                pdf.Info.Keywords.Should().Be("Freexcel, spreadsheet");
                pdf.Info.Creator.Should().Be("Freexcel");
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
                    .WithMessage("Page range starts after the last exportable page (2).");
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
                pdf.Info.Creator.Should().Be("Freexcel");
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
                pdf.Info.Creator.Should().Be("Freexcel");
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
                Author: "Freexcel",
                Subject: "Freexcel workbook export",
                Keywords: "Freexcel, spreadsheet"));
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
        package.PackageProperties.Creator.Should().Be("Freexcel");
        package.PackageProperties.Subject.Should().Be("Freexcel workbook export");
        package.PackageProperties.Keywords.Should().Be("Freexcel, spreadsheet");
    }

    private static FixedDocument CreateOnePageDocument()
        => CreateDocument(pageCount: 1);

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
            page.Children.Add(new TextBlock { Text = $"Freexcel PDF {i + 1}", Margin = new System.Windows.Thickness(12) });
            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            document.Pages.Add(content);
        }

        return document;
    }

    [Fact]
    public void PrintPreviewDialog_CreateTitle_IncludesWorkbookName()
    {
        PrintPreviewDialog.CreateTitle("Book1").Should().Be("Print Preview - Book1");
    }

    [Fact]
    public void PrintPreviewDialog_ContainsNativePrintCommandButton()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.cs"));

        source.Should().Contain("Content = \"_Print...\"");
        source.Should().Contain("ShowNativePrintDialog");
        source.Should().Contain("PrintDocument(document.DocumentPaginator");
    }

    [Fact]
    public void PrintSettingsPlanner_SummarizesExcelLikeActiveSheetSettings()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1")
        {
            PageOrientation = WorksheetPageOrientation.Landscape,
            PaperSize = WorksheetPaperSize.Letter,
            PrintGridlines = true,
            PrintHeadings = true,
            ScaleToFit = new WorksheetScaleToFit(85, 1, 2)
        };

        var plan = PrintSettingsPlanner.Build(sheet);

        plan.Lines.Should().Equal(
            "Print active sheet",
            "Orientation: Landscape",
            "Paper size: Letter",
            "Scaling: 85%; fit 1 page wide by 2 tall",
            "Gridlines: on",
            "Headings: on");
        plan.Summary.Should().Be("Print active sheet; Orientation: Landscape; Paper size: Letter; Scaling: 85%; fit 1 page wide by 2 tall; Gridlines: on; Headings: on");
    }

    [Fact]
    public void PrintPreviewDialog_DisplaysPrintSettingsSummary()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.cs"));
        var printExport = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PrintExport.cs"));

        source.Should().Contain("PrintSettingsPlan settings");
        source.Should().Contain("Action? showMargins = null");
        source.Should().Contain("Action? showPageSetup = null");
        source.Should().Contain("settings.Summary");
        printExport.Should().Contain("PrintSettingsPlanner.Build(sheet)");
        printExport.Should().Contain("showMargins: () => PageMarginsBtn_Click");
        printExport.Should().Contain("showPageSetup: () => PageSetupDialogBtn_Click");
    }

    [Fact]
    public void PrintPreviewDialog_WiresMarginsAndPageSetupToolbarCallbacks()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.cs"));

        source.Should().Contain("marginsButton.Click += (_, _) => showMargins?.Invoke()");
        source.Should().Contain("pageSetupButton.Click += (_, _) => showPageSetup?.Invoke()");
    }

    [Fact]
    public void ExportWorkflow_UsesOptionsDialogSelectionRangeAndOpenAfterPublish()
    {
        var printExport = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PrintExport.cs"));

        printExport.Should().Contain("new ExportOptionsDialog(SheetGrid.SelectedRange is not null)");
        printExport.Should().Contain("ExportPlanner.PlanExport(saveDlg.FileName, optionsDialog.Result)");
        printExport.Should().Contain("RenderExportDocument(options)");
        printExport.Should().Contain("RenderExportPaginator(options)");
        printExport.Should().Contain("ApplyExportPageRange(options");
        printExport.Should().Contain("ExportAsPdf(request.Path, ExportPlanner.DescribeRequest(request), request.Options)");
        printExport.Should().Contain("ExportAsXps(request.Path, ExportPlanner.DescribeRequest(request), request.Options)");
        printExport.Should().Contain("ResolveExportRange(options)");
        printExport.Should().Contain("PdfDocumentProperties.FromWorkbook(_workbook, options)");
        printExport.Should().Contain("XpsDocumentProperties.ApplyToPackage(pkg, XpsDocumentProperties.FromWorkbook(_workbook, options))");
        printExport.Should().Contain("ExportPlanner.TryValidatePageRange(options.PageRange, document.Pages.Count");
        printExport.Should().Contain("ExportPlanner.TryValidatePageRange(options.PageRange, paginator.PageCount");
        printExport.Should().Contain("PdfDocumentExporter.Save(document, pdfPath, properties, options.PageRange)");
        printExport.Should().Contain("OpenExportedFile(request.ActualPath)");
    }
}
