using System.Globalization;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Text;
using FreeX.App.Services;
using FreeX.Core.Model;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;

using AvaloniaControlShapesLine = Avalonia.Controls.Shapes.Line;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaImage = Avalonia.Controls.Image;
using AvaloniaPolygon = Avalonia.Controls.Shapes.Polygon;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;
using Free.Shared.Shell.Avalonia;

namespace FreeX.App.Avalonia;

/// <summary>
/// Print Preview window for the Avalonia shell (realignment R7), built on the portable page-content
/// model. For the active sheet it resolves the print range, paginates via
/// <see cref="PagePaginationPlanner"/>, and — one page at a time — builds a
/// <see cref="PageContentLayout"/> through <see cref="PageContentRenderModelBuilder"/> (measuring text
/// with the shared <see cref="AvaloniaTextMeasurer"/>) and paints it onto a Canvas of Avalonia
/// rectangles, lines, and text blocks positioned from the layout's device-independent points.
///
/// The window shows Prev / Next buttons with a "Page X of N" caption (navigation math lives in the
/// pure <see cref="PrintPreviewPageNavigator"/>), and document zoom scales the page within the scroll
/// area. The flattening of a page layout into ordered paint primitives lives in
/// the unit-tested <see cref="PrintPreviewInstructionBuilder"/>; this file only turns each primitive
/// into an Avalonia control.
///
/// The Print button uses the shared print workflow to submit to the chosen printer or save a PDF.
/// The shared page-content model supplies visible text boxes and embedded chart object blocks,
/// including selectable chart-text overlay primitives.
/// </summary>
public sealed partial class MainWindow
{
    private const string PrintPreviewDefaultPrinterName = "Windows print dialog";

    private static readonly ITextMeasurer PrintPreviewTextMeasurer = new AvaloniaTextMeasurer();
    // WPF DocumentViewer uses its light neutral control surface around the white paper.
    private static readonly IBrush PrintPreviewSurfaceBackground = Brush(240, 240, 240);
    private static readonly IBrush PrintPreviewChromeBackground = Brush(238, 245, 253);
    private static readonly Thickness PrintPreviewPaperPadding =
        new(PrintPreviewSurfacePlanner.PreviewPageLeftPadding, 5, 84, 8);
    private static AvaloniaCompactDialogChromeStyle PrintPreviewChromeStyle =>
        new(FormulaBarFontFamily) { ButtonPadding = new Thickness(6, 1) };
    private static readonly PrintSettingsTextResolver PrintPreviewSettingsTextResolver = new(
        UiText.Get,
        (key, args) => UiText.Format(key, args));

    private async Task ShowPrintPreviewDialogAsync(
        string? printerNameOverride = null,
        int? externalPageCount = null,
        Func<int, Control>? externalPageViewFactory = null)
    {
        await WaitForPendingDirtyWorkbookGateAsync();

        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        AvaloniaPrintPreviewPaginationContext context;
        if (externalPageViewFactory is not null)
        {
            if (externalPageCount is null or <= 0)
                throw new ArgumentOutOfRangeException(nameof(externalPageCount));

            context = AvaloniaPrintPreviewPaginationContext.Empty();
        }
        else if (!TryCreatePreviewContextForSettings(new PrintPreviewSettings(), out context))
        {
            ShowEditIssue(UiText.Get("ShellLoc_NothingToPreview"));
            return;
        }

        await ShowPrintPreviewWindowCoreAsync(
            context,
            printerNameOverride,
            externalPageCount,
            externalPageViewFactory);
    }

