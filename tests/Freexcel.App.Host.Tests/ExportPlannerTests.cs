using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using FluentAssertions;
using Freexcel.Core.Model;
using PdfSharp.Pdf;
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
    public void PlanExport_AppendsXpsExtensionForExplicitExtensionlessXpsRequests()
    {
        var request = ExportPlanner.PlanExport(@"C:\temp\report", ExportFormat.Xps, ExportOptions.ExcelLikeDefault);

        request.Should().Be(new ExportRequest(
            @"C:\temp\report.xps",
            ExportFormat.Xps,
            ExportOptions.ExcelLikeDefault,
            null));
        request.UsesXpsFallback.Should().BeFalse();
        request.ActualPath.Should().Be(@"C:\temp\report.xps");
    }

    [Theory]
    [InlineData(@"C:\temp\report.pdf", "xps", @"C:\temp\report.xps")]
    [InlineData(@"C:\temp\report.xps", "pdf", @"C:\temp\report.pdf")]
    [InlineData(@"C:\temp\report.export", "pdf", @"C:\temp\report.pdf")]
    [InlineData(@"C:\temp\report.export", "xps", @"C:\temp\report.xps")]
    public void PlanExport_NormalizesMismatchedExtensionForExplicitFormatRequests(
        string path,
        string explicitFormat,
        string expectedPath)
    {
        var format = explicitFormat == "xps"
            ? ExportFormat.Xps
            : ExportFormat.Pdf;

        var request = ExportPlanner.PlanExport(path, format, ExportOptions.ExcelLikeDefault);

        request.Path.Should().Be(expectedPath);
        request.Format.Should().Be(format);
        request.ActualPath.Should().Be(expectedPath);
    }

    [Fact]
    public void ExportOptions_DefaultsToActiveSheetWithoutDocumentProperties()
    {
        ExportOptions.ExcelLikeDefault.Should().Be(new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            IgnorePrintAreas: false,
            Quality: ExportQuality.Standard));

        ExportPlanner.DescribeOptions(ExportOptions.ExcelLikeDefault)
            .Should().Be("Active sheet only; standard quality; document properties are not included.");
    }

    [Fact]
    public void ExportOptions_DescribeSelectionAndOpenAfterPublish()
    {
        var options = new ExportOptions(
            ExportContentScope.Selection,
            IncludeDocumentProperties: true,
            OpenAfterPublish: true,
            IgnorePrintAreas: true,
            PageRange: new ExportPageRange(2, 4),
            Quality: ExportQuality.MinimumSize,
            BookmarkMode: PdfBookmarkMode.SheetNames,
            BitmapTextWhenFontsMayNotBeEmbedded: true);

        ExportPlanner.DescribeOptions(options)
            .Should().Be("Selection; pages 2-4; minimum size; print areas are ignored; document properties are included; bookmarks use sheet names; bitmap text when fonts may not be embedded; open after publishing.");
    }

    [Theory]
    [InlineData("sheet", "bookmarks use sheet names")]
    [InlineData("print-title", "bookmarks use print titles")]
    [InlineData("page-number", "bookmarks use page numbers")]
    public void ExportOptions_DescribePdfBookmarkModes(string mode, string expectedPart)
    {
        var bookmarkMode = mode switch
        {
            "print-title" => PdfBookmarkMode.PrintTitles,
            "page-number" => PdfBookmarkMode.PageNumbers,
            _ => PdfBookmarkMode.SheetNames
        };
        var options = new ExportOptions(
            ExportContentScope.EntireWorkbook,
            IncludeDocumentProperties: true,
            OpenAfterPublish: false,
            BookmarkMode: bookmarkMode);

        ExportPlanner.DescribeOptions(options)
            .Should().Be($"Entire workbook; standard quality; document properties are included; {expectedPart}.");
    }

    [Fact]
    public void ExportOptions_DescribePdfInitialViewAndOpenMode()
    {
        var options = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            InitialView: PdfInitialView.OneColumn,
            OpenMode: PdfOpenMode.FullScreen);

        ExportPlanner.DescribeOptions(options)
            .Should().Be("Active sheet only; standard quality; opens as one continuous column; opens full screen; document properties are not included.");
    }

    [Fact]
    public void ExportOptions_DescribePdfLanguageWhenNotDefault()
    {
        var options = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            PdfLanguage: "uk-UA");

        ExportPlanner.DescribeOptions(options)
            .Should().Be("Active sheet only; standard quality; document properties are not included; PDF language uk-UA.");
    }

    [Fact]
    public void ExportOptions_DescribeWithXpsFormatIncludesDocumentProperties()
    {
        var options = new ExportOptions(
            ExportContentScope.Selection,
            IncludeDocumentProperties: true,
            OpenAfterPublish: true);

        ExportPlanner.DescribeOptions(options, ExportFormat.Xps)
            .Should().Be("Selection; standard quality; document properties are included; open after publishing.");
    }

    [Fact]
    public void ExportOptions_DescribeWithXpsFormatExplainsPdfOnlyBookmarks()
    {
        var options = new ExportOptions(
            ExportContentScope.EntireWorkbook,
            IncludeDocumentProperties: true,
            OpenAfterPublish: false,
            BookmarkMode: PdfBookmarkMode.PrintTitles);

        ExportPlanner.DescribeOptions(options, ExportFormat.Xps)
            .Should().Be("Entire workbook; standard quality; document properties are included; bookmarks are PDF-only.");
    }

    [Fact]
    public void ExportOptions_DescribeWithXpsFormatExplainsPdfOnlyLanguage()
    {
        var options = new ExportOptions(
            ExportContentScope.EntireWorkbook,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            PdfLanguage: "uk-UA");

        ExportPlanner.DescribeOptions(options, ExportFormat.Xps)
            .Should().Be("Entire workbook; standard quality; document properties are not included; PDF language is PDF-only.");
    }

    [Fact]
    public void ExportOptions_DescribeWithXpsFormatExplainsPdfOnlyViewOptions()
    {
        var options = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            InitialView: PdfInitialView.TwoColumnLeft,
            OpenMode: PdfOpenMode.FullScreen);

        ExportPlanner.DescribeOptions(options, ExportFormat.Xps)
            .Should().Be("Active sheet only; standard quality; PDF initial view is PDF-only; PDF open mode is PDF-only; document properties are not included.");
    }

    [Fact]
    public void ExportOptions_DescribeWithXpsFormatExplainsPdfOnlyMinimumSize()
    {
        var options = new ExportOptions(
            ExportContentScope.Selection,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            Quality: ExportQuality.MinimumSize);

        ExportPlanner.DescribeOptions(options, ExportFormat.Xps)
            .Should().Be("Selection; minimum size is PDF-only; document properties are not included.");
    }

    [Fact]
    public void ExportOptions_DescribeEntireWorkbook()
    {
        var options = new ExportOptions(
            ExportContentScope.EntireWorkbook,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false);

        ExportPlanner.DescribeOptions(options)
            .Should().Be("Entire workbook; standard quality; document properties are not included.");
    }

    [Fact]
    public void ExportOptionsDialog_CreateResult_NormalizesExcelOptions()
    {
        ExportOptionsDialog.CreateResult(
                ExportContentScope.EntireWorkbook,
                includeDocumentProperties: true,
                openAfterPublish: true,
                ignorePrintAreas: true,
                pageRange: new ExportPageRange(3, 3),
                quality: ExportQuality.MinimumSize,
                createBookmarks: true,
                pdfLanguage: " uk-UA ")
            .Should()
            .Be(new ExportOptions(
                ExportContentScope.EntireWorkbook,
                IncludeDocumentProperties: true,
                OpenAfterPublish: true,
                IgnorePrintAreas: true,
                PageRange: new ExportPageRange(3, 3),
                Quality: ExportQuality.MinimumSize,
                CreateBookmarks: true,
                BookmarkMode: PdfBookmarkMode.SheetNames,
                PdfLanguage: "uk-UA"));
    }

    [Theory]
    [InlineData(" uk_ua ", "uk-UA")]
    [InlineData("EN-us", "en-US")]
    [InlineData("not a culture", "en-US")]
    public void NormalizePdfLanguage_CanonicalizesKnownCultureTags(string input, string expected)
    {
        ExportPlanner.NormalizePdfLanguage(input).Should().Be(expected);
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
            "Content = \"_Ignore print areas\"",
            "Content = \"Create _PDF bookmarks using sheet names\"",
            "Content = \"_Bitmap text when fonts may not be embedded\"",
            "Content = \"PDF _language:\"",
            "Target = _pdfLanguageBox",
            "Content = \"_Standard\"",
            "Content = \"_Minimum size\"",
            "Content = \"_All\"",
            "Content = \"_Pages\"",
            "_fromPageBox.IsEnabled = false",
            "Target = _fromPageBox",
            "Content = \"t_o\"",
            "Target = _toPageBox",
            "Content = \"_OK\"",
            "Content = \"_Cancel\""
        })
            source.Should().Contain(expected);

        source.Should().NotContain("CSV _delimiter:");
    }

    [Fact]
    public void ExportOptionsDialogOpenedFromKeyboard_FocusesActiveSheetChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ExportOptionsDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_activeSheetButton.Focus();");
        source.Should().Contain("Keyboard.Focus(_activeSheetButton);");
    }

    [Fact]
    public void ExportOptionsDialog_InvalidPageRange_RefocusesPageRangeEntry()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ExportOptionsDialog.cs"));

        source.Should().Contain("FocusInvalidPageRangeInput();");
        source.Should().Contain("private void FocusInvalidPageRangeInput()");
        source.Should().Contain("_pagesRangeButton.IsChecked = true;");
        source.Should().Contain("_fromPageBox.Focus();");
        source.Should().Contain("_fromPageBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_fromPageBox);");
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
            "Options: Active sheet only; standard quality; document properties are not included.");
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
            "Options: Active sheet only; standard quality; document properties are included.");
    }

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
                Keywords: "Freexcel, spreadsheet");

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
                Encoding.ASCII.GetString(bytes).Should().Contain("Freexcel PDF 1");
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
                Keywords: "Freexcel, spreadsheet  ");

            try
            {
                PdfDocumentExporter.Save(document, path, properties);

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.Info.Title.Should().Be("Quarterly Review");
                pdf.Info.Author.Should().Be("Finance Team");
                pdf.Info.Subject.Should().Be("Workbook export");
                pdf.Info.Keywords.Should().Be("Freexcel, spreadsheet");
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
                Keywords: "Freexcel, spreadsheet  "));

        package.PackageProperties.Title.Should().Be("Quarterly Review");
        package.PackageProperties.Creator.Should().Be("Finance Team");
        package.PackageProperties.Subject.Should().BeNull();
        package.PackageProperties.Keywords.Should().Be("Freexcel, spreadsheet");
    }

    private static FixedDocument CreateOnePageDocument()
        => CreateDocument(pageCount: 1);

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
    public void PrintPreviewDialogOpenedFromKeyboard_FocusesPrintButton()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget(printButton);");
        source.Should().Contain("private static void FocusInitialKeyboardTarget(Button printButton)");
        source.Should().Contain("printButton.Focus();");
        source.Should().Contain("Keyboard.Focus(printButton);");
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("0", 1)]
    [InlineData("2", 2)]
    [InlineData("1000", 999)]
    [InlineData("not a number", 1)]
    public void PrintPreviewDialog_NormalizeCopyCount_ClampsToExcelLikeCopiesRange(string? text, int expected)
    {
        PrintPreviewDialog.NormalizeCopyCount(text).Should().Be(expected);
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
        source.Should().Contain("Func<(FixedDocument Document, PrintSettingsPlan Settings)>? refreshPreview = null");
        source.Should().Contain("settings.Summary");
        printExport.Should().Contain("PrintSettingsPlanner.Build(sheet)");
        printExport.Should().Contain("showMargins: () => PageMarginsBtn_Click");
        printExport.Should().Contain("showPageSetup: () => PageSetupDialogBtn_Click");
        printExport.Should().Contain("refreshPreview: BuildActiveSheetPrintPreview");
    }

    [Fact]
    public void PrintPreviewDialog_WiresMarginsAndPageSetupToolbarCallbacks()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.cs"));

        source.Should().Contain("showMargins?.Invoke()");
        source.Should().Contain("showPageSetup?.Invoke()");
        source.Should().Contain("RefreshPreviewDocument()");
        source.Should().Contain("viewer.Document = previewDocument");
        source.Should().Contain("settingsSummaryText.Text = refreshed.Settings.Summary");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesPageEntryAndStatus()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.cs"));

        source.Should().Contain("Content = \"_Page:\"");
        source.Should().Contain("pageNumberBox");
        source.Should().Contain("pageStatusText");
        source.Should().Contain("Page 1 of");
        source.Should().Contain("NavigationCommands.GoToPage");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesHonestPrinterCopiesAndStatusSurface()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.cs"));

        source.Should().Contain("Content = \"Pr_inter:\"");
        source.Should().Contain("Content = \"_Copies:\"");
        source.Should().Contain("printerBox");
        source.Should().Contain("copiesBox");
        source.Should().Contain("statusText");
        source.Should().Contain("NormalizeCopyCount(copiesBox.Text)");
        source.Should().Contain("dialog.PrintTicket.CopyCount = copies");
        source.Should().Contain("AutomationProperties.SetHelpText");
        source.Should().Contain("RefreshPrintStatus");
    }

    [Fact]
    public void ExportWorkflow_UsesOptionsDialogSelectionRangeAndOpenAfterPublish()
    {
        var printExport = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PrintExport.cs"));

        printExport.Should().Contain("new ExportOptionsDialog(SheetGrid.SelectedRange is not null)");
        printExport.Should().Contain("saveDlg.FilterIndex == 2");
        printExport.Should().Contain("ExportPlanner.PlanExport(saveDlg.FileName, selectedFormat, optionsDialog.Result)");
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
        printExport.Should().Contain("CreatePdfBookmarks(options)");
        printExport.Should().Contain("includeSelectableText: !options.BitmapTextWhenFontsMayNotBeEmbedded");
        printExport.Should().Contain("pdfLanguage: options.PdfLanguage");
        printExport.Should().Contain("options.EffectiveBookmarkMode");
        printExport.Should().Contain(": sheet.Name");
        printExport.Should().Contain("BuildPrintTitleBookmark(sheet)");
        printExport.Should().Contain("Page {pageIndex + 1 + offset}");
        printExport.Should().Contain("OpenExportedFile(request.ActualPath)");
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
