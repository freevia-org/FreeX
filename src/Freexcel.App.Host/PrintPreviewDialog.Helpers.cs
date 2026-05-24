using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class PrintPreviewDialog
{
    public static string CreateTitle(string workbookName) =>
        $"Print Preview - {workbookName.Trim()}";

    public static int NormalizeCopyCount(string? text)
    {
        if (!int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var copies))
            return 1;

        return Math.Clamp(copies, 1, 999);
    }

    private static void ShowNativePrintDialog(FixedDocument document, PrintQueue? printQueue, int copies)
    {
        var dialog = new PrintDialog();
        if (printQueue is not null)
            dialog.PrintQueue = printQueue;

        copies = NormalizeCopyCount(copies.ToString(CultureInfo.InvariantCulture));
        if (dialog.PrintTicket is not null)
            dialog.PrintTicket.CopyCount = copies;

        if (dialog.ShowDialog() == true)
            dialog.PrintDocument(document.DocumentPaginator, "Freexcel worksheet");
    }

    private static void PopulatePrinterBox(ComboBox printerBox)
    {
        try
        {
            using var server = new LocalPrintServer();
            foreach (var queue in server.GetPrintQueues())
                printerBox.Items.Add(queue);

            if (printerBox.Items.Count > 0)
            {
                printerBox.DisplayMemberPath = nameof(PrintQueue.FullName);
                printerBox.SelectedItem = printerBox.Items
                    .OfType<PrintQueue>()
                    .FirstOrDefault(queue => string.Equals(
                        queue.FullName,
                        server.DefaultPrintQueue.FullName,
                        StringComparison.OrdinalIgnoreCase));
                if (printerBox.SelectedItem is null)
                    printerBox.SelectedIndex = 0;

                return;
            }
        }
        catch (PrintSystemException)
        {
        }

        printerBox.IsEnabled = false;
        printerBox.ToolTip = "No installed printers were detected. Print opens the Windows print dialog so a printer can be chosen there.";
        AutomationProperties.SetHelpText(printerBox, "No installed printers were detected. Use Print to choose a printer in the Windows print dialog.");
    }

    private static void RefreshPrintStatus(TextBlock statusText, ComboBox printerBox, TextBox copiesBox, int totalPages)
    {
        var copies = NormalizeCopyCount(copiesBox.Text);
        var pages = totalPages == 1 ? "1 page" : $"{totalPages} pages";
        var copyText = copies == 1 ? "1 copy" : $"{copies} copies";
        var printerName = printerBox.SelectedItem is PrintQueue queue
            ? queue.FullName
            : "Windows print dialog";

        statusText.Text = $"Ready: {printerName}; {copyText}; {pages}";
    }

    private static void NavigateToPage(DocumentViewer viewer, TextBox pageNumberBox, TextBlock pageStatusText, int totalPages)
    {
        if (!int.TryParse(pageNumberBox.Text.Trim(), out var pageNumber))
            return;

        pageNumber = Math.Clamp(pageNumber, 1, totalPages);
        viewer.GoToPage(pageNumber);
        pageNumberBox.Text = pageNumber.ToString(CultureInfo.InvariantCulture);
        pageStatusText.Text = $"Page {pageNumber} of {totalPages}";
    }

    private static void FocusInitialKeyboardTarget(Button printButton)
    {
        printButton.Focus();
        Keyboard.Focus(printButton);
    }

    private static StackPanel BuildPrintSettingsPanel(
        SheetId sheetId,
        Sheet? sheet,
        Action<IWorkbookCommand>? executeCommand,
        Action refreshPreview)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(10, 10, 10, 10),
            Orientation = Orientation.Vertical
        };

        void AddLabel(string text) =>
            panel.Children.Add(new TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 10, 0, 2),
                FontWeight = FontWeights.SemiBold
            });

        ComboBox MakeComboBox(string[] items, int selectedIndex)
        {
            var box = new ComboBox { Margin = new Thickness(0, 0, 0, 2) };
            foreach (var item in items)
                box.Items.Add(item);
            box.SelectedIndex = selectedIndex;
            return box;
        }

        // Orientation
        AddLabel("Orientation");
        var orientIndex = sheet?.PageOrientation == WorksheetPageOrientation.Landscape ? 1 : 0;
        var orientBox = MakeComboBox(["Portrait", "Landscape"], orientIndex);
        orientBox.SelectionChanged += (_, _) =>
        {
            if (orientBox.SelectedIndex < 0 || executeCommand is null) return;
            var orient = orientBox.SelectedIndex == 1
                ? WorksheetPageOrientation.Landscape
                : WorksheetPageOrientation.Portrait;
            executeCommand(new SetPageOrientationCommand(sheetId, orient));
            refreshPreview();
        };
        panel.Children.Add(orientBox);

        // Paper Size
        AddLabel("Paper Size");
        var paperIndex = sheet?.PaperSize switch
        {
            WorksheetPaperSize.Letter => 1,
            WorksheetPaperSize.Legal  => 2,
            _                         => 0
        };
        var paperBox = MakeComboBox(["A4", "Letter", "Legal"], paperIndex);
        paperBox.SelectionChanged += (_, _) =>
        {
            if (paperBox.SelectedIndex < 0 || executeCommand is null) return;
            var size = paperBox.SelectedIndex switch
            {
                1 => WorksheetPaperSize.Letter,
                2 => WorksheetPaperSize.Legal,
                _ => WorksheetPaperSize.A4
            };
            executeCommand(new SetPaperSizeCommand(sheetId, size));
            refreshPreview();
        };
        panel.Children.Add(paperBox);

        // Margins
        AddLabel("Margins");
        int marginsIndex;
        if (sheet?.PageMargins == WorksheetPageMargins.Normal)
            marginsIndex = 1;
        else if (sheet?.PageMargins == WorksheetPageMargins.Wide)
            marginsIndex = 2;
        else
            marginsIndex = 0;
        var marginsBox = MakeComboBox(["Narrow", "Normal", "Wide"], marginsIndex);
        marginsBox.SelectionChanged += (_, _) =>
        {
            if (marginsBox.SelectedIndex < 0 || executeCommand is null) return;
            var margins = marginsBox.SelectedIndex switch
            {
                1 => WorksheetPageMargins.Normal,
                2 => WorksheetPageMargins.Wide,
                _ => WorksheetPageMargins.Narrow
            };
            executeCommand(new SetPageMarginsCommand(sheetId, margins));
            refreshPreview();
        };
        panel.Children.Add(marginsBox);

        // Scaling
        AddLabel("Scaling");
        var stf = sheet?.ScaleToFit ?? WorksheetScaleToFit.Default;
        int scaleIndex;
        if (stf.FitToPagesWide == 1 && stf.FitToPagesTall == 1)
            scaleIndex = 1;
        else if (stf.FitToPagesWide == 1 && stf.FitToPagesTall is null)
            scaleIndex = 2;
        else
            scaleIndex = 0;
        var scaleBox = MakeComboBox(["100%", "Fit to 1 Page", "Fit to 1 Page Wide"], scaleIndex);
        scaleBox.SelectionChanged += (_, _) =>
        {
            if (scaleBox.SelectedIndex < 0 || executeCommand is null) return;
            var scale = scaleBox.SelectedIndex switch
            {
                1 => new WorksheetScaleToFit(null, 1, 1),
                2 => new WorksheetScaleToFit(null, 1, null),
                _ => WorksheetScaleToFit.Default
            };
            executeCommand(new SetScaleToFitCommand(sheetId, scale));
            refreshPreview();
        };
        panel.Children.Add(scaleBox);

        return panel;
    }
}
