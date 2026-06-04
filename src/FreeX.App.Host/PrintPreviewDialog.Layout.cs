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
        ConfigurePrintPreviewWindow(workbookName);

        var root = CreatePrintPreviewRootGrid();
        var toolbar = CreatePrintPreviewToolbar();
        var previewDocument = document;
        var viewer = new DocumentViewer { Document = previewDocument };
        var totalPages = Math.Max(1, previewDocument.Pages.Count);
        var printControls = CreatePrintControls();
        var printerBox = printControls.PrinterBox;
        var copiesBox = printControls.CopiesBox;
        var collatedBox = printControls.CollatedBox;
        var sidesBox = printControls.SidesBox;
        var statusText = printControls.StatusText;
        var printButton = printControls.PrintButton;
        var closeButton = printControls.CloseButton;
        var selectedPageRangeMode = PrintPreviewPageRangeMode.AllPages;
        var rangeControls = CreatePrintRangeControls(totalPages, mode => selectedPageRangeMode = mode);
        var fromPageBox = rangeControls.FromPageBox;
        var toPageBox = rangeControls.ToPageBox;
        var navigationControls = CreateNavigationControls(viewer, () => totalPages);
        var pageNumberBox = navigationControls.PageNumberBox;
        var pageStatusText = navigationControls.PageStatusText;
        var zoomBox = CreateZoomControl(viewer);
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

        WirePrintCommand(
            printButton,
            previewDocumentAccessor: () => previewDocument,
            totalPagesAccessor: () => totalPages,
            selectedPageRangeModeAccessor: () => selectedPageRangeMode,
            pageNumberBox,
            fromPageBox,
            toPageBox,
            printerBox,
            copiesBox,
            collatedBox,
            sidesBox,
            statusText);
        closeButton.Click += (_, _) => Close();
        WirePrintStatusRefresh(printerBox, copiesBox, statusText, () => totalPages);

        AddPrintControlsToToolbar(toolbar, printControls);
        AddPrintRangeControlsToToolbar(toolbar, rangeControls);
        AddNavigationControlsToToolbar(toolbar, navigationControls);
        AddZoomControlToToolbar(toolbar, zoomBox);

        var marginsButton = CreateMarginsButton();
        marginsButton.Click += (_, _) =>
        {
            showMargins?.Invoke();
            RefreshPreviewDocument();
        };
        toolbar.Items.Add(marginsButton);

        var pageSetupButton = CreatePageSetupButton();
        pageSetupButton.Click += (_, _) =>
        {
            showPageSetup?.Invoke();
            RefreshPreviewDocument();
        };
        toolbar.Items.Add(pageSetupButton);
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(closeButton);
        toolbar.Items.Add(new Separator());

        settingsSummaryText = CreateSettingsSummaryText(settings);
        toolbar.Items.Add(settingsSummaryText);

        var settingsScroll = CreateSettingsPanel(
            sheetId,
            sheet,
            executeCommand,
            RefreshPreviewDocument,
            refreshPreviewWithSettings is not null
                ? settings => currentPrintPreviewSettings = settings
                : null);
        AddPreviewSurfaceToRoot(root, toolbar, settingsScroll, viewer);

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget(printButton);
    }

    private void ConfigurePrintPreviewWindow(string workbookName)
    {
        Title = CreateTitle(workbookName);
        Width = 1120;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    private static Grid CreatePrintPreviewRootGrid()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        return root;
    }

    private static ToolBar CreatePrintPreviewToolbar()
    {
        return new ToolBar
        {
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static PrintControls CreatePrintControls()
    {
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

        var printButton = new Button
        {
            Content = UiText.Get("PrintPreview_PrintButton"),
            Padding = new Thickness(12, 4, 12, 4),
            ToolTip = UiText.Get("PrintPreview_PrintToolTip")
        };
        AutomationProperties.SetAutomationId(printButton, "PrintPreviewPrintButton");
        AutomationProperties.SetName(printButton, UiText.Get("PrintPreview_PrintAutomationName"));
        AutomationProperties.SetHelpText(printButton, UiText.Get("PrintPreview_PrintHelpText"));

        var closeButton = new Button
        {
            Content = UiText.Get("PrintPreview_CloseButton"),
            Padding = new Thickness(12, 4, 12, 4),
            IsCancel = true,
            ToolTip = UiText.Get("PrintPreview_CloseToolTip")
        };
        SetToolbarAutomation(closeButton, "PrintPreviewCloseButton", UiText.Get("PrintPreview_CloseAutomationName"), UiText.Get("PrintPreview_CloseHelpText"));

        return new PrintControls(printerBox, copiesBox, collatedBox, sidesBox, statusText, printButton, closeButton);
    }

    private void WirePrintCommand(
        Button printButton,
        Func<FixedDocument> previewDocumentAccessor,
        Func<int> totalPagesAccessor,
        Func<PrintPreviewPageRangeMode> selectedPageRangeModeAccessor,
        TextBox pageNumberBox,
        TextBox fromPageBox,
        TextBox toPageBox,
        ComboBox printerBox,
        TextBox copiesBox,
        CheckBox collatedBox,
        ComboBox sidesBox,
        TextBlock statusText)
    {
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
            var previewDocument = previewDocumentAccessor();
            var totalPages = totalPagesAccessor();
            var selectedPageRangeMode = selectedPageRangeModeAccessor();
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
    }

    private static void WirePrintStatusRefresh(ComboBox printerBox, TextBox copiesBox, TextBlock statusText, Func<int> totalPages)
    {
        printerBox.SelectionChanged += (_, _) => RefreshPrintStatus(statusText, printerBox, copiesBox, totalPages());
        copiesBox.TextChanged += (_, _) => RefreshPrintStatus(statusText, printerBox, copiesBox, totalPages());
        RefreshPrintStatus(statusText, printerBox, copiesBox, totalPages());
    }

    private static void AddPrintControlsToToolbar(ToolBar toolbar, PrintControls controls)
    {
        toolbar.Items.Add(controls.PrintButton);
        toolbar.Items.Add(new Label
        {
            Content = UiText.Get("PrintPreview_PrinterLabel"),
            Target = controls.PrinterBox,
            VerticalAlignment = VerticalAlignment.Center
        });
        toolbar.Items.Add(controls.PrinterBox);
        toolbar.Items.Add(new Label
        {
            Content = UiText.Get("PrintPreview_CopiesLabel"),
            Target = controls.CopiesBox,
            VerticalAlignment = VerticalAlignment.Center
        });
        toolbar.Items.Add(controls.CopiesBox);
        toolbar.Items.Add(controls.CollatedBox);
        toolbar.Items.Add(new Label
        {
            Content = UiText.Get("PrintPreview_SidesLabel"),
            Target = controls.SidesBox,
            VerticalAlignment = VerticalAlignment.Center
        });
        toolbar.Items.Add(controls.SidesBox);
        toolbar.Items.Add(controls.StatusText);
        toolbar.Items.Add(new Separator());
    }

    private static PrintRangeControls CreatePrintRangeControls(int totalPages, Action<PrintPreviewPageRangeMode> selectPageRangeMode)
    {
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
        var fromPageBox = new TextBox
        {
            Width = 34,
            Text = "1",
            Margin = new Thickness(0, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = false
        };
        var toPageBox = new TextBox
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

        allPagesButton.Checked += (_, _) => selectPageRangeMode(PrintPreviewPageRangeMode.AllPages);
        currentPageButton.Checked += (_, _) => selectPageRangeMode(PrintPreviewPageRangeMode.CurrentPage);
        pagesButton.Checked += (_, _) =>
        {
            selectPageRangeMode(PrintPreviewPageRangeMode.Pages);
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

        return new PrintRangeControls(allPagesButton, currentPageButton, pagesButton, fromPageBox, toPageBox);
    }

    private static void AddPrintRangeControlsToToolbar(ToolBar toolbar, PrintRangeControls controls)
    {
        toolbar.Items.Add(controls.AllPagesButton);
        toolbar.Items.Add(controls.CurrentPageButton);
        toolbar.Items.Add(controls.PagesButton);
        toolbar.Items.Add(controls.FromPageBox);
        toolbar.Items.Add(new TextBlock { Text = UiText.Get("PrintPreview_PageRangeToText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        toolbar.Items.Add(controls.ToPageBox);
        toolbar.Items.Add(new Separator());
    }

    private NavigationControls CreateNavigationControls(DocumentViewer viewer, Func<int> totalPagesAccessor)
    {
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

        var pageNumberBox = new TextBox
        {
            Width = 44,
            Text = "1",
            Margin = new Thickness(0, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var pageStatusText = new TextBlock
        {
            Text = CreateNavigationState(1, totalPagesAccessor()).StatusText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        pageNumberBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            var totalPages = totalPagesAccessor();
            NavigateToPage(viewer, pageNumberBox, pageStatusText, totalPages);
            e.Handled = true;
        };
        pageNumberBox.CommandBindings.Add(new CommandBinding(
            NavigationCommands.GoToPage,
            (_, e) =>
            {
                var totalPages = totalPagesAccessor();
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

        return new NavigationControls(firstButton, previousButton, nextButton, lastButton, pageNumberBox, pageStatusText);
    }

    private static void AddNavigationControlsToToolbar(ToolBar toolbar, NavigationControls controls)
    {
        toolbar.Items.Add(controls.FirstButton);
        toolbar.Items.Add(controls.PreviousButton);
        toolbar.Items.Add(controls.NextButton);
        toolbar.Items.Add(controls.LastButton);
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(new Label
        {
            Content = UiText.Get("PrintPreview_PageLabel"),
            Target = controls.PageNumberBox,
            VerticalAlignment = VerticalAlignment.Center
        });
        toolbar.Items.Add(controls.PageNumberBox);
        toolbar.Items.Add(controls.PageStatusText);
        toolbar.Items.Add(new Separator());
    }

    private static ComboBox CreateZoomControl(DocumentViewer viewer)
    {
        var zoomBox = new ComboBox
        {
            Width = 82,
            SelectedIndex = 2
        };
        AutomationProperties.SetAutomationId(zoomBox, "PrintPreviewZoomBox");
        AutomationProperties.SetName(zoomBox, UiText.Get("PrintPreview_ZoomAutomationName"));
        AutomationProperties.SetHelpText(zoomBox, UiText.Get("PrintPreview_ZoomHelpText"));
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

        return zoomBox;
    }

    private static void AddZoomControlToToolbar(ToolBar toolbar, ComboBox zoomBox)
    {
        toolbar.Items.Add(new Label
        {
            Content = UiText.Get("PrintPreview_ZoomLabel"),
            Target = zoomBox,
            VerticalAlignment = VerticalAlignment.Center
        });
        toolbar.Items.Add(zoomBox);
        toolbar.Items.Add(new Separator());
    }

    private static Button CreateMarginsButton()
    {
        var marginsButton = new Button
        {
            Content = UiText.Get("PrintPreview_MarginsButton"),
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = UiText.Get("PrintPreview_MarginsToolTip")
        };
        SetToolbarAutomation(marginsButton, "PrintPreviewMarginsButton", UiText.Get("PrintPreview_MarginsAutomationName"), UiText.Get("PrintPreview_MarginsHelpText"));

        return marginsButton;
    }

    private static Button CreatePageSetupButton()
    {
        var pageSetupButton = new Button
        {
            Content = UiText.Get("PrintPreview_PageSetupButton"),
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = UiText.Get("PrintPreview_PageSetupToolTip")
        };
        SetToolbarAutomation(pageSetupButton, "PrintPreviewPageSetupButton", UiText.Get("PrintPreview_PageSetupAutomationName"), UiText.Get("PrintPreview_PageSetupHelpText"));

        return pageSetupButton;
    }

    private static TextBlock CreateSettingsSummaryText(PrintSettingsPlan settings)
    {
        var settingsSummaryText = new TextBlock
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

        return settingsSummaryText;
    }

    private static ScrollViewer CreateSettingsPanel(
        SheetId sheetId,
        Sheet? sheet,
        Action<IWorkbookCommand>? executeCommand,
        Action refreshPreview,
        Action<PrintPreviewSettings>? updateSettings)
    {
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
            refreshPreview,
            updateSettings);

        return settingsScroll;
    }

    private static void AddPreviewSurfaceToRoot(Grid root, ToolBar toolbar, ScrollViewer settingsScroll, DocumentViewer viewer)
    {
        Grid.SetRow(settingsScroll, 1);
        Grid.SetColumn(settingsScroll, 0);
        root.Children.Add(settingsScroll);

        Grid.SetRow(viewer, 1);
        Grid.SetColumn(viewer, 1);
        root.Children.Add(viewer);

        Grid.SetRow(toolbar, 0);
        Grid.SetColumnSpan(toolbar, 2);
        root.Children.Add(toolbar);
    }

    private static void SetToolbarAutomation(Control control, string automationId, string name, string helpText)
    {
        AutomationProperties.SetAutomationId(control, automationId);
        AutomationProperties.SetName(control, name);
        AutomationProperties.SetHelpText(control, helpText);
    }

    private sealed class PrintControls(
        ComboBox printerBox,
        TextBox copiesBox,
        CheckBox collatedBox,
        ComboBox sidesBox,
        TextBlock statusText,
        Button printButton,
        Button closeButton)
    {
        public ComboBox PrinterBox { get; } = printerBox;

        public TextBox CopiesBox { get; } = copiesBox;

        public CheckBox CollatedBox { get; } = collatedBox;

        public ComboBox SidesBox { get; } = sidesBox;

        public TextBlock StatusText { get; } = statusText;

        public Button PrintButton { get; } = printButton;

        public Button CloseButton { get; } = closeButton;
    }

    private sealed class PrintRangeControls(
        RadioButton allPagesButton,
        RadioButton currentPageButton,
        RadioButton pagesButton,
        TextBox fromPageBox,
        TextBox toPageBox)
    {
        public RadioButton AllPagesButton { get; } = allPagesButton;

        public RadioButton CurrentPageButton { get; } = currentPageButton;

        public RadioButton PagesButton { get; } = pagesButton;

        public TextBox FromPageBox { get; } = fromPageBox;

        public TextBox ToPageBox { get; } = toPageBox;
    }

    private sealed class NavigationControls(
        Button firstButton,
        Button previousButton,
        Button nextButton,
        Button lastButton,
        TextBox pageNumberBox,
        TextBlock pageStatusText)
    {
        public Button FirstButton { get; } = firstButton;

        public Button PreviousButton { get; } = previousButton;

        public Button NextButton { get; } = nextButton;

        public Button LastButton { get; } = lastButton;

        public TextBox PageNumberBox { get; } = pageNumberBox;

        public TextBlock PageStatusText { get; } = pageStatusText;
    }
}
