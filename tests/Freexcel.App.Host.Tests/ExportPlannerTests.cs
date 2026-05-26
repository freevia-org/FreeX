using System.IO;
using System.Text;
using System.Printing;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using FluentAssertions;
using Freexcel.Core.Calc;
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
    public void ExportOptions_DescribeUnsupportedPdfPublishOptions()
    {
        var options = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            PdfConformance: PdfConformance.PdfA1b,
            IncludeDocumentStructureTags: true);

        ExportPlanner.DescribeOptions(options)
            .Should().Be("Active sheet only; standard quality; document properties are not included; PDF/A compliance is not supported; tagged PDF structure is not supported.");
    }

    [Fact]
    public void TryValidatePublishOptions_RejectsUnsupportedPdfA()
    {
        var options = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            PdfConformance: PdfConformance.PdfA1b);

        ExportPlanner.TryValidatePublishOptions(options, ExportFormat.Pdf, out var error)
            .Should()
            .BeFalse();

        error.Should().Be("PDF/A compliance is not supported by the current PDF exporter.");
    }

    [Fact]
    public void TryValidatePublishOptions_RejectsUnsupportedTaggedPdf()
    {
        var options = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            IncludeDocumentStructureTags: true);

        ExportPlanner.TryValidatePublishOptions(options, ExportFormat.Pdf, out var error)
            .Should()
            .BeFalse();

        error.Should().Be("Tagged PDF structure is not supported by the current PDF exporter.");
    }

    [Fact]
    public void TryValidatePublishOptions_AllowsPdfOnlyChoicesForXpsSummary()
    {
        var options = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            PdfConformance: PdfConformance.PdfA1b,
            IncludeDocumentStructureTags: true);

        ExportPlanner.TryValidatePublishOptions(options, ExportFormat.Xps, out var error)
            .Should()
            .BeTrue();

        error.Should().BeNull();
        ExportPlanner.DescribeOptions(options, ExportFormat.Xps)
            .Should()
            .Be("Active sheet only; standard quality; document properties are not included; PDF/A compliance is PDF-only and not supported; tagged PDF structure is PDF-only and not supported.");
    }

    [Fact]
    public void ResolveExportSheetIds_ActiveSheetUsesGroupedVisibleSheetsInWorkbookOrder()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var third = workbook.AddSheet("Third");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;

        var result = ExportSheetSelectionPlanner.ResolveSheetIds(
            workbook,
            new ExportOptions(ExportContentScope.ActiveSheet, false, false),
            second.Id,
            [third.Id, hidden.Id, second.Id]);

        result.Should().Equal(second.Id, third.Id);
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

    [Fact]
    public void ExportOptionsDialog_CreateResult_IgnoresBookmarkModeWhenBookmarksAreUnchecked()
    {
        ExportOptionsDialog.CreateResult(
                ExportContentScope.ActiveSheet,
                includeDocumentProperties: false,
                openAfterPublish: false,
                createBookmarks: false,
                bookmarkMode: PdfBookmarkMode.PrintTitles)
            .Should()
            .Be(new ExportOptions(
                ExportContentScope.ActiveSheet,
                IncludeDocumentProperties: false,
                OpenAfterPublish: false,
                BookmarkMode: PdfBookmarkMode.None));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(99, 1)]
    public void ExportOptionsDialogPlanner_MapsBookmarkModeIndexes(int index, int expected)
    {
        ((int)ExportOptionsDialogPlanner.BookmarkModeFromIndex(index)).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(99, 0)]
    public void ExportOptionsDialogPlanner_MapsInitialViewIndexes(int index, int expected)
    {
        ((int)ExportOptionsDialogPlanner.InitialViewFromIndex(index)).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(99, 0)]
    public void ExportOptionsDialogPlanner_MapsOpenModeIndexes(int index, int expected)
    {
        ((int)ExportOptionsDialogPlanner.OpenModeFromIndex(index)).Should().Be(expected);
    }

    [Theory]
    [InlineData("From page must be less than or equal to To page.", "4", 1)]
    [InlineData("Enter a valid page range.", "2", 1)]
    [InlineData("Enter a valid page range.", "0", 0)]
    [InlineData("Enter a valid page range.", "x", 0)]
    public void ExportOptionsDialogPlanner_SelectsInvalidPageRangeFocusTarget(
        string error,
        string fromPageText,
        int expected)
    {
        ((int)ExportOptionsDialogPlanner.ResolveInvalidPageRangeFocusTarget(error, fromPageText)).Should().Be(expected);
    }

    [Fact]
    public void ExportOptionsDialogPlanningFacade_ForwardsPureWorkToPlanner()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ExportOptionsDialog.cs"));

        source.Should().Contain("ExportOptionsDialogPlanner.CreateResult(");
        source.Should().Contain("ExportOptionsDialogPlanner.BookmarkModeFromIndex(_bookmarkModeBox.SelectedIndex)");
        source.Should().Contain("ExportOptionsDialogPlanner.InitialViewFromIndex(_initialViewBox.SelectedIndex)");
        source.Should().Contain("ExportOptionsDialogPlanner.OpenModeFromIndex(_openModeBox.SelectedIndex)");
        source.Should().Contain("ExportOptionsDialogPlanner.ResolveInvalidPageRangeFocusTarget(error, _fromPageBox.Text)");
    }

    [Theory]
    [InlineData(" uk_ua ", "uk-UA")]
    [InlineData("EN-us", "en-US")]
    [InlineData("not a culture", "en-US")]
    public void NormalizePdfLanguage_CanonicalizesKnownCultureTags(string input, string expected)
    {
        ExportPlanner.NormalizePdfLanguage(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(" uk_ua ", true, "uk-UA", null)]
    [InlineData("", true, "en-US", null)]
    [InlineData("not a culture", false, "en-US", "Enter a valid PDF language tag, for example en-US.")]
    public void TryNormalizePdfLanguage_ValidatesTypedLanguageTags(
        string input,
        bool expectedSuccess,
        string expectedLanguage,
        string? expectedError)
    {
        ExportPlanner.TryNormalizePdfLanguage(input, out var language, out var error)
            .Should()
            .Be(expectedSuccess);

        language.Should().Be(expectedLanguage);
        error.Should().Be(expectedError);
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
            "Content = \"Create _PDF bookmarks\"",
            "Content = \"_Bitmap text when fonts may not be embedded\"",
            "Content = \"PDF/_A compliant (not supported)\"",
            "Content = \"Document structure _tags (not supported)\"",
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

        source.Should().NotContain("Create _PDF bookmarks using sheet names");
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
        source.Should().Contain("SizeToContent = SizeToContent.Height;");
        source.Should().Contain("VerticalScrollBarVisibility = ScrollBarVisibility.Auto");
    }

    [Fact]
    public void ExportOptionsDialog_InvalidPageRange_RefocusesPageRangeEntry()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ExportOptionsDialog.cs"));

        source.Should().Contain("FocusInvalidPageRangeInput(error);");
        source.Should().Contain("private void FocusInvalidPageRangeInput(string? error)");
        source.Should().Contain("_pagesRangeButton.IsChecked = true;");
        source.Should().Contain("var target = ResolveInvalidPageRangeInput(error);");
        source.Should().Contain("private TextBox ResolveInvalidPageRangeInput(string? error)");
        source.Should().Contain("ExportOptionsDialogPlanner.ResolveInvalidPageRangeFocusTarget(error, _fromPageBox.Text)");
        source.Should().Contain("? _toPageBox");
        source.Should().Contain(": _fromPageBox");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void ExportOptionsDialog_InvalidPdfLanguage_RefocusesLanguageEntry()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ExportOptionsDialog.cs"));

        source.Should().Contain("ExportPlanner.TryNormalizePdfLanguage(_pdfLanguageBox.Text, out var pdfLanguage, out var pdfLanguageError)");
        source.Should().Contain("MessageBox.Show(this, pdfLanguageError, \"Export Options\", MessageBoxButton.OK, MessageBoxImage.Warning);");
        source.Should().Contain("FocusInvalidPdfLanguageInput();");
        source.Should().Contain("private void FocusInvalidPdfLanguageInput()");
        source.Should().Contain("_pdfLanguageBox.Focus();");
        source.Should().Contain("_pdfLanguageBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_pdfLanguageBox);");
    }

    [Fact]
    public void ExportOptionsDialog_SeedsPdfLanguageFromPersistedOption()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ExportOptionsDialog.cs"));

        source.Should().Contain("public ExportOptionsDialog(bool hasSelection, string? initialPdfLanguage = null)");
        source.Should().Contain("_pdfLanguageBox.Text = ExportPlanner.NormalizePdfLanguage(initialPdfLanguage);");
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
    [InlineData(null, null, 3, true, null)]
    [InlineData(1, 2, 3, true, null)]
    [InlineData(3, 3, 3, true, null)]
    [InlineData(4, 4, 3, false, "Page range starts after the last exportable page (3).")]
    [InlineData(1, 4, 3, false, "Page range ends after the last exportable page (3).")]
    [InlineData(1, 1, 0, false, "There are no exportable pages.")]
    public void TryValidatePageRange_ChecksRenderedPageCount(
        int? fromPage,
        int? toPage,
        int pageCount,
        bool expectedSuccess,
        string? expectedError)
    {
        var pageRange = fromPage is null || toPage is null ? null : new ExportPageRange(fromPage.Value, toPage.Value);

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
                    .WithMessage("Page range ends after the last exportable page (2).");
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
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = \"_Print...\"");
        source.Should().Contain("ShowNativePrintDialog");
        source.Should().Contain("ResolvePrintPaginator(previewDocument, selectedPageRangeMode, currentPrintPage, selectedPageRange)");
        source.Should().Contain("PrintDocument(paginator");
    }

    [Fact]
    public void PrintPreviewDialogOpenedFromKeyboard_FocusesPrintButton()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget(printButton);");
        source.Should().Contain("private static void FocusInitialKeyboardTarget(Button printButton)");
        source.Should().Contain("printButton.Focus();");
        source.Should().Contain("Keyboard.Focus(printButton);");
    }

    [Theory]
    [InlineData(null, false, 0)]
    [InlineData("", false, 0)]
    [InlineData("0", false, 0)]
    [InlineData("2", true, 2)]
    [InlineData("999", true, 999)]
    [InlineData("1000", false, 0)]
    [InlineData("not a number", false, 0)]
    public void PrintPreviewDialog_TryParseCopyCount_ValidatesExcelCopiesRange(string? text, bool expectedResult, int expectedCopies)
    {
        PrintPreviewDialog.TryParseCopyCount(text, out var copies).Should().Be(expectedResult);
        copies.Should().Be(expectedCopies);
    }

    [Theory]
    [InlineData(null, 5, false, 0)]
    [InlineData("", 5, false, 0)]
    [InlineData("0", 5, false, 0)]
    [InlineData("1", 5, true, 1)]
    [InlineData("5", 5, true, 5)]
    [InlineData("6", 5, false, 0)]
    [InlineData("2.5", 5, false, 0)]
    [InlineData("not a number", 5, false, 0)]
    public void PrintPreviewDialog_TryParsePageNumber_ValidatesPreviewPageRange(
        string? text,
        int totalPages,
        bool expectedResult,
        int expectedPage)
    {
        PrintPreviewDialog.TryParsePageNumber(text, totalPages, out var pageNumber).Should().Be(expectedResult);
        pageNumber.Should().Be(expectedPage);
    }

    [Theory]
    [InlineData(PrintPreviewSidesMode.OneSided, Duplexing.OneSided)]
    [InlineData(PrintPreviewSidesMode.TwoSidedLongEdge, Duplexing.TwoSidedLongEdge)]
    [InlineData(PrintPreviewSidesMode.TwoSidedShortEdge, Duplexing.TwoSidedShortEdge)]
    public void PrintPreviewDialog_MapsExcelSidesChoicesToPrintTicketDuplexing(
        PrintPreviewSidesMode mode,
        Duplexing expected)
    {
        PrintPreviewDialog.ResolvePrintTicketDuplexing(mode).Should().Be(expected);
    }

    [Fact]
    public void PrintPreviewDialog_ResolvesCurrentPagePaginatorForPrintRange()
    {
        StaTestRunner.Run(() =>
        {
            var document = new FixedDocument();
            document.Pages.Add(new PageContent());
            document.Pages.Add(new PageContent());
            document.Pages.Add(new PageContent());

            var allPages = PrintPreviewDialog.ResolvePrintPaginator(document, PrintPreviewPageRangeMode.AllPages, currentPage: 2);
            var currentPage = PrintPreviewDialog.ResolvePrintPaginator(document, PrintPreviewPageRangeMode.CurrentPage, currentPage: 2);
            var pageRange = PrintPreviewDialog.ResolvePrintPaginator(
                document,
                PrintPreviewPageRangeMode.Pages,
                currentPage: 1,
                new ExportPageRange(2, 3));

            allPages.PageCount.Should().Be(3);
            currentPage.PageCount.Should().Be(1);
            pageRange.PageCount.Should().Be(2);
            currentPage.GetPage(1).Should().Be(DocumentPage.Missing);
            pageRange.GetPage(2).Should().Be(DocumentPage.Missing);
        });
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
    public void PrintSettingsPlanner_SummarizesIgnoredPrintAreaForBackstagePreview()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1")
        {
            PrintArea = GridRange.Parse("B2:D10", sheetId)
        };

        var normal = PrintSettingsPlanner.Build(sheet);
        var ignored = PrintSettingsPlanner.Build(sheet, ignorePrintArea: true);

        normal.Lines[0].Should().Be("Print selected print area");
        ignored.Lines[0].Should().Be("Print active sheet (ignore print area)");
    }

    [Fact]
    public void PrintPreviewDialog_DisplaysPrintSettingsSummary()
    {
        var source = ReadPrintPreviewDialogSources();
        var printExport = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PrintExport.cs"));

        source.Should().Contain("PrintSettingsPlan settings");
        source.Should().Contain("Action? showMargins = null");
        source.Should().Contain("Action? showPageSetup = null");
        source.Should().Contain("Func<(FixedDocument Document, PrintSettingsPlan Settings)>? refreshPreview = null");
        source.Should().Contain("Func<PrintPreviewSettings, (FixedDocument Document, PrintSettingsPlan Settings)>? refreshPreviewWithSettings = null");
        source.Should().Contain("settings.Summary");
        printExport.Should().Contain("PrintSettingsPlanner.Build(sheet)");
        printExport.Should().Contain("showMargins: () => PageMarginsBtn_Click");
        printExport.Should().Contain("showPageSetup: () => PageSetupDialogBtn_Click");
        printExport.Should().Contain("refreshPreviewWithSettings: BuildActiveSheetPrintPreview");
        printExport.Should().Contain("PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService, ignorePrintArea: settings.IgnorePrintArea)");
        printExport.Should().Contain("PrintSettingsPlanner.Build(sheet, settings.IgnorePrintArea)");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesKeyboardPrintGridlineAndHeadingToggles()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = \"_Print gridlines\"");
        source.Should().Contain("Content = \"Print row and column _headings\"");
        source.Should().Contain("gridlinesBox.Checked +=");
        source.Should().Contain("gridlinesBox.Unchecked +=");
        source.Should().Contain("headingsBox.Checked +=");
        source.Should().Contain("headingsBox.Unchecked +=");
        source.Should().Contain("new SetPrintOptionsCommand(");
        source.Should().Contain("refreshPreview();");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesIgnorePrintAreaBackstageSetting()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = \"_Ignore print area\"");
        source.Should().Contain("new PrintPreviewSettings(ignorePrintAreaBox.IsChecked == true)");
        source.Should().Contain("ignorePrintAreaBox.Checked +=");
        source.Should().Contain("ignorePrintAreaBox.Unchecked +=");
        source.Should().Contain("ToolTip = \"Preview and print the active sheet instead of the stored print area.\"");
        source.Should().Contain("AutomationProperties.SetName(ignorePrintAreaBox, \"Ignore print area\");");
    }

    [Fact]
    public void PrintPreviewDialog_SettingsCombosHaveAccessKeyLabels()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("void AddLabel(string text, Control target)");
        source.Should().Contain("Content = text");
        source.Should().Contain("Target = target");
        source.Should().Contain("AddLabel(\"_Orientation\", orientBox);");
        source.Should().Contain("AddLabel(\"_Paper Size\", paperBox);");
        source.Should().Contain("AddLabel(\"_Margins\", marginsBox);");
        source.Should().Contain("AddLabel(\"_Scaling\", scaleBox);");
    }

    [Fact]
    public void PrintPreviewDialog_ToolbarZoomAccessKeyTargetsZoomCombo()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("var zoomBox = new ComboBox");
        source.Should().Contain("Content = \"_Zoom:\"");
        source.Should().Contain("Target = zoomBox");
    }

    [Fact]
    public void PrintPreviewDialog_WiresMarginsAndPageSetupToolbarCallbacks()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("showMargins?.Invoke()");
        source.Should().Contain("showPageSetup?.Invoke()");
        source.Should().Contain("RefreshPreviewDocument()");
        source.Should().Contain("viewer.Document = previewDocument");
        source.Should().Contain("settingsSummaryText.Text = refreshed.Settings.Summary");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesPageEntryAndStatus()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = \"_Page:\"");
        source.Should().Contain("pageNumberBox");
        source.Should().Contain("pageStatusText");
        source.Should().Contain("Page 1 of");
        source.Should().Contain("NavigationCommands.GoToPage");
        source.Should().Contain("TryParsePageNumber(pageNumberBox.Text, totalPages, out var pageNumber)");
        source.Should().Contain("ShowInvalidPageNumberWarning(pageNumberBox, totalPages)");
        source.Should().Contain("Enter a page number from 1 to");
        source.Should().Contain("pageNumberBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(pageNumberBox);");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesHonestPrinterCopiesAndStatusSurface()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = \"Pr_inter:\"");
        source.Should().Contain("Content = \"_Copies:\"");
        source.Should().Contain("Content = \"C_ollated\"");
        source.Should().Contain("Content = \"_Sides:\"");
        source.Should().Contain("Print One Sided");
        source.Should().Contain("Flip pages on long edge");
        source.Should().Contain("Flip pages on short edge");
        source.Should().Contain("printerBox");
        source.Should().Contain("copiesBox");
        source.Should().Contain("collatedBox");
        source.Should().Contain("sidesBox");
        source.Should().Contain("statusText");
        source.Should().Contain("TryParseCopyCount(copiesBox.Text, out var copies)");
        source.Should().Contain("ShowInvalidCopiesWarning(copiesBox)");
        source.Should().Contain("dialog.PrintTicket.CopyCount = copies");
        source.Should().Contain("dialog.PrintTicket.Collation = collated ? Collation.Collated : Collation.Uncollated");
        source.Should().Contain("dialog.PrintTicket.Duplexing = ResolvePrintTicketDuplexing(sidesMode)");
        source.Should().Contain("ResolveSelectedSidesMode(sidesBox)");
        source.Should().Contain("collatedBox.IsChecked == true");
        source.Should().Contain("MessageBox.Show(this, \"Enter a copy count from 1 to 999.\", Title, MessageBoxButton.OK, MessageBoxImage.Warning);");
        source.Should().Contain("copiesBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(copiesBox);");
        source.Should().Contain("AutomationProperties.SetHelpText");
        source.Should().Contain("RefreshPrintStatus");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesKeyboardPrintRangeChoices()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = \"_All pages\"");
        source.Should().Contain("Content = \"Current pa_ge\"");
        source.Should().Contain("Content = \"Pa_ges\"");
        source.Should().Contain("fromPageBox");
        source.Should().Contain("toPageBox");
        source.Should().Contain("PrintPreviewPageRangeMode.CurrentPage");
        source.Should().Contain("PrintPreviewPageRangeMode.Pages");
        source.Should().Contain("ResolvePrintPaginator(previewDocument, selectedPageRangeMode, currentPrintPage, selectedPageRange)");
        source.Should().Contain("ExportPlanner.TryCreatePageRange(fromPageBox.Text, toPageBox.Text, out selectedPageRange, out var pageRangeError)");
        source.Should().Contain("ExportPlanner.TryValidatePageRange(selectedPageRange, totalPages, out var validatedPageRangeError)");
        source.Should().Contain("TryParsePageNumber(pageNumberBox.Text, totalPages, out currentPrintPage)");
        source.Should().Contain("ShowInvalidPageNumberWarning(pageNumberBox, totalPages)");
        source.Should().Contain("ShowInvalidPageRangeWarning(fromPageBox, toPageBox, pageRangeError)");
    }

    [Fact]
    public void ExportWorkflow_UsesOptionsDialogSelectionRangeAndOpenAfterPublish()
    {
        var printExport = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PrintExport.cs"));
        var optionsSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FreexcelOptions.cs"));

        optionsSource.Should().Contain("public string PdfExportLanguage { get; set; } = ExportPlanner.DefaultPdfLanguage;");
        printExport.Should().Contain("new ExportOptionsDialog(SheetGrid.SelectedRange is not null, _options.PdfExportLanguage)");
        printExport.Should().Contain("_options.PdfExportLanguage = optionsDialog.Result.PdfLanguage;");
        printExport.Should().Contain("_options.Save();");
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

    private static string ReadPrintPreviewDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.Layout.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.Helpers.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewSettingsPanelFactory.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewToolbarPlanner.cs")));

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