    private async Task ShowPrintPreviewWindowCoreAsync(
        AvaloniaPrintPreviewPaginationContext context,
        string? printerNameOverride = null,
        int? externalPageCount = null,
        Func<int, Control>? externalPageViewFactory = null)
    {
        var hasExternalPageSource = externalPageViewFactory is not null;
        // A real preview prints to a real queue: list the installed printers so the Print button can
        // send the job (it used to export a PDF whatever the destination said). Without a spooler the
        // destination reads "Save as PDF" and Print keeps the print-ready-PDF fallback.
        // Discovery shells out to the spooler (lpstat), so it must never delay the window: the preview
        // opens at once on the Save-as-PDF destination and the printers fill in when discovery returns.
        var canSpool = false;
        IReadOnlyList<string> printerNames = [];
        string? defaultPrinterName = null;
        var printerBoxes = new List<ComboBox>();
        var printerName = printerNameOverride
            ?? UiText.Get("Print_SaveAsPdfButton").Replace("_", string.Empty, StringComparison.Ordinal).TrimEnd('.');
        var pageCount = externalPageCount ?? context.PageCount;
        var navigator = PrintPreviewPageNavigator.Create(pageCount);
        // Ephemeral print-job settings (Print What / Sides / Collation / Copies / Printer / page
        // range / ignore print area) tracked across the settings-rail's own controls -- see
        // CreatePrintPreviewSettingsRail's interactive wiring below.
        var currentSettings = new PrintPreviewSettings();
        var documentToolbarPlan = PrintPreviewSurfacePlanner.CreateDocumentToolbarPlan(
            pageCount,
            PrintPreviewSettingsTextResolver);
        var topToolbarPlan = PrintPreviewSurfacePlanner.CreateTopToolbarPlan(
            pageCount,
            printerName,
            PrintPreviewSettingsTextResolver);

        var dialog = new Window
        {
            Title = UiText.Format(
                PrintPreviewDialogPlanner.TitleFormatResourceKey,
                PrintPreviewDialogPlanner.NormalizeWorkbookName(_session.DisplayName)),
            Width = PrintPreviewDialogPlanner.WindowWidth,
            Height = PrintPreviewDialogPlanner.WindowHeight,
            MinWidth = PrintPreviewDialogPlanner.MinWindowWidth,
            MinHeight = PrintPreviewDialogPlanner.MinWindowHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, PrintPreviewDialogPlanner.DialogAutomationId);

        var pageHost = new Border
        {
            Background = PrintPreviewSurfaceBackground,
            Padding = new Thickness(0),
            ClipToBounds = true,
        };
        AutomationProperties.SetAutomationId(pageHost, PrintPreviewDialogPlanner.PageHostAutomationId);
        var zoomScale = new ScaleTransform(1, 1);
        var zoomedPage = new LayoutTransformControl
        {
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
        };
        var pageScroll = BuildPreviewDocumentViewerSurface(zoomedPage);
        pageHost.Child = pageScroll;

        var pageNumberBox = new TextBox
        {
            Text = documentToolbarPlan.PageNumberText,
            Width = 44,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(4, 0),
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        ApplyPreviewTextBoxChrome(pageNumberBox);

        var pageStatusText = new TextBlock
        {
            Text = documentToolbarPlan.PageStatusText,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        AutomationProperties.SetAutomationId(pageNumberBox, PrintPreviewDialogPlanner.PageNumberBoxAutomationId);
        AutomationProperties.SetAutomationId(pageStatusText, PrintPreviewDialogPlanner.PageStatusTextAutomationId);
        AutomationProperties.SetName(pageNumberBox, UiText.Get("PrintPreview_PageNumberAutomationName"));
        AutomationProperties.SetHelpText(pageNumberBox, UiText.Get("PrintPreview_PageNumberHelpText"));
        AutomationProperties.SetName(pageStatusText, UiText.Get("PrintPreview_PageStatusAutomationName"));

        var firstButton = CreatePreviewToolbarButton(documentToolbarPlan.NavigationButtons[0]);
        var prevButton = CreatePreviewToolbarButton(documentToolbarPlan.NavigationButtons[1]);
        var nextButton = CreatePreviewToolbarButton(documentToolbarPlan.NavigationButtons[2]);
        var lastButton = CreatePreviewToolbarButton(documentToolbarPlan.NavigationButtons[3]);
        var chrome = PrintPreviewSurfacePlanner.DocumentToolbarChrome;
        var printIcon = CreateDocumentToolbarIcon(RibbonCommandIconKind.Print, StripDisplayMnemonic(topToolbarPlan.PrintButtonText), chrome);
        var copyIcon = CreateDocumentToolbarIcon(RibbonCommandIconKind.Copy, UiText.Get("MainWindow_TooltipTitle_Copy"), chrome);
        AutomationProperties.SetAutomationId(copyIcon, "PrintPreviewCopyButton");
        copyIcon.IsEnabled = false;
        var (findBar, findBox, findPrevious, findNext, findStatus) =
            CreatePrintPreviewFindBar(PrintPreviewSurfacePlanner.CreateFindBarPlan(PrintPreviewSettingsTextResolver));
        PrintPreviewTextCanvas? textCanvas = null;
        PrintPreviewTextMatch? lastMatch = null;
        var zoomOutButton = CreateDocumentToolbarIcon(RibbonCommandIconKind.MinusSign, UiText.Get("MainWindow_AutomationName_ZoomOut"), chrome);
        var zoomInButton = CreateDocumentToolbarIcon(RibbonCommandIconKind.PlusSign, UiText.Get("MainWindow_AutomationName_ZoomIn"), chrome);
        var fitPageText = UiText.Get("PrintPreview_FitToOnePage");
        var fitPageButton = CreateDocumentToolbarIcon(RibbonCommandIconKind.Page, fitPageText, chrome);
        var fitWidthButton = CreateDocumentToolbarIcon(RibbonCommandIconKind.View, UiText.Get("PrintPreview_ZoomPageWidth"), chrome);
        AutomationProperties.SetAutomationId(printIcon, PrintPreviewDialogPlanner.PrintButtonAutomationId);
        AutomationProperties.SetAutomationId(zoomOutButton, "PrintPreviewZoomOutButton");
        AutomationProperties.SetAutomationId(zoomInButton, "PrintPreviewZoomInButton");
        AutomationProperties.SetAutomationId(fitPageButton, "PrintPreviewFitPageButton");
        AutomationProperties.SetAutomationId(fitWidthButton, "PrintPreviewFitWidthButton");
        var zoomOptions = documentToolbarPlan.ZoomOptions;
        var fitWidthIndex = zoomOptions.ToList().FindIndex(option => option.FitToWidth);
        var fitPageIndex = zoomOptions.Count;
        var zoomBox = CreatePreviewComboBox(documentToolbarPlan.ZoomComboWidth,
            zoomOptions.Select(option => option.Text).Append(fitPageText).ToArray(), documentToolbarPlan.ZoomSelectedIndex);
        AutomationProperties.SetAutomationId(zoomBox, PrintPreviewDialogPlanner.ZoomBoxAutomationId);
        AutomationProperties.SetName(zoomBox, UiText.Get("PrintPreview_ZoomAutomationName"));
        AutomationProperties.SetHelpText(zoomBox, UiText.Get("PrintPreview_ZoomHelpText"));

        var exportButton = new Button
        {
            Content = topToolbarPlan.PrintButtonText,
            MinWidth = 60,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(10, 1),
            Background = Brushes.White,
            BorderBrush = Brush(0, 120, 215),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(exportButton, PrintPreviewDialogPlanner.ExportPdfButtonAutomationId);
        exportButton.IsEnabled = canSpool || StorageProvider.CanSave;

        var topToolbar = CreatePrintPreviewTopToolbar(
            topToolbarPlan,
            exportButton,
            () => dialog.Close(),
            out var toolbarControls);
        printerBoxes.Add(toolbarControls.Printer);

        void UpdateCopyState(object? sender, EventArgs e) => copyIcon.IsEnabled = textCanvas?.HasSelection == true;

        void UpdateFindState()
        {
            findBox.IsEnabled = !hasExternalPageSource && pageCount > 0;
            findPrevious.IsEnabled = findNext.IsEnabled = findBox.IsEnabled && !string.IsNullOrWhiteSpace(findBox.Text);
        }

        void CopySelection()
        {
            if (textCanvas?.HasSelection != true)
                return;

            var text = textCanvas.GetSelectedText();
            RunGuarded(async () =>
            {
                var write = await _platformClipboard.WriteAsync(new PlatformClipboardContent(Text: text));
                if (write.Status == PlatformClipboardWriteStatus.Unavailable)
                    findStatus.Text = UiText.Get("Clipboard_UnavailableOnPlatform");
                else if (!write.IsSuccess)
                    throw new InvalidOperationException(write.ErrorMessage);
            });
        }

        int FindSteppedZoomIndex(bool increase)
        {
            var currentPercent = zoomScale.ScaleX * 100;
            var bestIndex = -1;
            for (var index = 0; index < zoomOptions.Count; index++)
            {
                if (zoomOptions[index].Percent is not { } percent)
                    continue;
                if (increase && percent > currentPercent + 0.01)
                    return index;
                if (!increase && percent < currentPercent - 0.01)
                    bestIndex = index;
            }
            return bestIndex;
        }

        void ApplyDocumentZoom()
        {
            var scale = 1.0;
            var zoomIndex = zoomBox.SelectedIndex;
            if (zoomIndex >= 0 && zoomIndex < zoomOptions.Count && zoomOptions[zoomIndex].Percent is { } percent)
            {
                scale = percent / 100;
            }
            else if (zoomedPage.Child is Control page)
            {
                page.Measure(Size.Infinity);
                // Allow room for a vertical scrollbar when a width-fitted page is taller than the viewport.
                var width = pageHost.Bounds.Width - PrintPreviewPaperPadding.Left - PrintPreviewPaperPadding.Right - 16;
                var height = pageHost.Bounds.Height - PrintPreviewPaperPadding.Top - PrintPreviewPaperPadding.Bottom;
                if (page.DesiredSize.Width > 0 && page.DesiredSize.Height > 0 && width > 0 && height > 0)
                {
                    scale = width / page.DesiredSize.Width;
                    if (zoomIndex == fitPageIndex)
                        scale = Math.Min(scale, height / page.DesiredSize.Height);
                }
            }

            zoomScale.ScaleX = scale;
            zoomScale.ScaleY = scale;
            zoomOutButton.IsEnabled = pageCount > 0 && FindSteppedZoomIndex(increase: false) >= 0;
            zoomInButton.IsEnabled = pageCount > 0 && FindSteppedZoomIndex(increase: true) >= 0;
            zoomBox.IsEnabled = fitPageButton.IsEnabled = fitWidthButton.IsEnabled = pageCount > 0;
        }

        void Render()
        {
            if (textCanvas is not null)
                textCanvas.SelectionChanged -= UpdateCopyState;
            var pageIndex = navigator.CurrentIndex;
            var page = externalPageViewFactory?.Invoke(pageIndex) ?? BuildPreviewPageView(context, pageIndex);
            zoomedPage.Child = page;
            textCanvas = (page as Border)?.Child as PrintPreviewTextCanvas;
            if (textCanvas is not null)
                textCanvas.SelectionChanged += UpdateCopyState;
            lastMatch = null;
            copyIcon.IsEnabled = false;
            findStatus.Text = "";
            UpdateFindState();
            zoomedPage.LayoutTransform = zoomScale;
            pageScroll.Offset = default;
            ApplyDocumentZoom();
            pageNumberBox.Text = (navigator.CurrentIndex + 1).ToString(CultureInfo.InvariantCulture);
            pageNumberBox.IsEnabled = pageCount > 0;
            pageStatusText.Text = PrintPreviewNavigationState.Create(navigator.CurrentIndex + 1, pageCount).StatusText;
            firstButton.IsEnabled = navigator.CanGoPrevious;
            prevButton.IsEnabled = navigator.CanGoPrevious;
            nextButton.IsEnabled = navigator.CanGoNext;
            lastButton.IsEnabled = navigator.CanGoNext;
            exportButton.IsEnabled = pageCount > 0 && (canSpool || StorageProvider.CanSave);
            if (!hasExternalPageSource)
            {
                toolbarControls.Status.Text = PrintPreviewToolbarStatePlanner.CreateStatusText(
                    currentSettings.PrinterName ?? printerName, currentSettings.Copies, pageCount);
            }
        }

        void FindText(bool backward)
        {
            if (!findNext.IsEnabled || findBox.Text is not { } query)
                return;

            var match = PrintPreviewTextSearch.Find(
                pageCount, navigator.CurrentIndex, context.BuildPainting, query, lastMatch, backward);
            if (match is not { } found)
            {
                findStatus.Text = UiText.Get("MainLoc_NoMatchesFound");
                return;
            }

            if (found.PageIndex != navigator.CurrentIndex)
            {
                navigator = navigator.JumpTo(found.PageIndex);
                Render();
            }

            findStatus.Text = "";
            lastMatch = found;
            if (textCanvas?.SelectMatch(found) is { } run)
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

        // Re-resolves the active sheet's pagination (honoring the current Ignore Print Area
        // setting) and repaints the current page. Called after any settings-rail control that can
        // change what the active sheet's preview pages look like (orientation, paper size, margins,
        // scaling, print gridlines/headings, ignore print area) so the preview never keeps showing a
        // stale layout for a setting the user just changed (R118-print-preview-settings-rail-wiring).
        void RepaginateAndRender()
        {
            if (!hasExternalPageSource)
            {
                if (TryCreatePreviewContextForSettings(currentSettings, out var updatedContext))
                {
                    context = updatedContext;
                    pageCount = updatedContext.PageCount;
                    navigator = PrintPreviewPageNavigator.Create(pageCount).JumpTo(navigator.CurrentIndex);
                }
                else
                {
                    context = AvaloniaPrintPreviewPaginationContext.Empty();
                    pageCount = 0;
                    navigator = PrintPreviewPageNavigator.Create(0);
                }
            }

            Render();
        }

        firstButton.Click += (_, _) =>
        {
            navigator = navigator.JumpTo(0);
            Render();
        };
        zoomBox.SelectionChanged += (_, _) => ApplyDocumentZoom();
        pageHost.SizeChanged += (_, _) => ApplyDocumentZoom();
        zoomInButton.Click += (_, _) => zoomBox.SelectedIndex = FindSteppedZoomIndex(increase: true);
        zoomOutButton.Click += (_, _) => zoomBox.SelectedIndex = FindSteppedZoomIndex(increase: false);
        fitPageButton.Click += (_, _) => zoomBox.SelectedIndex = fitPageIndex;
        fitWidthButton.Click += (_, _) => zoomBox.SelectedIndex = fitWidthIndex;
        copyIcon.Click += (_, _) => CopySelection();
        findPrevious.Click += (_, _) => FindText(backward: true);
        findNext.Click += (_, _) => FindText(backward: false);
        findBox.TextChanged += (_, _) =>
        {
            if (lastMatch is not null)
                textCanvas?.ClearSelection();
            lastMatch = null;
            findStatus.Text = "";
            UpdateFindState();
        };
        findBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            FindText(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        };
        prevButton.Click += (_, _) =>
        {
            navigator = navigator.Previous();
            Render();
        };
        nextButton.Click += (_, _) =>
        {
            navigator = navigator.Next();
            Render();
        };
        lastButton.Click += (_, _) =>
        {
            navigator = navigator.JumpTo(pageCount - 1);
            Render();
        };
        pageNumberBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            if (PrintPreviewDialogPlanner.TryParsePageNumber(pageNumberBox.Text, pageCount, out var pageNumber))
            {
                navigator = navigator.JumpTo(pageNumber - 1);
                Render();
            }

            e.Handled = true;
        };
        dialog.KeyDown += (_, e) =>
        {
            if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
            {
                if (e.Key == Key.F && findBox.IsEnabled)
                {
                    findBox.Focus();
                    findBox.SelectAll();
                    e.Handled = true;
                }
                else if (e.Key == Key.C && e.Source is not TextBox && copyIcon.IsEnabled)
                {
                    CopySelection();
                    e.Handled = true;
                }
                return;
            }

            switch (e.Key)
            {
                case Key.Escape:
                    dialog.Close();
                    e.Handled = true;
                    break;
                case Key.F3:
                    FindText(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                    e.Handled = true;
                    break;
                case Key.Left or Key.PageUp when e.Source is not TextBox:
                    navigator = navigator.Previous();
                    Render();
                    e.Handled = true;
                    break;
                case Key.Right or Key.PageDown when e.Source is not TextBox:
                    navigator = navigator.Next();
                    Render();
                    e.Handled = true;
                    break;
            }
        };

        var marginsButton = CreatePreviewToolbarButton(documentToolbarPlan.MarginsButtonText);
        var pageSetupButton = CreatePreviewToolbarButton(documentToolbarPlan.PageSetupButtonText);
        AutomationProperties.SetAutomationId(marginsButton, PrintPreviewDialogPlanner.MarginsButtonAutomationId);
        AutomationProperties.SetAutomationId(pageSetupButton, PrintPreviewDialogPlanner.PageSetupButtonAutomationId);
        marginsButton.IsEnabled = pageSetupButton.IsEnabled = !hasExternalPageSource;
        marginsButton.Click += (_, _) => RunGuarded(() =>
            ShowPrintPreviewPageSetupAsync(PageLayoutPageSetupOpenSource.CustomMargins, RepaginateAndRender));
        pageSetupButton.Click += (_, _) => RunGuarded(() =>
            ShowPrintPreviewPageSetupAsync(PageLayoutPageSetupOpenSource.DialogButton, RepaginateAndRender));
        printIcon.Click += (_, _) => exportButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        printIcon.Bind(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = exportButton });

        // WPF's DocumentViewer toolbar shows only icon buttons: print, copy | zoom in, zoom out |
        // actual size, page width, whole page. The page navigation, zoom box, Margins and Page Setup
        // controls stay fully functional but sit in a trailing overflow row (the chevron at the end
        // of the icon row toggles it), out of the default visible toolbar.
        var actualSizeIndex = documentToolbarPlan.ZoomSelectedIndex;
        var actualSizeButton = CreateDocumentToolbarIcon(RibbonCommandIconKind.Zoom, zoomOptions[actualSizeIndex].Text, chrome);
        AutomationProperties.SetAutomationId(actualSizeButton, "PrintPreviewActualSizeButton");
        actualSizeButton.Click += (_, _) => zoomBox.SelectedIndex = actualSizeIndex;
        actualSizeButton.IsEnabled = pageCount > 0;
        var overflowToggle = new global::Avalonia.Controls.Primitives.ToggleButton
        {
            Width = chrome.ButtonWidth,
            MinWidth = chrome.ButtonWidth,
            Height = chrome.ButtonHeight,
            MinHeight = chrome.ButtonHeight,
            MaxHeight = chrome.ButtonHeight,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            Content = new global::Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M 1 1 L 5 5 L 9 1"),
                Stroke = Brush(92, 92, 92),
                StrokeThickness = 1.5,
                Width = 10,
                Height = 6,
            },
        };
        AutomationProperties.SetAutomationId(overflowToggle, "PrintPreviewToolbarOverflowButton");
        AutomationProperties.SetName(overflowToggle, UiText.Get("PrintPreview_ZoomAutomationName"));
        var primaryToolbar = CreatePreviewDocumentToolbar(
        [
            printIcon, copyIcon,
            CreateDocumentToolbarSeparator(chrome),
            zoomInButton, zoomOutButton,
            CreateDocumentToolbarSeparator(chrome),
            actualSizeButton, fitWidthButton, fitPageButton,
            overflowToggle,
        ]);
        var overflowToolbar = CreatePreviewDocumentToolbar(
        [
            firstButton, prevButton, nextButton, lastButton,
            new TextBlock { Text = documentToolbarPlan.PageLabelText, VerticalAlignment = AvaloniaVerticalAlignment.Center },
            pageNumberBox, pageStatusText,
            CreateDocumentToolbarSeparator(chrome),
            zoomBox,
            CreateDocumentToolbarSeparator(chrome),
            marginsButton, pageSetupButton,
        ]);
        // The page navigation, page box, zoom combo, Margins and Page Setup live in this second row.
        // It starts COLLAPSED, matching WPF: WPF's DocumentViewer toolbar is a single icon row and these
        // controls sit behind the toolbar overflow chevron. A collapsed row is never templated, so tests
        // and flows that need these controls must expand it first via the chevron toggle.
        overflowToggle.IsChecked = false;
        overflowToolbar.IsVisible = false;
        overflowToggle.IsCheckedChanged += (_, _) => overflowToolbar.IsVisible = overflowToggle.IsChecked == true;
        var documentToolbar = new StackPanel { Children = { primaryToolbar, overflowToolbar } };
        var previewPane = CreatePrintPreviewPane(documentToolbar, pageHost, findBar);
        // An externally supplied static page source has no live sheet/session behind it, so its rail
        // stays read-only. A real preview is backed by the live active sheet and its settings rail is
        // fully interactive (R118-print-preview-settings-rail-wiring) --
        // previously canUpdatePrintPreviewSettings was hardcoded false even for real previews, which
        // left every control (Print What/Sides/Collation/Orientation/Paper Size/Margins/Scaling/
        // Ignore Print Area/Print Options) wired to nothing.
        var settingsRail = CreatePrintPreviewSettingsRail(
            PrintPreviewSurfacePlanner.CreateSettingsRailPlan(
                hasExternalPageSource ? null : _session.ActiveSheet,
                hasExternalPageSource ? 1 : pageCount,
                printerName,
                currentSettings,
                 // Match WPF's PrintRenderer.RenderWorksheet(printRangeOverride: selectionRange,
                 // ignorePrintArea: true) when the user switches the live preview to Selection.
                 hasSelection: HasPrintSelection(_session.SelectedRange),
                canUpdatePrintPreviewSettings: !hasExternalPageSource,
                PrintPreviewSettingsTextResolver),
            hasExternalPageSource
                ? null
                : new PrintPreviewSettingsRailInteraction(
                    _session.ActiveSheet.Id,
                    () => currentSettings,
                    updated => currentSettings = updated,
                    RepaginateAndRender,
                    dialog,
                    () => ResolvePreviewPrinterName(currentSettings.PrinterName, canSpool, printerNames, defaultPrinterName),
                    "PrintPreviewSettingsPrinterPropertiesButton"),
            printerBoxes.Add,
            out var rangeFields,
            hasExternalPageSource ? null : toolbarControls);

        exportButton.Click += (_, _) => RunGuarded(async () =>
        {
            var rangeIndex = toolbarControls.PageRangeMode.SelectedIndex;
            var rangeMode = rangeIndex >= 0
                ? topToolbarPlan.PageRangeChoices[rangeIndex].Mode
                : PrintPreviewPageRangeMode.AllPages;
            var currentPage = navigator.CurrentIndex + 1;
            if (rangeMode == PrintPreviewPageRangeMode.CurrentPage &&
                !PrintPreviewDialogPlanner.TryParsePageNumber(pageNumberBox.Text, pageCount, out currentPage))
            {
                var validation = PrintPreviewDialogPlanner.DescribeInvalidPageNumber(pageCount);
                await ShowPrintPreviewWarningAsync(dialog, validation.Message.Resolve(UiText.Get, UiText.Format), pageNumberBox);
                return;
            }

            ExportPageRange? pageRange = null;
            if (rangeMode == PrintPreviewPageRangeMode.Pages)
            {
                // A blank end of the rail range extends to the corresponding document edge.
                var fromText = string.IsNullOrWhiteSpace(rangeFields.From.Text) ? "1" : rangeFields.From.Text;
                var toText = string.IsNullOrWhiteSpace(rangeFields.To.Text)
                    ? pageCount.ToString(CultureInfo.InvariantCulture)
                    : rangeFields.To.Text;
                if (!ExportPlanner.TryCreatePageRange(
                        fromText, toText, out pageRange, out var error, AvaloniaExportPlannerTextResolver) ||
                    !ExportPlanner.TryValidatePageRange(pageRange, pageCount, out error, AvaloniaExportPlannerTextResolver))
                {
                    var focusTarget = ExportOptionsDialogSurfacePlanner.ResolveInvalidPageRangeFocusTarget(
                        error, fromText, UiText.Get("Export_PageRangeFromLessThanToError"));
                    var target = focusTarget == ExportOptionsDialogFocusTarget.ToPage ? rangeFields.To : rangeFields.From;
                    await ShowPrintPreviewWarningAsync(
                        dialog, error ?? UiText.Get("PrintPreview_InvalidPageRangeMessage"), target);
                    return;
                }
            }

            var range = PrintPreviewToolbarStatePlanner.ResolvePageRange(
                rangeMode, currentPage, pageRange?.FromPage, pageRange?.ToPage);
            dialog.Close();
            await PrintWithPreviewSettingsAsync(
                currentSettings with { PageFrom = range?.FromPage, PageTo = range?.ToPage },
                canSpool, printerNames, defaultPrinterName);
        });

        var layout = new Grid
        {
            // Keep the capture fixture calibrated to the WPF frame. Live preview uses the available
            // client area so fit-to-page and fit-to-width respond to resizing.
            Width = hasExternalPageSource ? PrintPreviewSurfacePlanner.ParityClientWidth : double.NaN,
            Height = hasExternalPageSource ? PrintPreviewSurfacePlanner.ParityClientHeight : double.NaN,
            HorizontalAlignment = hasExternalPageSource ? AvaloniaHorizontalAlignment.Left : AvaloniaHorizontalAlignment.Stretch,
            VerticalAlignment = hasExternalPageSource ? AvaloniaVerticalAlignment.Top : AvaloniaVerticalAlignment.Stretch,
            RowDefinitions = new RowDefinitions("Auto,*"),
            ColumnDefinitions = new ColumnDefinitions($"{PrintPreviewSurfacePlanner.SettingsRailWidth},*"),
        };
        Grid.SetRow(topToolbar, 0);
        Grid.SetColumnSpan(topToolbar, 2);
        Grid.SetRow(settingsRail, 1);
        Grid.SetColumn(settingsRail, 0);
        Grid.SetRow(previewPane, 1);
        Grid.SetColumn(previewPane, 1);
        layout.Children.Add(topToolbar);
        layout.Children.Add(settingsRail);
        layout.Children.Add(previewPane);

        dialog.Content = layout;
        dialog.Opened += (_, _) =>
        {
            Render();
            if (PrintPreviewDialogPlanner.InitialFocusCommand == PrintPreviewToolbarCommand.Print)
                exportButton.Focus();

            if (printerNameOverride is not null || hasExternalPageSource || !_printService.IsSupported)
                return;

            RunGuarded(async () =>
            {
                var discovery = await _printService.DiscoverAsync();
                if (!discovery.IsAvailable)
                    return;

                printerNames = discovery.Printers.Select(printer => printer.Name).ToArray();
                defaultPrinterName = printerNames.FirstOrDefault(
                        name => string.Equals(name, discovery.DefaultPrinter, StringComparison.Ordinal))
                    ?? printerNames[0];
                canSpool = true;
                exportButton.IsEnabled = pageCount > 0;
                foreach (var printerBox in printerBoxes)
                {
                    printerBox.ItemsSource = printerNames;
                    printerBox.SelectedItem = defaultPrinterName;
                }
            });
        };

        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Paginates the active sheet or sheet group, selection, or whole workbook for the preview surfaces.
    /// </summary>
    private bool TryCreatePreviewContextForSettings(
        PrintPreviewSettings settings,
        out AvaloniaPrintPreviewPaginationContext context)
    {
        var ignorePrintArea = settings.PrintWhat is PrintWhat.Selection || settings.IgnorePrintArea;
        var groupedSheetIds = settings.PrintWhat == PrintWhat.ActiveSheets ? GetGroupedPrintSheetIds() : null;
        return settings.PrintWhat == PrintWhat.EntireWorkbook || groupedSheetIds is not null
            ? AvaloniaPrintPreviewPaginationContext.TryCreateWorkbook(
                _session.Workbook,
                PrintPreviewTextMeasurer,
                out context,
                ResolveWorkbookDirectoryForHeaderFooter(),
                settings.IgnorePrintArea,
                groupedSheetIds)
            : AvaloniaPrintPreviewPaginationContext.TryCreate(
                _session.Workbook,
                _session.ActiveSheet,
                PrintPreviewTextMeasurer,
                settings.PrintWhat == PrintWhat.Selection
                    ? _session.SelectedRange
                    : null,
                out context,
                ResolveWorkbookDirectoryForHeaderFooter(),
                ignorePrintArea);
    }

    /// <summary>
    /// Sends what the print settings describe to the chosen printer (or, with no spooler, to the
    /// print-ready-PDF fallback). Shared by the preview window's and the File &gt; Print pane's Print button.
    /// </summary>
    private async Task PrintWithPreviewSettingsAsync(
        PrintPreviewSettings settings,
        bool canSpool,
        IReadOnlyList<string> printerNames,
        string? defaultPrinterName)
    {
        var hasPageRange = settings.PageFrom.HasValue || settings.PageTo.HasValue;
        var request = BuildPrintJobRequest(
            settings.PrintWhat switch
            {
                PrintWhat.EntireWorkbook => WorkbookExportPrintScope.VisibleWorkbook,
                PrintWhat.Selection => WorkbookExportPrintScope.SelectedRange,
                _ => WorkbookExportPrintScope.ActiveSheet,
            },
            hasPageRange ? PrintJobPageRangeKind.PageRange : PrintJobPageRangeKind.AllPages,
            hasPageRange ? settings.PageFrom : null,
            hasPageRange ? settings.PageTo : null,
            PrintSettingsPlanner.ClampCopies(settings.Copies),
            settings.Collated) with
        {
            IgnorePrintAreas = settings.IgnorePrintArea,
            Sides = settings.Sides switch
            {
                PrintPreviewSidesMode.TwoSidedLongEdge => PrintSides.TwoSidedLongEdge,
                PrintPreviewSidesMode.TwoSidedShortEdge => PrintSides.TwoSidedShortEdge,
                _ => PrintSides.OneSided,
            },
        };
        var targetPrinter = ResolvePreviewPrinterName(settings.PrinterName, canSpool, printerNames, defaultPrinterName);
        await ExecutePrintJobAsync(request, targetPrinter, canSpool);
    }

    private static string? ResolvePreviewPrinterName(
        string? selectedPrinterName,
        bool canSpool,
        IReadOnlyList<string> printerNames,
        string? defaultPrinterName) =>
        canSpool
            ? printerNames.FirstOrDefault(name => string.Equals(name, selectedPrinterName, StringComparison.Ordinal))
                ?? defaultPrinterName
            : null;

    private static async Task ShowPrintPreviewWarningAsync(Window owner, string message, TextBox target)
    {
        await AvaloniaUserMessageDialog.ShowWarningAsync(owner, message, owner.Title ?? string.Empty);
        AvaloniaCompactDialogChrome.FocusAndSelect(target);
    }

    private sealed record PrintPreviewToolbarJobControls(
        ComboBox Printer,
        TextBox Copies,
        CheckBox Collated,
        ComboBox Sides,
        TextBlock Status,
        ComboBox PageRangeMode);

    /// <summary>
    /// A WPF <c>CheckBox</c> placed in a <c>ToolBar</c> renders as a flat toggle button (no check glyph;
    /// a pressed, highlighted face when checked). The control stays a <see cref="CheckBox"/> for its
    /// checked state and automation, but is templated as that toggle button.
    /// </summary>
    private static void ApplyPreviewToolbarToggleChrome(CheckBox checkBox)
    {
        var checkedFace = Brush(201, 224, 247);
        var checkedBorder = Brush(86, 157, 229);
        checkBox.Padding = new Thickness(6, 2);
        checkBox.Template = new global::Avalonia.Controls.Templates.FuncControlTemplate<CheckBox>((owner, scope) =>
        {
            var presenter = new global::Avalonia.Controls.Presenters.ContentPresenter
            {
                Name = "PART_ContentPresenter",
                HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            };
            presenter.Bind(global::Avalonia.Controls.Presenters.ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = owner });
            presenter.Bind(global::Avalonia.Controls.Presenters.ContentPresenter.PaddingProperty, new Binding(nameof(TemplatedControl.Padding)) { Source = owner });
            scope.Register("PART_ContentPresenter", presenter);

            var face = new Border
            {
                Name = "PART_ToggleFace",
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Child = presenter,
            };
            face.Bind(
                Border.BackgroundProperty,
                new Binding(nameof(ToggleButton.IsChecked))
                {
                    Source = owner,
                    Converter = new global::Avalonia.Data.Converters.FuncValueConverter<bool?, IBrush>(
                        isChecked => isChecked == true ? checkedFace : Brushes.Transparent),
                });
            face.Bind(
                Border.BorderBrushProperty,
                new Binding(nameof(ToggleButton.IsChecked))
                {
                    Source = owner,
                    Converter = new global::Avalonia.Data.Converters.FuncValueConverter<bool?, IBrush>(
                        isChecked => isChecked == true ? checkedBorder : Brushes.Transparent),
                });
            return face;
        });
    }

    private static Border CreatePrintPreviewTopToolbar(
        PrintPreviewTopToolbarPlan plan,
        Button printButton,
        Action close,
        out PrintPreviewToolbarJobControls jobControls)
    {
        var printerBox = CreatePreviewComboBox(plan.PrinterComboWidth, plan.PrinterName);
        var copiesBox = new TextBox
        {
            Text = plan.CopiesText,
            Width = plan.CopiesBoxWidth,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        var collatedBox = new CheckBox
        {
            Content = plan.CollatedText,
            IsChecked = true,
            MinHeight = 20,
            MaxHeight = 20,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        ApplyPreviewToolbarToggleChrome(collatedBox);
        var sidesBox = CreatePreviewChoiceComboBox(plan.SidesComboWidth, plan.SidesOptions, plan.SidesSelectedIndex);
        var status = new TextBlock
        {
            Text = plan.StatusText,
            MinWidth = 120,
            MaxWidth = 280,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        var pageRange = CreatePreviewComboBox(plan.PageRangeComboWidth, plan.PageRangeChoices
            .Select(choice => choice.Text).ToArray(), (int)PrintPreviewPageRangeMode.AllPages);
        jobControls = new PrintPreviewToolbarJobControls(printerBox, copiesBox, collatedBox, sidesBox, status, pageRange);

        AutomationProperties.SetAutomationId(pageRange, "PrintPreviewToolbarPageRange");
        AutomationProperties.SetName(pageRange, UiText.Get("PrintPreview_PagesAutomationName"));
        AutomationProperties.SetAutomationId(printerBox, "PrintPreviewToolbarPrinter");
        AutomationProperties.SetAutomationId(copiesBox, "PrintPreviewToolbarCopies");
        AutomationProperties.SetAutomationId(collatedBox, "PrintPreviewToolbarCollated");
        AutomationProperties.SetAutomationId(sidesBox, "PrintPreviewToolbarSides");
        AutomationProperties.SetName(printerBox, plan.PrinterLabelText);
        AutomationProperties.SetName(copiesBox, plan.CopiesLabelText);
        AutomationProperties.SetName(sidesBox, plan.SidesLabelText);

        // The fixed-width controls take their natural width; the job status takes the remaining star
        // column and wraps inside it, so a wider platform font never pushes controls into the
        // overflow chevron or clips the "Ready: <printer>; 1 copy; N pages" text.
        var toolbarItems = new Control[]
        {
            printButton,
            new TextBlock { Text = plan.PrinterLabelText, FontSize = 12, FontFamily = FormulaBarFontFamily, VerticalAlignment = AvaloniaVerticalAlignment.Center },
            printerBox,
            new TextBlock { Text = plan.CopiesLabelText, FontSize = 12, FontFamily = FormulaBarFontFamily, VerticalAlignment = AvaloniaVerticalAlignment.Center },
            copiesBox,
            collatedBox,
            new TextBlock { Text = plan.SidesLabelText, FontSize = 12, FontFamily = FormulaBarFontFamily, VerticalAlignment = AvaloniaVerticalAlignment.Center },
            sidesBox,
            status,
            pageRange,
        };
        var toolbar = new Grid
        {
            Margin = new Thickness(1, 6, 0, 4),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,*,Auto"),
        };
        for (var i = 0; i < toolbarItems.Length; i++)
        {
            var item = toolbarItems[i];
            item.Margin = new Thickness(item.Margin.Left, item.Margin.Top, item.Margin.Right + 4, item.Margin.Bottom);
            Grid.SetColumn(item, i);
            toolbar.Children.Add(item);
        }

        printButton.Width = PrintPreviewSurfacePlanner.TopToolbarPrintButtonWidth;
        printButton.MinWidth = PrintPreviewSurfacePlanner.TopToolbarPrintButtonWidth;
        var overflowItem = new MenuItem { Header = plan.CloseButtonText };
        overflowItem.Click += (_, _) => close();
        var overflowButton = new Button
        {
            Width = 18,
            MinWidth = 18,
            Height = 18,
            MinHeight = 18,
            MaxHeight = 18,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = new global::Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M 2 3 L 7 8 L 12 3"),
                Stroke = Brush(92, 92, 92),
                StrokeThickness = 1,
                Width = 14,
                Height = 10,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            },
            Flyout = new MenuFlyout { Items = { overflowItem } },
        };
        var topControls = new Border
        {
            Background = PrintPreviewChromeBackground,
            Margin = new Thickness(3, 0, 0, 0),
            Child = toolbar,
        };
        AutomationProperties.SetAutomationId(overflowButton, PrintPreviewDialogPlanner.CloseButtonAutomationId);
        AutomationProperties.SetName(overflowButton, plan.CloseButtonText);
        var topLayout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,18"),
            Children = { topControls, overflowButton },
        };
        Grid.SetColumn(overflowButton, 1);

        return new Border
        {
            Height = PrintPreviewSurfacePlanner.TopToolbarHeight,
            MinHeight = PrintPreviewSurfacePlanner.TopToolbarHeight,
            MaxHeight = PrintPreviewSurfacePlanner.TopToolbarHeight,
            Background = Brushes.White,
            BorderBrush = Brush(190, 204, 220),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = topLayout,
        };
    }

    /// <summary>
    /// Wiring context for a live (non-parity-fixture) settings rail: which sheet the sheet-mutating
    /// commands (orientation/paper size/margins/scaling/print options) apply to, accessors for the
    /// ephemeral <see cref="PrintPreviewSettings"/> the rail's non-command controls update in place,
    /// and the callback that re-paginates and repaints the active preview page after any change
    /// (R118-print-preview-settings-rail-wiring). Null for the static parity-capture fixture, whose
    /// rail intentionally stays a read-only snapshot.
    /// </summary>
    private sealed record PrintPreviewSettingsRailInteraction(
        SheetId SheetId,
        Func<PrintPreviewSettings> GetSettings,
        Action<PrintPreviewSettings> SetSettings,
        Action Repaginate,
        Window Owner,
        Func<string?> ResolvePrinterName,
        string PrinterPropertiesAutomationId);

    private async Task ShowPrintPreviewPageSetupAsync(
        PageLayoutPageSetupOpenSource source,
        Action repaginate)
    {
        await ShowPageSetupDialogAsync(source);
        repaginate();
    }

    private ScrollViewer CreatePrintPreviewSettingsRail(
        PrintPreviewSettingsRailPlan plan,
        PrintPreviewSettingsRailInteraction? interaction,
        Action<ComboBox> registerPrinterBox,
        out (TextBox From, TextBox To) rangeFields,
        PrintPreviewToolbarJobControls? toolbarControls = null)
    {
        void ApplyAction(PrintPreviewSettingsPanelActionPlan action)
        {
            if (interaction is null)
                return;

            switch (action.Kind)
            {
                case PrintPreviewSettingsPanelActionKind.UpdatePreviewSettings:
                    if (action.Settings is null)
                        return;

                    interaction.SetSettings(action.Settings);
                    if (action.RefreshPreview)
                        interaction.Repaginate();
                    break;

                case PrintPreviewSettingsPanelActionKind.ExecuteCommand:
                    if (action.Command is { } command)
                        _session.ExecuteReviewCommand(command);
                    if (action.RefreshPreview)
                        interaction.Repaginate();
                    break;

                case PrintPreviewSettingsPanelActionKind.OpenCustomMargins:
                    if (interaction is not null)
                        RunGuarded(() => ShowPrintPreviewPageSetupAsync(
                            PageLayoutPageSetupOpenSource.CustomMargins,
                            interaction.Repaginate));
                    break;
                case PrintPreviewSettingsPanelActionKind.OpenPageSetup:
                    if (interaction is not null)
                        RunGuarded(() => ShowPrintPreviewPageSetupAsync(
                            PageLayoutPageSetupOpenSource.ScaleToFit,
                            interaction.Repaginate));
                    break;
            }
        }

        var copiesBox = new TextBox
        {
            Text = plan.CopiesText,
            Width = plan.CopiesBoxWidth,
            Height = PrintPreviewSurfacePlanner.SettingsTextBoxHeight,
            MinHeight = PrintPreviewSurfacePlanner.SettingsTextBoxHeight,
            MaxHeight = PrintPreviewSurfacePlanner.SettingsTextBoxHeight,
            Padding = new Thickness(4, 1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        if (interaction is not null)
        {
            copiesBox.TextChanged += (_, _) => ApplyAction(
                PrintPreviewSettingsPanelPlanner.CreateCopiesAction(interaction.GetSettings(), copiesBox.Text));
        }

        var printerBox = CreatePreviewComboBox(plan.PrinterComboWidth, plan.PrinterName);
        AutomationProperties.SetAutomationId(printerBox, "PrintPreviewSettingsPrinter");
        registerPrinterBox(printerBox);

        if (interaction is not null)
        {
            printerBox.SelectionChanged += (_, _) => ApplyAction(
                PrintPreviewSettingsPanelPlanner.CreatePrinterAction(
                    interaction.GetSettings(),
                    printerBox.SelectedItem as string ?? plan.PrinterName));
        }

        var printerPropertiesButton = new Button
        {
            Content = plan.PrinterPropertiesButtonText,
            Height = PrintPreviewSurfacePlanner.SettingsButtonHeight,
            MinHeight = PrintPreviewSurfacePlanner.SettingsButtonHeight,
            MaxHeight = PrintPreviewSurfacePlanner.SettingsButtonHeight,
            Padding = new Thickness(6, 1),
            Background = Brushes.White,
            BorderBrush = Brush(112, 112, 112),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        if (interaction is not null)
        {
            ConfigurePrinterPropertiesButton(
                printerPropertiesButton,
                interaction.PrinterPropertiesAutomationId,
                interaction.Owner,
                printerBox,
                interaction.ResolvePrinterName);
        }

        // Entire Workbook is backed by the shared workbook page stream, so changing this choice
        // repaginates the live preview instead of merely changing a disabled-looking option.
        var printWhatOptions = plan.Settings.PrintWhatOptions;
        var printWhatBox = CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, printWhatOptions, plan.Settings.PrintWhatSelectedIndex);
        AutomationProperties.SetAutomationId(printWhatBox, "PrintPreviewSettingsPrintWhatBox");
        if (interaction is not null)
        {
            printWhatBox.SelectionChanged += (_, _) => ApplyAction(
                PrintPreviewSettingsPanelPlanner.CreatePrintWhatAction(plan.Settings, interaction.GetSettings(), printWhatBox.SelectedIndex));
        }

        var pageRangeRow = CreatePageRangeRow(
            plan.PageRange,
            out rangeFields,
            interaction is null ? null : (fromText, toText) =>
            {
                if (toolbarControls is not null)
                {
                    var mode = string.IsNullOrWhiteSpace(fromText) && string.IsNullOrWhiteSpace(toText)
                        ? PrintPreviewPageRangeMode.AllPages
                        : PrintPreviewPageRangeMode.Pages;
                    toolbarControls.PageRangeMode.SelectedIndex = (int)mode;
                }
                else
                {
                    ApplyAction(PrintPreviewSettingsPanelPlanner.CreatePageRangeAction(
                        interaction.GetSettings(), fromText, toText));
                }
            });

        var sidesBox = CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.SidesOptions, plan.Settings.SidesSelectedIndex);
        if (interaction is not null)
        {
            sidesBox.SelectionChanged += (_, _) => ApplyAction(
                PrintPreviewSettingsPanelPlanner.CreateSidesAction(plan.Settings, interaction.GetSettings(), sidesBox.SelectedIndex));
        }

        var collationBox = CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.CollationOptions, plan.Settings.CollationSelectedIndex);
        AutomationProperties.SetAutomationId(collationBox, "PrintPreviewSettingsCollation");
        if (interaction is not null)
        {
            collationBox.SelectionChanged += (_, _) => ApplyAction(
                PrintPreviewSettingsPanelPlanner.CreateCollationAction(plan.Settings, interaction.GetSettings(), collationBox.SelectedIndex));
        }

        if (toolbarControls is not null)
        {
            toolbarControls.Copies.Bind(TextBox.TextProperty, new Binding(nameof(TextBox.Text))
            {
                Source = copiesBox,
                Mode = BindingMode.TwoWay,
            });
            toolbarControls.Sides.Bind(ComboBox.SelectedIndexProperty, new Binding(nameof(ComboBox.SelectedIndex))
            {
                Source = sidesBox,
                Mode = BindingMode.TwoWay,
            });

            // Discovery populates the two selectors separately. Ignore transient empty selections
            // and only copy a printer after that printer is present in the other selector.
            toolbarControls.Printer.SelectionChanged += (_, _) =>
            {
                if (toolbarControls.Printer.SelectedItem is string name &&
                    !Equals(printerBox.SelectedItem, name) && printerBox.Items.Contains(name))
                {
                    printerBox.SelectedItem = name;
                }
            };
            printerBox.SelectionChanged += (_, _) =>
            {
                if (printerBox.SelectedItem is string name &&
                    !Equals(toolbarControls.Printer.SelectedItem, name) && toolbarControls.Printer.Items.Contains(name))
                {
                    toolbarControls.Printer.SelectedItem = name;
                }
            };
            toolbarControls.Collated.IsCheckedChanged += (_, _) =>
                collationBox.SelectedIndex = toolbarControls.Collated.IsChecked == true ? 0 : 1;
            collationBox.SelectionChanged += (_, _) =>
            {
                if (collationBox.SelectedItem is not null)
                    toolbarControls.Collated.IsChecked = collationBox.SelectedIndex == 0;
            };
        }

        var orientationBox = CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.OrientationOptions, plan.Settings.OrientationSelectedIndex);
        AutomationProperties.SetAutomationId(orientationBox, "PrintPreviewSettingsOrientationBox");
        if (interaction is not null)
        {
            orientationBox.SelectionChanged += (_, _) => ApplyAction(
                PrintPreviewSettingsPanelPlanner.CreateOrientationAction(interaction.SheetId, plan.Settings, orientationBox.SelectedIndex));
        }

        var paperSizeBox = CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.PaperSizeOptions, plan.Settings.PaperSizeSelectedIndex);
        AutomationProperties.SetAutomationId(paperSizeBox, "PrintPreviewSettingsPaperSizeBox");
        if (interaction is not null)
        {
            paperSizeBox.SelectionChanged += (_, _) => ApplyAction(
                PrintPreviewSettingsPanelPlanner.CreatePaperSizeAction(interaction.SheetId, plan.Settings, paperSizeBox.SelectedIndex));
        }

        var marginsBox = CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.MarginOptions, plan.Settings.MarginsSelectedIndex);
        AutomationProperties.SetAutomationId(marginsBox, "PrintPreviewSettingsMarginsBox");
        if (interaction is not null)
        {
            marginsBox.SelectionChanged += (_, _) =>
            {
                var action = PrintPreviewSettingsPanelPlanner.CreateMarginsAction(interaction.SheetId, plan.Settings, marginsBox.SelectedIndex);
                ApplyAction(action);
                if (action.ResetSelection)
                    marginsBox.SelectedIndex = plan.Settings.MarginsSelectedIndex;
            };
        }

        var scalingBox = CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.ScalingOptions, plan.Settings.ScalingSelectedIndex);
        AutomationProperties.SetAutomationId(scalingBox, "PrintPreviewSettingsScalingBox");
        if (interaction is not null)
        {
            scalingBox.SelectionChanged += (_, _) =>
            {
                var action = PrintPreviewSettingsPanelPlanner.CreateScalingAction(interaction.SheetId, plan.Settings, scalingBox.SelectedIndex);
                ApplyAction(action);
                if (action.ResetSelection)
                    scalingBox.SelectedIndex = plan.Settings.ScalingSelectedIndex;
            };
        }

