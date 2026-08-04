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
        string? fixturePrinterName = null,
        IReadOnlyList<PrintPreviewParityPage>? parityPages = null)
    {
        await WaitForPendingDirtyWorkbookGateAsync();

        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        var sheet = _session.ActiveSheet;
        if (!PrintPreviewPaginationContext.TryCreate(_session.Workbook, sheet, PrintPreviewTextMeasurer, out var context, ResolveWorkbookDirectoryForHeaderFooter()))
        {
            ShowEditIssue(UiText.Get("ShellLoc_NothingToPreview"));
            return;
        }

        await ShowPrintPreviewWindowCoreAsync(
            AvaloniaPrintPreviewPaginationContext.FromSheetContext(context),
            fixturePrinterName,
            parityPages);
    }

    /// <summary>
    /// Seeds the active sheet with the "Parity Demo / Revenue by region" report used by the parity
    /// capture so the Linux preview renders the same column-aligned grid as the Windows ground truth:
    /// a bold title, a dimmed subtitle, a bold header row, and four data rows whose Revenue column is
    /// currency-formatted. Without this seed the active sheet still holds the leftover Text-to-Columns
    /// demo cells (raw "North,Widget,120" strings in column F), which previewed as comma-joined CSV
    /// text instead of a laid-out table. A tight A1:D8 print area constrains the preview to the report.
    /// </summary>
    private void SeedPrintPreviewParityReport()
    {
        var workbook = _session.Workbook;
        var sheet = _session.ActiveSheet;

        var titleStyle = workbook.RegisterStyle(new CellStyle { Bold = true, FontSize = 16 });
        var subtitleStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(112, 112, 112),
        });
        var headerStyle = workbook.RegisterStyle(new CellStyle { Bold = true });
        var currencyStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0" });

        // Clear any leftover cells (e.g. the Text-to-Columns CSV demo in column F) inside the report
        // print area so they cannot bleed into the previewed grid.
        for (uint row = 1; row <= 8; row++)
            for (uint col = 1; col <= 6; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(BlankValue.Instance));

        SetReportCell(sheet, 1, 1, new TextValue("Parity Demo"), titleStyle);
        SetReportCell(sheet, 2, 1, new TextValue("Revenue by region"), subtitleStyle);

        var headers = new[] { "Region", "Product", "Units", "Revenue" };
        for (var col = 0; col < headers.Length; col++)
            SetReportCell(sheet, 4, (uint)(col + 1), new TextValue(headers[col]), headerStyle);

        (string Region, string Product, double Units, double Revenue)[] rows =
        {
            ("North", "Widget", 120, 12480),
            ("South", "Gadget", 85, 8925),
            ("East", "Sprocket", 200, 21700),
            ("West", "Gizmo", 64, 6080),
        };

        for (var i = 0; i < rows.Length; i++)
        {
            var row = (uint)(5 + i);
            var data = rows[i];
            SetReportCell(sheet, row, 1, new TextValue(data.Region), StyleId.Default);
            SetReportCell(sheet, row, 2, new TextValue(data.Product), StyleId.Default);
            SetReportCell(sheet, row, 3, new NumberValue(data.Units), StyleId.Default);
            SetReportCell(sheet, row, 4, new NumberValue(data.Revenue), currencyStyle);
        }

        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 8, 4));
    }

    private static void SetReportCell(Sheet sheet, uint row, uint col, ScalarValue value, StyleId styleId)
    {
        var cell = Cell.FromValue(value);
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
    }

    private async Task ShowPrintPreviewWindowCoreAsync(
        AvaloniaPrintPreviewPaginationContext context,
        string? fixturePrinterName = null,
        IReadOnlyList<PrintPreviewParityPage>? parityPages = null)
    {
        var printerName = fixturePrinterName ?? PrintPreviewDefaultPrinterName;
        var pageCount = parityPages?.Count ?? context.PageCount;
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
            pageHost.Child = BuildPreviewDocumentViewerSurface(context, navigator.CurrentIndex, parityPages);
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
            if (parityPages is null &&
                AvaloniaPrintPreviewPaginationContext.TryCreate(
                    _session.Workbook,
                    _session.ActiveSheet,
                    PrintPreviewTextMeasurer,
                    currentSettings.PrintWhat == PrintWhat.Selection
                        ? _session.SelectedRange
                        : null,
                    out var updatedContext,
                    ResolveWorkbookDirectoryForHeaderFooter(),
                    currentSettings.PrintWhat == PrintWhat.Selection || currentSettings.IgnorePrintArea))
            {
                context = updatedContext;
                pageCount = updatedContext.PageCount;
                navigator = PrintPreviewPageNavigator.Create(pageCount).JumpTo(navigator.CurrentIndex);
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
            await ExportActiveSheetPdfAsync();
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
        // The parity fixture path (a static, pre-rendered set of pages captured for cross-platform
        // screenshot comparison) has no live sheet/session behind it, so its rail stays the
        // read-only snapshot it always was. A real preview is backed by the live active sheet and
        // its settings rail is fully interactive (R118-print-preview-settings-rail-wiring) --
        // previously canUpdatePrintPreviewSettings was hardcoded false even for real previews, which
        // left every control (Print What/Sides/Collation/Orientation/Paper Size/Margins/Scaling/
        // Ignore Print Area/Print Options) wired to nothing.
        var settingsRail = CreatePrintPreviewSettingsRail(
            PrintPreviewSurfacePlanner.CreateSettingsRailPlan(
                parityPages is null ? _session.ActiveSheet : null,
                parityPages is null ? pageCount : 1,
                printerName,
                currentSettings,
                 // Match WPF's PrintRenderer.RenderWorksheet(printRangeOverride: selectionRange,
                 // ignorePrintArea: true) when the user switches the live preview to Selection.
                 hasSelection: HasPrintSelection(_session.SelectedRange),
                canUpdatePrintPreviewSettings: parityPages is null,
                PrintPreviewSettingsTextResolver),
            parityPages is null
                ? new PrintPreviewSettingsRailInteraction(
                    _session.ActiveSheet.Id,
                    () => currentSettings,
                    updated => currentSettings = updated,
                    RepaginateAndRender)
                : null);
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

        // "Print Entire Workbook" stays disabled on the live rail too: the Avalonia preview context
        // only ever paginates the one active sheet passed to it (there is no multi-sheet workbook
        // pagination context yet), so selecting this choice could update the setting but could never
        // re-paginate the preview to show it -- the same looks-functional-does-nothing gap this fix
        // removes everywhere else (R118-print-preview-settings-rail-wiring; leftOpen follow-up).
        var printWhatOptions = interaction is null
            ? plan.Settings.PrintWhatOptions
            : DisableUnsupportedPrintWhatScopes(plan.Settings.PrintWhatOptions);
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

    private static IReadOnlyList<PrintPreviewChoice<PrintWhat>> DisableUnsupportedPrintWhatScopes(
        IReadOnlyList<PrintPreviewChoice<PrintWhat>> options) =>
        options
            .Select(option => option.Value == PrintWhat.EntireWorkbook ? option with { IsEnabled = false } : option)
            .ToArray();

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
        IReadOnlyList<PrintPreviewParityPage>? parityPages = null)
    {
        var surface = new Border
        {
            Background = PrintPreviewSurfaceBackground,
            Padding = new Thickness(PrintPreviewSurfacePlanner.PreviewPageLeftPadding, 5, 84, 8),
            Child = parityPages is null
                ? BuildPreviewPageView(context, pageIndex)
                : BuildPreviewParityPageView(parityPages[pageIndex]),
        };

        return new ScrollViewer
        {
            Background = PrintPreviewSurfaceBackground,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = surface,
        };
    }

    private static Control BuildPreviewParityPageView(PrintPreviewParityPage page)
    {
        var canvas = new Canvas
        {
            Width = PrintPreviewParityFixture.PageWidth,
            Height = PrintPreviewParityFixture.PageHeight,
            Background = Brushes.White,
            ClipToBounds = true,
        };
        AutomationProperties.SetAutomationId(canvas, PrintPreviewDialogPlanner.PageCanvasAutomationId);

        foreach (var run in page.TextRuns)
        {
            var text = new TextBlock
            {
                Text = run.Text,
                FontFamily = FormulaBarFontFamily,
                FontSize = run.FontSize,
                FontWeight = run.Bold ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = PreviewBrush(run.Color),
                TextWrapping = TextWrapping.NoWrap,
            };
            Canvas.SetLeft(text, run.Left);
            Canvas.SetTop(text, run.Top);
            canvas.Children.Add(text);
        }

        var paper = new Grid
        {
            ClipToBounds = true,
            Children =
            {
                canvas,
                new Border
                {
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    IsHitTestVisible = false,
                },
            },
        };

        return new Border
        {
            Width = PrintPreviewParityFixture.PageWidth,
            Height = PrintPreviewParityFixture.PageHeight,
            Background = Brushes.White,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 4,
                OffsetY = 4,
                Blur = 0,
                Color = Color.FromArgb(89, 0, 0, 0),
            }),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
            Child = paper,
        };
    }

    /// <summary>
    /// Builds the zoom-to-fit view for one preview page: a <see cref="Viewbox"/> wrapping a Canvas the
    /// size of the page rectangle, onto which the page's flattened paint primitives are rendered.
    /// </summary>
    internal static Control BuildPreviewPageView(PrintPreviewPaginationContext context, int pageIndex) =>
        BuildPreviewPageViewCore(context.BuildPage(pageIndex));

    internal static Control BuildPreviewPageView(AvaloniaPrintPreviewPaginationContext context, int pageIndex) =>
        BuildPreviewPageViewCore(context.BuildPage(pageIndex));

    private static Control BuildPreviewPageViewCore(PageContentLayout? layout)
    {
        if (layout is null)
        {
            return new TextBlock
            {
                Text = UiText.Get("ShellLoc_PageCouldNotRender"),
                Foreground = Brush(92, 92, 92),
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
        }

        var painting = PrintPreviewInstructionBuilder.Build(layout);
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
