using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    /// <summary>
    /// Directory that contains the workbook's saved file, with a trailing separator, for the
    /// print/export header/footer <c>&amp;Z</c> / <c>&amp;[Path]</c> tokens
    /// (R15-header-footer-print-titles-2); empty when the workbook has never been saved.
    /// </summary>
    private string ResolveWorkbookDirectoryForHeaderFooter() =>
        Path.GetDirectoryName(_currentFilePath) is { Length: > 0 } directory
            ? directory + Path.DirectorySeparatorChar
            : "";

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        var workflowPlan = WorkbookPrintWorkflow.CreatePlan(
            _workbook,
            SheetGrid.SelectedRange is not null,
            new PrintJobRequest(
                WorkbookExportPrintScope.ActiveSheet,
                ActiveSheetIndex: _workbook.ActiveSheetIndex),
            PrintExportHostCapabilities.WindowsWpf());
        if (!workflowPlan.IsReady)
            return;

        var doc = PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService, workbookDirectory: ResolveWorkbookDirectoryForHeaderFooter());
        var sheet = _workbook.GetSheet(_currentSheetId);
        var settings = sheet is null
            ? new PrintSettingsPlan([UiText.Get("MainWindowPrintSettings_ActiveSheet")])
            : PrintSettingsPlanner.Build(sheet, textResolver: WpfPrintSettingsTextResolver.Instance);
        var dialog = new PrintPreviewDialog(
            _workbook.Name,
            doc,
            settings,
            showMargins: () => PageMarginsBtn_Click(this, new RoutedEventArgs()),
            showPageSetup: () => PageSetupDialogBtn_Click(this, new RoutedEventArgs()),
            refreshPreviewWithSettings: BuildActiveSheetPrintPreview,
            sheetId: _currentSheetId,
            sheet: sheet,
            executeCommand: cmd => TryExecuteCommand(cmd, "Print Settings"))
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private (FixedDocument Document, PrintSettingsPlan Settings) BuildActiveSheetPrintPreview(PrintPreviewSettings settings)
    {
        FixedDocument document;
        switch (settings.PrintWhat)
        {
            case PrintWhat.EntireWorkbook:
                document = PrintRenderer.RenderWorkbook(_workbook, _viewportService, settings.IgnorePrintArea, ResolveWorkbookDirectoryForHeaderFooter());
                break;

            case PrintWhat.Selection:
            {
                var selectionRange = SheetGrid.SelectedRange;
                document = PrintRenderer.RenderWorksheet(
                    _workbook,
                    _currentSheetId,
                    _viewportService,
                    printRangeOverride: selectionRange,
                    ignorePrintArea: true,
                    workbookDirectory: ResolveWorkbookDirectoryForHeaderFooter());
                break;
            }

            default: // ActiveSheets
                document = PrintRenderer.RenderWorksheet(
                    _workbook,
                    _currentSheetId,
                    _viewportService,
                    ignorePrintArea: settings.IgnorePrintArea,
                    workbookDirectory: ResolveWorkbookDirectoryForHeaderFooter());
                break;
        }

        // Apply page range subset to the preview document when a range is specified.
        if ((settings.PageFrom.HasValue || settings.PageTo.HasValue) && document.Pages.Count > 0)
        {
            if (PrintSettingsPlanner.TryValidatePageRange(
                    settings.PageFrom, settings.PageTo, document.Pages.Count,
                    out var from, out var to))
            {
                var rangedDoc = new FixedDocument();
                rangedDoc.DocumentPaginator.PageSize = document.DocumentPaginator.PageSize;
                // Each source PageContent (and its FixedPage) is still a logical child of the
                // source 'document'; WPF's PageContentCollection.Add throws InvalidOperationException
                // ("already the logical child of another element") if we add one still parented
                // elsewhere, and PageContentCollection exposes no Remove. So detach each selected
                // page's FixedPage and re-wrap it in a fresh PageContent added to rangedDoc.
                // 'document' is discarded once this subset is built (reassigned below, never read).
                var selectedPages = new List<PageContent>();
                for (var i = from - 1; i <= to - 1 && i < document.Pages.Count; i++)
                    selectedPages.Add(document.Pages[i]);
                foreach (var page in selectedPages)
                {
                    var fixedPage = page.Child;
                    page.Child = null;
                    var moved = new PageContent();
                    ((IAddChild)moved).AddChild(fixedPage);
                    rangedDoc.Pages.Add(moved);
                }
                document = rangedDoc;
            }
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        var plan = sheet is null
            ? new PrintSettingsPlan([UiText.Get("MainWindowPrintSettings_ActiveSheet")])
            : PrintSettingsPlanner.Build(
                sheet,
                settings.IgnorePrintArea,
                WpfPrintSettingsTextResolver.Instance);
        return (document, plan);
    }

    private async void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        var savePlan = ExportFilePickerPlanner.BuildPdfXpsDialogPlan(_workbook.Name, "FreeX");
        var saveResult = WpfFileDialogService.ShowSaveDialog(
            this,
            UiText.Get("MainWindowDialog_ExportPdfXpsFilter"),
            savePlan.SuggestedFileName,
            savePlan.DefaultExtensionWithDot,
            savePlan.DefaultFilterIndex,
            UiText.Get("MainWindowDialog_ExportPdfXpsTitle"));
        if (!saveResult.Chosen) return;

        var selectedExportFileFormat = ExportFilePickerPlanner.FormatFromPdfXpsFilterIndex(saveResult.FilterIndex);
        var selectedFormat = selectedExportFileFormat == ExportFileFormat.Xps
            ? ExportFormat.Xps
            : ExportFormat.Pdf;
        var optionsDialog = new ExportOptionsDialog(SheetGrid.SelectedRange is not null, _options.PdfExportLanguage, selectedFormat) { Owner = this };
        if (optionsDialog.ShowDialog() != true)
            return;

        if (selectedFormat == ExportFormat.Pdf)
        {
            _options.PdfExportLanguage = optionsDialog.Result.PdfLanguage;
            AppOptionsStore.Save(_options);
        }

        var request = ExportPlanner.PlanExport(saveResult.FileName!, selectedFormat, optionsDialog.Result);
        if (ExportPlanner.ShouldPromptForNormalizedOverwrite(saveResult.FileName!, request, File.Exists) &&
            ShowOwnedMessage(
                UiText.Format("MainWindowMessage_ExportNormalizedOverwritePrompt", request.Path),
                UiText.Get("MainWindowDialog_ExportPdfXpsTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var exportResult = await WorkbookExportWorkflow.ExecuteBooleanAsync(
            request,
            (effectiveRequest, _) => effectiveRequest.Format == ExportFormat.Pdf
                ? ExportAsPdf(
                    effectiveRequest.Path,
                    WpfExportDescriptionPlanner.DescribeRequest(effectiveRequest),
                    effectiveRequest.Options)
                : ExportAsXps(
                    effectiveRequest.Path,
                    WpfExportDescriptionPlanner.DescribeRequest(effectiveRequest),
                    effectiveRequest.Options),
            WpfExportPlannerTextResolver.Instance);
        if (exportResult.Outcome == WorkbookExportExecutionOutcome.ValidationFailed)
        {
            ShowOwnedMessage(
                exportResult.Message,
                UiText.Get("MainWindowMessage_ExportOptionsTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var exported = exportResult.Succeeded;
        if (exported && request.Options.OpenAfterPublish)
            OpenExportedFile(request.ActualPath);
        // Return to the workbook after a successful export instead of leaving the user
        // stranded in the File backstage (Issue 118).
        if (exported && IsStartScreenVisible())
            HideStartScreen();
    }

    private async Task<bool> ExportAsPdf(string pdfPath, string optionSummary, ExportOptions options)
    {
        if (_isExportingFile)
            return false;

        try
        {
            _isExportingFile = true;
            RootGrid.IsEnabled = false;
            ShowSaveProgress(
                UiText.Get("Progress_ExportingFile"),
                UiText.Get("Progress_ExportingFileRendering"),
                null);

            var effectiveOptions = ExportPlanner.CreateEffectiveOptionsForFormat(options, ExportFormat.Pdf);
            if (!ExportPlanner.TryValidatePublishOptions(effectiveOptions, ExportFormat.Pdf, out var publishOptionsError, WpfExportPlannerTextResolver.Instance))
                throw new InvalidOperationException(publishOptionsError);

            var document = RenderExportDocument(effectiveOptions);
            if (!ExportPlanner.TryValidatePageRange(effectiveOptions.PageRange, document.Pages.Count, out var pageRangeError, WpfExportPlannerTextResolver.Instance))
                throw new InvalidOperationException(pageRangeError);

            var properties = PdfDocumentProperties.FromWorkbook(_workbook, effectiveOptions);
            var bookmarks = CreatePdfBookmarks(effectiveOptions);

            // Render the PDF bytes on the UI thread (WPF visual tree access), then flush to disk on a
            // background thread via temp+replace so the disk write does not block the message pump.
            var pdfBytes = PdfDocumentExporter.RenderToBytes(
                document,
                properties,
                effectiveOptions.PageRange,
                effectiveOptions.Quality,
                bookmarks,
                effectiveOptions.InitialView,
                effectiveOptions.OpenMode,
                includeSelectableText: !effectiveOptions.BitmapTextWhenFontsMayNotBeEmbedded,
                pdfLanguage: effectiveOptions.PdfLanguage);

            ShowSaveProgress(
                UiText.Get("Progress_ExportingFile"),
                UiText.Get("Progress_ExportingFileWriting"),
                50);

            await Task.Run(() => ExportAtomicWriter.WriteAllBytes(pdfPath, pdfBytes));

            ShowOwnedMessage(
                UiText.Format("MainWindowMessage_ExportPdfSavedFormat", optionSummary, pdfPath),
                UiText.Get("MainWindowMessage_ExportPdfTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RecordDiagnosticEvent("export_completed", new Dictionary<string, string?>
            {
                ["fileType"] = "pdf",
                ["format"] = "pdf",
                ["scope"] = effectiveOptions.Scope.ToString()
            });
            return true;
        }
        catch (Exception ex)
        {
            RecordDiagnosticEvent("export_failed", new Dictionary<string, string?>
            {
                ["fileType"] = "pdf",
                ["format"] = "pdf",
                ["scope"] = options.Scope.ToString(),
                ["reason"] = ex.GetType().Name
            });
            ShowOwnedMessage(
                UiText.Format("MainWindowMessage_ExportPdfFailed", ex.Message),
                UiText.Get("MainWindowMessage_ExportErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _isExportingFile = false;
            RootGrid.IsEnabled = true;
            HideSaveProgress();
        }
    }

    /// <summary>
    /// Writes the current sheet as an XPS package to <paramref name="xpsPath"/>.
    /// Uses the internal <c>XpsDocumentWriter(XpsDocument)</c> constructor (available in
    /// ReachFramework on .NET 10 / .NET Framework) to write directly to a file without
    /// showing a print dialog.
    /// <para>
    /// The XPS write stays synchronous on the UI thread because <c>XpsDocumentWriter.Write</c>
    /// drives the WPF visual tree (DocumentPaginator) and is thread-affine; promoting it to a
    /// background thread would require a second STA thread and carries high regression risk.
    /// Input is blocked and a progress indicator is shown for the duration so the window does
    /// not appear frozen ("Not Responding") to the shell.
    /// </para>
    /// </summary>
    private async Task<bool> ExportAsXps(
        string xpsPath,
        string? optionSummary,
        ExportOptions options,
        bool showSuccessMessage = true)
    {
        if (_isExportingFile)
            return false;

        try
        {
            _isExportingFile = true;
            RootGrid.IsEnabled = false;
            ShowSaveProgress(
                UiText.Get("Progress_ExportingFile"),
                UiText.Get("Progress_ExportingFileRendering"),
                null);

            var effectiveOptions = ExportPlanner.CreateEffectiveOptionsForFormat(options, ExportFormat.Xps);
            if (!ExportPlanner.TryValidatePublishOptions(effectiveOptions, ExportFormat.Xps, out var publishOptionsError, WpfExportPlannerTextResolver.Instance))
                throw new InvalidOperationException(publishOptionsError);

            var paginator = RenderExportPaginator(effectiveOptions);

            ShowSaveProgress(
                UiText.Get("Progress_ExportingFile"),
                UiText.Get("Progress_ExportingFileWriting"),
                50);

            // Allow at least one render pass so the progress indicator becomes visible before the
            // synchronous XPS write begins.
            await Task.Yield();

            // Write to a sibling temp file so that a mid-write failure does not corrupt or lock the
            // destination the user chose, then atomically replace the destination on success.
            var tempPath = ExportAtomicWriter.CreateTempPath(xpsPath);
            try
            {
                // Open the XPS package for write and close it before replacing the destination.
                // XpsDocument takes ownership of the package when constructed — the package is
                // disposed when XpsDocument is disposed, which is why the package using-block must
                // nest OUTSIDE the XpsDocument using-block.
                using (var pkg = System.IO.Packaging.Package.Open(
                    tempPath,
                    System.IO.FileMode.Create,
                    System.IO.FileAccess.ReadWrite))
                {
                    XpsDocumentProperties.ApplyToPackage(pkg, XpsDocumentProperties.FromWorkbook(_workbook, effectiveOptions));

                    using var xpsDoc = new System.Windows.Xps.Packaging.XpsDocument(pkg);

                    // XpsDocumentWriter(XpsDocument) is internal in ReachFramework; create it via reflection
                    var writerType = typeof(System.Windows.Xps.XpsDocumentWriter);
                    var ctor = writerType.GetConstructor(
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                        null,
                        [typeof(System.Windows.Xps.Packaging.XpsDocument)],
                        null);

                    if (ctor == null)
                        throw new InvalidOperationException("XpsDocumentWriter(XpsDocument) constructor not found in ReachFramework.");

                    var writer = (System.Windows.Xps.XpsDocumentWriter)ctor.Invoke([xpsDoc]);
                    writer.Write(paginator);
                }

                ExportAtomicWriter.ReplaceTarget(tempPath, xpsPath);
            }
            catch
            {
                // On any failure ensure the temp artifact is cleaned up.  The destination is
                // untouched — ReplaceTarget has not been called yet.
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* best effort */ }
                }

                throw;
            }

            if (showSuccessMessage)
            {
                var detail = string.IsNullOrWhiteSpace(optionSummary)
                    ? UiText.Format("MainWindowMessage_ExportXpsSavedFormat", xpsPath)
                    : UiText.Format("MainWindowMessage_ExportXpsSavedWithOptionsFormat", optionSummary, xpsPath);
                ShowOwnedMessage(
                    detail,
                    UiText.Get("MainWindowMessage_ExportXpsTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            RecordDiagnosticEvent("export_completed", new Dictionary<string, string?>
            {
                ["fileType"] = "xps",
                ["format"] = "xps",
                ["scope"] = effectiveOptions.Scope.ToString()
            });
            return true;
        }
        catch (Exception ex)
        {
            RecordDiagnosticEvent("export_failed", new Dictionary<string, string?>
            {
                ["fileType"] = "xps",
                ["format"] = "xps",
                ["scope"] = options.Scope.ToString(),
                ["reason"] = ex.GetType().Name
            });
            ShowOwnedMessage(
                UiText.Format("MainWindowMessage_ExportXpsFailed", ex.Message),
                UiText.Get("MainWindowMessage_ExportErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _isExportingFile = false;
            RootGrid.IsEnabled = true;
            HideSaveProgress();
        }
    }

    private GridRange? ResolveExportRange(ExportOptions options) =>
        options.Scope == ExportContentScope.Selection
            ? SheetGrid.SelectedRange
            : null;

    private System.Windows.Documents.FixedDocument RenderExportDocument(ExportOptions options) =>
        options.Scope == ExportContentScope.EntireWorkbook
            ? PrintRenderer.RenderWorkbook(_workbook, _viewportService, options.IgnorePrintAreas, ResolveWorkbookDirectoryForHeaderFooter())
            : RenderExportSheets(
                WorkbookExportSheetSelectionPlanner.ResolveSheetIds(_workbook, options, _currentSheetId, _groupedSheetIds),
                options);

    private System.Windows.Documents.FixedDocument RenderExportSheets(
        IReadOnlyList<SheetId> sheetIds,
        ExportOptions options)
    {
        if (sheetIds.Count == 1)
        {
            var sheetId = sheetIds[0];
            return PrintRenderer.RenderWorksheet(
                _workbook,
                sheetId,
                _viewportService,
                options.Scope == ExportContentScope.Selection && sheetId == _currentSheetId
                    ? ResolveExportRange(options)
                    : null,
                options.IgnorePrintAreas,
                workbookDirectory: ResolveWorkbookDirectoryForHeaderFooter());
        }

        var result = new FixedDocument();
        foreach (var sheetId in sheetIds)
        {
            var document = PrintRenderer.RenderWorksheet(
                _workbook,
                sheetId,
                _viewportService,
                options.Scope == ExportContentScope.Selection && sheetId == _currentSheetId
                    ? ResolveExportRange(options)
                    : null,
                options.IgnorePrintAreas,
                workbookDirectory: ResolveWorkbookDirectoryForHeaderFooter());
            if (result.Pages.Count == 0)
                result.DocumentPaginator.PageSize = document.DocumentPaginator.PageSize;

            foreach (var page in document.Pages)
                result.Pages.Add(CloneExportPage(document, page));
        }

        return result;
    }

    private static PageContent CloneExportPage(FixedDocument document, PageContent pageContent)
    {
        pageContent.GetPageRoot(forceReload: false);
        var sourcePage = pageContent.Child ??
            throw new InvalidOperationException("FixedDocument page content did not contain a FixedPage.");
        var width = sourcePage.Width > 0 && !double.IsNaN(sourcePage.Width)
            ? sourcePage.Width
            : document.DocumentPaginator.PageSize.Width;
        var height = sourcePage.Height > 0 && !double.IsNaN(sourcePage.Height)
            ? sourcePage.Height
            : document.DocumentPaginator.PageSize.Height;
        var size = new Size(width, height);
        sourcePage.Measure(size);
        sourcePage.Arrange(new Rect(size));
        sourcePage.UpdateLayout();
        var textOverlays = PdfTextOverlayExtractor.Extract(sourcePage);
        var linkOverlays = PdfLinkOverlayExtractor.Extract(sourcePage);
        var cellDestinationOverlays = PdfCellDestinationOverlayExtractor.Extract(sourcePage);

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(width)),
            Math.Max(1, (int)Math.Ceiling(height)),
            96,
            96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(sourcePage);
        bitmap.Freeze();

        var fixedPage = new FixedPage { Width = width, Height = height };
        fixedPage.Children.Add(new System.Windows.Controls.Image
        {
            Source = bitmap,
            Width = width,
            Height = height
        });
        if (textOverlays.Count > 0 || linkOverlays.Count > 0 || cellDestinationOverlays.Count > 0)
        {
            fixedPage.Children.Add(new VisualHost
            {
                TextOverlays = textOverlays,
                LinkOverlays = linkOverlays,
                CellDestinationOverlays = cellDestinationOverlays
            });
        }

        var clone = new PageContent();
        ((System.Windows.Markup.IAddChild)clone).AddChild(fixedPage);
        return clone;
    }

    private IReadOnlyList<PdfBookmark>? CreatePdfBookmarks(ExportOptions options)
    {
        if (options.EffectiveBookmarkMode == PdfBookmarkMode.None)
            return null;

        var result = new List<PdfBookmark>();
        var pageIndex = 0;
        var sheets = WorkbookExportSheetSelectionPlanner
            .ResolveSheetIds(_workbook, options, _currentSheetId, _groupedSheetIds)
            .Select(_workbook.GetSheet)
            .OfType<Sheet>();

        foreach (var sheet in sheets)
        {
            var range = options.Scope == ExportContentScope.Selection && sheet.Id == _currentSheetId
                ? ResolveExportRange(options)
                : null;
            var document = PrintRenderer.RenderWorksheet(
                _workbook,
                sheet.Id,
                _viewportService,
                range,
                options.IgnorePrintAreas,
                workbookDirectory: ResolveWorkbookDirectoryForHeaderFooter());
            if (document.Pages.Count > 0)
            {
                if (options.EffectiveBookmarkMode == PdfBookmarkMode.PageNumbers)
                {
                    for (var offset = 0; offset < document.Pages.Count; offset++)
                        result.Add(new PdfBookmark($"Page {pageIndex + 1 + offset}", pageIndex + offset));
                }
                else
                {
                    var title = options.EffectiveBookmarkMode == PdfBookmarkMode.PrintTitles
                        ? BuildPrintTitleBookmark(sheet)
                        : sheet.Name;
                    result.Add(new PdfBookmark(title, pageIndex));
                }
            }
            pageIndex += document.Pages.Count;
        }

        return result;
    }

    private static string BuildPrintTitleBookmark(Sheet sheet)
    {
        var parts = new List<string>();
        if (sheet.PrintTitleRows is { } rows)
            parts.Add(rows.Start == rows.End ? $"Rows {rows.Start}" : $"Rows {rows.Start}-{rows.End}");
        if (sheet.PrintTitleColumns is { } columns)
            parts.Add(columns.Start == columns.End ? $"Columns {columns.Start}" : $"Columns {columns.Start}-{columns.End}");

        return parts.Count == 0
            ? sheet.Name
            : $"{sheet.Name} ({string.Join(", ", parts)})";
    }

    private System.Windows.Documents.DocumentPaginator RenderExportPaginator(ExportOptions options)
    {
        var paginator = options.Scope == ExportContentScope.EntireWorkbook
            ? PrintRenderer.CreateWorkbookPaginator(_workbook, _viewportService, options.IgnorePrintAreas, ResolveWorkbookDirectoryForHeaderFooter())
            : RenderExportDocument(options).DocumentPaginator;

        if (!ExportPlanner.TryValidatePageRange(options.PageRange, paginator.PageCount, out var pageRangeError, WpfExportPlannerTextResolver.Instance))
            throw new InvalidOperationException(pageRangeError);

        return ApplyExportPageRange(options, paginator);
    }

    private static System.Windows.Documents.DocumentPaginator ApplyExportPageRange(
        ExportOptions options,
        System.Windows.Documents.DocumentPaginator paginator) =>
        options.PageRange is { } pageRange
            ? new PageRangeDocumentPaginator(paginator, pageRange)
            : paginator;

    private static void OpenExportedFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // Export has already succeeded; opening the shell association is best effort.
        }
    }
}
