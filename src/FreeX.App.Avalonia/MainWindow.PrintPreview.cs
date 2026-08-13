using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

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
/// pure <see cref="PrintPreviewPageNavigator"/>), and a <see cref="Viewbox"/> zooms each rendered page
/// to fit the available area. The flattening of a page layout into ordered paint primitives lives in
/// the unit-tested <see cref="PrintPreviewInstructionBuilder"/>; this file only turns each primitive
/// into an Avalonia control.
///
/// Deferred: the OS print dialog (no system print is invoked yet — this is a preview/export surface)
/// and richer drawing-object families. The shared page-content model supplies visible text boxes and
/// embedded chart object blocks, including selectable chart-text overlay primitives.
/// </summary>
public sealed partial class MainWindow
{
    private const string PrintPreviewDefaultPrinterName = "Windows print dialog";

    private static readonly ITextMeasurer PrintPreviewTextMeasurer = new AvaloniaTextMeasurer();
    // WPF DocumentViewer uses its light neutral control surface around the white paper.
    private static readonly IBrush PrintPreviewSurfaceBackground = Brush(240, 240, 240);
    private static readonly IBrush PrintPreviewChromeBackground = Brush(238, 245, 253);
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
        else if (!PrintPreviewPaginationContext.TryCreate(
                     _session.Workbook,
                     _session.ActiveSheet,
                     PrintPreviewTextMeasurer,
                     out var sheetContext,
                     ResolveWorkbookDirectoryForHeaderFooter()))
        {
            ShowEditIssue(UiText.Get("ShellLoc_NothingToPreview"));
            return;
        }
        else
        {
            context = AvaloniaPrintPreviewPaginationContext.FromSheetContext(sheetContext);
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
        var printerName = printerNameOverride ?? PrintPreviewDefaultPrinterName;
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

        var firstButton = CreatePreviewToolbarButton(documentToolbarPlan.NavigationButtons[0]);
        var prevButton = CreatePreviewToolbarButton(documentToolbarPlan.NavigationButtons[1]);
        var nextButton = CreatePreviewToolbarButton(documentToolbarPlan.NavigationButtons[2]);
        var lastButton = CreatePreviewToolbarButton(documentToolbarPlan.NavigationButtons[3]);

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
        exportButton.IsEnabled = StorageProvider.CanSave;

        void Render()
        {
            pageHost.Child = BuildPreviewDocumentViewerSurface(
                context,
                navigator.CurrentIndex,
                externalPageViewFactory);
            pageNumberBox.Text = (navigator.CurrentIndex + 1).ToString(CultureInfo.InvariantCulture);
            pageStatusText.Text = PrintPreviewNavigationState.Create(navigator.CurrentIndex + 1, pageCount).StatusText;
            firstButton.IsEnabled = navigator.CanGoPrevious;
            prevButton.IsEnabled = navigator.CanGoPrevious;
            nextButton.IsEnabled = navigator.CanGoNext;
            lastButton.IsEnabled = navigator.CanGoNext;
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
                var ignorePrintArea = currentSettings.PrintWhat is PrintWhat.Selection || currentSettings.IgnorePrintArea;
                AvaloniaPrintPreviewPaginationContext updatedContext;
                var created = currentSettings.PrintWhat == PrintWhat.EntireWorkbook
                    ? AvaloniaPrintPreviewPaginationContext.TryCreateWorkbook(
                        _session.Workbook,
                        PrintPreviewTextMeasurer,
                        out updatedContext,
                        ResolveWorkbookDirectoryForHeaderFooter(),
                        currentSettings.IgnorePrintArea)
                    : AvaloniaPrintPreviewPaginationContext.TryCreate(
                        _session.Workbook,
                        _session.ActiveSheet,
                        PrintPreviewTextMeasurer,
                        currentSettings.PrintWhat == PrintWhat.Selection
                            ? _session.SelectedRange
                            : null,
                        out updatedContext,
                        ResolveWorkbookDirectoryForHeaderFooter(),
                        ignorePrintArea);

                if (created)
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
        exportButton.Click += async (_, _) =>
        {
            dialog.Close();
            switch (currentSettings.PrintWhat)
            {
                case PrintWhat.EntireWorkbook:
                    await ExportWorkbookPdfAsync(
                        WorkbookExportPrintScope.VisibleWorkbook,
                        WorkbookExportPrintOutputKind.Pdf);
                    break;
                case PrintWhat.Selection:
                    await ExportWorkbookPdfAsync(
                        WorkbookExportPrintScope.SelectedRange,
                        WorkbookExportPrintOutputKind.Pdf);
                    break;
                default:
                    await ExportActiveSheetPdfAsync();
                    break;
            }
        };
        dialog.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape:
                    dialog.Close();
                    e.Handled = true;
                    break;
                case Key.Left or Key.PageUp:
                    navigator = navigator.Previous();
                    Render();
                    e.Handled = true;
                    break;
                case Key.Right or Key.PageDown:
                    navigator = navigator.Next();
                    Render();
                    e.Handled = true;
                    break;
            }
        };