        var ignorePrintAreaBox = new CheckBox { Content = plan.IgnorePrintAreaText, IsChecked = plan.Settings.IgnorePrintAreaChecked, IsEnabled = plan.Settings.IgnorePrintAreaEnabled, MinHeight = 20, MaxHeight = 20, FontSize = 12, FontFamily = FormulaBarFontFamily };
        AutomationProperties.SetAutomationId(ignorePrintAreaBox, "PrintPreviewSettingsIgnorePrintAreaBox");
        // Print windows are outside the scoped dialog chrome (DialogControlStyles.IsOutOfScope), so
        // Fluent's disabled foreground would grey the label. WPF greys only the glyph of a disabled
        // check box (the label keeps its ink), so the disabled label brushes stay at the rail's ink.
        ignorePrintAreaBox.Resources["CheckBoxForegroundUncheckedDisabled"] = Brushes.Black;
        ignorePrintAreaBox.Resources["CheckBoxForegroundCheckedDisabled"] = Brushes.Black;
        ignorePrintAreaBox.Resources["CheckBoxForegroundIndeterminateDisabled"] = Brushes.Black;
        if (interaction is not null)
        {
            ignorePrintAreaBox.IsCheckedChanged += (_, _) => ApplyAction(
                PrintPreviewSettingsPanelPlanner.CreateIgnorePrintAreaAction(interaction.GetSettings(), ignorePrintAreaBox.IsChecked == true));
        }

