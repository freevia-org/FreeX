using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public partial class ExportPlannerTests
{
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

    [Fact]
    public void ExportOptionsDialog_CreateResult_ClearsPdfOnlyChoicesForXps()
    {
        ExportOptionsDialog.CreateResult(
                ExportContentScope.EntireWorkbook,
                includeDocumentProperties: true,
                openAfterPublish: true,
                ignorePrintAreas: true,
                pageRange: new ExportPageRange(4, 5),
                quality: ExportQuality.MinimumSize,
                createBookmarks: true,
                bookmarkMode: PdfBookmarkMode.PageNumbers,
                initialView: PdfInitialView.TwoColumnLeft,
                openMode: PdfOpenMode.FullScreen,
                bitmapTextWhenFontsMayNotBeEmbedded: true,
                pdfLanguage: "uk-UA",
                pdfConformance: PdfConformance.PdfA1b,
                includeDocumentStructureTags: true,
                format: ExportFormat.Xps)
            .Should()
            .Be(new ExportOptions(
                ExportContentScope.EntireWorkbook,
                IncludeDocumentProperties: true,
                OpenAfterPublish: true,
                IgnorePrintAreas: true,
                PageRange: new ExportPageRange(4, 5),
                Quality: ExportQuality.Standard,
                CreateBookmarks: false,
                BookmarkMode: PdfBookmarkMode.None,
                InitialView: PdfInitialView.SinglePage,
                OpenMode: PdfOpenMode.Normal,
                BitmapTextWhenFontsMayNotBeEmbedded: false,
                PdfLanguage: ExportPlanner.DefaultPdfLanguage,
                PdfConformance: PdfConformance.Standard,
                IncludeDocumentStructureTags: false));
    }

    [Fact]
    public void ExportOptionsDialogPlanner_CreatesFormatAvailability()
    {
        ExportOptionsDialogPlanner.CreateFormatAvailability(ExportFormat.Pdf)
            .Should()
            .Be(new ExportOptionsFormatAvailability(
                PdfBookmarksEnabled: true,
                PdfInitialViewEnabled: true,
                PdfOpenModeEnabled: true,
                PdfLanguageEnabled: true,
                PdfBitmapTextEnabled: true,
                MinimumSizeEnabled: true));

        ExportOptionsDialogPlanner.CreateFormatAvailability(ExportFormat.Xps)
            .Should()
            .Be(new ExportOptionsFormatAvailability(
                PdfBookmarksEnabled: false,
                PdfInitialViewEnabled: false,
                PdfOpenModeEnabled: false,
                PdfLanguageEnabled: false,
                PdfBitmapTextEnabled: false,
                MinimumSizeEnabled: false));
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
    [InlineData("Export_PageRangeFromLessThanToError", "4", 1)]
    [InlineData("Enter a valid page range.", "2", 1)]
    [InlineData("Enter a valid page range.", "0", 0)]
    [InlineData("Enter a valid page range.", "x", 0)]
    public void ExportOptionsDialogPlanner_SelectsInvalidPageRangeFocusTarget(
        string errorOrKey,
        string fromPageText,
        int expected)
    {
        var error = errorOrKey.StartsWith("Export_", StringComparison.Ordinal)
            ? UiText.Get(errorOrKey)
            : errorOrKey;

        ((int)ExportOptionsDialogPlanner.ResolveInvalidPageRangeFocusTarget(error, fromPageText)).Should().Be(expected);
    }

    [Fact]
    public void ExportOptionsDialogPlanningFacade_ForwardsPureWorkToPlanner()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ExportOptionsDialog.cs"));

        source.Should().Contain("ExportOptionsDialogPlanner.CreateResult(");
        source.Should().Contain("ExportOptionsDialogPlanner.BookmarkModeFromIndex(_bookmarkModeBox.SelectedIndex)");
        source.Should().Contain("ExportOptionsDialogPlanner.InitialViewFromIndex(_initialViewBox.SelectedIndex)");
        source.Should().Contain("ExportOptionsDialogPlanner.OpenModeFromIndex(_openModeBox.SelectedIndex)");
        source.Should().Contain("ExportOptionsDialogPlanner.ResolveInvalidPageRangeFocusTarget(error, _fromPageBox.Text)");
        source.Should().Contain("ExportOptionsDialogPlanner.CreateFormatAvailability(format)");
        source.Should().Contain("ApplyFormatAvailability(ExportOptionsDialogPlanner.CreateFormatAvailability(format));");
        source.Should().Contain("DisableOption(_bookmarksBox, UiText.Get(\"Export_BookmarksPdfOnly\"));");
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
    [InlineData("not a culture", false, "en-US", "Export_InvalidPdfLanguage")]
    public void TryNormalizePdfLanguage_ValidatesTypedLanguageTags(
        string input,
        bool expectedSuccess,
        string expectedLanguage,
        string? expectedErrorKey)
    {
        ExportPlanner.TryNormalizePdfLanguage(input, out var language, out var error)
            .Should()
            .Be(expectedSuccess);

        language.Should().Be(expectedLanguage);
        var expectedError = expectedErrorKey is null
            ? null
            : UiText.Format(expectedErrorKey, ExportPlanner.DefaultPdfLanguage);
        error.Should().Be(expectedError);
    }

    [Fact]
    public void ExportOptionsDialog_ExposesKeyboardAccessKeys()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ExportOptionsDialog.cs"));

        foreach (var expected in new[]
        {
            "Content = UiText.Get(\"ExportOptions_ActiveSheetS\")",
            "Content = UiText.Get(\"ExportOptions_SelectedRange\")",
            "Content = UiText.Get(\"ExportOptions_Workbook\")",
            "Content = UiText.Get(\"ExportOptions_IncludeDocumentProperties\")",
            "Content = UiText.Get(\"ExportOptions_OpenAfterPublishing\")",
            "Content = UiText.Get(\"ExportOptions_IgnorePrintAreas\")",
            "Content = UiText.Get(\"ExportOptions_CreatePdfBookmarks\")",
            "Content = UiText.Get(\"ExportOptions_BitmapTextWhenFontsMayNotBeEmbedded\")",
            "Content = UiText.Get(\"ExportOptions_PdfACompliantNotSupported\")",
            "Content = UiText.Get(\"ExportOptions_DocumentStructureTagsNotSupported\")",
            "Content = UiText.Get(\"ExportOptions_PdfLanguage\")",
            "Target = _pdfLanguageBox",
            "Content = UiText.Get(\"ExportOptions_Standard\")",
            "Content = UiText.Get(\"ExportOptions_MinimumSize\")",
            "Content = UiText.Get(\"ExportOptions_All\")",
            "Content = UiText.Get(\"ExportOptions_Pages\")",
            "_fromPageBox.IsEnabled = false",
            "Target = _fromPageBox",
            "Content = UiText.Get(\"ExportOptions_To\")",
            "Target = _toPageBox",
            "Content = UiText.Ok",
            "Content = UiText.Cancel"
        })
            source.Should().Contain(expected);

        source.Should().NotContain("Create _PDF bookmarks using sheet names");
        source.Should().NotContain("CSV _delimiter:");
    }

    [Fact]
    public void ExportOptionsDialog_ExposesPublishScopePageRangeQualityAndOpenAfterPublishControls()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ExportOptionsDialog.cs"));

        source.Should().Contain("Content = UiText.Get(\"ExportOptions_ActiveSheetS\"), IsChecked = true");
        source.Should().Contain("Content = UiText.Get(\"ExportOptions_SelectedRange\")");
        source.Should().Contain("Content = UiText.Get(\"ExportOptions_Workbook\")");
        source.Should().Contain("_selectionButton.IsEnabled = hasSelection;");
        source.Should().Contain("Content = UiText.Get(\"ExportOptions_All\"), GroupName = \"PageRange\", IsChecked = true");
        source.Should().Contain("Content = UiText.Get(\"ExportOptions_Pages\"), GroupName = \"PageRange\"");
        source.Should().Contain("_allPagesButton.Checked += (_, _) => SetPageRangeFieldsEnabled(false);");
        source.Should().Contain("_pagesRangeButton.Checked += (_, _) =>");
        source.Should().Contain("SetPageRangeFieldsEnabled(true);");
        source.Should().Contain("DialogFocus.FocusAndSelect(_fromPageBox);");
        source.Should().Contain("Content = UiText.Get(\"ExportOptions_Standard\"), IsChecked = true");
        source.Should().Contain("Content = UiText.Get(\"ExportOptions_MinimumSize\")");
        source.Should().Contain("Content = UiText.Get(\"ExportOptions_OpenAfterPublishing\")");
        source.Should().Contain("ExportPlanner.TryCreatePageRange(_fromPageBox.Text, _toPageBox.Text, out pageRange, out var error)");
        source.Should().Contain("_minimumSizeButton.IsChecked == true");
        source.Should().Contain("_openAfterPublishBox.IsChecked == true");
    }

    [Fact]
    public void ExportOptionsDialog_PageRangeEditorsExposeAutomationNames()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ExportOptionsDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_fromPageBox, UiText.Get(\"ExportOptions_FromPage\"));");
        source.Should().Contain("AutomationProperties.SetName(_toPageBox, UiText.Get(\"ExportOptions_ToPage\"));");
    }

    [Fact]
    public void ExportOptionsDialog_DisabledChoicesExposeAutomationHelpText()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ExportOptionsDialog.cs"));

        source.Should().Contain("AutomationProperties.SetHelpText(_selectionButton, UiText.Get(\"ExportOptions_SelectACellRangeBeforeExportingTheSelection\"));");
        source.Should().Contain("ApplyUnsupportedPdfPublishOptionHelpText(format);");
        source.Should().Contain("UiText.Get(\"ExportOptions_FreeXSCurrentPdfExporterCannotWritePdfAConformanceMetadata\")");
        source.Should().Contain("UiText.Get(\"ExportOptions_FreeXSCurrentPdfExporterCannotWriteTaggedPdfStructureTrees\")");
        source.Should().Contain("UiText.Get(\"Export_PdfAPdfOnlyUnsupported\")");
        source.Should().Contain("UiText.Get(\"Export_TaggedPdfPdfOnlyUnsupported\")");
        source.Should().Contain("private static void DisableOption(Control control, string helpText)");
        source.Should().Contain("AutomationProperties.SetHelpText(control, helpText);");
        source.Should().Contain("DisableOption(_minimumSizeButton, UiText.Get(\"Export_QualityMinimumSizePdfOnly\"));");
    }

    [Theory]
    [InlineData("pdf", "ExportOptions_FreeXSCurrentPdfExporterCannotWritePdfAConformanceMetadata", "ExportOptions_FreeXSCurrentPdfExporterCannotWriteTaggedPdfStructureTrees")]
    [InlineData("xps", "Export_PdfAPdfOnlyUnsupported", "Export_TaggedPdfPdfOnlyUnsupported")]
    public void ExportOptionsDialog_UnsupportedPdfPublishChoicesUseFormatAwareHelpText(
        string formatName,
        string expectedPdfAHelpTextKey,
        string expectedStructureTagsHelpTextKey)
    {
        var format = formatName == "xps"
            ? ExportFormat.Xps
            : ExportFormat.Pdf;

        StaTestRunner.Run(() =>
        {
            var dialog = new ExportOptionsDialog(hasSelection: true, format: format);
            try
            {
                var pdfABox = FindLogicalChildren<CheckBox>(dialog)
                    .Single(box => Equals(box.Content, UiText.Get("ExportOptions_PdfACompliantNotSupported")));
                var structureTagsBox = FindLogicalChildren<CheckBox>(dialog)
                    .Single(box => Equals(box.Content, UiText.Get("ExportOptions_DocumentStructureTagsNotSupported")));
                var expectedPdfAHelpText = UiText.Get(expectedPdfAHelpTextKey);
                var expectedStructureTagsHelpText = UiText.Get(expectedStructureTagsHelpTextKey);

                pdfABox.IsEnabled.Should().BeFalse();
                pdfABox.ToolTip.Should().Be(expectedPdfAHelpText);
                AutomationProperties.GetHelpText(pdfABox).Should().Be(expectedPdfAHelpText);

                structureTagsBox.IsEnabled.Should().BeFalse();
                structureTagsBox.ToolTip.Should().Be(expectedStructureTagsHelpText);
                AutomationProperties.GetHelpText(structureTagsBox).Should().Be(expectedStructureTagsHelpText);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ExportOptionsDialogOpenedFromKeyboard_FocusesActiveSheetChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ExportOptionsDialog.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ExportOptionsDialog.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ExportOptionsDialog.cs"));

        source.Should().Contain("ExportPlanner.TryNormalizePdfLanguage(_pdfLanguageBox.Text, out var pdfLanguage, out var pdfLanguageError)");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, pdfLanguageError, UiText.Get(\"ExportOptions_ExportOptions\"));");
        source.Should().Contain("FocusInvalidPdfLanguageInput();");
        source.Should().Contain("private void FocusInvalidPdfLanguageInput()");
        source.Should().Contain("_pdfLanguageBox.Focus();");
        source.Should().Contain("_pdfLanguageBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_pdfLanguageBox);");
    }

    [Fact]
    public void ExportOptionsDialog_SeedsPdfLanguageFromPersistedOption()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ExportOptionsDialog.cs"));

        source.Should().Contain("public ExportOptionsDialog(bool hasSelection, string? initialPdfLanguage = null, ExportFormat format = ExportFormat.Pdf)");
        source.Should().Contain("_pdfLanguageBox.Text = ExportPlanner.NormalizePdfLanguage(initialPdfLanguage);");
    }
}