        var documentToolbar = CreatePreviewDocumentToolbar(
            documentToolbarPlan,
            firstButton,
            prevButton,
            nextButton,
            lastButton,
            pageNumberBox,
            pageStatusText);
        var previewPane = CreatePrintPreviewPane(
            documentToolbar,
            pageHost,
            PrintPreviewSurfacePlanner.CreateFindBarPlan(PrintPreviewSettingsTextResolver));
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
                    RepaginateAndRender));
        var topToolbar = CreatePrintPreviewTopToolbar(
            topToolbarPlan,
            exportButton,
            () => dialog.Close());

        var layout = new Grid
        {
            // Match the WPF capture's client rectangle inside the same 1120x700 outer window. The
            // explicit top-left alignment is intentional: WPF leaves the outer right/bottom bands
            // unoccupied when its native frame is included in the evidence PNG.
            Width = PrintPreviewSurfacePlanner.ParityClientWidth,
            Height = PrintPreviewSurfacePlanner.ParityClientHeight,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
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
        };

        await dialog.ShowDialog(this);
    }

    private static Border CreatePrintPreviewTopToolbar(
        PrintPreviewTopToolbarPlan plan,
        Button printButton,
        Action close)
    {
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(1, 6, 0, 4),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Children =
            {
                printButton,
                new TextBlock { Text = plan.PrinterLabelText, FontSize = 12, FontFamily = FormulaBarFontFamily, VerticalAlignment = AvaloniaVerticalAlignment.Center },
                CreatePreviewComboBox(plan.PrinterComboWidth, plan.PrinterName),
                new TextBlock { Text = plan.CopiesLabelText, FontSize = 12, FontFamily = FormulaBarFontFamily, VerticalAlignment = AvaloniaVerticalAlignment.Center },
                new TextBox
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
                },
                new CheckBox
                {
                    Content = plan.CollatedText,
                    IsChecked = true,
                    MinHeight = 20,
                    MaxHeight = 20,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    VerticalAlignment = AvaloniaVerticalAlignment.Center,
                },
                new TextBlock { Text = plan.SidesLabelText, FontSize = 12, FontFamily = FormulaBarFontFamily, VerticalAlignment = AvaloniaVerticalAlignment.Center },
                CreatePreviewChoiceComboBox(
                    plan.SidesComboWidth,
                    plan.SidesOptions,
                    plan.SidesSelectedIndex),
                new TextBlock
                {
                    Text = plan.StatusText,
                    MaxWidth = 280,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = AvaloniaVerticalAlignment.Center,
                },
                CreatePreviewComboBox(plan.PageRangeComboWidth, plan.PageRangeText),
            },
        };

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
        Action Repaginate);

    private async Task ShowPrintPreviewPageSetupAsync(
        PageLayoutPageSetupOpenSource source,
        Action repaginate)
    {
        await ShowPageSetupDialogAsync(source);
        repaginate();
    }

    private ScrollViewer CreatePrintPreviewSettingsRail(
        PrintPreviewSettingsRailPlan plan,
        PrintPreviewSettingsRailInteraction? interaction)
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
                        _ = ShowPrintPreviewPageSetupAsync(
                            PageLayoutPageSetupOpenSource.CustomMargins,
                            interaction.Repaginate);
                    break;
                case PrintPreviewSettingsPanelActionKind.OpenPageSetup:
                    if (interaction is not null)
                        _ = ShowPrintPreviewPageSetupAsync(
                            PageLayoutPageSetupOpenSource.ScaleToFit,
                            interaction.Repaginate);
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
            interaction is null
                ? null
                : (fromText, toText) => ApplyAction(
                    PrintPreviewSettingsPanelPlanner.CreatePageRangeAction(interaction.GetSettings(), fromText, toText)));

        var sidesBox = CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.SidesOptions, plan.Settings.SidesSelectedIndex);
        if (interaction is not null)
        {
            sidesBox.SelectionChanged += (_, _) => ApplyAction(
                PrintPreviewSettingsPanelPlanner.CreateSidesAction(plan.Settings, interaction.GetSettings(), sidesBox.SelectedIndex));
        }

        var collationBox = CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.CollationOptions, plan.Settings.CollationSelectedIndex);
        if (interaction is not null)
        {
            collationBox.SelectionChanged += (_, _) => ApplyAction(
                PrintPreviewSettingsPanelPlanner.CreateCollationAction(plan.Settings, interaction.GetSettings(), collationBox.SelectedIndex));
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

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brush(245, 245, 245),
            Content = panel,
        };
    }

    private static Grid CreatePageRangeRow(
        PrintPreviewPageRangeFieldsPlan plan,
        Action<string, string>? onChanged = null)
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

    private static Border CreatePrintPreviewPane(
        Control documentToolbar,
        Border pageHost,
        PrintPreviewFindBarPlan plan)
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
        Grid.SetColumn(findBox, 0);
        Grid.SetColumn(previous, 1);
        Grid.SetColumn(next, 2);
        findBar.Children.Add(findBox);
        findBar.Children.Add(previous);
        findBar.Children.Add(next);

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

    private static Border CreatePreviewDocumentToolbar(
        PrintPreviewDocumentToolbarPlan plan,
        Button firstButton,
        Button prevButton,
        Button nextButton,
        Button lastButton,
        TextBox pageNumberBox,
        TextBlock pageStatusText)
    {
        var chrome = PrintPreviewSurfacePlanner.DocumentToolbarChrome;
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = chrome.ButtonSpacing,
            Margin = new Thickness(chrome.LeftPadding, 4, 0, 4),
            Children =
            {
                CreateDocumentToolbarIcon(RibbonCommandIconKind.Print, "Print preview", chrome),
                CreateDocumentToolbarIcon(RibbonCommandIconKind.Copy, "Copy", chrome),
                CreateDocumentToolbarSeparator(chrome),
                CreateDocumentToolbarIcon(RibbonCommandIconKind.Zoom, "Zoom in", chrome),
                CreateDocumentToolbarIcon(RibbonCommandIconKind.Zoom, "Zoom out", chrome),
                CreateDocumentToolbarSeparator(chrome),
                CreateDocumentToolbarIcon(RibbonCommandIconKind.Page, "Fit page", chrome),
                CreateDocumentToolbarIcon(RibbonCommandIconKind.View, "Fit width", chrome),
            },
        };

        // Keep the planner-backed controls in the visual tree for keyboard navigation and automation.
        // WPF's native DocumentViewer places these commands in its command surface rather than showing
        // the textual controls here; the compact icon row above is the faithful Avalonia equivalent.
        var functionalControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            IsVisible = false,
            Children =
            {
                firstButton,
                prevButton,
                nextButton,
                lastButton,
                new TextBlock { Text = plan.PageLabelText },
                pageNumberBox,
                pageStatusText,
                CreatePreviewZoomComboBox(plan),
                CreatePreviewToolbarButton(plan.MarginsButtonText),
                CreatePreviewToolbarButton(plan.PageSetupButtonText),
            },
        };

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,0"),
        };
        content.Children.Add(toolbar);
        Grid.SetColumn(functionalControls, 1);
        content.Children.Add(functionalControls);

        return new Border
        {
            Height = chrome.Height,
            MinHeight = chrome.Height,
            MaxHeight = chrome.Height,
            Background = Brush(245, 245, 245),
            BorderBrush = Brush(208, 208, 208),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = content,
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
        return button;
    }

    private static Button CreatePreviewToolbarButton(string text) =>
        ApplyPreviewToolbarButtonChrome(new Button { Content = text }, 26);

    private static Button CreatePreviewToolbarButton(PrintPreviewNavigationGlyphPlan plan)
    {
        var button = CreatePreviewToolbarButton(plan.Text);
        button.Content = CreatePreviewNavigationGlyph(plan.Command);
        AutomationProperties.SetAutomationId(button, plan.AutomationId);
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

    private static ComboBox CreatePreviewZoomComboBox(PrintPreviewDocumentToolbarPlan plan) =>
        CreatePreviewComboBox(
            plan.ZoomComboWidth,
            plan.ZoomOptions.Select(option => option.Text).ToArray(),
            plan.ZoomSelectedIndex);

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

    private static Control BuildPreviewDocumentViewerSurface(
        AvaloniaPrintPreviewPaginationContext context,
        int pageIndex,
        Func<int, Control>? externalPageViewFactory = null)
    {
        var surface = new Border
        {
            Background = PrintPreviewSurfaceBackground,
            Padding = new Thickness(PrintPreviewSurfacePlanner.PreviewPageLeftPadding, 5, 84, 8),
            Child = externalPageViewFactory?.Invoke(pageIndex)
                ?? BuildPreviewPageView(context, pageIndex),
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
    /// Builds the zoom-to-fit view for one preview page: a <see cref="Viewbox"/> wrapping a Canvas the
    /// size of the page rectangle, onto which the page's flattened paint primitives are rendered.
    /// </summary>
    internal static Control BuildPreviewPageView(PrintPreviewPaginationContext context, int pageIndex) =>
        BuildPreviewPageViewCore(context.BuildPage(pageIndex));

    internal static Control BuildPreviewPageView(AvaloniaPrintPreviewPaginationContext context, int pageIndex) =>
        BuildPreviewPageViewCore(context.BuildPainting(pageIndex));

    private static Control BuildPreviewPageViewCore(PageContentLayout? layout)
        => BuildPreviewPageViewCore(layout is null ? null : PrintPreviewInstructionBuilder.Build(layout));

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

        var canvas = new Canvas
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
        Canvas canvas,
        IReadOnlyList<PrintPreviewPaintInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            switch (instruction.Kind)
            {
                case PrintPreviewPaintKind.Rectangle:
                    AddPreviewRectangle(canvas, instruction);
                    break;
                case PrintPreviewPaintKind.Line:
                    AddPreviewLine(canvas, instruction);
                    break;
                case PrintPreviewPaintKind.Text:
                    AddPreviewText(canvas, instruction);
                    break;
                case PrintPreviewPaintKind.Ellipse:
                    AddPreviewEllipse(canvas, instruction);
                    break;
                case PrintPreviewPaintKind.Polygon:
                    AddPreviewPolygon(canvas, instruction);
                    break;
            }
        }
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

    private static void AddPreviewText(Canvas canvas, PrintPreviewPaintInstruction instruction)
    {
        if (string.IsNullOrEmpty(instruction.Text))
            return;

        var font = instruction.Font;
        var text = new TextBlock
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
