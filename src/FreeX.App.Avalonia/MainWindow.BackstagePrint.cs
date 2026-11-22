using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using FreeX.App.Presentation.PageLayout;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// The working part of File &gt; Print, laid out like Excel's Print pane: a Print button and the print
/// settings on the left, the live page preview on the right. It reuses the Print Preview window's
/// settings rail, pagination and page painting, so both surfaces print and preview identically.
/// </summary>
public sealed partial class MainWindow
{
    private const double BackstagePrintPreviewHeight = 560;

    private Control BuildLiveBackstagePrintWorkspace()
    {
        var currentSettings = new PrintPreviewSettings();
        var canSpool = false;
        IReadOnlyList<string> printerNames = [];
        string? defaultPrinterName = null;
        var printerBoxes = new List<ComboBox>();

        if (!TryCreatePreviewContextForSettings(currentSettings, out var context))
            context = AvaloniaPrintPreviewPaginationContext.Empty();
        var navigator = PrintPreviewPageNavigator.Create(context.PageCount);

        // WPF SsPrintSettingsSummary: the active sheet's print settings as one 13px paragraph under the
        // "Print" sub-heading, refreshed whenever the settings change.
        var settingsSummary = new TextBlock
        {
            FontFamily = FormulaBarFontFamily,
            FontSize = 13,
            LineHeight = 20,
            Foreground = Brush(0x1F, 0x1F, 0x1F),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        AutomationProperties.SetAutomationId(settingsSummary, "BackstagePrintSettingsSummary");
        AutomationProperties.SetName(settingsSummary, UiText.Get("PrintPreview_SettingsSummaryAutomationName"));
        AutomationProperties.SetHelpText(settingsSummary, UiText.Get("PrintPreview_SettingsSummaryHelpText"));

        void RefreshSummary() =>
            settingsSummary.Text = PrintSettingsPlanner.Build(
                _session.ActiveSheet,
                currentSettings.IgnorePrintArea,
                PrintPreviewSettingsTextResolver).Summary;

        var pageHost = new Border
        {
            Background = PrintPreviewSurfaceBackground,
            Height = BackstagePrintPreviewHeight,
            ClipToBounds = true,
        };
        AutomationProperties.SetAutomationId(pageHost, "BackstagePrintPreviewHost");

        var pageStatus = new TextBlock
        {
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(8, 0),
        };
        AutomationProperties.SetAutomationId(pageStatus, "BackstagePrintPageStatus");
        var previousButton = CreatePreviewToolbarButton("◀");
        var nextButton = CreatePreviewToolbarButton("▶");
        AutomationProperties.SetAutomationId(previousButton, "BackstagePrintPreviousPage");
        AutomationProperties.SetAutomationId(nextButton, "BackstagePrintNextPage");

        // WPF's backstage DocumentViewer keeps its stock icon toolbar: zoom in / zoom out | actual size,
        // page width, whole page. It opens fitted to the page width (FitToWidth after every refresh).
        var zoomMode = BackstagePreviewZoomMode.PageWidth;
        var zoomPercent = 100d;
        var chrome = PrintPreviewSurfacePlanner.DocumentToolbarChrome;
        var zoomInButton = CreateDocumentToolbarIcon(RibbonCommandIconKind.PlusSign, UiText.Get("MainWindow_AutomationName_ZoomIn"), chrome);
        var zoomOutButton = CreateDocumentToolbarIcon(RibbonCommandIconKind.MinusSign, UiText.Get("MainWindow_AutomationName_ZoomOut"), chrome);
        var actualSizeButton = CreateDocumentToolbarIcon(RibbonCommandIconKind.Zoom, "100%", chrome);
        var pageWidthButton = CreateDocumentToolbarIcon(RibbonCommandIconKind.View, UiText.Get("PrintPreview_ZoomPageWidth"), chrome);
        var wholePageButton = CreateDocumentToolbarIcon(RibbonCommandIconKind.Page, UiText.Get("PrintPreview_FitToOnePage"), chrome);
        AutomationProperties.SetAutomationId(zoomInButton, "BackstagePrintZoomInButton");
        AutomationProperties.SetAutomationId(zoomOutButton, "BackstagePrintZoomOutButton");
        AutomationProperties.SetAutomationId(actualSizeButton, "BackstagePrintActualSizeButton");
        AutomationProperties.SetAutomationId(pageWidthButton, "BackstagePrintPageWidthButton");
        AutomationProperties.SetAutomationId(wholePageButton, "BackstagePrintWholePageButton");
        var zoomToolbar = CreatePreviewDocumentToolbar(
        [
            zoomInButton, zoomOutButton,
            CreateDocumentToolbarSeparator(chrome),
            actualSizeButton, pageWidthButton, wholePageButton,
        ]);
        AutomationProperties.SetAutomationId(zoomToolbar, "BackstagePrintZoomToolbar");

        double EffectiveZoomPercent()
        {
            if (zoomMode == BackstagePreviewZoomMode.Percent)
                return zoomPercent;

            var pageWidth = context.PageCount > 0 ? context.BuildPainting(navigator.CurrentIndex)?.PageBounds.Width ?? 0 : 0;
            var available = pageHost.Bounds.Width - 48;
            return pageWidth > 0 && available > 0 ? available / pageWidth * 100 : 100;
        }

        void StepZoom(bool increase)
        {
            var current = EffectiveZoomPercent();
            var next = increase
                ? BackstagePreviewZoomSteps.FirstOrDefault(step => step > current + 0.5)
                : BackstagePreviewZoomSteps.LastOrDefault(step => step < current - 0.5);
            if (next <= 0)
                return;

            zoomMode = BackstagePreviewZoomMode.Percent;
            zoomPercent = next;
            Render();
        }

        zoomInButton.Click += (_, _) => StepZoom(increase: true);
        zoomOutButton.Click += (_, _) => StepZoom(increase: false);
        actualSizeButton.Click += (_, _) =>
        {
            zoomMode = BackstagePreviewZoomMode.Percent;
            zoomPercent = 100;
            Render();
        };
        pageWidthButton.Click += (_, _) =>
        {
            zoomMode = BackstagePreviewZoomMode.PageWidth;
            Render();
        };
        wholePageButton.Click += (_, _) =>
        {
            zoomMode = BackstagePreviewZoomMode.WholePage;
            Render();
        };

        void Render()
        {
            var hasPages = context.PageCount > 0;
            zoomInButton.IsEnabled = zoomOutButton.IsEnabled = actualSizeButton.IsEnabled =
                pageWidthButton.IsEnabled = wholePageButton.IsEnabled = hasPages;
            pageHost.Child = context.PageCount > 0
                ? BuildBackstagePreviewSurface(context, navigator.CurrentIndex, zoomMode, zoomPercent)
                : new TextBlock
                {
                    Text = UiText.Get("ShellLoc_NothingToPreview"),
                    FontFamily = FormulaBarFontFamily,
                    FontSize = 13,
                    Margin = new Thickness(16),
                };
            pageStatus.Text = PrintPreviewNavigationState
                .Create(context.PageCount == 0 ? 0 : navigator.CurrentIndex + 1, context.PageCount)
                .StatusText;
            previousButton.IsEnabled = navigator.CanGoPrevious;
            nextButton.IsEnabled = navigator.CanGoNext;
            RefreshSummary();
        }

        void Repaginate()
        {
            if (!TryCreatePreviewContextForSettings(currentSettings, out context))
                context = AvaloniaPrintPreviewPaginationContext.Empty();
            navigator = PrintPreviewPageNavigator.Create(context.PageCount).JumpTo(navigator.CurrentIndex);
            Render();
        }

        previousButton.Click += (_, _) =>
        {
            navigator = navigator.Previous();
            Render();
        };
        nextButton.Click += (_, _) =>
        {
            navigator = navigator.Next();
            Render();
        };

        var printButton = CreateLiveBackstageActionButton(
            // WPF SsBackstagePrintNowButton content is PrintPreview_PrintButton ("_Print...").
            UiText.Get("PrintPreview_PrintButton"),
            "BackstagePrintNowButton",
            () => PrintWithPreviewSettingsAsync(currentSettings, canSpool, printerNames, defaultPrinterName));
        AutomationProperties.SetName(printButton, UiText.Get("PrintPreview_PrintAutomationName"));
        AutomationProperties.SetHelpText(printButton, UiText.Get("PrintPreview_PrintHelpText"));
        ToolTip.SetTip(printButton, UiText.Get("PrintPreview_PrintToolTip"));
        printButton.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;

        var rail = CreatePrintPreviewSettingsRail(
            PrintPreviewSurfacePlanner.CreateSettingsRailPlan(
                _session.ActiveSheet,
                context.PageCount,
                UiText.Get("Print_SaveAsPdfButton").Replace("_", string.Empty, StringComparison.Ordinal).TrimEnd('.'),
                currentSettings,
                hasSelection: HasPrintSelection(_session.SelectedRange),
                canUpdatePrintPreviewSettings: true,
                PrintPreviewSettingsTextResolver),
            new PrintPreviewSettingsRailInteraction(
                _session.ActiveSheet.Id,
                () => currentSettings,
                updated => currentSettings = updated,
                Repaginate,
                this,
                () => ResolvePreviewPrinterName(currentSettings.PrinterName, canSpool, printerNames, defaultPrinterName),
                "BackstagePrinterPropertiesButton"),
            printerBoxes.Add,
            out _);
        rail.Background = Brushes.Transparent;
        // The shared rail insets its rows by 10 px for the preview window; flush them with the Print button here.
        rail.Margin = new Thickness(-10, 0, 0, 0);

        // WPF left column: ONE 220x34 Print button, the 13px SemiBold "Print" sub-heading, the settings
        // summary and the settings host, all in one scroller 300 px wide (24 px gutter to the divider).
        printButton.Width = 220;
        printButton.Margin = new Thickness(0, 0, 0, 16);
        var leftStack = new StackPanel
        {
            Children =
            {
                printButton,
                new TextBlock
                {
                    Text = UiText.Get("MainWindow_Text_Print"),
                    FontFamily = FormulaBarFontFamily,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brush(0x55, 0x55, 0x55),
                    Margin = new Thickness(0, 0, 0, 8),
                },
                settingsSummary,
                rail,
            },
        };
        var leftColumn = new ScrollViewer
        {
            Width = 300,
            MaxHeight = BackstagePrintPreviewHeight,
            Padding = new Thickness(0, 0, 24, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = leftStack,
        };

        var navigation = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { previousButton, pageStatus, nextButton },
        };
        var rightDock = new DockPanel();
        DockPanel.SetDock(zoomToolbar, Dock.Top);
        rightDock.Children.Add(zoomToolbar);
        DockPanel.SetDock(navigation, Dock.Bottom);
        rightDock.Children.Add(navigation);
        rightDock.Children.Add(new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Child = pageHost,
        });
        var rightColumn = new Border
        {
            BorderBrush = Brush(0xE0, 0xE0, 0xE0),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(24, 0, 0, 0),
            Child = rightDock,
        };

        var workspace = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        Grid.SetColumn(leftColumn, 0);
        Grid.SetColumn(rightColumn, 1);
        workspace.Children.Add(leftColumn);
        workspace.Children.Add(rightColumn);
        AutomationProperties.SetAutomationId(workspace, "BackstagePrintWorkspace");

        Render();

        // Fill the printer list without delaying the pane; lpstat can take a moment.
        if (_printService.IsSupported)
        {
            workspace.AttachedToVisualTree += (_, _) => RunGuarded(async () =>
            {
                var discovery = await _printService.DiscoverAsync();
                if (!discovery.IsAvailable)
                    return;

                printerNames = discovery.Printers.Select(printer => printer.Name).ToArray();
                defaultPrinterName = printerNames.FirstOrDefault(
                        name => string.Equals(name, discovery.DefaultPrinter, StringComparison.Ordinal))
                    ?? printerNames[0];
                canSpool = true;
                foreach (var printerBox in printerBoxes)
                {
                    printerBox.ItemsSource = printerNames;
                    printerBox.SelectedItem = defaultPrinterName;
                }
            });
        }

        return workspace;
    }