        var printGridlinesBox = new CheckBox { Content = plan.PrintGridlinesText, IsChecked = plan.Settings.PrintGridlines, MinHeight = 20, MaxHeight = 20, FontSize = 12, FontFamily = FormulaBarFontFamily };
        var printHeadingsBox = new CheckBox { Content = plan.PrintHeadingsText, IsChecked = plan.Settings.PrintHeadings, MinHeight = 20, MaxHeight = 20, FontSize = 12, FontFamily = FormulaBarFontFamily };
        AutomationProperties.SetAutomationId(printGridlinesBox, "PrintPreviewSettingsGridlinesBox");
        AutomationProperties.SetAutomationId(printHeadingsBox, "PrintPreviewSettingsHeadingsBox");
        if (interaction is not null)
        {
            void ApplyPrintOptions() => ApplyAction(
                PrintPreviewSettingsPanelPlanner.CreatePrintOptionsAction(
                    interaction.SheetId,
                    printGridlinesBox.IsChecked == true,
                    printHeadingsBox.IsChecked == true));

            printGridlinesBox.IsCheckedChanged += (_, _) => ApplyPrintOptions();
            printHeadingsBox.IsCheckedChanged += (_, _) => ApplyPrintOptions();
        }

        var panel = new StackPanel
        {
            Spacing = PrintPreviewSurfacePlanner.SettingsRailSpacing,
            Margin = new Thickness(10, PrintPreviewSurfacePlanner.SettingsRailTopMargin, 10, 10),
            Children =
            {
                CreateSettingsSection(plan.CopiesSectionText),
                copiesBox,
                CreateSettingsSection(plan.PrinterSectionText),
                printerBox,
                printerPropertiesButton,
                CreateSettingsSection(plan.PrintWhatLabelText),
                printWhatBox,
                CreateSettingsSection(plan.PagesLabelText),
                pageRangeRow,
                CreateSettingsSection(plan.SidesSectionText),
                sidesBox,
                CreateSettingsSection(plan.CollationSectionText),
                collationBox,
                CreateSettingsSection(plan.OrientationLabelText),
                orientationBox,
                CreateSettingsSection(plan.PaperSizeLabelText),
                paperSizeBox,
                CreateSettingsSection(plan.MarginsLabelText),
                marginsBox,
                CreateSettingsSection(plan.ScalingLabelText),
                scalingBox,
                ignorePrintAreaBox,
                CreateSettingsSection(plan.PrintOptionsSectionText),
                printGridlinesBox,
                printHeadingsBox,
            },
        };

