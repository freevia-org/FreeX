using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.AppServices;
using FreeX.App.Services;
using Free.Shared.Ribbon.Avalonia;
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
        // The stock DocumentViewer toolbar also leads with Print and Copy and ends with a Find box.
        var printIcon = CreateDocumentToolbarIcon(RibbonCommandIconKind.Print, StripDisplayMnemonic(UiText.Get("PrintPreview_PrintButton")), chrome);
        var copyIcon = CreateDocumentToolbarIcon(RibbonCommandIconKind.Copy, UiText.Get("MainWindow_TooltipTitle_Copy"), chrome);
        AutomationProperties.SetAutomationId(printIcon, "BackstagePrintToolbarPrintButton");
        AutomationProperties.SetAutomationId(copyIcon, "BackstagePrintCopyButton");
        copyIcon.IsEnabled = false;
        var findPlan = PrintPreviewSurfacePlanner.CreateFindBarPlan(PrintPreviewSettingsTextResolver);
        var findBox = new TextBox
        {
            PlaceholderText = findPlan.PlaceholderText,
            Width = 150,
            Height = 22,
            MinHeight = 22,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            FontStyle = FontStyle.Italic,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
        };
        AutomationProperties.SetAutomationId(findBox, "BackstagePrintFindBox");
        AutomationProperties.SetName(findBox, findPlan.PlaceholderText);
        var findPreviousButton = CreateFindNavigationButton(findPlan.PreviousButtonText, isPrevious: true);
        var findNextButton = CreateFindNavigationButton(findPlan.NextButtonText, isPrevious: false);
        AutomationProperties.SetAutomationId(findPreviousButton, "BackstagePrintFindPreviousButton");
        AutomationProperties.SetAutomationId(findNextButton, "BackstagePrintFindNextButton");
        AutomationProperties.SetName(findPreviousButton, StripDisplayMnemonic(UiText.Get("ErrorChecking_PreviousButton")));
        AutomationProperties.SetName(findNextButton, StripDisplayMnemonic(UiText.Get("FindReplace_FindNext")));
        var findStatus = new TextBlock
        {
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(findStatus, "BackstagePrintFindStatus");
        var zoomToolbar = CreatePreviewDocumentToolbar(
        [
            printIcon, copyIcon,
            CreateDocumentToolbarSeparator(chrome),
            zoomInButton, zoomOutButton,
            CreateDocumentToolbarSeparator(chrome),
            actualSizeButton, pageWidthButton, wholePageButton,
            CreateDocumentToolbarSeparator(chrome),
            findBox, findPreviousButton, findNextButton, findStatus,
        ]);
        AutomationProperties.SetAutomationId(zoomToolbar, "BackstagePrintZoomToolbar");

        // Continuous page scrolling like DocumentViewer: every page is stacked in one scroll surface;
        // Previous/Next and the status line follow the scroll position.
        BackstagePreviewSurface? surface = null;
        PrintPreviewTextMatch? lastMatch = null;
        var syncingScroll = false;
        int? pendingPage = null;

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

        void UpdatePageStatus()
        {
            pageStatus.Text = PrintPreviewNavigationState
                .Create(context.PageCount == 0 ? 0 : navigator.CurrentIndex + 1, context.PageCount)
                .StatusText;
            previousButton.IsEnabled = navigator.CanGoPrevious;
            nextButton.IsEnabled = navigator.CanGoNext;
        }

        void ScrollToPage(int index)
        {
            if (surface is null || index < 0 || index >= surface.Pages.Count)
                return;

            var target = surface.Pages[index];
            pendingPage = index;
            Dispatcher.UIThread.Post(() =>
            {
                if (!target.IsAttachedToVisualTree())
                    return;

                syncingScroll = true;
                try
                {
                    target.BringIntoView();
                }
                finally
                {
                    syncingScroll = false;
                }
            }, DispatcherPriority.Loaded);
        }

        void OnScrollChanged()
        {
            if (syncingScroll || surface?.Scroll is not { } scroll || context.PageCount == 0)
                return;

            // A Previous/Next/Find jump owns the position until the scroll has brought its page into view.
            if (pendingPage is { } pending && pending < surface.Pages.Count)
            {
                var targetTop = surface.Pages[pending].TranslatePoint(default, scroll)?.Y ?? 0;
                var targetBottom = targetTop + surface.Pages[pending].Bounds.Height * surface.PageScale;
                if (targetBottom > 0 && targetTop < scroll.Viewport.Height)
                    pendingPage = null;

                return;
            }

            var current = 0;
            for (var index = 0; index < surface.Pages.Count; index++)
            {
                var top = surface.Pages[index].TranslatePoint(default, scroll)?.Y ?? 0;
                var bottom = top + surface.Pages[index].Bounds.Height * surface.PageScale;
                if (bottom > scroll.Viewport.Height / 3)
                {
                    current = index;
                    break;
                }

                current = index;
            }

            if (current == navigator.CurrentIndex)
                return;

            navigator = navigator.JumpTo(current);
            UpdatePageStatus();
        }

        void UpdateCopyState() =>
            copyIcon.IsEnabled = surface?.Canvases.Any(canvas => canvas.HasSelection) == true;

        void Render()
        {
            var hasPages = context.PageCount > 0;
            zoomInButton.IsEnabled = zoomOutButton.IsEnabled = actualSizeButton.IsEnabled =
                pageWidthButton.IsEnabled = wholePageButton.IsEnabled = hasPages;
            findBox.IsEnabled = hasPages;
            findPreviousButton.IsEnabled = findNextButton.IsEnabled = hasPages && !string.IsNullOrWhiteSpace(findBox.Text);
            lastMatch = null;
            findStatus.Text = "";
            copyIcon.IsEnabled = false;
            if (hasPages)
            {
                surface = BuildBackstagePreviewSurface(
                    context, zoomMode, zoomPercent, new Size(pageHost.Bounds.Width, pageHost.Bounds.Height));
                foreach (var canvas in surface.Canvases)
                    canvas.SelectionChanged += (_, _) => UpdateCopyState();
                if (surface.Scroll is { } builtScroll)
                {
                    builtScroll.ScrollChanged += (_, _) => OnScrollChanged();
                    builtScroll.AddHandler(
                        InputElement.PointerWheelChangedEvent,
                        (_, _) => pendingPage = null,
                        RoutingStrategies.Tunnel,
                        handledEventsToo: true);
                }
                pageHost.Child = surface.Root;
                ScrollToPage(navigator.CurrentIndex);
            }
            else
            {
                surface = null;
                pageHost.Child = new TextBlock
                {
                    Text = UiText.Get("ShellLoc_NothingToPreview"),
                    FontFamily = FormulaBarFontFamily,
                    FontSize = 13,
                    Margin = new Thickness(16),
                };
            }

            UpdatePageStatus();
            RefreshSummary();
        }

        // The whole-page zoom depends on the viewport, which is only known after the first layout pass.
        pageHost.SizeChanged += (_, e) =>
        {
            if (zoomMode == BackstagePreviewZoomMode.WholePage && context.PageCount > 0
                && (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5 || Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 0.5))
            {
                Render();
            }
        };

        void FindText(bool backward)
        {
            if (!findNextButton.IsEnabled || findBox.Text is not { } query || surface is null)
                return;

            var match = PrintPreviewTextSearch.Find(
                context.PageCount, navigator.CurrentIndex, context.BuildPainting, query, lastMatch, backward);
            if (match is not { } found)
            {
                findStatus.Text = UiText.Get("MainLoc_NoMatchesFound");
                return;
            }

            findStatus.Text = "";
            lastMatch = found;
            navigator = navigator.JumpTo(found.PageIndex);
            UpdatePageStatus();
            ScrollToPage(found.PageIndex);
            if (found.PageIndex < surface.Canvases.Count && surface.Canvases[found.PageIndex].SelectMatch(found) is { } run)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (lastMatch != found || !run.IsAttachedToVisualTree())
                        return;

                    var rectangles = run.TextLayout.HitTestTextRange(found.Start, found.Length).ToArray();
                    if (rectangles.Length > 0)
                        run.BringIntoView(rectangles.Aggregate((bounds, rectangle) => bounds.Union(rectangle)));
                }, DispatcherPriority.Loaded);
            }
        }

        findBox.TextChanged += (_, _) =>
            findPreviousButton.IsEnabled = findNextButton.IsEnabled =
                context.PageCount > 0 && !string.IsNullOrWhiteSpace(findBox.Text);
        findBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            FindText(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        };
        findPreviousButton.Click += (_, _) => FindText(backward: true);
        findNextButton.Click += (_, _) => FindText(backward: false);
        copyIcon.Click += (_, _) =>
        {
            var text = string.Join(
                Environment.NewLine,
                surface?.Canvases.Where(canvas => canvas.HasSelection).Select(canvas => canvas.GetSelectedText()) ?? []);
            if (string.IsNullOrEmpty(text))
                return;

            RunGuarded(async () =>
            {
                var write = await _platformClipboard.WriteAsync(new PlatformClipboardContent(Text: text));
                if (write.Status == PlatformClipboardWriteStatus.Unavailable)
                    findStatus.Text = UiText.Get("Clipboard_UnavailableOnPlatform");
                else if (!write.IsSuccess)
                    throw new InvalidOperationException(write.ErrorMessage);
            });
        };
        printIcon.Click += (_, _) => RunGuarded(
            () => PrintWithPreviewSettingsAsync(currentSettings, canSpool, printerNames, defaultPrinterName));

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
            UpdatePageStatus();
            ScrollToPage(navigator.CurrentIndex);
        };
        nextButton.Click += (_, _) =>
        {
            navigator = navigator.Next();
            UpdatePageStatus();
            ScrollToPage(navigator.CurrentIndex);
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

    private sealed record BackstagePreviewSurface(
        Control Root,
        ScrollViewer? Scroll,
        IReadOnlyList<Control> Pages,
        IReadOnlyList<PrintPreviewTextCanvas> Canvases,
        double PageScale);

    private static BackstagePreviewSurface BuildBackstagePreviewSurface(
        AvaloniaPrintPreviewPaginationContext context,
        BackstagePreviewZoomMode mode,
        double percent,
        Size viewport)
    {
        var pages = new List<Control>();
        var canvases = new List<PrintPreviewTextCanvas>();
        var stack = new StackPanel { Spacing = 12 };
        for (var index = 0; index < context.PageCount; index++)
        {
            var page = BuildPreviewPageView(context, index);
            pages.Add(page);
            canvases.Add((page as Border)?.Child as PrintPreviewTextCanvas ?? new PrintPreviewTextCanvas());
            stack.Children.Add(page);
        }

        var first = pages.Count > 0 ? pages[0] as Layoutable : null;
        var pageWidth = first is { Width: > 0 } ? first.Width : 0;
        var pageHeight = first is { Height: > 0 } ? first.Height : 0;
        var scale = 1d;
        Control content;
        var horizontal = ScrollBarVisibility.Disabled;
        switch (mode)
        {
            case BackstagePreviewZoomMode.Percent:
                scale = percent / 100;
                horizontal = ScrollBarVisibility.Auto;
                content = new LayoutTransformControl
                {
                    Margin = new Thickness(16, 8),
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                    LayoutTransform = new ScaleTransform(scale, scale),
                    Child = stack,
                };
                break;
            case BackstagePreviewZoomMode.WholePage when viewport.Width > 0 && viewport.Height > 0 && pageWidth > 0 && pageHeight > 0:
                // One whole page fits the viewport; the following pages are reached by scrolling.
                scale = Math.Min((viewport.Width - 32) / pageWidth, (viewport.Height - 16) / pageHeight);
                scale = Math.Max(scale, 0.05);
                content = new LayoutTransformControl
                {
                    Margin = new Thickness(16, 8),
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                    LayoutTransform = new ScaleTransform(scale, scale),
                    Child = stack,
                };
                break;
            default:
                // WPF's FitToWidth: scale to the viewport width, scroll vertically.
                scale = viewport.Width > 0 && pageWidth > 0 ? (viewport.Width - 32) / pageWidth : 1;
                content = new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.Both,
                    Margin = new Thickness(16, 8),
                    Child = stack,
                };
                break;
        }

        var scroll = new ScrollViewer
        {
            Background = PrintPreviewSurfaceBackground,
            HorizontalScrollBarVisibility = horizontal,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content,
        };
        return new BackstagePreviewSurface(scroll, scroll, pages, canvases, scale);
    }
}
