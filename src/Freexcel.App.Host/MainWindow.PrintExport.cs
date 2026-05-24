using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        var doc = PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService);
        var sheet = _workbook.GetSheet(_currentSheetId);
        var settings = sheet is null
            ? new PrintSettingsPlan(["Print active sheet"])
            : PrintSettingsPlanner.Build(sheet);
        var dialog = new PrintPreviewDialog(
            _workbook.Name,
            doc,
            settings,
            showMargins: () => PageMarginsBtn_Click(this, new RoutedEventArgs()),
            showPageSetup: () => PageSetupDialogBtn_Click(this, new RoutedEventArgs()),
            refreshPreview: BuildActiveSheetPrintPreview)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private (FixedDocument Document, PrintSettingsPlan Settings) BuildActiveSheetPrintPreview()
    {
        var document = PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService);
        var sheet = _workbook.GetSheet(_currentSheetId);
        var settings = sheet is null
            ? new PrintSettingsPlan(["Print active sheet"])
            : PrintSettingsPlanner.Build(sheet);
        return (document, settings);
    }

    private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        var optionsDialog = new ExportOptionsDialog(SheetGrid.SelectedRange is not null) { Owner = this };
        if (optionsDialog.ShowDialog() != true)
            return;

        var saveDlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Export as PDF / XPS",
            Filter     = "PDF files (*.pdf)|*.pdf|XPS files (*.xps)|*.xps",
            DefaultExt = ".pdf",
            FileName   = _workbook.Name
        };
        if (saveDlg.ShowDialog() != true) return;

        var selectedFormat = saveDlg.FilterIndex == 2
            ? ExportFormat.Xps
            : ExportFormat.Pdf;
        var request = ExportPlanner.PlanExport(saveDlg.FileName, selectedFormat, optionsDialog.Result);
        var exported = request.Format == ExportFormat.Pdf
            ? ExportAsPdf(request.Path, ExportPlanner.DescribeRequest(request), request.Options)
            : ExportAsXps(request.Path, ExportPlanner.DescribeRequest(request), request.Options);
        if (exported && request.Options.OpenAfterPublish)
            OpenExportedFile(request.ActualPath);
    }

    private bool ExportAsPdf(string pdfPath, string optionSummary, ExportOptions options)
    {
        try
        {
            var document = RenderExportDocument(options);
            if (!ExportPlanner.TryValidatePageRange(options.PageRange, document.Pages.Count, out var pageRangeError))
                throw new InvalidOperationException(pageRangeError);

            var properties = PdfDocumentProperties.FromWorkbook(_workbook, options);
            PdfDocumentExporter.Save(
                document,
                pdfPath,
                properties,
                options.PageRange,
                options.Quality,
                CreatePdfBookmarks(options),
                options.InitialView,
                options.OpenMode,
                includeSelectableText: !options.BitmapTextWhenFontsMayNotBeEmbedded,
                pdfLanguage: options.PdfLanguage);

            MessageBox.Show(
                $"{optionSummary}\n\nSaved PDF file:\n{pdfPath}",
                "Export PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RecordDiagnosticEvent("export_completed", new Dictionary<string, string?>
            {
                ["format"] = "pdf",
                ["scope"] = options.Scope.ToString()
            });
            return true;
        }
        catch (Exception ex)
        {
            RecordDiagnosticEvent("export_failed", new Dictionary<string, string?>
            {
                ["format"] = "pdf",
                ["scope"] = options.Scope.ToString(),
                ["reason"] = ex.GetType().Name
            });
            MessageBox.Show(
                $"Failed to save PDF file:\n{ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// Writes the current sheet as an XPS package to <paramref name="xpsPath"/>.
    /// Uses the internal <c>XpsDocumentWriter(XpsDocument)</c> constructor (available in
    /// ReachFramework on .NET 10 / .NET Framework) to write directly to a file without
    /// showing a print dialog.
    /// </summary>

    private bool ExportAsXps(
        string xpsPath,
        string? optionSummary,
        ExportOptions options,
        bool showSuccessMessage = true)
    {
        try
        {
            var paginator = RenderExportPaginator(options);

            // Open the XPS package for write
            var pkg = System.IO.Packaging.Package.Open(
                xpsPath,
                System.IO.FileMode.Create,
                System.IO.FileAccess.ReadWrite);
            XpsDocumentProperties.ApplyToPackage(pkg, XpsDocumentProperties.FromWorkbook(_workbook, options));

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
            // xpsDoc closed by 'using'

            if (showSuccessMessage)
            {
                var detail = string.IsNullOrWhiteSpace(optionSummary)
                    ? $"Saved XPS file:\n{xpsPath}"
                    : $"{optionSummary}\n\nSaved XPS file:\n{xpsPath}";
                MessageBox.Show(
                    detail,
                    "Export XPS",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            RecordDiagnosticEvent("export_completed", new Dictionary<string, string?>
            {
                ["format"] = "xps",
                ["scope"] = options.Scope.ToString()
            });
            return true;
        }
        catch (Exception ex)
        {
            RecordDiagnosticEvent("export_failed", new Dictionary<string, string?>
            {
                ["format"] = "xps",
                ["scope"] = options.Scope.ToString(),
                ["reason"] = ex.GetType().Name
            });
            MessageBox.Show(
                $"Failed to save XPS file:\n{ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private GridRange? ResolveExportRange(ExportOptions options) =>
        options.Scope == ExportContentScope.Selection
            ? SheetGrid.SelectedRange
            : null;

    private System.Windows.Documents.FixedDocument RenderExportDocument(ExportOptions options) =>
        options.Scope == ExportContentScope.EntireWorkbook
            ? PrintRenderer.RenderWorkbook(_workbook, _viewportService, options.IgnorePrintAreas)
            : PrintRenderer.RenderWorksheet(
                _workbook,
                _currentSheetId,
                _viewportService,
                ResolveExportRange(options),
                options.IgnorePrintAreas);

    private IReadOnlyList<PdfBookmark>? CreatePdfBookmarks(ExportOptions options)
    {
        if (options.EffectiveBookmarkMode == PdfBookmarkMode.None)
            return null;

        var result = new List<PdfBookmark>();
        var pageIndex = 0;
        IEnumerable<Sheet> sheets = options.Scope == ExportContentScope.EntireWorkbook
            ? _workbook.Sheets.Where(sheet => !sheet.IsHidden && !sheet.IsVeryHidden)
            : _workbook.GetSheet(_currentSheetId) is { } activeSheet
                ? [activeSheet]
                : [];

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
                options.IgnorePrintAreas);
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
            ? PrintRenderer.CreateWorkbookPaginator(_workbook, _viewportService, options.IgnorePrintAreas)
            : RenderExportDocument(options).DocumentPaginator;

        if (!ExportPlanner.TryValidatePageRange(options.PageRange, paginator.PageCount, out var pageRangeError))
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