        // WPF shows the classic always-expanded vertical bar beside the rail while its rows overflow.
        // The print windows sit outside the scoped dialog chrome, so the rail carries the classic bar
        // resources and reserves the bar's lane (no overlay auto-hide) itself.
        var railScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            AllowAutoHide = false,
            Background = Brush(245, 245, 245),
            Content = panel,
        };
        ClassicScrollBarChrome.ApplyResources(railScroll.Resources);
        foreach (var barStyle in ClassicScrollBarChrome.CreateDialogScrollBarStyles())
            railScroll.Styles.Add(barStyle);
        return railScroll;
    }

    private static Grid CreatePageRangeRow(
        PrintPreviewPageRangeFieldsPlan plan,
        out (TextBox From, TextBox To) rangeFields,
        Action<string, string>? onChanged)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto"),
        };
        var fromBox = new TextBox
        {
            Text = plan.FromPageText,
            Width = plan.PageBoxWidth,
            Height = PrintPreviewSurfacePlanner.SettingsTextBoxHeight,
            MinHeight = PrintPreviewSurfacePlanner.SettingsTextBoxHeight,
            MaxHeight = PrintPreviewSurfacePlanner.SettingsTextBoxHeight,
            Padding = new Thickness(4, 1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        var toLabel = new TextBlock { Text = plan.ToSeparatorText, FontSize = 12, FontFamily = FormulaBarFontFamily, Margin = new Thickness(6, 0), VerticalAlignment = AvaloniaVerticalAlignment.Center };
        var toBox = new TextBox
        {
            Text = plan.ToPageText,
            Width = plan.PageBoxWidth,
            Height = PrintPreviewSurfacePlanner.SettingsTextBoxHeight,
            MinHeight = PrintPreviewSurfacePlanner.SettingsTextBoxHeight,
            MaxHeight = PrintPreviewSurfacePlanner.SettingsTextBoxHeight,
            Padding = new Thickness(4, 1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        rangeFields = (fromBox, toBox);
        AutomationProperties.SetAutomationId(fromBox, "PrintPreviewSettingsFromPage");
        AutomationProperties.SetAutomationId(toBox, "PrintPreviewSettingsToPage");
        AutomationProperties.SetName(fromBox, UiText.Get("PrintPreview_FromPageAutomationName"));
        AutomationProperties.SetName(toBox, UiText.Get("PrintPreview_ToPageAutomationName"));
        if (onChanged is not null)
        {
            fromBox.TextChanged += (_, _) => onChanged(fromBox.Text ?? "", toBox.Text ?? "");
            toBox.TextChanged += (_, _) => onChanged(fromBox.Text ?? "", toBox.Text ?? "");
        }

        Grid.SetColumn(fromBox, 0);
        Grid.SetColumn(toLabel, 1);
        Grid.SetColumn(toBox, 2);
        row.Children.Add(fromBox);
        row.Children.Add(toLabel);
        row.Children.Add(toBox);
        return row;
    }

    private static TextBlock CreateSettingsSection(string text) =>
        new()
        {
            // WPF Labels strip mnemonic underscores automatically; Avalonia TextBlocks render the
            // leading/embedded "_" literally (e.g. "_Copies:", "Pa_ges:"). Strip them for parity.
            Text = StripDisplayMnemonic(text),
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 4, 0, -4),
        };

    private static (Grid Bar, TextBox Query, Button Previous, Button Next, TextBlock Status)
        CreatePrintPreviewFindBar(PrintPreviewFindBarPlan plan)
    {
        var findBar = new Grid
        {
            Background = PrintPreviewChromeBackground,
            ColumnDefinitions = new ColumnDefinitions("240,Auto,Auto,*"),
            MinHeight = 26,
        };
        var findBox = new TextBox
        {
            PlaceholderText = plan.PlaceholderText,
            Margin = new Thickness(6, 2),
            Height = 22,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            FontStyle = FontStyle.Italic,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
        };
        var previous = CreateFindNavigationButton(plan.PreviousButtonText, isPrevious: true);
        var next = CreateFindNavigationButton(plan.NextButtonText, isPrevious: false);
        var status = new TextBlock
        {
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(6, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(findBox, "PrintPreviewFindBox");
        AutomationProperties.SetName(findBox, plan.PlaceholderText);
        AutomationProperties.SetAutomationId(previous, "PrintPreviewFindPreviousButton");
        AutomationProperties.SetName(previous, StripDisplayMnemonic(UiText.Get("ErrorChecking_PreviousButton")));
        AutomationProperties.SetAutomationId(next, "PrintPreviewFindNextButton");
        AutomationProperties.SetName(next, StripDisplayMnemonic(UiText.Get("FindReplace_FindNext")));
        AutomationProperties.SetAutomationId(status, "PrintPreviewFindStatus");
        ConfigureTextAnnouncement(status);
        Grid.SetColumn(findBox, 0);
        Grid.SetColumn(previous, 1);
        Grid.SetColumn(next, 2);
        Grid.SetColumn(status, 3);
        findBar.Children.Add(findBox);
        findBar.Children.Add(previous);
        findBar.Children.Add(next);
        findBar.Children.Add(status);
        return (findBar, findBox, previous, next, status);
    }

    private static Border CreatePrintPreviewPane(Control documentToolbar, Border pageHost, Grid findBar)
    {
        var pane = new DockPanel();
        DockPanel.SetDock(documentToolbar, Dock.Top);
        DockPanel.SetDock(findBar, Dock.Bottom);
        pane.Children.Add(documentToolbar);
        pane.Children.Add(findBar);
        pane.Children.Add(pageHost);

        return new Border
        {
            Background = Brushes.White,
            Child = pane,
        };
    }

    private static Button CreateFindNavigationButton(string toolTip, bool isPrevious)
    {
        var button = new Button
        {
            Width = 18,
            MinWidth = 18,
            Height = 18,
            MinHeight = 18,
            MaxHeight = 18,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            Content = new global::Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(isPrevious ? "M 10 2 L 5 7 L 10 12" : "M 4 2 L 9 7 L 4 12"),
                Stroke = Brush(0, 102, 204),
                StrokeThickness = 1.5,
                Width = 12,
                Height = 14,
            },
        };
        ToolTip.SetTip(button, toolTip);
        return button;
    }

    private static Border CreatePreviewDocumentToolbar(IReadOnlyList<Control> controls)
    {
        var chrome = PrintPreviewSurfacePlanner.DocumentToolbarChrome;
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = chrome.ButtonSpacing,
            Margin = new Thickness(chrome.LeftPadding, 4, 0, 4),
        };
        toolbar.Children.AddRange(controls);

        return new Border
        {
            MinHeight = chrome.Height,
            Background = Brush(245, 245, 245),
            BorderBrush = Brush(208, 208, 208),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = toolbar,
            },
        };
    }

    private static Border CreateDocumentToolbarSeparator(PrintPreviewDocumentToolbarChromePlan chrome) =>
        new()
        {
            Width = 1,
            Height = chrome.SeparatorHeight,
            Background = Brush(190, 190, 190),
            Margin = new Thickness(1, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };

    private static Button CreateDocumentToolbarIcon(
        RibbonCommandIconKind kind,
        string toolTip,
        PrintPreviewDocumentToolbarChromePlan chrome)
    {
        var button = new Button
        {
            Width = chrome.ButtonWidth,
            MinWidth = chrome.ButtonWidth,
            Height = chrome.ButtonHeight,
            MinHeight = chrome.ButtonHeight,
            MaxHeight = chrome.ButtonHeight,
            Padding = new Thickness(4, 2),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            Content = AvaloniaRibbonIcons.BuildMonochrome(kind, chrome.IconSize, null, Brush(92, 92, 92)),
        };
        ToolTip.SetTip(button, toolTip);
        AutomationProperties.SetName(button, toolTip);
        return button;
    }

    private static Button CreatePreviewToolbarButton(string text) =>
        ApplyPreviewToolbarButtonChrome(new Button { Content = text }, 26);

    private static Button CreatePreviewToolbarButton(PrintPreviewNavigationGlyphPlan plan)
    {
        var button = CreatePreviewToolbarButton(plan.Text);
        button.Content = CreatePreviewNavigationGlyph(plan.Command);
        AutomationProperties.SetAutomationId(button, plan.AutomationId);
        var commandPlan = PrintPreviewDialogPlanner.CreateToolbarCommandPlan(plan.Command);
        var name = UiText.Get(commandPlan.AutomationNameResourceKey);
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        return button;
    }

    private static Control CreatePreviewNavigationGlyph(PrintPreviewToolbarCommand command)
    {
        var kind = command switch
        {
            PrintPreviewToolbarCommand.FirstPage or PrintPreviewToolbarCommand.PreviousPage => RibbonCommandIconKind.Previous,
            PrintPreviewToolbarCommand.NextPage or PrintPreviewToolbarCommand.LastPage => RibbonCommandIconKind.Next,
            _ => RibbonCommandIconKind.Generic,
        };

        var glyph = AvaloniaRibbonIcons.BuildMonochrome(kind, 14, null, Brush(92, 92, 92));
        if (command is not (PrintPreviewToolbarCommand.FirstPage or PrintPreviewToolbarCommand.LastPage))
            return glyph;

        var wrapper = new Grid { Width = 14, Height = 14 };
        wrapper.Children.Add(glyph);
        wrapper.Children.Add(new Border
        {
            Width = 1,
            Height = 11,
            Background = Brush(92, 92, 92),
            HorizontalAlignment = command == PrintPreviewToolbarCommand.FirstPage
                ? AvaloniaHorizontalAlignment.Left
                : AvaloniaHorizontalAlignment.Right,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        });
        return wrapper;
    }

    private static Button ApplyPreviewToolbarButtonChrome(Button button, double minWidth)
    {
        AvaloniaCompactDialogChrome.ApplyButton(button, PrintPreviewChromeStyle, minWidth);
        return button;
    }

    private static ComboBox CreatePreviewComboBox(double width, string selectedText) =>
        ApplyPreviewComboBoxChrome(new()
        {
            Width = width,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(5, 0, 4, 0),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            ItemsSource = new[] { selectedText },
            SelectedIndex = 0,
        });

    private static ComboBox CreatePreviewChoiceComboBox<TValue>(
        double width,
        IReadOnlyList<PrintPreviewChoice<TValue>> choices,
        int selectedIndex)
    {
        var selected = choices.Count == 0
            ? -1
            : Math.Clamp(selectedIndex, 0, choices.Count - 1);

        return CreatePreviewComboBox(
            width,
            choices.Select(choice => new ComboBoxItem
            {
                Content = choice.Text,
                IsEnabled = choice.IsEnabled,
            }).ToArray(),
            selected);
    }

    private static ComboBox CreatePreviewComboBox(double width, IReadOnlyList<object> items, int selectedIndex) =>
        ApplyPreviewComboBoxChrome(new()
        {
            Width = width,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(5, 0, 4, 0),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            ItemsSource = items,
            SelectedIndex = selectedIndex,
        });

    private static TextBox ApplyPreviewTextBoxChrome(TextBox textBox)
    {
        AvaloniaCompactDialogChrome.ApplyTextBox(textBox, PrintPreviewChromeStyle);
        return textBox;
    }

    private static ComboBox ApplyPreviewComboBoxChrome(ComboBox comboBox)
    {
        AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, PrintPreviewChromeStyle);
        return comboBox;
    }

    private static ScrollViewer BuildPreviewDocumentViewerSurface(Control page)
    {
        var surface = new Border
        {
            Background = PrintPreviewSurfaceBackground,
            Padding = PrintPreviewPaperPadding,
            Child = page,
        };

        return new ScrollViewer
        {
            Background = PrintPreviewSurfaceBackground,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = surface,
        };
    }

    /// <summary>
    /// Builds one preview page at its natural paper size and paints the flattened print instructions.
    /// </summary>
    internal static Control BuildPreviewPageView(PrintPreviewPaginationContext context, int pageIndex) =>
        BuildPreviewPageViewCore(context.BuildPainting(pageIndex));

    internal static Control BuildPreviewPageView(AvaloniaPrintPreviewPaginationContext context, int pageIndex) =>
        BuildPreviewPageViewCore(context.BuildPainting(pageIndex));

    private static Control BuildPreviewPageViewCore(PrintPreviewPagePainting? painting)
    {
        if (painting is null)
        {
            return new TextBlock
            {
                Text = UiText.Get("ShellLoc_PageCouldNotRender"),
                Foreground = Brush(92, 92, 92),
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
        }

        var canvas = new PrintPreviewTextCanvas
        {
            Width = painting.PageBounds.Width,
            Height = painting.PageBounds.Height,
            Background = Brushes.White,
            ClipToBounds = true,
        };
        AutomationProperties.SetAutomationId(canvas, PrintPreviewDialogPlanner.PageCanvasAutomationId);

        RenderPreviewInstructions(canvas, painting.Instructions);

        var pageBorder = new Border
        {
            Width = painting.PageBounds.Width,
            Height = painting.PageBounds.Height,
            Background = Brushes.White,
            BorderBrush = Brush(160, 160, 160),
            BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0,
                OffsetY = 2,
                Blur = 8,
                Color = Color.FromArgb(64, 0, 0, 0),
            }),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
            Child = canvas,
        };

        return pageBorder;
    }

    private static void RenderPreviewInstructions(
        PrintPreviewTextCanvas canvas,
        IReadOnlyList<PrintPreviewPaintInstruction> instructions)
    {
        for (var index = 0; index < instructions.Count; index++)
        {
            var instruction = instructions[index];
            switch (instruction.Kind)
            {
                case PrintPreviewPaintKind.Rectangle:
                    AddPreviewRectangle(canvas, instruction);
                    break;
                case PrintPreviewPaintKind.Line:
                    AddPreviewLine(canvas, instruction);
                    break;
                case PrintPreviewPaintKind.Text:
                    AddPreviewText(canvas, index, instruction);
                    break;
                case PrintPreviewPaintKind.Ellipse:
                    AddPreviewEllipse(canvas, instruction);
                    break;
                case PrintPreviewPaintKind.Polygon:
                    AddPreviewPolygon(canvas, instruction);
                    break;
                case PrintPreviewPaintKind.Image:
                    AddPreviewImage(canvas, instruction);
                    break;
                case PrintPreviewPaintKind.Shape:
                    AddPreviewShape(canvas, instruction);
                    break;
            }
        }
    }

    private static void AddPreviewShape(Canvas canvas, PrintPreviewPaintInstruction instruction)
    {
        if (instruction.ShapeBlock is not { } shape) return;
        var clip = instruction.ShapeClip;
        var layer = new Canvas { Width = Math.Max(0, clip.Width), Height = Math.Max(0, clip.Height), ClipToBounds = true };
        Canvas.SetLeft(layer, clip.Left);
        Canvas.SetTop(layer, clip.Top);
        var bounds = shape.Bounds;
        Free.Shared.Drawing.LayoutPoint Local(Free.Shared.Drawing.LayoutPoint p) => new(p.X - bounds.Left, p.Y - bounds.Top);
        Free.Shared.Drawing.ShapeGeometry LocalGeometry(Free.Shared.Drawing.ShapeGeometry source) => new(source.Contours.Select(contour =>
            new Free.Shared.Drawing.ShapeContour(Local(contour.Start), contour.Segments.Select(segment => segment with
            {
                End = Local(segment.End), Control1 = Local(segment.Control1), Control2 = Local(segment.Control2)
            }).ToArray(), contour.Closed, contour.Filled)).ToArray());
        var path = new global::Avalonia.Controls.Shapes.Path
        {
            Width = Math.Max(1, bounds.Width), Height = Math.Max(1, bounds.Height),
            Data = Free.Shared.Drawing.Avalonia.AvaloniaShapeGeometryAdapter.ToGeometry(LocalGeometry(shape.Geometry)),
            Fill = shape.Appearance.Paint.HasFill && !shape.BlackAndWhite ? PreviewBrush(FreeX.App.Presentation.ConditionalFormatting.PresentationRgb.FromCellColor(shape.Appearance.Paint.Fill)) : null,
            Stroke = shape.Appearance.Paint.HasOutline ? shape.BlackAndWhite ? Brushes.Black : PreviewBrush(FreeX.App.Presentation.ConditionalFormatting.PresentationRgb.FromCellColor(shape.Appearance.Paint.Outline)) : null,
            StrokeThickness = shape.Appearance.Outline.ThicknessDip,
            Stretch = Stretch.None
        };
        if (!shape.BlackAndWhite && shape.Appearance.Paint.HasFill && shape.Appearance.FillGradient is { } gradient)
            path.Fill = CreateDrawingShapeGradientBrush(shape.Appearance.Paint.Fill, gradient);
        if (GetShapeOutlineDashArray(shape.Appearance.Outline.Dash) is { } dash)
            path.StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double>(dash);
        ApplyDrawingObjectTransform(path, shape.Appearance.Transform.RotationDegrees, shape.Appearance.Transform.FlipHorizontal, shape.Appearance.Transform.FlipVertical);
        Canvas.SetLeft(path, bounds.Left - clip.Left);
        Canvas.SetTop(path, bounds.Top - clip.Top);
        layer.Children.Add(path);
        foreach (var arrow in shape.Arrowheads)
        {
            var arrowPath = new global::Avalonia.Controls.Shapes.Path
            {
                Width = Math.Max(1, bounds.Width), Height = Math.Max(1, bounds.Height),
                Data = Free.Shared.Drawing.Avalonia.AvaloniaShapeGeometryAdapter.ToGeometry(LocalGeometry(arrow)),
                Fill = shape.BlackAndWhite ? Brushes.Black : PreviewBrush(FreeX.App.Presentation.ConditionalFormatting.PresentationRgb.FromCellColor(shape.Appearance.Paint.Outline)),
                Stretch = Stretch.None
            };
            ApplyDrawingObjectTransform(arrowPath, shape.Appearance.Transform.RotationDegrees,
                shape.Appearance.Transform.FlipHorizontal, shape.Appearance.Transform.FlipVertical);
            Canvas.SetLeft(arrowPath, bounds.Left - clip.Left);
            Canvas.SetTop(arrowPath, bounds.Top - clip.Top);
            layer.Children.Add(arrowPath);
        }
        if (!string.IsNullOrWhiteSpace(shape.Text))
        {
            var text = new TextBlock { Text = shape.Text, FontSize = shape.TextFont.FontSize,
                FontWeight = shape.TextFont.Bold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = shape.TextFont.Italic ? FontStyle.Italic : FontStyle.Normal,
                Foreground = shape.BlackAndWhite ? Brushes.Black : PreviewBrush(shape.TextFont.Color),
                TextAlignment = shape.TextHorizontalAlignment switch
                {
                    DrawingShapeTextHAlign.Center => TextAlignment.Center,
                    DrawingShapeTextHAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left
                },
                VerticalAlignment = shape.TextVerticalAlignment switch
                {
                    DrawingShapeTextVAnchor.Middle => global::Avalonia.Layout.VerticalAlignment.Center,
                    DrawingShapeTextVAnchor.Bottom => global::Avalonia.Layout.VerticalAlignment.Bottom,
                    _ => global::Avalonia.Layout.VerticalAlignment.Top
                },
                TextWrapping = shape.TextWrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
            var textFrame = new Border { Width = Math.Max(0, bounds.Width), Height = Math.Max(0, bounds.Height),
                Padding = new Thickness(shape.TextInsets?.Left ?? 4, shape.TextInsets?.Top ?? 4,
                    shape.TextInsets?.Right ?? 4, shape.TextInsets?.Bottom ?? 4), ClipToBounds = true, Child = text };
            ApplyDrawingObjectTransform(textFrame, shape.Appearance.Transform.RotationDegrees, false, false);
            Canvas.SetLeft(textFrame, bounds.Left - clip.Left);
            Canvas.SetTop(textFrame, bounds.Top - clip.Top);
            layer.Children.Add(textFrame);
        }
        canvas.Children.Add(layer);
    }

    /// <summary>
    /// R168-presentation-preview-headerfooter-picture-1: paints a header/footer <c>&amp;G</c> picture
    /// as the real raster image, stretched to the bounds the shared geometry planner already scaled
    /// uniformly (so <c>Stretch.Fill</c> here cannot distort it -- the rect carries the picture's own
    /// aspect ratio). Undecodable bytes fall back to the same neutral placeholder box this preview
    /// paints for a worksheet picture, rather than leaving a hole where the picture should be.
    /// </summary>
    private static void AddPreviewImage(Canvas canvas, PrintPreviewPaintInstruction instruction)
    {
        var width = Math.Max(0, instruction.Width);
        var height = Math.Max(0, instruction.Height);
        if (instruction.ImageBytes is not { Length: > 0 } bytes || !TryCreateDrawingBitmap(bytes, out var bitmap))
        {
            AddPreviewRectangle(canvas, PrintPreviewPaintInstruction.Rectangle(
                new LayoutRect(instruction.Left, instruction.Top, width, height),
                PrintPreviewInstructionBuilder.PicturePlaceholderFill));
            return;
        }

        var image = new AvaloniaImage
        {
            Source = bitmap,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill,
        };

        Canvas.SetLeft(image, instruction.Left);
        Canvas.SetTop(image, instruction.Top);
        canvas.Children.Add(image);
    }

    private static void AddPreviewRectangle(Canvas canvas, PrintPreviewPaintInstruction instruction)
    {
        var rect = new AvaloniaRectangle
        {
            Width = Math.Max(0, instruction.Width),
            Height = Math.Max(0, instruction.Height),
        };
        if (instruction.Fill is { } fill)
            rect.Fill = PreviewBrush(fill);
        if (instruction.Stroke is { } stroke && instruction.StrokeThickness > 0)
        {
            rect.Stroke = PreviewBrush(stroke);
            rect.StrokeThickness = instruction.StrokeThickness;
        }

        Canvas.SetLeft(rect, instruction.Left);
        Canvas.SetTop(rect, instruction.Top);
        canvas.Children.Add(rect);
    }

    /// <summary>
    /// R96-render-cf-databar-iconset-preview-1: an icon-set glyph primitive drawn as a filled/outlined
    /// ellipse (e.g. the traffic-light dot, or the Quarter/Pie style's full-disc fallback).
    /// </summary>
    private static void AddPreviewEllipse(Canvas canvas, PrintPreviewPaintInstruction instruction)
    {
        var ellipse = new AvaloniaEllipse
        {
            Width = Math.Max(0, instruction.Width),
            Height = Math.Max(0, instruction.Height),
        };
        if (instruction.Fill is { } fill)
            ellipse.Fill = PreviewBrush(fill);
        if (instruction.Stroke is { } stroke && instruction.StrokeThickness > 0)
        {
            ellipse.Stroke = PreviewBrush(stroke);
            ellipse.StrokeThickness = instruction.StrokeThickness;
        }

        Canvas.SetLeft(ellipse, instruction.Left);
        Canvas.SetTop(ellipse, instruction.Top);
        canvas.Children.Add(ellipse);
    }

    /// <summary>
    /// R96-render-cf-databar-iconset-preview-1: an icon-set glyph primitive drawn as a closed,
    /// filled/outlined polygon (arrow/flag/rating-bar/star glyph shapes).
    /// </summary>
    private static void AddPreviewPolygon(Canvas canvas, PrintPreviewPaintInstruction instruction)
    {
        if (instruction.Points is not { Count: >= 2 } points)
            return;

        var polygon = new AvaloniaPolygon
        {
            Points = points.Select(p => new Point(p.X, p.Y)).ToList(),
        };
        if (instruction.Fill is { } fill)
            polygon.Fill = PreviewBrush(fill);
        if (instruction.Stroke is { } stroke && instruction.StrokeThickness > 0)
        {
            polygon.Stroke = PreviewBrush(stroke);
            polygon.StrokeThickness = instruction.StrokeThickness;
        }

        canvas.Children.Add(polygon);
    }

    private static void AddPreviewLine(Canvas canvas, PrintPreviewPaintInstruction instruction)
    {
        if (instruction.Stroke is not { } stroke)
            return;

        var line = new AvaloniaControlShapesLine
        {
            StartPoint = new Point(instruction.X1, instruction.Y1),
            EndPoint = new Point(instruction.X2, instruction.Y2),
            Stroke = PreviewBrush(stroke),
            StrokeThickness = instruction.StrokeThickness,
        };
        canvas.Children.Add(line);
    }

    private static void AddPreviewText(PrintPreviewTextCanvas canvas, int instructionIndex, PrintPreviewPaintInstruction instruction)
    {
        if (string.IsNullOrEmpty(instruction.Text))
            return;

        var font = instruction.Font;
        var text = new SelectableTextBlock
        {
            Text = instruction.Text,
            FontFamily = new FontFamily(string.IsNullOrWhiteSpace(font.FontFamily)
                ? AvaloniaTextMeasurer.DefaultFontFamily
                : font.FontFamily),
            FontSize = font.FontSize > 0 ? font.FontSize : 9,
            FontWeight = font.Bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = font.Italic ? FontStyle.Italic : FontStyle.Normal,
            Foreground = PreviewBrush(font.Color),
            Width = Math.Max(0, instruction.Width),
            TextAlignment = ToTextAlignment(instruction.Alignment),
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap,
        };

        Canvas.SetLeft(text, instruction.Left);
        Canvas.SetTop(text, instruction.Top);
        canvas.RegisterText(instructionIndex, text);
        canvas.Children.Add(text);
    }

    private static TextAlignment ToTextAlignment(PageTextAlignment alignment) =>
        alignment switch
        {
            PageTextAlignment.Center => TextAlignment.Center,
            PageTextAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };

    private static IBrush PreviewBrush(PresentationRgb color) =>
        new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
}
