using System;
using System.Windows;
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
        var dialog = new PrintPreviewDialog(_workbook.Name, doc, settings) { Owner = this };
        dialog.ShowDialog();
    }

    private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        var saveDlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Export as PDF / XPS",
            Filter     = "PDF files (*.pdf)|*.pdf|XPS files (*.xps)|*.xps",
            DefaultExt = ".pdf",
            FileName   = _workbook.Name
        };
        if (saveDlg.ShowDialog() != true) return;

        var request = ExportPlanner.PlanExport(saveDlg.FileName);
        if (request.Format == ExportFormat.PdfViaWindowsPrinter)
            ExportViaPrintToPdf(request);
        else
            ExportAsXps(request.Path, ExportPlanner.DescribeOptions(request.Options));
    }

    /// <summary>
    /// Handles PDF requests through the deterministic XPS export path. WPF's managed
    /// print APIs cannot set the target PDF file path for virtual PDF printers.
    /// </summary>
    private void ExportViaPrintToPdf(ExportRequest request)
    {
        ExportPdfFallbackAsXps(request);
    }

    private void ExportPdfFallbackAsXps(ExportRequest request)
    {
        if (!ExportAsXps(request.ActualPath, showSuccessMessage: false))
            return;

        MessageBox.Show(
            $"{ExportPlanner.DescribeRequest(request)}\n\nSaved XPS file:\n{request.ActualPath}",
            "Export PDF",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// Writes the current sheet as an XPS package to <paramref name="xpsPath"/>.
    /// Uses the internal <c>XpsDocumentWriter(XpsDocument)</c> constructor (available in
    /// ReachFramework on .NET 10 / .NET Framework) to write directly to a file without
    /// showing a print dialog.
    /// </summary>

    private bool ExportAsXps(string xpsPath, string? optionSummary = null, bool showSuccessMessage = true)
    {
        try
        {
            var doc = PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService);

            // Open the XPS package for write
            var pkg = System.IO.Packaging.Package.Open(
                xpsPath,
                System.IO.FileMode.Create,
                System.IO.FileAccess.ReadWrite);

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
            writer.Write(doc.DocumentPaginator);
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

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to save XPS file:\n{ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }
}
