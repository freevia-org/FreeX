using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace FreeX.App.Host;

public sealed partial class PrintPreviewDialog : Window
{
    private void InitializePrintPreviewLayout(
        string workbookName,
        FixedDocument document,
        PrintSettingsPlan settings,
        Action? showMargins = null,
        Action? showPageSetup = null,
        Func<(FixedDocument Document, PrintSettingsPlan Settings)>? refreshPreview = null,
        Func<PrintPreviewSettings, (FixedDocument Document, PrintSettingsPlan Settings)>? refreshPreviewWithSettings = null,
        SheetId sheetId = default,
        Sheet? sheet = null,
        Action<IWorkbookCommand>? executeCommand = null)
    {
        Title = CreateTitle(workbookName);
        Width = 1120;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

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
        var collatedBox = new CheckBox
        {
            Content = "C_ollated",
            IsChecked = true,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Print multiple copies as collated sets when the printer supports collation."
        };
        AutomationProperties.SetName(collatedBox, "Collated");
        AutomationProperties.SetHelpText(collatedBox, "When checked, multiple copies print as collated sets.");
        var sidesBox = new ComboBox
        {
            Width = 178,
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Choose one-sided or duplex printing when the printer supports it."
        };
        sidesBox.Items.Add("Print One Sided");
        sidesBox.Items.Add("Flip pages on long edge");
        sidesBox.Items.Add("Flip pages on short edge");
        AutomationProperties.SetName(sidesBox, "Sides");
        AutomationProperties.SetHelpText(sidesBox, "Selects one-sided or two-sided duplex printing for the Windows print dialog.");
        var statusText = new TextBlock
        {
            Margin = new Thickness(4, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 280
        };
        AutomationProperties.SetName(statusText, "Print status");
        AutomationProperties.SetHelpText(statusText, "Shows the selected printer, copy count, and preview page count.");
        var selectedPageRangeMode = PrintPreviewPageRangeMode.AllPages;
        TextBox pageNumberBox = null!;
        TextBox fromPageBox = null!;
        TextBox toPageBox = null!;
        var firstButton = new Button
        {
            Content = "_First Page",
            Padding = new Thickness(10, 4, 10, 4),
            Command = NavigationCommands.FirstPage,
            CommandTarget = viewer
        };
        SetToolbarAutomation(firstButton, "PrintPreviewFirstPageButton", "First page", "Go to the first preview page.");
        var previousButton = new Button
        {
            Content = "_Previous Page",
            Padding = new Thickness(10, 4, 10, 4),
            Command = NavigationCommands.PreviousPage,
            CommandTarget = viewer
        };
        SetToolbarAutomation(previousButton, "PrintPreviewPreviousPageButton", "Previous page", "Go to the previous preview page.");
        var nextButton = new Button
        {
            Content = "_Next Page",
            Padding = new Thickness(10, 4, 10, 4),
            Command = NavigationCommands.NextPage,
            CommandTarget = viewer
        };
        SetToolbarAutomation(nextButton, "PrintPreviewNextPageButton", "Next page", "Go to the next preview page.");
        var lastButton = new Button
        {
            Content = "_Last Page",
            Padding = new Thickness(10, 4, 10, 4),
            Command = NavigationCommands.LastPage,
            CommandTarget = viewer
        };
        SetToolbarAutomation(lastButton, "PrintPreviewLastPageButton", "Last page", "Go to the last preview page.");
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
            IsCancel = true,
            ToolTip = "Return to the workbook."
        };
        AutomationProperties.SetAutomationId(printButton, "PrintPreviewPrintButton");
        AutomationProperties.SetName(printButton, "Print");
        AutomationProperties.SetHelpText(printButton, "Opens the Windows print dialog and applies the selected printer and copies when possible.");
        SetToolbarAutomation(closeButton, "PrintPreviewCloseButton", "Close preview", "Return to the workbook.");
        printButton.Click += (_, _) =>
        {
            if (!TryParseCopyCount(copiesBox.Text, out var copies))
            {
                ShowInvalidCopiesWarning(copiesBox);
                return;
            }

            copiesBox.Text = copies.ToString(CultureInfo.InvariantCulture);
            var currentPrintPage = 1;
            ExportPageRange? selectedPageRange = null;
            if (selectedPageRangeMode == PrintPreviewPageRangeMode.CurrentPage &&
                !TryParsePageNumber(pageNumberBox.Text, totalPages, out currentPrintPage))
            {
                ShowInvalidPageNumberWarning(pageNumberBox, totalPages);
                return;
            }
            if (selectedPageRangeMode == PrintPreviewPageRangeMode.Pages &&
                !ExportPlanner.TryCreatePageRange(fromPageBox.Text, toPageBox.Text, out selectedPageRange, out var pageRangeError))
            {
                ShowInvalidPageRangeWarning(fromPageBox, toPageBox, pageRangeError);
                return;
            }
            if (selectedPageRangeMode == PrintPreviewPageRangeMode.Pages &&
                !ExportPlanner.TryValidatePageRange(selectedPageRange, totalPages, out var validatedPageRangeError))
            {
                ShowInvalidPageRangeWarning(fromPageBox, toPageBox, validatedPageRangeError);
                return;
            }

            ShowNativePrintDialog(
                ResolvePrintPaginator(previewDocument, selectedPageRangeMode, currentPrintPage, selectedPageRange),
                printerBox.SelectedItem as PrintQueue,
                copies,
                collatedBox.IsChecked == true,
                ResolveSelectedSidesMode(sidesBox));
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
        toolbar.Items.Add(collatedBox);
        toolbar.Items.Add(new Label
        {
            Content = "_Sides:",
            Target = sidesBox,
            VerticalAlignment = VerticalAlignment.Center
        });
        toolbar.Items.Add(sidesBox);
        toolbar.Items.Add(statusText);
        toolbar.Items.Add(new Separator());
        var allPagesButton = new RadioButton
        {
            Content = "_All pages",
            IsChecked = true,
            GroupName = "PrintPageRange",
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Print every page in the preview."
        };
        var currentPageButton = new RadioButton
        {
            Content = "Current pag_e",
            GroupName = "PrintPageRange",
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Print only the page number shown in the Page box."
        };
        var pagesButton = new RadioButton
        {
            Content = "Pa_ges",
            GroupName = "PrintPageRange",
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Print only the entered page range."
        };
        fromPageBox = new TextBox
        {
            Width = 34,
            Text = "1",
            Margin = new Thickness(0, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = false
        };
        toPageBox = new TextBox
        {
            Width = 34,
            Text = totalPages.ToString(CultureInfo.InvariantCulture),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = false
        };
        void SetPageRangeBoxesEnabled(bool enabled)
        {
            fromPageBox.IsEnabled = enabled;
            toPageBox.IsEnabled = enabled;
        }

        allPagesButton.Checked += (_, _) => selectedPageRangeMode = PrintPreviewPageRangeMode.AllPages;
        currentPageButton.Checked += (_, _) => selectedPageRangeMode = PrintPreviewPageRangeMode.CurrentPage;
        pagesButton.Checked += (_, _) =>
        {
            selectedPageRangeMode = PrintPreviewPageRangeMode.Pages;
            SetPageRangeBoxesEnabled(true);
        };
        allPagesButton.Unchecked += (_, _) => SetPageRangeBoxesEnabled(pagesButton.IsChecked == true);
        currentPageButton.Unchecked += (_, _) => SetPageRangeBoxesEnabled(pagesButton.IsChecked == true);
        pagesButton.Unchecked += (_, _) => SetPageRangeBoxesEnabled(false);
        AutomationProperties.SetName(allPagesButton, "All pages");
        AutomationProperties.SetName(currentPageButton, "Current page");
        AutomationProperties.SetName(pagesButton, "Pages");
        AutomationProperties.SetName(fromPageBox, "From page");
        AutomationProperties.SetName(toPageBox, "To page");
        toolbar.Items.Add(allPagesButton);
        toolbar.Items.Add(currentPageButton);
        toolbar.Items.Add(pagesButton);
        toolbar.Items.Add(fromPageBox);
        toolbar.Items.Add(new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        toolbar.Items.Add(toPageBox);
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(firstButton);
        toolbar.Items.Add(previousButton);
        toolbar.Items.Add(nextButton);
        toolbar.Items.Add(lastButton);
        toolbar.Items.Add(new Separator());
        pageNumberBox = new TextBox
        {
            Width = 44,
            Text = "1",
            Margin = new Thickness(0, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var pageStatusText = new TextBlock
        {
            Text = CreateNavigationState(1, totalPages).StatusText,
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
        AutomationProperties.SetAutomationId(pageNumberBox, "PrintPreviewPageNumberBox");
        AutomationProperties.SetName(pageNumberBox, "Page number");
        AutomationProperties.SetHelpText(pageNumberBox, "Enter a preview page number and press Enter.");
        AutomationProperties.SetAutomationId(pageStatusText, "PrintPreviewPageStatusText");
        AutomationProperties.SetName(pageStatusText, "Page status");
        AutomationProperties.SetHelpText(pageStatusText, "Shows the current preview page and total page count.");
        toolbar.Items.Add(pageNumberBox);
        toolbar.Items.Add(pageStatusText);
        toolbar.Items.Add(new Separator());
        var zoomBox = new ComboBox
        {
            Width = 82,
            SelectedIndex = 2
        };
        AutomationProperties.SetAutomationId(zoomBox, "PrintPreviewZoomBox");
        AutomationProperties.SetName(zoomBox, "Zoom");
        AutomationProperties.SetHelpText(zoomBox, "Select a print preview zoom level.");
        toolbar.Items.Add(new Label
        {
            Content = "_Zoom:",
            Target = zoomBox,
            VerticalAlignment = VerticalAlignment.Center
        });
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
        var currentPrintPreviewSettings = new PrintPreviewSettings();
        void RefreshPreviewDocument()
        {
            if (refreshPreview is null && refreshPreviewWithSettings is null)
                return;

            var refreshed = refreshPreviewWithSettings is not null
                ? refreshPreviewWithSettings(currentPrintPreviewSettings)
                : refreshPreview!();
            previewDocument = refreshed.Document;
            viewer.Document = previewDocument;
            totalPages = Math.Max(1, previewDocument.Pages.Count);
            pageNumberBox.Text = "1";
            toPageBox.Text = totalPages.ToString(CultureInfo.InvariantCulture);
            pageStatusText.Text = CreateNavigationState(1, totalPages).StatusText;
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
        SetToolbarAutomation(marginsButton, "PrintPreviewMarginsButton", "Margins", "Review worksheet margin settings before printing.");
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
        SetToolbarAutomation(pageSetupButton, "PrintPreviewPageSetupButton", "Page Setup", "Open Page Setup options for paper, orientation, margins, and scaling.");
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
        AutomationProperties.SetAutomationId(settingsSummaryText, "PrintPreviewSettingsSummaryText");
        AutomationProperties.SetName(settingsSummaryText, "Print settings summary");
        AutomationProperties.SetHelpText(settingsSummaryText, "Summarizes the active print scope and page setup options.");
        toolbar.Items.Add(settingsSummaryText);

        // Left settings panel
        var settingsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = System.Windows.Media.Brushes.WhiteSmoke
        };
        settingsScroll.Content = PrintPreviewSettingsPanelFactory.Build(
            sheetId,
            sheet,
            executeCommand,
            RefreshPreviewDocument,
            refreshPreviewWithSettings is not null
                ? settings => currentPrintPreviewSettings = settings
                : null);
        Grid.SetRow(settingsScroll, 1);
        Grid.SetColumn(settingsScroll, 0);
        root.Children.Add(settingsScroll);

        Grid.SetRow(viewer, 1);
        Grid.SetColumn(viewer, 1);
        root.Children.Add(viewer);

        Grid.SetRow(toolbar, 0);
        Grid.SetColumnSpan(toolbar, 2);
        root.Children.Add(toolbar);

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget(printButton);
    }

    private static void SetToolbarAutomation(Control control, string automationId, string name, string helpText)
    {
        AutomationProperties.SetAutomationId(control, automationId);
        AutomationProperties.SetName(control, name);
        AutomationProperties.SetHelpText(control, helpText);
    }
}
