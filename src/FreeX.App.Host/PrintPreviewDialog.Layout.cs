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
            ToolTip = UiText.Get("PrintPreview_PrinterToolTip")
        };
        AutomationProperties.SetName(printerBox, UiText.Get("PrintPreview_PrinterAutomationName"));
        AutomationProperties.SetHelpText(printerBox, UiText.Get("PrintPreview_PrinterHelpText"));
        PopulatePrinterBox(printerBox);
        var copiesBox = new TextBox
        {
            Width = 44,
            Text = "1",
            Margin = new Thickness(0, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = UiText.Get("PrintPreview_CopiesToolTip")
        };
        AutomationProperties.SetName(copiesBox, UiText.Get("PrintPreview_CopiesAutomationName"));
        AutomationProperties.SetHelpText(copiesBox, UiText.Get("PrintPreview_CopiesHelpText"));
        var collatedBox = new CheckBox
        {
            Content = UiText.Get("PrintPreview_CollatedLabel"),
            IsChecked = true,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = UiText.Get("PrintPreview_CollatedToolTip")
        };
        AutomationProperties.SetName(collatedBox, UiText.Get("PrintPreview_CollatedAutomationName"));
        AutomationProperties.SetHelpText(collatedBox, UiText.Get("PrintPreview_CollatedHelpText"));
        var sidesBox = new ComboBox
        {
            Width = 178,
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = UiText.Get("PrintPreview_SidesToolTip")
        };
        sidesBox.Items.Add(UiText.Get("PrintPreview_SidesOneSided"));
        sidesBox.Items.Add(UiText.Get("PrintPreview_SidesFlipLongEdge"));
        sidesBox.Items.Add(UiText.Get("PrintPreview_SidesFlipShortEdge"));
        AutomationProperties.SetName(sidesBox, UiText.Get("PrintPreview_SidesAutomationName"));
        AutomationProperties.SetHelpText(sidesBox, UiText.Get("PrintPreview_SidesHelpText"));
        var statusText = new TextBlock
        {
            Margin = new Thickness(4, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 280
        };
        AutomationProperties.SetName(statusText, UiText.Get("PrintPreview_StatusAutomationName"));
        AutomationProperties.SetHelpText(statusText, UiText.Get("PrintPreview_StatusHelpText"));
        var selectedPageRangeMode = PrintPreviewPageRangeMode.AllPages;
        TextBox pageNumberBox = null!;
        TextBox fromPageBox = null!;
        TextBox toPageBox = null!;
        var firstButton = new Button
        {
            Content = UiText.Get("PrintPreview_FirstPageButton"),
            Padding = new Thickness(10, 4, 10, 4),
            Command = NavigationCommands.FirstPage,
            CommandTarget = viewer
        };
        SetToolbarAutomation(firstButton, "PrintPreviewFirstPageButton", UiText.Get("PrintPreview_FirstPageAutomationName"), UiText.Get("PrintPreview_FirstPageHelpText"));
        var previousButton = new Button
        {
            Content = UiText.Get("PrintPreview_PreviousPageButton"),
            Padding = new Thickness(10, 4, 10, 4),
            Command = NavigationCommands.PreviousPage,
            CommandTarget = viewer
        };
        SetToolbarAutomation(previousButton, "PrintPreviewPreviousPageButton", UiText.Get("PrintPreview_PreviousPageAutomationName"), UiText.Get("PrintPreview_PreviousPageHelpText"));
        var nextButton = new Button
        {
            Content = UiText.Get("PrintPreview_NextPageButton"),
            Padding = new Thickness(10, 4, 10, 4),
            Command = NavigationCommands.NextPage,
            CommandTarget = viewer
        };
        SetToolbarAutomation(nextButton, "PrintPreviewNextPageButton", UiText.Get("PrintPreview_NextPageAutomationName"), UiText.Get("PrintPreview_NextPageHelpText"));
        var lastButton = new Button
        {
            Content = UiText.Get("PrintPreview_LastPageButton"),
            Padding = new Thickness(10, 4, 10, 4),
            Command = NavigationCommands.LastPage,
            CommandTarget = viewer
        };
        SetToolbarAutomation(lastButton, "PrintPreviewLastPageButton", UiText.Get("PrintPreview_LastPageAutomationName"), UiText.Get("PrintPreview_LastPageHelpText"));
        var printButton = new Button
        {
            Content = UiText.Get("PrintPreview_PrintButton"),
            Padding = new Thickness(12, 4, 12, 4),
            ToolTip = UiText.Get("PrintPreview_PrintToolTip")
        };
        var closeButton = new Button
        {
            Content = UiText.Get("PrintPreview_CloseButton"),
            Padding = new Thickness(12, 4, 12, 4),
            IsCancel = true,
            ToolTip = UiText.Get("PrintPreview_CloseToolTip")
        };
        AutomationProperties.SetAutomationId(printButton, "PrintPreviewPrintButton");
        AutomationProperties.SetName(printButton, UiText.Get("PrintPreview_PrintAutomationName"));
        AutomationProperties.SetHelpText(printButton, UiText.Get("PrintPreview_PrintHelpText"));
        SetToolbarAutomation(closeButton, "PrintPreviewCloseButton", UiText.Get("PrintPreview_CloseAutomationName"), UiText.Get("PrintPreview_CloseHelpText"));
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
            Content = UiText.Get("PrintPreview_PrinterLabel"),
            Target = printerBox,
            VerticalAlignment = VerticalAlignment.Center
        });
        toolbar.Items.Add(printerBox);
        toolbar.Items.Add(new Label
        {
            Content = UiText.Get("PrintPreview_CopiesLabel"),
            Target = copiesBox,
            VerticalAlignment = VerticalAlignment.Center
        });
        toolbar.Items.Add(copiesBox);
        toolbar.Items.Add(collatedBox);
        toolbar.Items.Add(new Label
        {
            Content = UiText.Get("PrintPreview_SidesLabel"),
            Target = sidesBox,
            VerticalAlignment = VerticalAlignment.Center
        });
        toolbar.Items.Add(sidesBox);
        toolbar.Items.Add(statusText);
        toolbar.Items.Add(new Separator());
        var allPagesButton = new RadioButton
        {
            Content = UiText.Get("PrintPreview_AllPagesLabel"),
            IsChecked = true,
            GroupName = "PrintPageRange",
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = UiText.Get("PrintPreview_AllPagesToolTip")
        };
        var currentPageButton = new RadioButton
        {
            Content = UiText.Get("PrintPreview_CurrentPageLabel"),
            GroupName = "PrintPageRange",
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = UiText.Get("PrintPreview_CurrentPageToolTip")
        };
        var pagesButton = new RadioButton
        {
            Content = UiText.Get("PrintPreview_PagesLabel"),
            GroupName = "PrintPageRange",
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = UiText.Get("PrintPreview_PagesToolTip")
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
        AutomationProperties.SetName(allPagesButton, UiText.Get("PrintPreview_AllPagesAutomationName"));
        AutomationProperties.SetName(currentPageButton, UiText.Get("PrintPreview_CurrentPageAutomationName"));
        AutomationProperties.SetName(pagesButton, UiText.Get("PrintPreview_PagesAutomationName"));
        AutomationProperties.SetName(fromPageBox, UiText.Get("PrintPreview_FromPageAutomationName"));
        AutomationProperties.SetName(toPageBox, UiText.Get("PrintPreview_ToPageAutomationName"));
        toolbar.Items.Add(allPagesButton);
        toolbar.Items.Add(currentPageButton);
        toolbar.Items.Add(pagesButton);
        toolbar.Items.Add(fromPageBox);
        toolbar.Items.Add(new TextBlock { Text = UiText.Get("PrintPreview_PageRangeToText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
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
            Content = UiText.Get("PrintPreview_PageLabel"),
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
        AutomationProperties.SetName(pageNumberBox, UiText.Get("PrintPreview_PageNumberAutomationName"));
        AutomationProperties.SetHelpText(pageNumberBox, UiText.Get("PrintPreview_PageNumberHelpText"));
        AutomationProperties.SetAutomationId(pageStatusText, "PrintPreviewPageStatusText");
        AutomationProperties.SetName(pageStatusText, UiText.Get("PrintPreview_PageStatusAutomationName"));
        AutomationProperties.SetHelpText(pageStatusText, UiText.Get("PrintPreview_PageStatusHelpText"));
        toolbar.Items.Add(pageNumberBox);
        toolbar.Items.Add(pageStatusText);
        toolbar.Items.Add(new Separator());
        var zoomBox = new ComboBox
        {
            Width = 82,
            SelectedIndex = 2
        };
        AutomationProperties.SetAutomationId(zoomBox, "PrintPreviewZoomBox");
        AutomationProperties.SetName(zoomBox, UiText.Get("PrintPreview_ZoomAutomationName"));
        AutomationProperties.SetHelpText(zoomBox, UiText.Get("PrintPreview_ZoomHelpText"));
        toolbar.Items.Add(new Label
        {
            Content = UiText.Get("PrintPreview_ZoomLabel"),
            Target = zoomBox,
            VerticalAlignment = VerticalAlignment.Center
        });
        var pageWidthZoomText = UiText.Get("PrintPreview_ZoomPageWidth");
        foreach (var zoom in new[] { "50%", "75%", "100%", "125%", pageWidthZoomText })
            zoomBox.Items.Add(zoom);
        zoomBox.SelectionChanged += (_, _) =>
        {
            if (zoomBox.SelectedItem is not string value)
                return;

            if (value == pageWidthZoomText)
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
            Content = UiText.Get("PrintPreview_MarginsButton"),
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = UiText.Get("PrintPreview_MarginsToolTip")
        };
        SetToolbarAutomation(marginsButton, "PrintPreviewMarginsButton", UiText.Get("PrintPreview_MarginsAutomationName"), UiText.Get("PrintPreview_MarginsHelpText"));
        marginsButton.Click += (_, _) =>
        {
            showMargins?.Invoke();
            RefreshPreviewDocument();
        };
        toolbar.Items.Add(marginsButton);
        var pageSetupButton = new Button
        {
            Content = UiText.Get("PrintPreview_PageSetupButton"),
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = UiText.Get("PrintPreview_PageSetupToolTip")
        };
        SetToolbarAutomation(pageSetupButton, "PrintPreviewPageSetupButton", UiText.Get("PrintPreview_PageSetupAutomationName"), UiText.Get("PrintPreview_PageSetupHelpText"));
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
        AutomationProperties.SetName(settingsSummaryText, UiText.Get("PrintPreview_SettingsSummaryAutomationName"));
        AutomationProperties.SetHelpText(settingsSummaryText, UiText.Get("PrintPreview_SettingsSummaryHelpText"));
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
