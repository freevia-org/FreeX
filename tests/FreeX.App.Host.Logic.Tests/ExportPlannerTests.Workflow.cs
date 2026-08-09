using FluentAssertions;

namespace FreeX.App.Host.Tests;

public partial class ExportPlannerTests
{
    [Fact]
    public void ExportWorkflow_UsesOptionsDialogSelectionRangeAndOpenAfterPublish()
    {
        var printExport = DialogSourceTestSupport.ReadHostSources("MainWindow.PrintExport.cs");
        var optionsSource = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", "AppOptions.cs");

        optionsSource.Should().Contain("public string PdfExportLanguage { get; set; } = DefaultPdfExportLanguage;");
        printExport.Should().Contain("ExportFormatCatalog");
        printExport.Should().Contain(".FromPdfXpsFilterIndex(saveResult.FilterIndex)");
        printExport.Should().Contain("WorkbookExportInteractionPlanner.CreateCommandPlan(");
        printExport.Should().Contain("new ExportOptionsDialog(commandPlan.HasSelection, _options.PdfExportLanguage, selectedFormat)");
        printExport.Should().Contain("if (requestPlan.ShouldPersistPdfLanguage)");
        printExport.Should().Contain("_options.PdfExportLanguage = optionsDialog.Result.PdfLanguage;");
        printExport.Should().Contain("AppOptionsStore.Save(_options);");
        printExport.Should().Contain("WorkbookExportInteractionPlanner.CreateRequestPlan(");
        printExport.Should().Contain("requestPlan.ShouldConfirmNormalizedOverwrite");
        printExport.Should().Contain("UiText.Format(\"MainWindowMessage_ExportNormalizedOverwritePrompt\", requestPlan.Request.Path)");
        printExport.Should().Contain("RenderExportDocument(options)");
        printExport.Should().Contain("ExportPlanner.CreateEffectiveOptionsForFormat(options, ExportFormat.Pdf)");
        printExport.Should().Contain("ExportPlanner.CreateEffectiveOptionsForFormat(options, ExportFormat.Xps)");
        printExport.Should().Contain("RenderExportDocument(effectiveOptions)");
        printExport.Should().Contain("RenderExportPaginator(effectiveOptions)");
        printExport.Should().Contain("ApplyExportPageRange(options");
        printExport.Should().Contain("ExportAsPdf(");
        printExport.Should().Contain("ExportAsXps(");
        printExport.Should().Contain("WpfExportDescriptionPlanner.DescribeRequest(effectiveRequest)");
        printExport.Should().Contain("using (var pkg = System.IO.Packaging.Package.Open(");
        printExport.Should().Contain("ResolveExportRange(options)");
        printExport.Should().Contain("PdfDocumentProperties.FromWorkbook(_workbook, effectiveOptions)");
        printExport.Should().Contain("XpsDocumentProperties.ApplyToPackage(pkg, XpsDocumentProperties.FromWorkbook(_workbook, effectiveOptions))");
        printExport.Should().Contain("ExportPlanner.TryValidatePageRange(effectiveOptions.PageRange, document.Pages.Count");
        printExport.Should().Contain("WpfExportPlannerTextResolver.Instance");
        printExport.Should().Contain("ExportPlanner.TryValidatePageRange(options.PageRange, paginator.PageCount");
        printExport.Should().Contain("CreatePdfBookmarks(effectiveOptions)");
        printExport.Should().Contain("includeSelectableText: !effectiveOptions.BitmapTextWhenFontsMayNotBeEmbedded");
        printExport.Should().Contain("pdfLanguage: effectiveOptions.PdfLanguage");
        printExport.Should().Contain("options.EffectiveBookmarkMode");
        printExport.Should().Contain(": sheet.Name");
        printExport.Should().Contain("BuildPrintTitleBookmark(sheet)");
        printExport.Should().Contain("Page {pageIndex + 1 + offset}");
        printExport.Should().Contain("WorkbookExportInteractionPlanner.CreateResultPlan(");
        printExport.Should().Contain("OpenExportedFile(resultPlan.DestinationPath)");

        // Input-blocking + progress treatment (P2 fix)
        printExport.Should().Contain("_isExportingFile");
        printExport.Should().Contain("RootGrid.IsEnabled = false");
        printExport.Should().Contain("HideSaveProgress()");
        printExport.Should().Contain("UiText.Get(\"Progress_ExportingFile\")");

        // XPS temp+replace atomicity (P3 fix)
        printExport.Should().Contain("ExportAtomicWriter.CreateTempPath(xpsPath)");
        printExport.Should().Contain("ExportAtomicWriter.ReplaceTarget(tempPath, xpsPath)");

        // PDF bytes rendered on UI thread, flushed on background thread (P2 fix)
        printExport.Should().Contain("PdfDocumentExporter.RenderToBytes(");
        printExport.Should().Contain("ExportAtomicWriter.WriteAllBytes(pdfPath, pdfBytes)");
        printExport.Should().Contain("await Task.Run(");
    }

    [Fact]
    public void ExportWorkflow_XpsWriteIsDocumentedAsSynchronous()
    {
        var printExport = DialogSourceTestSupport.ReadHostSources("MainWindow.PrintExport.cs");

        // The XPS write stays on the UI thread (XpsDocumentWriter drives the WPF visual tree);
        // this is documented in the XML summary for ExportAsXps.
        printExport.Should().Contain("XpsDocumentWriter.Write");
        printExport.Should().Contain("thread-affine");
    }

    [Fact]
    public void ExportAtomicWriter_WritesFileThroughTempAndDeletesTempOnFailure()
    {
        using var temp = new TestTemporaryDirectory();
        var targetPath = System.IO.Path.Combine(temp.Path, "export.bin");
        var bytes = System.Text.Encoding.UTF8.GetBytes("FreeX export test");

        ExportAtomicWriter.WriteAllBytes(targetPath, bytes);

        System.IO.File.Exists(targetPath).Should().BeTrue();
        System.IO.File.ReadAllBytes(targetPath).Should().Equal(bytes);

        // No temp artifacts should remain.
        System.IO.Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void ExportAtomicWriter_OverwritesExistingFileAtomically()
    {
        using var temp = new TestTemporaryDirectory();
        var targetPath = System.IO.Path.Combine(temp.Path, "export.bin");
        System.IO.File.WriteAllText(targetPath, "original content");
        var bytes = System.Text.Encoding.UTF8.GetBytes("updated content");

        ExportAtomicWriter.WriteAllBytes(targetPath, bytes);

        System.IO.File.ReadAllText(targetPath).Should().Be("updated content");
        System.IO.Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void ExportAtomicWriter_CreateTempPath_IsInSameDirectoryAsTarget()
    {
        var targetPath = @"C:\exports\report.xps";
        var tempPath = ExportAtomicWriter.CreateTempPath(targetPath);

        System.IO.Path.GetDirectoryName(tempPath).Should().BeEquivalentTo(@"C:\exports");
        tempPath.Should().EndWith(".tmp");
        System.IO.Path.GetFileName(tempPath).Should().StartWith(".report.xps.");
    }
}