    // WPF's Backstage DocumentViewer calls FitToWidth after every refresh. The page painting has a fixed
    // paper size, so scale it to the viewport width (vertical scroll only) instead of clipping the right edge.
    private enum BackstagePreviewZoomMode
    {
        PageWidth,
        WholePage,
        Percent,
    }

    private static readonly double[] BackstagePreviewZoomSteps = [25, 50, 75, 100, 125, 150, 200, 300, 400];

    private static Control BuildBackstagePreviewSurface(
        AvaloniaPrintPreviewPaginationContext context,
        int pageIndex,
        BackstagePreviewZoomMode mode,
        double percent)
    {
        var page = BuildPreviewPageView(context, pageIndex);
        switch (mode)
        {
            case BackstagePreviewZoomMode.WholePage:
                return new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.Both,
                    Margin = new Thickness(16, 8),
                    Child = page,
                };
            case BackstagePreviewZoomMode.Percent:
                return new ScrollViewer
                {
                    Background = PrintPreviewSurfaceBackground,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new LayoutTransformControl
                    {
                        Margin = new Thickness(16, 8),
                        HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                        LayoutTransform = new ScaleTransform(percent / 100, percent / 100),
                        Child = page,
                    },
                };
            default:
                return new ScrollViewer
                {
                    Background = PrintPreviewSurfaceBackground,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new Viewbox
                    {
                        Stretch = Stretch.Uniform,
                        StretchDirection = StretchDirection.Both,
                        Margin = new Thickness(16, 8),
                        Child = page,
                    },
                };
        }
    }
}
