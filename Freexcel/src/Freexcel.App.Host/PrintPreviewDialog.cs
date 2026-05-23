using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace Freexcel.App.Host;

public sealed class PrintPreviewDialog : Window
{
    public PrintPreviewDialog(string workbookName, FixedDocument document)
        : this(workbookName, document, new PrintSettingsPlan(["Print active sheet"]))
    {
    }

    public PrintPreviewDialog(
        string workbookName,
        FixedDocument document,
        PrintSettingsPlan settings,
        Action? showMargins = null,
        Action? showPageSetup = null,
        Func<(FixedDocument Document, PrintSettingsPlan Settings)>? refreshPreview = null)
    {
        Title = CreateTitle(workbookName);
        Width = 900;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel();
        var toolbar = new ToolBar
        {
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var previewDocument = document;
        var viewer = new DocumentViewer { Document = previewDocument };
        var totalPages = Math.Max(1, previewDocument.Pages.Count);
        var printerBox = new ComboBox
        {
            Width = 190,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Choose the printer used when Print opens the Windows print dialog."
        };
        AutomationProperties.SetName(printerBox, "Printer");
        AutomationProperties.SetHelpText(printerBox, "Selects the initial printer for the Windows print dialog.");
        PopulatePrinterBox(printerBox);
        var copiesBox = new TextBox
        {
            Width = 44,
            Text = "1",
            Margin = new Thickness(0, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Number of copies to send to the Windows print dialog."
        };
        AutomationProperties.SetName(copiesBox, "Copies");
        AutomationProperties.SetHelpText(copiesBox, "Enter a copy count from 1 to 999.");
        var statusText = new TextBlock
        {
            Margin = new Thickness(4, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 280
        };
        AutomationProperties.SetName(statusText, "Print status");
        AutomationProperties.SetHelpText(statusText, "Shows the selected printer, copy count, and preview page count.");
        var firstButton = new Button
        {
            Content = "_First Page",
            Padding = new Thickness(10, 4, 10, 4),
            Command = NavigationCommands.FirstPage,
            CommandTarget = viewer
        };
        var previousButton = new Button
        {
            Content = "_Previous Page",
            Padding = new Thickness(10, 4, 10, 4),
            Command = NavigationCommands.PreviousPage,
            CommandTarget = viewer
        };
        var nextButton = new Button
        {
            Content = "_Next Page",
            Padding = new Thickness(10, 4, 10, 4),
            Command = NavigationCommands.NextPage,
            CommandTarget = viewer
        };
        var lastButton = new Button
        {
            Content = "_Last Page",
            Padding = new Thickness(10, 4, 10, 4),
            Command = NavigationCommands.LastPage,
            CommandTarget = viewer
        };
        var printButton = new Button
        {
            Content = "_Print...",
            Padding = new Thickness(12, 4, 12, 4),
            ToolTip = "Open the Windows print dialog with the selected printer and copy count."
        };
        var closeButton = new Button
        {
            Content = "_Close Preview",
            Padding = new Thickness(12, 4, 12, 4),
            ToolTip = "Return to the workbook."
        };
        AutomationProperties.SetName(printButton, "Print");
        AutomationProperties.SetHelpText(printButton, "Opens the Windows print dialog and applies the selected printer and copies when possible.");
        printButton.Click += (_, _) =>
        {
            var copies = NormalizeCopyCount(copiesBox.Text);
            copiesBox.Text = copies.ToString(CultureInfo.InvariantCulture);
            ShowNativePrintDialog(previewDocument, printerBox.SelectedItem as PrintQueue, copies);
            RefreshPrintStatus(statusText, printerBox, copiesBox, totalPages);
        };
        closeButton.Click += (_, _) => Close();
        printerBox.SelectionChanged += (_, _) => RefreshPrintStatus(statusText, printerBox, copiesBox, totalPages);
        copiesBox.TextChanged += (_, _) => RefreshPrintStatus(statusText, printerBox, copiesBox, totalPages);
        RefreshPrintStatus(statusText, printerBox, copiesBox, totalPages);
        toolbar.Items.Add(printButton);
        toolbar.Items.Add(new Label
        {
            Content = "Pr_inter:",
            Target = printerBox,
            VerticalAlignment = VerticalAlignment.Center
        });
        toolbar.Items.Add(printerBox);
        toolbar.Items.Add(new Label
        {
            Content = "_Copies:",
            Target = copiesBox,
            VerticalAlignment = VerticalAlignment.Center
        });
        toolbar.Items.Add(copiesBox);
        toolbar.Items.Add(statusText);
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(firstButton);
        toolbar.Items.Add(previousButton);
        toolbar.Items.Add(nextButton);
        toolbar.Items.Add(lastButton);
        toolbar.Items.Add(new Separator());
        var pageNumberBox = new TextBox
        {
            Width = 44,
            Text = "1",
            Margin = new Thickness(0, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var pageStatusText = new TextBlock
        {
            Text = $"Page 1 of {totalPages}",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        toolbar.Items.Add(new Label
        {
            Content = "_Page:",
            Target = pageNumberBox,
            VerticalAlignment = VerticalAlignment.Center
        });
        pageNumberBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            NavigateToPage(viewer, pageNumberBox, pageStatusText, totalPages);
            e.Handled = true;
        };
        pageNumberBox.CommandBindings.Add(new CommandBinding(
            NavigationCommands.GoToPage,
            (_, e) =>
            {
                NavigateToPage(viewer, pageNumberBox, pageStatusText, totalPages);
                e.Handled = true;
            }));
        pageNumberBox.InputBindings.Add(new KeyBinding(NavigationCommands.GoToPage, new KeyGesture(Key.Enter)));
        toolbar.Items.Add(pageNumberBox);
        toolbar.Items.Add(pageStatusText);
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(new Label
        {
            Content = "_Zoom:",
            VerticalAlignment = VerticalAlignment.Center
        });
        var zoomBox = new ComboBox
        {
            Width = 82,
            SelectedIndex = 2
        };
        foreach (var zoom in new[] { "50%", "75%", "100%", "125%", "Page Width" })
            zoomBox.Items.Add(zoom);
        zoomBox.SelectionChanged += (_, _) =>
        {
            if (zoomBox.SelectedItem is not string value)
                return;

            if (value == "Page Width")
                viewer.FitToWidth();
            else if (double.TryParse(value.TrimEnd('%'), out var zoom))
                viewer.Zoom = zoom;
        };
        toolbar.Items.Add(zoomBox);
        toolbar.Items.Add(new Separator());
        TextBlock? settingsSummaryText = null;
        void RefreshPreviewDocument()
        {
            if (refreshPreview is null)
                return;

            var refreshed = refreshPreview();
            previewDocument = refreshed.Document;
            viewer.Document = previewDocument;
            totalPages = Math.Max(1, previewDocument.Pages.Count);
            pageNumberBox.Text = "1";
            pageStatusText.Text = $"Page 1 of {totalPages}";
            RefreshPrintStatus(statusText, printerBox, copiesBox, totalPages);
            if (settingsSummaryText is not null)
                settingsSummaryText.Text = refreshed.Settings.Summary;
        }

        var marginsButton = new Button
        {
            Content = "_Margins",
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = "Review worksheet margin settings before printing."
        };
        marginsButton.Click += (_, _) =>
        {
            showMargins?.Invoke();
            RefreshPreviewDocument();
        };
        toolbar.Items.Add(marginsButton);
        var pageSetupButton = new Button
        {
            Content = "Page _Setup...",
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = "Use Page Layout settings to change paper, orientation, margins, and scaling."
        };
        pageSetupButton.Click += (_, _) =>
        {
            showPageSetup?.Invoke();
            RefreshPreviewDocument();
        };
        toolbar.Items.Add(pageSetupButton);
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(closeButton);
        toolbar.Items.Add(new Separator());
        settingsSummaryText = new TextBlock
        {
            Text = settings.Summary,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 620
        };
        toolbar.Items.Add(settingsSummaryText);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(viewer);
        Content = root;
    }

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
}
