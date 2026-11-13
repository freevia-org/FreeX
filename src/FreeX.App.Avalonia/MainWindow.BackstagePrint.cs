using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
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

        void Render()
        {
            pageHost.Child = context.PageCount > 0
                ? BuildBackstagePreviewFitToWidthSurface(context, navigator.CurrentIndex)
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
            UiText.Get("MainWindow_Text_Print"),
            "BackstagePrintNowButton",
            () => PrintWithPreviewSettingsAsync(currentSettings, canSpool, printerNames, defaultPrinterName));
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
                Repaginate),
            printerBoxes.Add);
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
    private static Control BuildBackstagePreviewFitToWidthSurface(
        AvaloniaPrintPreviewPaginationContext context,
        int pageIndex) =>
        new ScrollViewer
        {
            Background = PrintPreviewSurfaceBackground,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Margin = new Thickness(16, 8),
                Child = BuildPreviewPageView(context, pageIndex),
            },
        };
}
