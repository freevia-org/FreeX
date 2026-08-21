using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.Accessibility;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.Consolidate;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.Interactions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.Protection;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.App.Presentation.SparklineUI;
using FreeX.App.Presentation.TextToColumns;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using SkiaSharp;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

internal sealed record ParityInteractionDialogRoute(
    string CatalogId,
    string SurfaceId,
    string AvaloniaProductionSurface,
    string MissingReason)
{
    public bool IsMissing => MissingReason.Length > 0;
}

/// <summary>
/// Per-surface visual capture for the headless <c>--parity-capture</c> mode. Each surface is rendered to a PNG
/// via Avalonia's in-process <see cref="RenderTargetBitmap"/> (no external screenshot tooling), mirroring how
/// the WPF shell captures the same canonical surface ids so a cross-platform comparison runner can diff them:
/// <list type="bullet">
///   <item><c>tab.&lt;Name&gt;</c> — the live shell window with that ribbon tab selected.</item>
///   <item><c>grid.demo</c> — the live shell window over the startup demo workbook (Home tab).</item>
///   <item><c>dialog.&lt;Name&gt;</c> — each canonical dialog window, opened, rendered, then closed.</item>
///   <item><c>backstage.&lt;Pane&gt;</c> — each File-backstage pane window.</item>
/// </list>
/// Surface ids use the shared ribbon definition's tab ids and the canonical dialog / backstage names so the
/// WPF capture matches one-for-one. A surface that cannot be captured headlessly is recorded with
/// <c>captured:false</c> + a reason rather than aborting the run.
/// </summary>
public sealed partial class MainWindow
{
    private async Task ShowHeaderFooterPictureFormatParityDialogAsync()
    {
        var picture = new WorksheetHeaderFooterPicture(
            [],
            "image/png",
            "QuarterlyHeader.png",
            Width: 160,
            Height: 80);
        await ShowHeaderFooterPictureFormatDialogAsync(picture);
    }

    private async Task ShowUnhideWindowParityDialogAsync()
    {
        var hidden = new MainWindow(
            App.StartupArguments,
            _session.CreateSiblingView(InitialViewportHeight, InitialViewportWidth),
            _optionsRuntimeSession);
        hidden.Show();
        if (!WindowRegistry.Hide(hidden))
        {
            hidden.Close();
            throw new InvalidOperationException("Unable to create the hidden workbook-window capture state.");
        }

        try
        {
            await ShowUnhideWindowDialogAsync();
        }
        finally
        {
            hidden.Close();
        }
    }

    private Task<SelectDataSourceResult?> ShowSelectDataDialogAsync(
        string initialRange,
        bool firstColumnIsCategories) =>
        ShowSelectDataSourceDialogAsync(initialRange, firstColumnIsCategories);

    // The capture canvas is sized to the shell's default window so ribbon/grid framing matches the WPF shell.
    private const int ParityCaptureWindowWidth = 1120;
    private const int ParityCaptureWindowHeight = 720;
    private const int ParityCaptureTitleBarHeight = 30;
    // Only bounds how long a dialog may take to appear; the wait returns as soon as it does, so a
    // healthy run never spends it. Eight seconds held when a capture project ran alone but not in
    // the full gate, where two dialogs missed the window and their contracts went unrecorded --
    // surfacing as a cohort count short by two rather than as anything resembling a timeout.
    private const int ParityCaptureDialogWaitMilliseconds = 30000;
    private const int ParityCaptureDialogPollMilliseconds = 50;
    private static readonly FontFamily ParityNarrowUiFontFamily =
        new("Segoe UI, Arial Narrow, Aptos Narrow, Liberation Sans Narrow, Nimbus Sans Narrow, DejaVu Sans Condensed, Arial, Liberation Sans, sans-serif");
    private static readonly IBrush ParityBackstageSidebarBrush = Brush(0x10, 0x25, 0x3A);
    private static readonly IBrush ParityBackstageSelectedBrush = Brush(0x24, 0x44, 0x5E);
    private static readonly IBrush ParityBackstageSeparatorBrush = Brush(0x24, 0x44, 0x5E);

    /// <summary>The ordered static ribbon-tab surface ids and the shared-definition tab id each maps to.</summary>
    private static readonly (string SurfaceId, string TabId)[] ParityStaticRibbonTabs =
        BuildStaticRibbonTabSurfaces();

    /// <summary>The contextual tab surfaces and the activation key that makes each tab visible.</summary>
    private static readonly (string SurfaceId, string TabId, string ActivationKey)[] ParityContextualRibbonTabs =
        BuildContextualRibbonTabSurfaces();

    private static readonly IReadOnlyList<FreeXBackstageCaptureSurfacePlan> ParityBackstageCaptures =
        FreeXBackstageCapturePlanner.Build(FreeXBackstageCaptureHost.Avalonia);

    /// <summary>
    /// Stable mapping from the authoritative WPF-named interaction catalog to portable production UI.
    /// Several Avalonia dialogs intentionally have different names or combine a WPF child dialog into a
    /// larger production surface; those aliases are explicit here instead of relying on fuzzy name matching.
    /// </summary>
    internal static IReadOnlyList<ParityInteractionDialogRoute> ParityInteractionDialogRoutes { get; } =
    [
        Opened("AboutDialog", "dialog.About", "ShowAboutDialogAsync"),
        Opened("AccessibilityCheckerDialog", "dialog.AccessibilityChecker", "ShowAccessibilityCheckerIssuesDialogAsync"),
        Opened("ActivateSheetDialog", "dialog.ActivateSheet", "ShowSwitchWindowsDialogAsync"),
        Opened("AddWatchDialog", "dialog.AddWatch", "ShowAddWatchDialogAsync"),
        Opened("AdvancedFilterDialog", "dialog.AdvancedFilter", "ShowAdvancedFilterInputDialogAsync"),
        Opened("AllowEditRangeDialog", "dialog.AllowEditRanges", "ShowAllowEditRangeDialogAsync"),
        Opened("AutoFilterDialog", "dialog.AutoFilter", "ShowAutoFilterParityWindowAsync"),
        Opened("BookmarkDialog", "dialog.Bookmark", "ShowHyperlinkSubPromptAsync (bookmark)"),
        Opened("CellShiftDialog", "dialog.CellShift", "ShowInsertCellsDialogAsync"),
        Opened("ChangeChartTypeDialog", "dialog.ChangeChartType", "ShowChangeChartTypeDialog"),
        Opened("ChartAreaLegendDialog", "dialog.FormatChartArea", "ShowFormatChartAreaDialog"),
        Opened("ChartAxisFormatDialog", "dialog.ChartAxisFormat", "ShowChartAxisFormatDialog"),
        Opened("ChartBarFormatDialog", "dialog.ChartBarFormat", "ShowChartBarFormatDialog"),
        Opened("ChartBubbleFormatDialog", "dialog.ChartBubbleFormat", "ShowChartBubbleFormatDialog"),
        Opened("ChartDataLabelsDialog", "dialog.ChartDataLabels", "ShowChartDataLabelsDialog"),
        Opened("ChartErrorBarsDialog", "dialog.ChartErrorBars", "ShowChartErrorBarsDialog"),
        Opened("ChartPieFormatDialog", "dialog.ChartPieFormat", "ShowChartPieFormatDialog"),
        Opened("ChartSeriesFormatDialog", "dialog.ChartSeriesFormat", "ShowChartSeriesFormatDialog"),
        Opened("ChartStockFormatDialog", "dialog.ChartStockFormat", "ShowChartStockFormatDialog"),
        Opened("ChartStyleDialog", "dialog.ChartStyle", "ShowChartStyleDialogAsync"),
        Opened("ChartTitlesDialog", "dialog.ChartTitles", "ShowChartTitlesDialog"),
        Opened("ChartTrendlineOptionsDialog", "dialog.ChartTrendlineOptions", "ShowChartTrendlineDialog"),
        Opened("ColorPickerDialog", "dialog.ColorPicker", "ShowMoreColorsDialogAsync"),
        Opened("ColorScaleRuleDialog", "dialog.ColorScaleRule", "ShowConditionalFormatNewRuleDialogAsync (ColorScale)"),
        Opened("ColumnWidthDialog", "dialog.ColumnWidth", "ShowColumnWidthDialogAsync"),
        Opened("CommentListWindow", "dialog.CommentList", "ShowCommentsListAsync"),
        Opened("ConditionalFormatDialog", "dialog.ConditionalFormatNewRule", "ShowConditionalFormatNewRuleDialogAsync"),
        Opened("ConditionalFormatThresholdDialog", "dialog.ConditionalFormatThreshold", "ShowConditionalFormatValuePromptAsync"),
        Opened("ConfirmPasswordDialog", "dialog.ProtectSheet", "ShowProtectSheetDialogAsync (integrated confirmation field)"),
        Opened("ConsolidateDialog", "dialog.Consolidate", "ShowConsolidateDialogAsync"),
        Opened("CreateNamesFromSelectionDialog", "dialog.CreateNamesFromSelection", "ShowCreateNamesFromSelectionDialogAsync"),
        Opened("CreateTableDialog", "dialog.CreateTable", "ShowCreateTableDialogAsync"),
        Opened("CustomViewNameDialog", "dialog.CustomViewName", "ShowAddCustomViewDialogAsync"),
        Opened("CustomViewsDialog", "dialog.CustomViews", "ShowCustomViewsManagerDialogAsync"),
        Opened("DataBarRuleDialog", "dialog.DataBarRule", "ShowConditionalFormatNewRuleDialogAsync (DataBar)"),
        Opened("DataTableDialog", "dialog.DataTable", "ShowDataTableInputDialogAsync"),
        Opened("DataValidationDialog", "dialog.DataValidation", "ShowDataValidationDialogAsync"),
        Opened("ErrorCheckingDialog", "dialog.ErrorChecking", "ShowErrorCheckingDialogAsync"),
        Opened("EvaluateFormulaDialog", "dialog.EvaluateFormula", "ShowEvaluateFormulaDialogAsync"),
        Opened("ExportOptionsDialog", "dialog.ExportOptions", "ShowExportOptionsDialogAsync"),
        Opened("FillSeriesStepDialog", "dialog.FillSeriesStep", "ShowFillSeriesDialogAsync"),
        Opened("FindReplaceDialog", "dialog.FindReplace", "ShowFindReplaceTabbedDialogAsync"),
        Opened("ForecastSheetDialog", "dialog.ForecastSheet", "ShowForecastSheetInputDialogAsync"),
        Opened("FormatCellsDialog", "dialog.FormatCells", "ShowFormatCellsDialogAsync"),
        Opened("FormatPictureDialog", "dialog.FormatPicture", "OpenFormatPictureDialogAsync"),
        Opened("FunctionArgumentsDialog", "dialog.FunctionArguments", "ShowFunctionArgumentsDialogAsync"),
        Opened("GoalSeekDialog", "dialog.GoalSeek", "ShowGoalSeekInputDialogAsync"),
        Opened("GoalSeekStatusDialog", "dialog.GoalSeekStatus", "ShowGoalSeekStatusDialogAsync"),
        Opened("GoToDialog", "dialog.GoTo", "ShowGoToDialogAsync"),
        Opened("GoToSpecialDialog", "dialog.GoToSpecial", "ShowGoToSpecialDialogAsync"),
        Opened("HeaderFooterDialog", "dialog.HeaderFooterDialog", "ShowHeaderFooterDialogAsync (dedicated editor)"),
        Opened("HeaderFooterPictureFormatDialog", "dialog.HeaderFooterPictureFormat", "ShowHeaderFooterPictureFormatDialogAsync"),
        Opened("HighlightCellsRuleDialog", "dialog.HighlightCellsRule", "ShowConditionalFormatNewRuleDialogAsync (CellValue)"),
        Opened("HyperlinkDialog", "dialog.InsertHyperlink", "ShowInsertHyperlinkInputDialogAsync"),
        Opened("IconSetRuleDialog", "dialog.IconSetRule", "ShowConditionalFormatNewRuleDialogAsync (IconSet)"),
        Opened("InsertChartDialog", "dialog.InsertChart", "ShowChartTypePickerAsync"),
        Opened("InsertFunctionDialog", "dialog.InsertFunction", "ShowInsertFunctionPickerDialogAsync"),
        Opened("InsertSlicerDialog", "dialog.InsertSlicer", "ShowPivotControlPickerParityDialogAsync"),
        Opened("InsertTimelineDialog", "dialog.InsertTimeline", "ShowPivotControlPickerParityDialogAsync"),
        Opened("LegalNoticesDialog", "dialog.LegalNotices", "ShowLegalNoticesDialogAsync"),
        Opened("ManageConditionalFormatsDialog", "dialog.ConditionalFormatManage", "ShowManageConditionalFormatsDialogAsync"),
        Opened("MergeCellsContentWarningDialog", "dialog.MergeCellsContentWarning", "ShowMergeCellsContentWarningDialogAsync"),
        Opened("MoveChartDialog", "dialog.MoveChart", "ShowMoveChartDialog"),
        Opened("MoveOrCopySheetDialog", "dialog.MoveOrCopySheet", "ShowMoveOrCopySheetDialogAsync"),
        Opened("MovePivotTableDialog", "dialog.MovePivotTable", "OpenPivotMoveDialogAsync"),
        Opened("NameDefinitionDialog", "dialog.NameDefinition", "ShowDefineNameDialogAsync"),
        Opened("NamedRangeDialog", "dialog.NamedRange", "ShowNameManagerDialogAsync"),
        Opened("NewConditionalFormatRuleDialog", "dialog.ConditionalFormatNewRule", "ShowConditionalFormatNewRuleDialogAsync"),
        Opened("ObjectSizeDialog", "dialog.ObjectSize", "ShowSizeDialogAsync"),
        Opened("OptionsDialog", "dialog.Options", "ShowOptionsDialogAsync"),
        Opened("OutlineGroupDialog", "dialog.OutlineGroup", "ShowOutlineSettingsDialogAsync"),
        Opened("PageBreakDialog", "dialog.PageBreak", "ShowPageBreaksMenuAsync"),
        Opened("PageSetupDialog", "dialog.PageSetup", "ShowPageSetupDialogAsync"),
        Opened("PasswordProtectionDialog", "dialog.ProtectSheet", "ShowProtectSheetDialogAsync"),
        Opened("PasteNamesDialog", "dialog.PasteNames", "ShowPasteNamesDialogAsync"),
        Opened("PasteSpecialDialog", "dialog.PasteSpecial", "ShowPasteSpecialDialogAsync"),
        Opened("PictureCropDialog", "dialog.PictureCrop", "OpenPictureCropDialogAsync"),
        Opened("PivotCalculatedFieldDialog", "dialog.PivotCalculatedField", "OpenPivotCalculatedFieldDialogAsync"),
        Opened("PivotCalculatedItemDialog", "dialog.PivotCalculatedItem", "OpenPivotCalculatedItemDialogAsync"),
        Opened("PivotChartOptionsDialog", "dialog.PivotChartOptions", "OpenPivotChartOptionsAsync"),
        Opened("PivotChartTypeDialog", "dialog.PivotChartType", "ChangeActivePivotChartTypeAsync"),
        Opened("PivotFieldFilterDialog", "dialog.PivotFieldFilter", "OpenPivotItemFilterDialogAsync"),
        Opened("PivotFieldGroupingDialog", "dialog.PivotFieldGrouping", "OpenPivotGroupFieldDialogAsync"),
        Opened("PivotLabelFilterDialog", "dialog.PivotLabelFilter", "OpenPivotLabelFilterDialogAsync"),
        Opened("PivotSortOptionsDialog", "dialog.PivotSortOptions", "OpenPivotSortOptionsDialogAsync"),
        Opened("PivotStyleGalleryDialog", "dialog.PivotStyleGallery", "OpenPivotStyleGalleryDialogAsync"),
        Opened("PivotTableDataSourceDialog", "dialog.PivotTableDataSource", "OpenPivotDataSourceDialogAsync"),
        Opened("PivotTableDialog", "dialog.PivotTable", "ShowInsertPivotTableDialogAsync"),
        Opened("PivotTableNameDialog", "dialog.PivotTableName", "OpenPivotNameDialogAsync"),
        Opened("PivotTableOptionsDialog", "dialog.PivotTableOptions", "OpenPivotTableOptionsDialogAsync"),
        Opened("PivotValueFieldSettingsDialog", "dialog.PivotValueFieldSettings", "OpenPivotValueFieldSettingsDialogAsync"),
        Opened("PivotValueFilterDialog", "dialog.PivotValueFilter", "OpenPivotValueFilterDialogAsync"),
        Opened("PrintPreviewDialog", "dialog.PrintPreview", "ShowPrintPreviewDialogAsync"),
        Opened("RecommendedPivotTablesDialog", "dialog.RecommendedPivotTables", "ShowRecommendedPivotTablesDialogAsync"),
        Opened("RemoveDuplicatesDialog", "dialog.RemoveDuplicates", "ShowRemoveDuplicatesInputDialogAsync"),
        Opened("RotationDialog", "dialog.Rotation", "RotateSelectedDrawingObjectAsync"),
        Opened("RowHeightDialog", "dialog.RowHeight", "ShowRowHeightDialogAsync"),
        Opened("ScenarioManagerDialog", "dialog.ScenarioManager", "ShowScenarioManagerCompactDialogAsync"),
        Opened("ScreenTipDialog", "dialog.ScreenTip", "ShowHyperlinkSubPromptAsync (ScreenTip)"),
        Opened("SelectDataSourceDialog", "dialog.SelectDataSource", "ShowSelectDataDialogAsync"),
        Opened("SelectionPaneDialog", "dialog.SelectionPane", "OpenSelectionPaneDialogAsync"),
        Opened("ShapeEffectsDialog", "dialog.ShapeEffects", "OpenShapeEffectsDialogAsync"),
        Opened("ShapeGradientDialog", "dialog.ShapeGradient", "OpenShapeGradientDialogAsync"),
        Opened("SheetNameDialog", "dialog.RenameSheet", "ShowRenameSheetDialogAsync"),
        Opened("SortDialog", "dialog.Sort", "ShowSortInputDialogAsync"),
        Opened("SortOptionsDialog", "dialog.SortOptions", "ShowSortOptionsDialogAsync"),
        Opened("SparklineDialog", "dialog.Sparkline", "ShowInsertSparklineDialogAsync"),
        Opened("SpellCheckDialog", "dialog.SpellCheck", "ShowSpellingDialogAsync"),
        Opened("SubtotalDialog", "dialog.Subtotal", "ShowSubtotalInputDialogAsync"),
        Opened("SymbolPickerDialog", "dialog.SymbolPicker", "ShowSymbolPickerAsync"),
        Opened("TextEntryDialog", "dialog.TextEntry", "ShowSingleInputDialogAsync"),
        Opened("TextToColumnsDialog", "dialog.TextToColumns", "ShowTextToColumnsDialogAsync"),
        Opened("ThreadedCommentDialog", "dialog.ThreadedComment", "ShowThreadedCommentEditorAsync"),
        Opened("TopBottomRuleDialog", "dialog.TopBottomRule", "ShowConditionalFormatNewRuleDialogAsync (Top10)"),
        Opened("UnhideSheetDialog", "dialog.UnhideSheet", "ShowUnhideSheetDialogAsync"),
        Opened("UnhideWindowDialog", "dialog.UnhideWindow", "ShowUnhideWindowDialogAsync"),
        Opened("WatchWindowDialog", "dialog.WatchWindow", "ShowWatchWindowDialogAsync"),
        Opened("WorkbookStatisticsDialog", "dialog.WorkbookStatistics", "ShowWorkbookStatisticsDialogAsync"),
        Opened("WorkbookThemeDialog", "dialog.WorkbookTheme", "ShowThemesGalleryAsync"),
        Opened("ZoomDialog", "dialog.Zoom", "ShowZoomDialogAsync"),
    ];

    internal static IReadOnlyList<ParityInteractionDialogRoute> SupplementalInteractionDialogRoutes { get; } =
    [
        new("dialog.OpenWorkbookNativeDialog", "dialog.OpenWorkbook", "ShowWorkbookFileDialogParitySurfaceAsync (Open)", ""),
        new("dialog.SaveAsWorkbookNativeDialog", "dialog.SaveAsWorkbook", "ShowWorkbookFileDialogParitySurfaceAsync (Save As)", ""),
        new("dialog.ProtectWorkbookDialog", "dialog.ProtectWorkbook", "ShowProtectWorkbookDialogAsync", ""),
        new("dialog.TableResizeDialog", "dialog.TableResize", "OpenTableResizeDialogAsync", ""),
    ];

    internal static IReadOnlyList<ParityInteractionDialogRoute> InteractiveValidationDialogRoutes { get; } =
        ParityInteractionDialogRoutes.Concat(SupplementalInteractionDialogRoutes).ToArray();

    internal static int InteractiveValidationDialogRouteCount => InteractiveValidationDialogRoutes.Count;

    private static new ParityInteractionDialogRoute Opened(
        string catalogName,
        string surfaceId,
        string avaloniaProductionSurface) =>
        new("dialog." + catalogName, surfaceId, avaloniaProductionSurface, "");

    private static ParityInteractionDialogRoute Missing(string catalogName, string reason) =>
        new("dialog." + catalogName, "dialog." + TrimDialogSuffix(catalogName), "", reason);

    private static string TrimDialogSuffix(string name) =>
        name.EndsWith("Dialog", StringComparison.Ordinal)
            ? name[..^"Dialog".Length]
            : name.EndsWith("Window", StringComparison.Ordinal)
                ? name[..^"Window".Length]
                : name;

    /// <summary>
    /// Renders every app surface to <c>&lt;outputDirectory&gt;/&lt;surfaceId&gt;.png</c> and returns the per-surface
    /// outcome list that drives the manifest. Runs on the UI thread (the coordinator awaits it from the
    /// <see cref="Window.Opened"/> handler). Each surface is wrapped so one failure does not stop the others.
    /// </summary>
    internal async Task<IReadOnlyList<ParitySurfaceResult>> CaptureParitySurfacesAsync(
        string outputDirectory,
        int? maxDialogSurfaces = null,
        string? targetSurfaceId = null,
        bool interactionOnly = false,
        IReadOnlySet<string>? interactionDialogCatalogIds = null)
    {
        var results = new List<ParitySurfaceResult>();
        ResetDialogInteractionContracts();
        var captureAll = string.IsNullOrWhiteSpace(targetSurfaceId);
        var requestedSurfaceId = targetSurfaceId ?? "";
        var requestedCatalogRoute = captureAll
            ? null
            : InteractiveValidationDialogRoutes.FirstOrDefault(route =>
                string.Equals(route.CatalogId, requestedSurfaceId, StringComparison.Ordinal));
        if (requestedCatalogRoute is not null)
            requestedSurfaceId = requestedCatalogRoute.SurfaceId;
        var interactionSurfaceIds = interactionDialogCatalogIds is null
            ? null
            : InteractiveValidationDialogRoutes
                .Where(route => interactionDialogCatalogIds.Contains(route.CatalogId) && !route.IsMissing)
                .Select(route => route.SurfaceId)
                .ToHashSet(StringComparer.Ordinal);

        // ── Ribbon tabs + grid: render the live shell window with each tab selected. ──
        if (!interactionOnly && (captureAll || requestedSurfaceId.StartsWith("tab.", StringComparison.Ordinal) ||
            requestedSurfaceId.StartsWith("contextual.", StringComparison.Ordinal) ||
            requestedSurfaceId.StartsWith("grid.", StringComparison.Ordinal)))
        {
            var ribbonTabControl = FindParityRibbonTabControl();
            foreach (var (surfaceId, tabId) in ParityStaticRibbonTabs)
            {
                if (captureAll || string.Equals(requestedSurfaceId, surfaceId, StringComparison.Ordinal))
                    results.Add(CaptureRibbonTab(outputDirectory, ribbonTabControl, surfaceId, tabId, ParitySurfaceKind.StaticRibbonTab));
            }

            foreach (var (surfaceId, tabId, activationKey) in ParityContextualRibbonTabs)
            {
                if (!captureAll && !string.Equals(requestedSurfaceId, surfaceId, StringComparison.Ordinal))
                    continue;

                _ribbonContextSource.SetParityCaptureContext(null);
                LayoutWindow();
                _ribbonContextSource.SetParityCaptureContext(activationKey);
                LayoutWindow();
                ribbonTabControl = FindParityRibbonTabControl();
                results.Add(CaptureRibbonTab(outputDirectory, ribbonTabControl, surfaceId, tabId, ParitySurfaceKind.ContextualRibbonTab));
            }
            _ribbonContextSource.SetParityCaptureContext(null);
            LayoutWindow();

            // grid.demo: the worksheet over the startup demo workbook, framed by the Home tab.
            if (captureAll || string.Equals(requestedSurfaceId, "grid.demo", StringComparison.Ordinal))
            {
                SelectParityRibbonTab(ribbonTabControl, "HomeTab");
                results.Add(CaptureWindowSurface(outputDirectory, "grid.demo", ParitySurfaceKind.Screen));
            }

            if (captureAll || string.Equals(requestedSurfaceId, "grid.sheetTabsOverflow", StringComparison.Ordinal))
            {
                PrepareSheetTabsOverflowParityCapture();
                SelectParityRibbonTab(ribbonTabControl, "HomeTab");
                results.Add(CaptureWindowSurface(outputDirectory, "grid.sheetTabsOverflow", ParitySurfaceKind.Screen));
            }
        }

        // ── Dialogs: open each, render the dialog window, close it. ──
        if (!interactionOnly && (captureAll || string.Equals(requestedSurfaceId, "popup.nameBoxDropdown", StringComparison.Ordinal)))
            results.Add(CaptureNameBoxDropdownSurface(outputDirectory));

        var dialogOpeners = ParityDialogOpeners();
        if (interactionOnly)
            dialogOpeners = dialogOpeners.Concat(ParityInteractionOnlyDialogOpeners()).ToArray();
        if (interactionSurfaceIds is not null)
            dialogOpeners = dialogOpeners
                .Where(opener => interactionSurfaceIds.Contains(opener.SurfaceId))
                .ToArray();
        if (!captureAll)
            dialogOpeners = dialogOpeners
                .Where(opener => string.Equals(opener.SurfaceId, requestedSurfaceId, StringComparison.Ordinal))
                .ToArray();
        if (maxDialogSurfaces is { } limit)
            dialogOpeners = dialogOpeners.Take(Math.Max(0, limit)).ToArray();

        foreach (var (surfaceId, opener) in dialogOpeners)
        {
            results.Add(await CaptureModalSurfaceAsync(
                outputDirectory,
                surfaceId,
                ParitySurfaceKind.Dialog,
                opener,
                render: !interactionOnly));
            await ReleaseCompletedDialogCaptureResourcesAsync();
        }

        // ── Multi-tab / multi-category dialogs: open once, render the default surface plus one
        //    PNG per tab/category (`<surfaceId>.<TabName>`) so the comparison runner pairs each
        //    tab against the matching WPF tab. ──
        var tabDialogs = ParityTabDialogOpeners();
        if (interactionSurfaceIds is not null)
            tabDialogs = tabDialogs
                .Where(dialog => interactionSurfaceIds.Contains(dialog.SurfaceId))
                .ToArray();
        if (maxDialogSurfaces is not null)
            tabDialogs = [];
        if (!captureAll)
            tabDialogs = tabDialogs
                .Where(dialog => string.Equals(dialog.SurfaceId, requestedSurfaceId, StringComparison.Ordinal) ||
                    requestedSurfaceId.StartsWith(dialog.SurfaceId + ".", StringComparison.Ordinal))
                .ToArray();

        foreach (var (surfaceId, opener, tabNames) in tabDialogs)
        {
            results.AddRange(await CaptureModalTabsAsync(
                outputDirectory,
                surfaceId,
                ParitySurfaceKind.Dialog,
                opener,
                tabNames,
                render: !interactionOnly));
            await ReleaseCompletedDialogCaptureResourcesAsync();
        }

        // The original 57 logical routes above intentionally retain their stable ids and tab expansion.
        // Add only catalog routes that are not already represented by one of those production surfaces.
        // This preserves every legacy capture while extending the interaction inventory to all 120 rows.
        if (maxDialogSurfaces is null)
        {
            var existingSingleSurfaceIds = ParityDialogOpeners()
                .Select(opener => opener.SurfaceId)
                .ToHashSet(StringComparer.Ordinal);
            var existingTabSurfaceIds = ParityTabDialogOpeners()
                .Select(dialog => dialog.SurfaceId)
                .ToArray();
            var supplementalRoutes = ParityInteractionDialogRoutes
                .Where(route => !route.IsMissing)
                .Where(route => interactionDialogCatalogIds is null || interactionDialogCatalogIds.Contains(route.CatalogId))
                .Where(route => !existingSingleSurfaceIds.Contains(route.SurfaceId))
                .Where(route => !existingTabSurfaceIds.Any(tabSurfaceId =>
                    string.Equals(route.SurfaceId, tabSurfaceId, StringComparison.Ordinal) ||
                    route.SurfaceId.StartsWith(tabSurfaceId + ".", StringComparison.Ordinal)))
                .GroupBy(route => route.SurfaceId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();

            if (!captureAll)
                supplementalRoutes = supplementalRoutes
                    .Where(route => string.Equals(route.SurfaceId, requestedSurfaceId, StringComparison.Ordinal))
                    .ToArray();

            foreach (var route in supplementalRoutes)
            {
                var opener = ResolveSupplementalParityDialogOpener(route.CatalogId);
                results.Add(await CaptureModalSurfaceAsync(
                    outputDirectory,
                    route.SurfaceId,
                    ParitySurfaceKind.Dialog,
                    opener,
                    render: !interactionOnly));
                await ReleaseCompletedDialogCaptureResourcesAsync();
            }

            var missingRoutes = ParityInteractionDialogRoutes.Where(route => route.IsMissing);
            if (interactionDialogCatalogIds is not null)
                missingRoutes = missingRoutes.Where(route => interactionDialogCatalogIds.Contains(route.CatalogId));
            if (!captureAll)
                missingRoutes = missingRoutes.Where(route =>
                    ReferenceEquals(route, requestedCatalogRoute) ||
                    string.Equals(route.SurfaceId, requestedSurfaceId, StringComparison.Ordinal));

            foreach (var route in missingRoutes)
            {
                results.Add(new ParitySurfaceResult(
                    route.SurfaceId,
                    ParitySurfaceKind.Dialog,
                    route.SurfaceId + ".png",
                    Captured: false,
                    "Missing Avalonia production dialog: " + route.MissingReason));
            }
        }

        foreach (var capture in interactionOnly ? [] : ParityBackstageCaptures)
        {
            if (captureAll || string.Equals(requestedSurfaceId, capture.SurfaceId, StringComparison.Ordinal))
                results.Add(CaptureBackstageSurface(outputDirectory, capture));
        }

        return results;
    }

    private static Task ReleaseCompletedDialogCaptureResourcesAsync()
    {
        // Closed Avalonia windows can retain sizeable visual/native graphs until a full collection.
        // Exhaustive capture creates 120 of them in one process, so reclaim each completed unit before
        // opening the next rather than allowing the Docker memory ceiling to become the collector trigger.
        // Finalizers can require the Avalonia dispatcher. Run the blocking drain off-thread so the
        // dispatcher remains available while finalized render resources release their UI references.
        return Task.Run(() =>
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        });
    }

    /// <summary>The canonical dialog surfaces and the shell method that opens each. Ordered for stable output.</summary>
    private IReadOnlyList<(string SurfaceId, Func<Task> Opener)> ParityDialogOpeners() =>
    [
        ("dialog.GoTo", () => ShowGoToDialogAsync()),
        ("dialog.GoToSpecial", () => ShowGoToSpecialDialogAsync()),
        ("dialog.CreateTable", () => ShowCreateTableParityDialogAsync()),
        ("dialog.RecommendedPivotTables", async () => { await ShowRecommendedPivotTablesDialogAsync(); }),
        ("dialog.Sort", () => ShowSortDialogAsync()),
        ("dialog.SortOptions", async () =>
        {
            await ShowSortOptionsDialogAsync(new SortDialogOptions(
                CaseSensitive: true,
                LeftToRight: true,
                FirstKeySortOrder: "Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec"));
        }),
        ("dialog.AutoFilter", () => ShowAutoFilterParityDialogAsync()),
        ("dialog.TextToColumns", () => ShowTextToColumnsParityDialogAsync()),
        ("dialog.AdvancedFilter", () => ShowAdvancedFilterParityDialogAsync()),
        ("dialog.Consolidate", () =>
        {
            PrepareConsolidateParityCaptureState();
            return ShowConsolidateDialogAsync(ConsolidateParityFixture.CreateDialogInitialState());
        }),
        ("dialog.RemoveDuplicates", () => ShowRemoveDuplicatesParityDialogAsync()),
        ("dialog.GoalSeek", () => ShowGoalSeekParityDialogAsync()),
        ("dialog.GoalSeekStatus", () => ShowGoalSeekStatusParityDialogAsync()),
        ("dialog.DataTable", () => ShowDataTableParityDialogAsync()),
        ("dialog.ScenarioManager", () => ShowScenarioManagerParityDialogAsync()),
        ("dialog.ForecastSheet", () => ShowForecastSheetParityDialogAsync()),
        ("dialog.Subtotal", () => ShowSubtotalParityDialogAsync()),
        ("dialog.Sparkline", () => ShowSparklineParityDialogAsync()),
        ("dialog.InsertHyperlink", () => ShowInsertHyperlinkParityDialogAsync()),
        ("dialog.SymbolPicker", () => ShowSymbolPickerAsync()),
        ("dialog.EvaluateFormula", () => ShowEvaluateFormulaParityDialogAsync()),
        ("dialog.ErrorChecking", () => ShowErrorCheckingParityDialogAsync()),
        ("dialog.WatchWindow", () => ShowWatchWindowParityDialogAsync()),
        ("dialog.AddWatch", () => ShowAddWatchParityDialogAsync()),
        ("dialog.WorkbookStatistics", () => ShowWorkbookStatisticsDialogAsync()),
        ("dialog.RenameSheet", () => ShowRenameSheetParityDialogAsync()),
        ("dialog.UnhideSheet", () => ShowUnhideSheetParityDialogAsync()),
        ("dialog.About", () => ShowAboutDialogAsync()),
        ("dialog.LegalNotices", () => ShowLegalNoticesDialogAsync()),
        ("dialog.SelectDataSource", () => ShowSelectDataSourceParityDialogAsync()),
        ("dialog.ChangeChartType", () => ShowChangeChartTypeParityDialogAsync()),
        ("dialog.FormatChartArea", () => ShowFormatChartAreaParityDialogAsync()),
        ("dialog.ShapeEffects", () => ShowShapeEffectsParityDialogAsync()),
        ("dialog.ShapeGradient", () => ShowShapeGradientParityDialogAsync()),
        ("dialog.Zoom", () => ShowZoomDialogAsync()),
        ("dialog.CustomViews", () => ShowCustomViewsParityDialogAsync()),
        ("dialog.PrintPreview", () => ShowPrintPreviewParityDialogAsync()),
        ("dialog.OpenWorkbook", () => ShowWorkbookFileDialogParitySurfaceAsync(CreateOpenWorkbookDialogSurfacePlan())),
        ("dialog.SaveAsWorkbook", () => ShowWorkbookFileDialogParitySurfaceAsync(CreateSaveAsWorkbookDialogSurfacePlan())),
        ("dialog.ExportOptions", () => ShowExportOptionsParityDialogAsync()),
        ("dialog.SelectionPane", () => ShowSelectionPaneParityDialogAsync()),
        ("dialog.InsertSlicer", () => ShowInsertSlicerParityDialogAsync()),
        ("dialog.InsertTimeline", () => ShowInsertTimelineParityDialogAsync()),
        ("dialog.AllowEditRanges", () => ShowAllowEditRangesParityDialogAsync()),
        ("dialog.ProtectSheet", () => ShowProtectSheetDialogAsync()),
        ("dialog.ProtectWorkbook", () => ShowProtectWorkbookParityDialogAsync()),
        ("dialog.TableResize", () => ShowTableResizeParityDialogAsync()),
        ("dialog.AccessibilityChecker", () => ShowAccessibilityCheckerParityDialogAsync()),
        ("dialog.DataValidation", () => ShowDataValidationDialogAsync()),
        ("dialog.ConditionalFormatNewRule", () => ShowConditionalFormatNewRuleDialogAsync()),
        ("dialog.ConditionalFormatManage", () => ShowManageConditionalFormatsParityDialogAsync()),
        ("dialog.HeaderFooterDialog", ShowHeaderFooterDialogAsync),
    ];

    private IReadOnlyList<(string SurfaceId, Func<Task> Opener)> ParityInteractionOnlyDialogOpeners() => [];

    /// <summary>
    /// The multi-tab / multi-category dialog surfaces: each opens once and is rendered per tab
    /// (<c>dialog.&lt;Name&gt;.&lt;TabName&gt;</c>) plus its default <c>dialog.&lt;Name&gt;</c> surface.
    /// The tab-name lists are stable, English, position-ordered identifiers that match the WPF
    /// capture's per-tab surface ids one-for-one (the renderer drives <c>SelectedIndex = i</c>, not
    /// the localized header text, so the names here only need to agree across the two shells).
    /// </summary>
    private IReadOnlyList<(string SurfaceId, Func<Task> Opener, string[] TabNames)> ParityTabDialogOpeners() =>
    [
        ("dialog.FormatCells", () => ShowFormatCellsDialogAsync(),
            ["Number", "Alignment", "Font", "Border", "Fill", "Protection"]),
        // Page Setup: both shells have the same 4 tabs in the same order (Page/Margins/Header-Footer/Sheet).
        ("dialog.PageSetup", () => ShowPageSetupDialogAsync(),
            ["Page", "Margins", "HeaderFooter", "Sheet"]),
        ("dialog.FindReplace", () => ShowFindDialogAsync(),
            ["Find", "Replace"]),
        ("dialog.PivotTableOptions", () => ShowPivotTableOptionsParityDialogAsync(),
            ["LayoutAndFormat", "TotalsAndFilters", "Display", "Printing", "Data", "AltText"]),
        ("dialog.PivotFieldFilter", () => ShowPivotFieldFilterParityDialogAsync(),
            ["SelectItems", "LabelFilters", "ValueFilters"]),
        ("dialog.PivotValueFieldSettings", () => ShowPivotValueFieldSettingsParityDialogAsync(),
            ["SummarizeValuesBy", "ShowValuesAs", "NumberFormat"]),
        ("dialog.Options", () => ShowOptionsDialogAsync(),
            [
                "General", "Formulas", "Proofing", "Save", "Language", "EaseOfAccess",
                "Advanced", "CustomizeRibbon", "QuickAccessToolbar", "AddIns", "TrustCenter", "View",
            ]),
    ];

    internal int ParityLegacyDialogImageCount =>
        ParityDialogOpeners().Count +
        ParityTabDialogOpeners().Sum(dialog => 1 + dialog.TabNames.Length);

    private Func<Task> ResolveSupplementalParityDialogOpener(string catalogId) =>
        catalogId switch
        {
            "dialog.ActivateSheetDialog" => ShowActivateSheetParityDialogAsync,
            "dialog.BookmarkDialog" => async () =>
            {
                await ShowHyperlinkSubPromptAsync(this, "Bookmark", "Cell reference", "A1");
            },
            "dialog.CellShiftDialog" => ShowInsertCellsDialogAsync,
            "dialog.ChartAxisFormatDialog" => () => ShowWithSelectedParityChartAsync(ShowChartXAxisFormatDialog),
            "dialog.ChartBarFormatDialog" => () => ShowWithParityChartTypeAsync(ChartType.Bar, ShowChartBarFormatDialog),
            "dialog.ChartBubbleFormatDialog" => () => ShowWithParityChartTypeAsync(ChartType.Bubble, ShowChartBubbleFormatDialog),
            "dialog.ChartDataLabelsDialog" => () => ShowWithSelectedParityChartAsync(ShowChartDataLabelsDialog),
            "dialog.ChartErrorBarsDialog" => () => ShowWithSelectedParityChartAsync(ShowChartErrorBarsDialog),
            "dialog.ChartPieFormatDialog" => () => ShowWithParityChartTypeAsync(ChartType.Pie, ShowChartPieFormatDialog),
            "dialog.ChartSeriesFormatDialog" => () => ShowWithSelectedParityChartAsync(ShowChartSeriesFormatDialog),
            "dialog.ChartStockFormatDialog" => () => ShowWithParityChartTypeAsync(ChartType.Stock, ShowChartStockFormatDialog),
            "dialog.ChartStyleDialog" => () => ShowWithSelectedParityChartAsync(ShowChartStyleDialogAsync),
            "dialog.ChartTitlesDialog" => () => ShowWithSelectedParityChartAsync(ShowChartTitlesDialog),
            "dialog.ChartTrendlineOptionsDialog" => () => ShowWithSelectedParityChartAsync(ShowChartTrendlineDialog),
            "dialog.ColorPickerDialog" => async () => { await ShowMoreColorsDialogAsync("More Colors", new CellColor(91, 155, 213)); },
            "dialog.ColorScaleRuleDialog" => () => ShowConditionalFormatNewRuleDialogAsync(CfRuleType.ColorScale),
            "dialog.ColumnWidthDialog" => ShowColumnWidthDialogAsync,
            "dialog.CommentListWindow" => ShowCommentListParityDialogAsync,
            "dialog.ConditionalFormatThresholdDialog" => async () =>
            {
                await ShowConditionalFormatValuePromptAsync("Conditional Formatting", "Threshold", "100");
            },
            "dialog.CreateNamesFromSelectionDialog" => ShowCreateNamesFromSelectionDialogAsync,
            "dialog.CustomViewNameDialog" => async () => { await ShowAddCustomViewDialogAsync(); },
            "dialog.DataBarRuleDialog" => () => ShowConditionalFormatNewRuleDialogAsync(CfRuleType.DataBar),
            "dialog.FillSeriesStepDialog" => ShowFillSeriesDialogAsync,
            "dialog.FormatPictureDialog" => () => ShowWithSelectedParityPictureAsync(OpenFormatPictureDialogAsync),
            "dialog.FunctionArgumentsDialog" => ShowFunctionArgumentsParityDialogAsync,
            "dialog.HighlightCellsRuleDialog" => () => ShowConditionalFormatNewRuleDialogAsync(CfRuleType.CellValue),
            "dialog.HeaderFooterPictureFormatDialog" => ShowHeaderFooterPictureFormatParityDialogAsync,
            "dialog.IconSetRuleDialog" => () => ShowConditionalFormatNewRuleDialogAsync(CfRuleType.IconSet),
            "dialog.InsertChartDialog" => async () => { await ShowChartTypePickerAsync(ChartType.Column); },
            "dialog.InsertFunctionDialog" => async () => { await ShowInsertFunctionPickerDialogAsync(); },
            "dialog.MergeCellsContentWarningDialog" => ShowMergeCellsWarningParityDialogAsync,
            "dialog.MoveChartDialog" => () => ShowWithSelectedParityChartAsync(ShowMoveChartDialog),
            "dialog.MoveOrCopySheetDialog" => ShowMoveOrCopySheetDialogAsync,
            "dialog.MovePivotTableDialog" => () => ShowWithParityPivotAsync(
                pivot => OpenPivotMoveDialogAsync(new PivotApplicationTarget(_session.ActiveSheet, pivot))),
            "dialog.NameDefinitionDialog" => async () => { await ShowDefineNameDialogAsync(seed: null); },
            "dialog.NamedRangeDialog" => ShowNameManagerDialogAsync,
            "dialog.ObjectSizeDialog" => () => ShowWithSelectedParityShapeAsync(ResizeSelectedDrawingObjectAsync),
            "dialog.OutlineGroupDialog" => ShowOutlineSettingsDialogAsync,
            "dialog.PageBreakDialog" => ShowPageBreaksMenuAsync,
            "dialog.PasteNamesDialog" => ShowPasteNamesDialogAsync,
            "dialog.PasteSpecialDialog" => ShowPasteSpecialDialogAsync,
            "dialog.PictureCropDialog" => () => ShowWithSelectedParityPictureAsync(OpenPictureCropDialogAsync),
            "dialog.PivotCalculatedFieldDialog" => () => ShowWithParityPivotAsync(OpenPivotCalculatedFieldDialogAsync),
            "dialog.PivotCalculatedItemDialog" => () => ShowWithParityPivotAsync(OpenPivotCalculatedItemDialogAsync),
            "dialog.PivotChartOptionsDialog" => () => ShowWithParityPivotChartAsync(OpenPivotChartOptionsAsync),
            "dialog.PivotChartTypeDialog" => () => ShowWithParityPivotChartAsync(ChangeActivePivotChartTypeAsync),
            "dialog.PivotFieldGroupingDialog" => () => ShowWithParityPivotAsync(OpenPivotGroupFieldDialogAsync),
            "dialog.PivotLabelFilterDialog" => () => ShowWithParityPivotTargetAsync(OpenPivotLabelFilterDialogAsync),
            "dialog.PivotSortOptionsDialog" => ShowPivotSortOptionsParityDialogAsync,
            "dialog.PivotStyleGalleryDialog" => () => ShowWithParityPivotAsync(OpenPivotStyleGalleryDialogAsync),
            "dialog.PivotTableDataSourceDialog" => () => ShowWithParityPivotAsync(
                pivot => OpenPivotDataSourceDialogAsync(new PivotApplicationTarget(_session.ActiveSheet, pivot))),
            "dialog.PivotTableDialog" => ShowPivotTableCreateParityDialogAsync,
            "dialog.PivotTableNameDialog" => () => ShowWithParityPivotAsync(
                pivot => OpenPivotNameDialogAsync(new PivotApplicationTarget(_session.ActiveSheet, pivot))),
            "dialog.PivotValueFilterDialog" => () => ShowWithParityPivotTargetAsync(OpenPivotValueFilterDialogAsync),
            "dialog.RotationDialog" => () => ShowWithSelectedParityShapeAsync(RotateSelectedDrawingObjectAsync),
            "dialog.RowHeightDialog" => ShowRowHeightDialogAsync,
            "dialog.ScreenTipDialog" => async () =>
            {
                await ShowHyperlinkSubPromptAsync(this, "Set Hyperlink ScreenTip", "ScreenTip text", "Quarterly sales");
            },
            "dialog.SpellCheckDialog" => ShowSpellCheckParityDialogAsync,
            "dialog.TextEntryDialog" => async () =>
            {
                await ShowSingleInputDialogAsync("Enter Text", "Text", "FreeX", "OK", "ParityTextEntryBox");
            },
            "dialog.ThreadedCommentDialog" => ShowThreadedCommentParityDialogAsync,
            "dialog.TopBottomRuleDialog" => () => ShowConditionalFormatNewRuleDialogAsync(CfRuleType.Top10),
            "dialog.UnhideWindowDialog" => ShowUnhideWindowParityDialogAsync,
            "dialog.WorkbookThemeDialog" => ShowThemesGalleryAsync,
            _ => throw new InvalidOperationException($"No supplemental production opener is registered for {catalogId}.")
        };

    private async Task ShowActivateSheetParityDialogAsync()
    {
        var auxiliaryWindow = new Window
        {
            Title = "Parity workbook window",
            Width = 320,
            Height = 200,
            ShowInTaskbar = false,
        };
        auxiliaryWindow.Show();
        try
        {
            await ShowSwitchWindowsDialogAsync();
        }
        finally
        {
            auxiliaryWindow.Close();
        }
    }

    private async Task ShowWithParityChartTypeAsync(ChartType chartType, Func<Task> showDialogAsync)
    {
        var chart = EnsureParityChart();
        if (chart is null)
            return;

        var previousType = chart.Type;
        chart.Type = chartType;
        try
        {
            await ShowWithSelectedParityChartAsync(showDialogAsync);
        }
        finally
        {
            chart.Type = previousType;
        }
    }

    private async Task ShowCommentListParityDialogAsync()
    {
        var sheet = _session.ActiveSheet;
        var address = new CellAddress(sheet.Id, 2, 2);
        _session.ExecuteReviewCommand(new SetThreadedCommentCommand(
            sheet.Id,
            address,
            "Review the quarterly total.",
            "FreeX"));
        await ShowCommentsListAsync();
    }

    private async Task ShowFunctionArgumentsParityDialogAsync()
    {
        var function = FreeX.App.Presentation.Dialogs.InsertFunctionCatalogPlanner.BuildCatalog()
            .First(entry => string.Equals(entry.Name, "SUM", StringComparison.Ordinal));
        await ShowFunctionArgumentsDialogAsync(function);
    }

    private async Task ShowMergeCellsWarningParityDialogAsync()
    {
        var sheetId = _session.ActiveSheet.Id;
        var plan = new MergeCellContentPlan(
            WouldLoseContent: true,
            Entries:
            [
                new MergeCellContentEntry(new CellAddress(sheetId, 2, 2), "North", IsTopLeft: true),
                new MergeCellContentEntry(new CellAddress(sheetId, 2, 3), "Widget", IsTopLeft: false),
            ],
            ConcatenatedText: "North Widget");
        await ShowMergeCellsContentWarningDialogAsync(plan);
    }

    private async Task ShowWithSelectedParityPictureAsync(Func<Task> showDialogAsync)
    {
        var previousKind = _selectedDrawingObjectKind;
        var previousId = _selectedDrawingObjectId;
        var picture = EnsureParityPicture();

        _selectedDrawingObjectKind = SelectionPaneObjectKind.Picture;
        _selectedDrawingObjectId = picture.Id;
        _ribbonContextSource.OnDrawingObjectSelected(SelectionPaneObjectKind.Picture);
        RefreshShell(_statusText.Text ?? "Ready");

        try
        {
            await showDialogAsync();
        }
        finally
        {
            RestoreParityDrawingSelection(previousKind, previousId);
        }
    }

    private PictureModel EnsureParityPicture()
    {
        var sheet = _session.ActiveSheet;
        if (sheet.Pictures.FirstOrDefault(picture => picture.IsVisible) is { } existing)
            return existing;

        var picture = new PictureModel
        {
            Name = "Parity picture",
            Anchor = new CellAddress(sheet.Id, 6, 5),
            Kind = PictureKind.Image,
            ImageBytes = [0x89, 0x50, 0x4E, 0x47],
            ContentType = "image/png",
            Width = 180,
            Height = 110,
            AltText = "Quarterly sales preview",
        };
        sheet.Pictures.Add(picture);
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id));
        RefreshShell(_statusText.Text ?? "Ready");
        return picture;
    }

    private async Task ShowWithParityPivotAsync(Func<PivotTableModel, Task> showDialogAsync)
    {
        var previousSelection = _session.SelectedRange;
        var pivot = EnsureParityPivot();
        if (pivot is null)
            return;

        _session.SelectRange(new GridRange(pivot.TargetRange.Start, pivot.TargetRange.Start));
        RefreshShell(_statusText.Text ?? "Ready");
        try
        {
            await showDialogAsync(pivot);
        }
        finally
        {
            _session.SelectRange(previousSelection);
            RefreshShell(_statusText.Text ?? "Ready");
        }
    }

    private async Task ShowWithParityPivotTargetAsync(
        Func<PivotTableModel, PivotHeaderDropdownTargetModel, Task> showDialogAsync)
    {
        await ShowWithParityPivotAsync(async pivot =>
        {
            if (pivot.RowFields.Count == 0)
                return;

            var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
            var field = pivot.RowFields[0];
            var target = new PivotHeaderDropdownTargetModel(
                pivot.Name,
                PivotFieldListPaneBuilder.FieldCaption(headers, field.SourceFieldIndex),
                field.SourceFieldIndex,
                PivotHeaderArea.Row,
                IsActive: false);
            await showDialogAsync(pivot, target);
        });
    }

    private async Task ShowPivotSortOptionsParityDialogAsync()
    {
        await ShowWithParityPivotAsync(async pivot =>
        {
            if (pivot.RowFields.Count == 0)
                return;

            var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
            var field = pivot.RowFields[0];
            var target = new PivotHeaderDropdownTargetModel(
                pivot.Name,
                PivotFieldListPaneBuilder.FieldCaption(headers, field.SourceFieldIndex),
                field.SourceFieldIndex,
                PivotHeaderArea.Row,
                IsActive: false);
            await OpenPivotSortOptionsDialogAsync(pivot, headers, target);
        });
    }

    private async Task ShowWithParityPivotChartAsync(Func<Task> showDialogAsync)
    {
        await ShowWithParityPivotAsync(async pivot =>
        {
            var sheet = _session.ActiveSheet;
            if (!sheet.Charts.Any(chart => chart.IsPivotChart &&
                string.Equals(chart.PivotTableName, pivot.Name, StringComparison.Ordinal)))
            {
                _session.ExecuteReviewCommand(new AddPivotChartCommand(sheet.Id, pivot.Name, ChartType.Column));
                RefreshShell(_statusText.Text ?? "Ready");
            }

            await showDialogAsync();
        });
    }

    private async Task ShowPivotTableCreateParityDialogAsync()
    {
        var sheet = _session.ActiveSheet;
        SeedParityPivotSource(sheet);
        await ShowWithParitySelectionAsync(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 8, 5),
            ShowInsertPivotTableDialogAsync);
    }

    private async Task ShowSpellCheckParityDialogAsync()
    {
        var sheet = _session.ActiveSheet;
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, Cell.FromValue(new TextValue("Quaterly reveneu")));
        await ShowWithParitySelectionAsync(address, address, ShowSpellingDialogAsync);
    }

    private async Task ShowThreadedCommentParityDialogAsync()
    {
        var sheet = _session.ActiveSheet;
        var address = new CellAddress(sheet.Id, 2, 2);
        _session.ExecuteReviewCommand(new SetThreadedCommentCommand(
            sheet.Id,
            address,
            "Please verify this total.",
            author: "Reviewer"));
        await ShowWithParitySelectionAsync(address, address, ShowThreadedCommentDialogAsync);
    }

    private Task ShowPrintPreviewParityDialogAsync()
    {
        var pages = PrintPreviewParityFixture.Pages;
        return ShowPrintPreviewDialogAsync(
            PrintPreviewSurfacePlanner.ParityPrinterName,
            pages.Count,
            pageIndex => BuildPrintPreviewParityPageView(pages[pageIndex]));
    }

    private static Control BuildPrintPreviewParityPageView(PrintPreviewParityPage page)
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

    private WorkbookFileDialogSurfacePlan CreateOpenWorkbookDialogSurfacePlan() =>
        WorkbookFileDialogSurfacePlanner.CreateOpenPlan(
            WorkbookFilePickerPlanner.BuildOpenPickerPlan(_session.OpenFormats));

    private WorkbookFileDialogSurfacePlan CreateSaveAsWorkbookDialogSurfacePlan() =>
        WorkbookFileDialogSurfacePlanner.CreateSaveAsPlan(
            WorkbookFilePickerPlanner.BuildSavePickerPlan(
                _session.SaveFormats,
                _session.Workbook.Name,
                _session.DisplayName,
                NativeWorkbookExtension));

    private async Task ShowWorkbookFileDialogParitySurfaceAsync(WorkbookFileDialogSurfacePlan plan)
    {
        var dialog = new Window
        {
            Title = plan.Title,
            Width = WorkbookFileDialogSurfacePlanner.Width,
            Height = WorkbookFileDialogSurfacePlanner.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, plan.DialogAutomationId);

        var places = new StackPanel
        {
            Width = 128,
            Margin = new Thickness(0, 0, 12, 0),
            Background = Brush(0xF3, 0xF5, 0xF8),
        };
        foreach (var place in new[] { "Recent", "Desktop", "Documents", "This PC" })
            places.Children.Add(new TextBlock { Text = place, Margin = new Thickness(12, 10, 8, 2) });

        var fileList = new ListBox
        {
            MinHeight = 220,
            ItemsSource = new[]
            {
                "Budget.xlsx",
                "Quarterly Report.fxl",
                "Sales.csv",
                "Forecast.xlsx",
            },
        };

        var fileNameBox = new TextBox
        {
            Text = plan.FileName,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            Height = 26,
            MinHeight = 26,
            Padding = new Thickness(4, 3, 4, 3),
        };
        AutomationProperties.SetAutomationId(fileNameBox, WorkbookFileDialogSurfacePlanner.FileNameBoxAutomationId);

        var fileTypeBox = new ComboBox
        {
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            Height = 26,
            MinHeight = 26,
            ItemsSource = plan.FileTypes.Select(type => $"{type.DisplayName} ({string.Join("; ", type.Patterns)})").ToArray(),
            SelectedIndex = 0,
        };
        AutomationProperties.SetAutomationId(fileTypeBox, WorkbookFileDialogSurfacePlanner.FileTypeBoxAutomationId);

        var form = new AvaloniaGrid
        {
            Margin = new Thickness(0, 10, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
        };
        AddWorkbookFileDialogField(form, 0, plan.FileNameLabel, fileNameBox);
        AddWorkbookFileDialogField(form, 1, plan.FileTypeLabel, fileTypeBox);

        var primaryButton = new Button { Content = plan.PrimaryCommandText, IsDefault = true };
        var cancelFileButton = new Button { Content = UiText.Get("InsertLoc_CancelButton"), IsCancel = true };
        ApplyDialogButtonChrome(primaryButton, 84, isDefault: true);
        ApplyDialogButtonChrome(cancelFileButton, 84);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            Spacing = 8,
            Children = { primaryButton, cancelFileButton },
        };

        var right = new DockPanel();
        DockPanel.SetDock(form, Dock.Bottom);
        DockPanel.SetDock(buttons, Dock.Bottom);
        right.Children.Add(buttons);
        right.Children.Add(form);
        right.Children.Add(fileList);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(places, Dock.Left);
        root.Children.Add(places);
        root.Children.Add(right);

        dialog.Content = root;
        dialog.Opened += (_, _) => fileNameBox.Focus();
        await dialog.ShowDialog(this);
    }

    private static void AddWorkbookFileDialogField(AvaloniaGrid form, int row, string label, Control control)
    {
        var labelControl = new Label
        {
            Content = label,
            Target = control,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 4),
        };
        AvaloniaGrid.SetRow(labelControl, row);
        AvaloniaGrid.SetColumn(labelControl, 0);
        AvaloniaGrid.SetRow(control, row);
        AvaloniaGrid.SetColumn(control, 1);
        control.Margin = new Thickness(0, 0, 0, 4);
        form.Children.Add(labelControl);
        form.Children.Add(control);
    }

    private async Task ShowExportOptionsParityDialogAsync()
    {
        var availability = ExportOptionsDialogSurfacePlanner.CreateFormatAvailability(ExportFileFormat.Pdf);
        var dialog = new Window
        {
            Title = UiText.Get(ExportOptionsDialogSurfacePlanner.TitleResourceKey),
            Width = ExportOptionsDialogSurfacePlanner.Width,
            SizeToContent = SizeToContent.Height,
            MaxHeight = ExportOptionsDialogSurfacePlanner.MaxHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, ExportOptionsDialogSurfacePlanner.DialogAutomationId);

        var activeSheetButton = new RadioButton { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_ActiveSheetS")), IsChecked = true };
        var selectionButton = new RadioButton { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_SelectedRange")) };
        var workbookButton = new RadioButton { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_Workbook")) };
        var allPagesButton = new RadioButton { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_All")), GroupName = "PageRange", IsChecked = true };
        var pagesButton = new RadioButton { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_Pages")), GroupName = "PageRange" };
        var fromPageBox = new TextBox { Width = 56, Height = 24, MinHeight = 24, Padding = new Thickness(4, 2, 4, 2), IsEnabled = false };
        var toPageBox = new TextBox { Width = 56, Height = 24, MinHeight = 24, Padding = new Thickness(4, 2, 4, 2), IsEnabled = false };
        var documentPropertiesBox = new CheckBox { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_IncludeDocumentProperties")) };
        var ignorePrintAreasBox = new CheckBox { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_IgnorePrintAreas")) };
        var bookmarksBox = new CheckBox { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_CreatePdfBookmarks")) };
        var bookmarkModeBox = new ComboBox { Width = 180, Height = 28, MinHeight = 28, VerticalContentAlignment = AvaloniaVerticalAlignment.Center, IsEnabled = false };
        bookmarkModeBox.Items.Add(UiText.Get("ExportOptions_SheetNames"));
        bookmarkModeBox.Items.Add(UiText.Get("ExportOptions_PrintTitles"));
        bookmarkModeBox.Items.Add(UiText.Get("ExportOptions_PageNumbers"));
        bookmarkModeBox.SelectedIndex = 0;
        var initialViewBox = new ComboBox { Width = 180, Height = 28, MinHeight = 28, VerticalContentAlignment = AvaloniaVerticalAlignment.Center, IsEnabled = availability.PdfInitialViewEnabled };
        initialViewBox.Items.Add(UiText.Get("ExportOptions_SinglePage"));
        initialViewBox.Items.Add(UiText.Get("ExportOptions_OneContinuousColumn"));
        initialViewBox.Items.Add(UiText.Get("ExportOptions_TwoColumnsOddPagesLeft"));
        initialViewBox.Items.Add(UiText.Get("ExportOptions_TwoColumnsOddPagesRight"));
        initialViewBox.SelectedIndex = 0;
        var openModeBox = new ComboBox { Width = 180, Height = 28, MinHeight = 28, VerticalContentAlignment = AvaloniaVerticalAlignment.Center, IsEnabled = availability.PdfOpenModeEnabled };
        openModeBox.Items.Add(UiText.Get("ExportOptions_Normal"));
        openModeBox.Items.Add(UiText.Get("ExportOptions_BookmarksVisible"));
        openModeBox.Items.Add(UiText.Get("ExportOptions_FullScreen"));
        openModeBox.SelectedIndex = 0;
        var pdfLanguageBox = new TextBox { Width = 88, Height = 24, MinHeight = 24, Padding = new Thickness(4, 2, 4, 2), Text = "en-US", IsEnabled = availability.PdfLanguageEnabled };
        var bitmapTextBox = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("ExportOptions_BitmapTextWhenFontsMayNotBeEmbedded")),
            IsEnabled = availability.PdfBitmapTextEnabled,
        };
        var pdfABox = new CheckBox { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_PdfACompliantNotSupported")), IsEnabled = false };
        var structureTagsBox = new CheckBox { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_DocumentStructureTagsNotSupported")), IsEnabled = false };
        var standardQualityButton = new RadioButton { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_Standard")), IsChecked = true };
        var minimumSizeButton = new RadioButton { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_MinimumSize")), IsEnabled = availability.MinimumSizeEnabled };
        var openAfterPublishBox = new CheckBox { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_OpenAfterPublishing")), Margin = new Thickness(0, 8, 0, 0) };

        bookmarksBox.IsEnabled = availability.PdfBookmarksEnabled;
        bookmarksBox.IsCheckedChanged += (_, _) => bookmarkModeBox.IsEnabled = bookmarksBox.IsChecked == true && availability.PdfBookmarksEnabled;
        pagesButton.IsCheckedChanged += (_, _) =>
        {
            var enabled = pagesButton.IsChecked == true;
            fromPageBox.IsEnabled = enabled;
            toPageBox.IsEnabled = enabled;
        };

        var stack = new StackPanel { Margin = new Thickness(16), Spacing = 2 };
        stack.Children.Add(CreateExportOptionsSectionLabel("ExportOptions_PublishWhat"));
        stack.Children.Add(activeSheetButton);
        stack.Children.Add(selectionButton);
        stack.Children.Add(workbookButton);
        stack.Children.Add(CreateExportOptionsSectionLabel("ExportOptions_PageRange", topMargin: 12));
        stack.Children.Add(allPagesButton);

        var pageRangePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Spacing = 6 };
        pageRangePanel.Children.Add(pagesButton);
        pageRangePanel.Children.Add(new Label { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_From")), Target = fromPageBox, VerticalAlignment = AvaloniaVerticalAlignment.Center });
        pageRangePanel.Children.Add(fromPageBox);
        pageRangePanel.Children.Add(new Label { Content = StripDisplayMnemonic(UiText.Get("ExportOptions_To")), Target = toPageBox, VerticalAlignment = AvaloniaVerticalAlignment.Center });
        pageRangePanel.Children.Add(toPageBox);
        stack.Children.Add(pageRangePanel);

        stack.Children.Add(CreateExportOptionsSectionLabel("ExportOptions_PdfXpsOptions", topMargin: 14));
        stack.Children.Add(documentPropertiesBox);
        stack.Children.Add(ignorePrintAreasBox);
        stack.Children.Add(bookmarksBox);
        stack.Children.Add(CreateExportOptionsLabeledControl("ExportOptions_BookmarkMode", bookmarkModeBox, leftIndent: 22));
        stack.Children.Add(CreateExportOptionsLabeledControl("ExportOptions_InitialView", initialViewBox));
        stack.Children.Add(CreateExportOptionsLabeledControl("ExportOptions_OpenMode", openModeBox));
        stack.Children.Add(CreateExportOptionsLabeledControl("ExportOptions_PdfLanguage", pdfLanguageBox));
        stack.Children.Add(bitmapTextBox);
        stack.Children.Add(pdfABox);
        stack.Children.Add(structureTagsBox);
        stack.Children.Add(standardQualityButton);
        stack.Children.Add(minimumSizeButton);
        stack.Children.Add(openAfterPublishBox);

        var okButton = new Button { Content = UiText.Get("InsertLoc_OkButton"), IsDefault = true };
        var cancelButton = new Button { Content = UiText.Get("InsertLoc_CancelButton"), IsCancel = true };
        ApplyDialogButtonChrome(okButton, 84, isDefault: true);
        ApplyDialogButtonChrome(cancelButton, 84);
        okButton.Click += (_, _) => dialog.Close();
        cancelButton.Click += (_, _) => dialog.Close();

        // Buttons live in their own row docked to the bottom of the dialog so they stay fully
        // visible even when the option list is tall — mirrors the compact WPF layout where the
        // whole dialog sizes to content and OK/Cancel are never clipped.
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(16, 8, 16, 12),
            Spacing = 8,
            Children = { okButton, cancelButton },
        };

        var root = new DockPanel();
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);
        root.Children.Add(new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        dialog.Content = root;
        dialog.Opened += (_, _) => activeSheetButton.Focus();
        await dialog.ShowDialog(this);
    }

    private Task ShowTextToColumnsParityDialogAsync()
    {
        var sheet = _session.ActiveSheet;
        for (var i = 0; i < TextToColumnsParityFixture.SampleRows.Count; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(2 + i), 6), Cell.FromValue(new TextValue(TextToColumnsParityFixture.SampleRows[i])));

        return ShowWithParitySelectionAsync(
            new CellAddress(sheet.Id, 2, 6),
            new CellAddress(sheet.Id, 5, 6),
            ShowTextToColumnsDialogAsync);
    }

    private Task ShowCreateTableParityDialogAsync() =>
        ShowCreateTableDialogAsync("Sheet1!$A$1:$D$5", "TableStyleMedium2");

    private async Task ShowTableResizeParityDialogAsync()
    {
        var previousSelection = _session.SelectedRange;
        var sheet = _session.ActiveSheet;
        var anchor = new CellAddress(sheet.Id, 1, 1);
        _session.SelectCell(anchor);
        if (!TryGetActiveStructuredTable(out var table))
        {
            var range = new GridRange(anchor, new CellAddress(sheet.Id, 5, 4));
            _session.ExecuteReviewCommand(
                new CreateStructuredTableCommand(sheet.Id, range, "TableStyleMedium2", firstRowHasHeaders: true));
            _session.SelectCell(anchor);
            if (!TryGetActiveStructuredTable(out table))
            {
                _session.SelectRange(previousSelection);
                return;
            }
        }

        try
        {
            await OpenTableResizeDialogAsync(table);
        }
        finally
        {
            _session.SelectRange(previousSelection);
            RefreshShell(_statusText.Text ?? "Ready");
        }
    }

    private async Task ShowAutoFilterParityDialogAsync()
    {
        var previousSelection = _session.SelectedRange;
        var sheet = _session.ActiveSheet;
        var fixture = AutoFilterParityFixturePlanner.CreateFixturePlan(
            _session.Workbook,
            sheet,
            AvaloniaAutoFilterParityTextProvider.Instance,
            UiText.Get("AutoFilter_BlankDisplayText"));
        _session.SelectRange(new GridRange(fixture.Range.Start, fixture.Range.Start));
        RefreshShell(_statusText.Text ?? "Ready");

        try
        {
            await ShowAutoFilterParityWindowAsync(fixture.MenuPlan);
        }
        finally
        {
            _session.SelectRange(previousSelection);
            RefreshShell(_statusText.Text ?? "Ready");
        }
    }

    private Task ShowAutoFilterParityWindowAsync(AutoFilterMenuPlan menuPlan)
    {
        var dialog = new Window
        {
            Title = UiText.Format("AutoFilter_TitleWithHeader", menuPlan.HeaderText),
            Width = 312,
            Height = 437,
            SizeToContent = SizeToContent.Manual,
            MaxHeight = 560,
            Background = Brushes.White,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowDecorations = global::Avalonia.Controls.WindowDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "AutoFilterParityDialog");
        dialog.Content = CreateAutoFilterParityContent(AutoFilterMenuPlanner.Build(menuPlan));
        ShowOwnedModelessWindow(
            dialog,
            () => dialog.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control =>
                    control.Focusable && control.IsVisible && control.IsEffectivelyEnabled)
                ?.Focus(),
            closeOnDeactivate: true);
        return Task.CompletedTask;
    }

    private Control CreateAutoFilterParityContent(AutoFilterMenuModel model)
    {
        var stack = new StackPanel();
        var checkBoxes = new List<CheckBox>();

        foreach (var item in model.Items)
        {
            switch (item.Kind)
            {
                case AutoFilterMenuItemKind.SortAscending:
                case AutoFilterMenuItemKind.SortDescending:
                case AutoFilterMenuItemKind.ClearFilter:
                case AutoFilterMenuItemKind.FilterByColor:
                case AutoFilterMenuItemKind.FilterFamily:
                case AutoFilterMenuItemKind.FilterFamilyCommand:
                    stack.Children.Add(CreateAutoFilterParityMenuButton(item.Label, item.IsEnabled));
                    break;
                case AutoFilterMenuItemKind.Search:
                    stack.Children.Add(new TextBox
                    {
                        PlaceholderText = item.Label,
                        Height = 24,
                        MinHeight = 24,
                        Margin = new Thickness(0, 2, 0, 4),
                        FontFamily = FormulaBarFontFamily,
                    });
                    break;
                case AutoFilterMenuItemKind.SelectAll:
                    stack.Children.Add(new CheckBox
                    {
                        Content = item.Label,
                        IsChecked = true,
                        Margin = new Thickness(0, 0, 0, 4),
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                    });
                    break;
                case AutoFilterMenuItemKind.ChecklistItem:
                    var box = new CheckBox
                    {
                        Content = item.Label,
                        IsChecked = true,
                        Tag = item.Value,
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                    };
                    checkBoxes.Add(box);
                    break;
                case AutoFilterMenuItemKind.Separator:
                    AddAutoFilterParitySeparator(stack);
                    break;
            }
        }

        if (checkBoxes.Count > 0)
        {
            var checklistPanel = new StackPanel();
            foreach (var box in checkBoxes)
                checklistPanel.Children.Add(box);
            stack.Children.Add(new ScrollViewer { Content = checklistPanel, Height = 180 });
        }

        var okButton = new Button { Content = "OK", IsDefault = true };
        var cancelButton = new Button { Content = UiText.Get("InsertLoc_CancelButton"), IsCancel = true };
        ApplyDialogButtonChrome(okButton, 72, isDefault: true);
        ApplyDialogButtonChrome(cancelButton, 72);
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 8, 0, 0));

        var root = new DockPanel { Margin = new Thickness(10), LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        return root;
    }

    private static Button CreateAutoFilterParityMenuButton(string label, bool isEnabled)
    {
        var button = new Button
        {
            Content = label,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsEnabled = isEnabled,
            FontFamily = FormulaBarFontFamily,
        };
        return button;
    }

    private static void AddAutoFilterParitySeparator(StackPanel stack) =>
        stack.Children.Add(new Border
        {
            Height = 1,
            Background = Brush(0xDA, 0xDC, 0xDF),
            Margin = new Thickness(0, 3),
        });

    private sealed class AvaloniaAutoFilterParityTextProvider : IAutoFilterMenuTextProvider
    {
        public static AvaloniaAutoFilterParityTextProvider Instance { get; } = new();

        public string Get(string resourceKey) => UiText.Get(resourceKey);

        public string Format(string resourceKey, string value) => UiText.Format(resourceKey, value);
    }

    private async Task ShowAdvancedFilterParityDialogAsync()
    {
        var previousSelection = _session.SelectedRange;
        var sheetId = _session.ActiveSheet.Id;
        _session.SelectRange(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 5, 4)));
        RefreshShell(_statusText.Text ?? "Ready");

        try
        {
            await ShowAdvancedFilterDialogAsync();
        }
        finally
        {
            _session.SelectRange(previousSelection);
            RefreshShell(_statusText.Text ?? "Ready");
        }
    }

    private async Task ShowRemoveDuplicatesParityDialogAsync()
    {
        var previousSheetId = _session.ActiveSheet.Id;
        var previousSelection = _session.SelectedRange;
        var sheetId = _session.Workbook.Sheets.Count > 0
            ? _session.Workbook.Sheets[0].Id
            : previousSheetId;
        if (!previousSheetId.Equals(sheetId))
            _session.SelectSheet(sheetId);

        var sheet = _session.ActiveSheet;
        var previousHeaders = CaptureHeaderCells(sheet, row: 1, startColumn: 1, columnCount: 4);
        SeedRemoveDuplicatesParityHeaders(sheet);
        _session.SelectRange(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 4)));
        RefreshShell(_statusText.Text ?? "Ready");

        try
        {
            await ShowRemoveDuplicatesInputDialogAsync(forceHasHeaders: true);
        }
        finally
        {
            RestoreHeaderCells(sheet, row: 1, startColumn: 1, previousHeaders);
            if (!previousSheetId.Equals(_session.ActiveSheet.Id))
                _session.SelectSheet(previousSheetId);
            _session.SelectRange(previousSelection);
            RefreshShell(_statusText.Text ?? "Ready");
        }
    }

    private static Cell?[] CaptureHeaderCells(Sheet sheet, uint row, uint startColumn, int columnCount)
    {
        var cells = new Cell?[columnCount];
        for (var index = 0; index < cells.Length; index++)
            cells[index] = sheet.GetCell(row, startColumn + (uint)index)?.Clone();
        return cells;
    }

    private static void SeedRemoveDuplicatesParityHeaders(Sheet sheet)
    {
        string[] headers = ["Region", "Product", "Revenue", "Units"];
        for (var index = 0; index < headers.Length; index++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(index + 1)), Cell.FromValue(new TextValue(headers[index])));
    }

    private static void RestoreHeaderCells(Sheet sheet, uint row, uint startColumn, IReadOnlyList<Cell?> cells)
    {
        for (var index = 0; index < cells.Count; index++)
        {
            var address = new CellAddress(sheet.Id, row, startColumn + (uint)index);
            if (cells[index] is { } cell)
                sheet.SetCell(address, cell);
            else
                sheet.ClearCell(address);
        }
    }

    private async Task ShowGoalSeekParityDialogAsync()
    {
        var previousSheetId = _session.ActiveSheet.Id;
        var previousSelection = _session.SelectedRange;
        var sheetId = _session.Workbook.Sheets.Count > 0
            ? _session.Workbook.Sheets[0].Id
            : previousSheetId;
        if (!previousSheetId.Equals(sheetId))
            _session.SelectSheet(sheetId);

        _session.SelectRange(new GridRange(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 2, 3)));
        RefreshShell(_statusText.Text ?? "Ready");

        try
        {
            await ShowGoalSeekInputDialogAsync(
                initialSetCellText: "C2",
                initialTargetValueText: "5000",
                initialChangingCellText: "E2");
        }
        finally
        {
            if (!previousSheetId.Equals(_session.ActiveSheet.Id))
                _session.SelectSheet(previousSheetId);
            _session.SelectRange(previousSelection);
            RefreshShell(_statusText.Text ?? "Ready");
        }
    }

    private Task ShowGoalSeekStatusParityDialogAsync()
    {
        var sheetId = _session.ActiveSheet.Id;
        var request = new GoalSeekRequest(
            new CellAddress(sheetId, 2, 3),
            5000d,
            new CellAddress(sheetId, 2, 5));
        var proposal = WorkbookGoalSeekProposal.Ready(
            request,
            new GoalSeekResult(true, 125d, 5000d, 7));

        return ShowGoalSeekStatusDialogAsync(proposal);
    }

    private Task ShowDataTableParityDialogAsync() =>
        ShowWithParitySelectionAsync(
            new CellAddress(_session.ActiveSheet.Id, 1, 1),
            new CellAddress(_session.ActiveSheet.Id, 4, 4),
            async () => { await ShowDataTableInputDialogAsync(); });

    private async Task ShowScenarioManagerParityDialogAsync()
    {
        var changingCellsRange = ScenarioManagerParityFixture.ChangingCellsRange(_session.ActiveSheet.Id);
        await ShowWithParitySelectionAsync(
            changingCellsRange.Start,
            changingCellsRange.End,
            async () =>
            {
                ScenarioManagerParityFixture.Seed(_session.Workbook, _session.ActiveSheet.Id);
                var plan = ScenarioManagerPlanner.CreateDialogPlan(
                    _session.Workbook,
                    ScenarioManagerParityFixture.ScenarioName);
                if (plan.IsReady)
                    await ShowScenarioManagerCompactDialogAsync(plan);
            });
    }

    private Task ShowForecastSheetParityDialogAsync() =>
        ShowWithParitySelectionAsync(
            new CellAddress(_session.ActiveSheet.Id, 1, 1),
            new CellAddress(_session.ActiveSheet.Id, 4, 2),
            async () => { await ShowForecastSheetInputDialogAsync(); });

    private Task ShowSubtotalParityDialogAsync()
    {
        SubtotalParityFixture.ApplySheetState(_session.ActiveSheet);
        var fixture = SubtotalParityFixture.CreateState(_session.ActiveSheet);
        return ShowWithParitySelectionAsync(
            fixture.SelectedRange.Start,
            fixture.SelectedRange.End,
            async ()
                => await ShowSubtotalInputDialogAsync(
                    fixture.SelectedRange,
                    fixture.Columns,
                    fixture.CreatePlan()));
    }

    private void PrepareConsolidateParityCaptureState()
    {
        var sourceRange = ConsolidateParityFixture.CreateSourceRange(_session.ActiveSheet.Id);
        _session.SelectRange(sourceRange);
        RefreshShell("Ready");
    }

    private static IReadOnlyList<FormulaErrorIssue> CreateErrorCheckingParityIssues(SheetId sheetId) =>
        ErrorCheckingParityFixture.CreateIssues(sheetId);

    private Task ShowErrorCheckingParityDialogAsync() =>
        ShowErrorCheckingDialogAsync(CreateErrorCheckingParityIssues(_session.ActiveSheet.Id));

    private Task ShowSparklineParityDialogAsync() =>
        ShowInsertSparklineDialogAsync(
            SparklineKind.Line,
            initialDataRangeText: "Sheet1!$D$2:$D$5",
            initialLocationText: "Sheet1!$H$2:$H$5");

    private Task ShowInsertHyperlinkParityDialogAsync() =>
        ShowInsertHyperlinkParityDialogCoreAsync();

    private async Task ShowInsertHyperlinkParityDialogCoreAsync()
    {
        var address = new CellAddress(_session.ActiveSheet.Id, 2, 2);
        HyperlinkDialogParityFixture.Seed(_session.ActiveSheet, address);
        await ShowWithParitySelectionAsync(address, address, async () =>
        {
            await ShowInsertHyperlinkInputDialogAsync();
        });
    }

    private Task ShowEvaluateFormulaParityDialogAsync() =>
        ShowEvaluateFormulaDialogAsync(EvaluateFormulaParityFixture.CreateSummary(_session.ActiveSheet.Id));

    private Task ShowWatchWindowParityDialogAsync() =>
        ShowWithParitySelectionAsync(
            new CellAddress(_session.ActiveSheet.Id, 2, 2),
            new CellAddress(_session.ActiveSheet.Id, 3, 3),
            async () =>
            {
                // Seed watches on the "Demo" data sheet's Units cells (C2=120, C3=85) so the Watch
                // Window has populated rows that match the WPF parity capture (same sheet name +
                // values). The active sheet here is a later empty sheet, so target Sheets[0].
                var watchSheetId = _session.Workbook.Sheets[0].Id;
                WatchWindowService.AddWatches(
                    _session.Workbook,
                    new GridRange(
                        new CellAddress(watchSheetId, 2, 3),
                        new CellAddress(watchSheetId, 3, 3)));
                await ShowWatchWindowDialogAsync();
            });

    private Task ShowAddWatchParityDialogAsync() =>
        ShowAddWatchDialogAsync(AddWatchDialogPlanner.ParitySelectedRangeText);

    private async Task ShowRenameSheetParityDialogAsync() =>
        await ShowRenameSheetDialogAsync(_session.ActiveSheet.Name);

    private async Task ShowUnhideSheetParityDialogAsync() =>
        await ShowUnhideSheetDialogAsync([new WorkbookHiddenSheet(_session.ActiveSheet.Id, "Archive")]);

    private async Task ShowSelectDataSourceParityDialogAsync() =>
        await ShowSelectDataDialogAsync("A1:C6", firstColumnIsCategories: true);

    private async Task ShowChangeChartTypeParityDialogAsync() =>
        await ShowWithSelectedParityChartAsync(ShowChangeChartTypeDialog);

    private async Task ShowFormatChartAreaParityDialogAsync() =>
        await ShowWithSelectedParityChartAsync(ShowFormatChartAreaDialog);

    private async Task ShowShapeEffectsParityDialogAsync() =>
        await ShowWithSelectedParityShapeAsync(async () =>
        {
            if (ResolveSelectedShape() is { } shape)
            {
                var outcome = _session.ExecuteReviewCommand(new SetDrawingShapeEffectCommand(
                    _session.ActiveSheet.Id,
                    shape.Id,
                    DrawingShapeEffectPreset.Shadow));
                if (outcome.Success)
                    RefreshShell(_statusText.Text ?? "Ready");
            }

            await OpenShapeEffectsDialogAsync();
        });

    private async Task ShowShapeGradientParityDialogAsync()
    {
        if (EnsureParityShape() is { } shape)
            ShapeGradientParityFixture.Apply(shape);

        await ShowWithSelectedParityShapeAsync(OpenShapeGradientDialogAsync);
    }

    private async Task ShowSelectionPaneParityDialogAsync()
    {
        var chart = EnsureParityChart();
        if (chart is not null)
            chart.Name = SelectionPaneParityFixture.ChartName;

        var shape = EnsureParityShape();
        if (shape is not null)
            shape.Name = SelectionPaneParityFixture.ShapeName;

        if (chart is not null && shape is not null)
        {
            var items = SelectionPaneParityFixture.CreateDialogItems(
                chart.Id,
                shape.Id,
                chart.IsVisible,
                shape.IsVisible);

            await OpenSelectionPaneDialogAsync(items);
            return;
        }

        await OpenSelectionPaneDialogAsync();
    }

    private async Task ShowAccessibilityCheckerParityDialogAsync()
    {
        var issues = AccessibilityCheckerParityFixture.CreateDialogIssues(_session.ActiveSheet.Id);
        var plan = AccessibilityCheckerDialogPlanner.Create(issues, UiText.Get);
        await ShowAccessibilityCheckerIssuesDialogAsync(plan);
    }

    private async Task ShowWithParityPivotAsync(Func<Task> showDialogAsync)
    {
        var previousSelection = _session.SelectedRange;
        var pivot = EnsureParityPivot();
        if (pivot is null)
            return;

        _session.SelectRange(new GridRange(pivot.TargetRange.Start, pivot.TargetRange.Start));
        RefreshShell(_statusText.Text ?? "Ready");

        try
        {
            await showDialogAsync();
        }
        finally
        {
            _session.SelectRange(previousSelection);
            RefreshShell(_statusText.Text ?? "Ready");
        }
    }

    private async Task ShowPivotTableOptionsParityDialogAsync()
    {
        var pivot = EnsureParityPivot();
        if (pivot is null)
            return;

        await OpenPivotTableOptionsDialogAsync(pivot);
    }

    private async Task ShowPivotFieldFilterParityDialogAsync()
    {
        var pivot = EnsureParityPivot();
        if (pivot is null || pivot.RowFields.Count == 0)
            return;

        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        var field = pivot.RowFields[0];
        var target = new PivotHeaderDropdownTargetModel(
            pivot.Name,
            PivotFieldListPaneBuilder.FieldCaption(headers, field.SourceFieldIndex),
            field.SourceFieldIndex,
            PivotHeaderArea.Row,
            IsActive: false);

        await OpenPivotItemFilterDialogAsync(pivot, headers, target, exposeActiveFilterActions: false);
    }

    private async Task ShowPivotValueFieldSettingsParityDialogAsync()
    {
        var pivot = EnsureParityPivot();
        if (pivot is null || pivot.DataFields.Count == 0)
            return;

        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        var field = pivot.DataFields[0];
        var caption = string.IsNullOrWhiteSpace(field.Name)
            ? PivotFieldListPaneBuilder.FieldCaption(headers, field.SourceFieldIndex)
            : field.Name;
        var target = new PivotHeaderDropdownTargetModel(
            pivot.Name,
            caption,
            field.SourceFieldIndex,
            PivotHeaderArea.Value,
            IsActive: false,
            DataFieldIndex: 0);

        await OpenPivotValueFieldSettingsDialogAsync(pivot, headers, target);
    }

    private async Task ShowInsertSlicerParityDialogAsync()
    {
        var pivot = EnsureParityPivot();
        if (pivot is null)
            return;

        var field = FirstParityPivotField(pivot);
        await ShowPivotControlPickerParityDialogAsync(
            title: UiText.Get("PivotSlicerTimeline_InsertSlicer"),
            automationId: "InsertSlicerDialog",
            fieldListAutomationId: "InsertSlicerFieldList",
            groupResourceKey: "PivotSlicerTimeline_ChooseFieldsGroup",
            fieldLabelResourceKey: "PivotSlicerTimeline_FieldToConnectLabel",
            captionLabelResourceKey: "PivotSlicerTimeline_SlicerCaptionLabel",
            selectedField: field,
            captionText: UiText.Format("PivotSlicerTimeline_DefaultSlicerName", field));
    }

    private async Task ShowInsertTimelineParityDialogAsync()
    {
        var pivot = EnsureParityPivot();
        if (pivot is null)
            return;

        var field = FirstParityPivotField(pivot);
        await ShowPivotControlPickerParityDialogAsync(
            title: UiText.Get("PivotSlicerTimeline_InsertTimeline"),
            automationId: "InsertTimelineDialog",
            fieldListAutomationId: "InsertTimelineFieldList",
            groupResourceKey: "PivotSlicerTimeline_ChooseDateFieldsGroup",
            fieldLabelResourceKey: "PivotSlicerTimeline_DateFieldToConnectLabel",
            captionLabelResourceKey: "PivotSlicerTimeline_TimelineCaptionLabel",
            selectedField: field,
            captionText: UiText.Format("PivotSlicerTimeline_DefaultTimelineName", field));
    }

    private string FirstParityPivotField(PivotTableModel pivot)
    {
        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        return headers.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h)) ?? string.Empty;
    }

    /// <summary>
    /// Mirrors the WPF <c>InsertSlicerDialog</c> / <c>InsertTimelineDialog</c>: a "Choose fields"
    /// group box holding an editable "Field to connect" combo and a caption text box, with the
    /// OK/Cancel row beneath — instead of the old plain checkbox list.
    /// </summary>
    private async Task ShowPivotControlPickerParityDialogAsync(
        string title,
        string automationId,
        string fieldListAutomationId,
        string groupResourceKey,
        string fieldLabelResourceKey,
        string captionLabelResourceKey,
        string selectedField,
        string captionText)
    {
        var pivot = EnsureParityPivot();
        if (pivot is null)
            return;

        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .ToArray();
        if (headers.Length == 0)
            return;

        var dialog = new Window
        {
            Title = title,
            Width = PivotSlicerTimelineDialogContract.Width,
            Height = PivotSlicerTimelineDialogContract.Height,
            SizeToContent = SizeToContent.Manual,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, automationId);

        var fieldBox = new ComboBox
        {
            ItemsSource = headers,
            SelectedIndex = Math.Max(0, Array.IndexOf(headers, selectedField)),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            Height = 24,
            MinHeight = 24,
            // Without explicit padding + vertical-centering the Fluent ComboBox's default content
            // padding pushes the selected item out of the forced 24px clip, leaving it visibly cut off.
            // Match the proven chart/drawing combo chrome so the selected field reads fully (win.png).
            Padding = new Thickness(5, 0, 4, 0),
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(fieldBox, fieldListAutomationId);

        var captionBox = new TextBox
        {
            Text = captionText,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            Height = 24,
            MinHeight = 24,
            Padding = new Thickness(4, 2, 4, 2),
        };

        var groupBody = new StackPanel { Spacing = 4 };
        groupBody.Children.Add(new TextBlock
        {
            Text = UiText.Get(groupResourceKey).Replace("_", string.Empty),
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });
        groupBody.Children.Add(new Label
        {
            Content = UiText.Get(fieldLabelResourceKey),
            Target = fieldBox,
            Padding = new Thickness(0, 0, 0, 2),
        });
        groupBody.Children.Add(fieldBox);
        groupBody.Children.Add(new Label
        {
            Content = UiText.Get(captionLabelResourceKey),
            Target = captionBox,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(0, 0, 0, 2),
        });
        groupBody.Children.Add(captionBox);

        var group = new Border
        {
            Child = groupBody,
            BorderBrush = Brush(200, 200, 200),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(12, 10, 12, 12),
        };

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyDialogButtonChrome(okButton, 84, isDefault: true);
        ApplyDialogButtonChrome(cancelButton, 84);
        okButton.Click += (_, _) => dialog.Close();
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                group,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                    // WPF order: [OK] [Cancel]
                    Children = { okButton, cancelButton },
                },
            },
        };

        dialog.Opened += (_, _) => fieldBox.Focus();
        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Seeds a few conditional-format rules onto the demo sheet (over a range that overlaps the capture
    /// selection, so the manager's default "current selection" scope lists them) before opening the Manage
    /// Conditional Formats dialog — otherwise its rules list renders empty and there is nothing to compare.
    /// </summary>
    private async Task ShowManageConditionalFormatsParityDialogAsync()
    {
        var sheet = _session.Workbook.Sheets.Count > 0 ? _session.Workbook.Sheets[0] : _session.ActiveSheet;
        var ruleRange = ConditionalFormatManageParityFixture.CreateRange(sheet.Id);
        sheet.ConditionalFormats.Clear();
        foreach (var rule in ConditionalFormatManageParityFixture.CreateRules(sheet.Id))
            sheet.ConditionalFormats.Add(rule);
        RefreshShell(_statusText.Text ?? "Ready");

        var previousSheetId = _session.ActiveSheet.Id;
        if (!previousSheetId.Equals(sheet.Id))
            _session.SelectSheet(sheet.Id);
        var previousSelection = _session.SelectedRange;
        _session.SelectRange(ruleRange);
        RefreshShell(_statusText.Text ?? "Ready");

        try
        {
            await ShowManageConditionalFormatsDialogAsync(access => access.AppliesToRow.IsVisible = false);
        }
        finally
        {
            if (!previousSheetId.Equals(_session.ActiveSheet.Id))
                _session.SelectSheet(previousSheetId);
            _session.SelectRange(previousSelection);
            RefreshShell(_statusText.Text ?? "Ready");
        }
    }

    private async Task ShowCustomViewsParityDialogAsync()
    {
        _session.Workbook.CustomViews.Clear();
        // Seed named views so the manager has meaningful rows to compare (mirrors the WPF
        // parity capture, which seeds the same view names).
        _session.Workbook.CustomViews.Add(new WorkbookCustomView("Summary View", []));
        _session.Workbook.CustomViews.Add(new WorkbookCustomView("Detailed View", []));
        await ShowCustomViewsManagerDialogAsync();
    }

    private async Task ShowProtectWorkbookParityDialogAsync()
    {
        // Protect the workbook (with a password) first so the dialog renders the SAME "Unprotect
        // Workbook" variant the WPF host capture hard-codes — otherwise Linux shows the Protect
        // variant and the two captures can't be compared.
        if (!_session.Workbook.IsStructureProtected)
        {
            _session.ExecuteReviewCommand(new ProtectWorkbookCommand("pw"));
            RefreshShell(_statusText.Text ?? "Ready");
        }

        await ShowProtectWorkbookDialogAsync();
    }

    private async Task ShowAllowEditRangesParityDialogAsync()
    {
        var sheetId = _session.ActiveSheet.Id;
        var previousSelection = _session.SelectedRange;
        var existingRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 5, 5));
        if (!_session.ActiveSheet.AllowEditRanges.Contains(existingRange))
        {
            _session.ExecuteReviewCommand(new AllowEditRangeCommand(sheetId, existingRange));
            RefreshShell(_statusText.Text ?? "Ready");
        }

        _session.SelectRange(new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 5, 4)));
        RefreshShell(_statusText.Text ?? "Ready");

        try
        {
            await ShowAllowEditRangeDialogAsync("Sheet1!$B$2:$D$5");
        }
        finally
        {
            _session.SelectRange(previousSelection);
            RefreshShell(_statusText.Text ?? "Ready");
        }
    }

    private async Task ShowWithSelectedParityChartAsync(Func<Task> showDialogAsync)
    {
        var previousKind = _selectedDrawingObjectKind;
        var previousId = _selectedDrawingObjectId;
        var chart = EnsureParityChart();
        if (chart is null)
            return;

        _selectedDrawingObjectKind = SelectionPaneObjectKind.Chart;
        _selectedDrawingObjectId = chart.Id;
        _ribbonContextSource.OnDrawingObjectSelected(SelectionPaneObjectKind.Chart);
        RefreshShell(_statusText.Text ?? "Ready");

        try
        {
            await showDialogAsync();
        }
        finally
        {
            RestoreParityDrawingSelection(previousKind, previousId);
        }
    }

    private async Task ShowWithSelectedParityShapeAsync(Func<Task> showDialogAsync)
    {
        var previousKind = _selectedDrawingObjectKind;
        var previousId = _selectedDrawingObjectId;
        var shape = EnsureParityShape();
        if (shape is null)
            return;

        _selectedDrawingObjectKind = SelectionPaneObjectKind.Shape;
        _selectedDrawingObjectId = shape.Id;
        _ribbonContextSource.OnDrawingObjectSelected(SelectionPaneObjectKind.Shape);
        RefreshShell(_statusText.Text ?? "Ready");

        try
        {
            await showDialogAsync();
        }
        finally
        {
            RestoreParityDrawingSelection(previousKind, previousId);
        }
    }

    private ChartModel? EnsureParityChart()
    {
        var sheet = _session.ActiveSheet;
        if (sheet.Charts.FirstOrDefault(chart => chart.IsVisible) is { } existing)
            return existing;

        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 4));
        var command = new AddChartCommand(
            sheet.Id,
            dataRange,
            ChartType.Column,
            title: "Quarterly Sales",
            left: 260,
            top: 96,
            width: 360,
            height: 240);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
            return sheet.Charts.FirstOrDefault(chart => chart.IsVisible);

        RefreshShell(_statusText.Text ?? "Ready");
        return sheet.Charts.FirstOrDefault(chart => chart.Id == command.ChartId)
            ?? sheet.Charts.FirstOrDefault(chart => chart.IsVisible);
    }

    private DrawingShapeModel? EnsureParityShape()
    {
        var sheet = _session.ActiveSheet;
        if (sheet.DrawingShapes.FirstOrDefault(shape => shape.IsVisible) is { } existing)
            return existing;

        var command = new AddDrawingShapeCommand(
            sheet.Id,
            new CellAddress(sheet.Id, 6, 2),
            DrawingShapeKind.Rectangle,
            width: 150,
            height: 90,
            fillColor: new CellColor(91, 155, 213),
            outlineColor: new CellColor(47, 84, 150));
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
            return sheet.DrawingShapes.FirstOrDefault(shape => shape.IsVisible);

        RefreshShell(_statusText.Text ?? "Ready");
        return sheet.DrawingShapes.FirstOrDefault(shape => shape.Id == command.ShapeId)
            ?? sheet.DrawingShapes.FirstOrDefault(shape => shape.IsVisible);
    }

    private PivotTableModel? EnsureParityPivot()
    {
        var sheet = _session.ActiveSheet;
        if (sheet.PivotTables.FirstOrDefault() is { } existing)
            return existing;

        var sheetId = sheet.Id;
        SeedParityPivotSource(sheet);
        var sourceRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 8, 5));
        var targetRange = new GridRange(
            new CellAddress(sheetId, 2, 7),
            new CellAddress(sheetId, 2, 7));
        var cacheId = _session.Workbook.PivotCaches.Count == 0
            ? 1
            : _session.Workbook.PivotCaches.Max(cache => cache.CacheId) + 1;
        var cache = new PivotCacheModel
        {
            CacheId = cacheId,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = sourceRange.ToString(),
        };
        for (var col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
            cache.Fields.Add(new PivotCacheFieldModel(ParityPivotHeader(sheet, sourceRange.Start.Row, col)));
        _session.Workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "ParityPivot",
            CacheId = cacheId,
            SourceRange = sourceRange,
            TargetRange = targetRange,
            StyleName = PivotStyleGalleryPlanner.DefaultStyleName,
            ShowRowStripes = true,
            LastRenderedRange = new GridRange(
                targetRange.Start,
                new CellAddress(sheetId, targetRange.Start.Row + 4, targetRange.Start.Col + 2)),
        };
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["North", "South"]));
        pivot.DataFields.Add(new PivotDataFieldModel(4, "Sum of Revenue", "sum"));
        sheet.PivotTables.Add(pivot);

        RefreshShell(_statusText.Text ?? "Ready");
        return sheet.PivotTables.FirstOrDefault(item => string.Equals(item.Name, "ParityPivot", StringComparison.Ordinal))
            ?? sheet.PivotTables.FirstOrDefault();
    }

    private static string ParityPivotHeader(Sheet sheet, uint row, uint col) =>
        sheet.GetCell(row, col)?.Value switch
        {
            TextValue { Value.Length: > 0 } text => text.Value,
            NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
            DateTimeValue date => date.Value.ToString(CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            _ => $"Column{col}",
        };

    private static void SeedParityPivotSource(Sheet sheet)
    {
        string[][] rows =
        {
            ["Region", "Product", "Units", "Price", "Revenue"],
            ["North", "Widget", "120", "9.5", "1140"],
            ["South", "Gadget", "85", "14.25", "1211.25"],
            ["East", "Sprocket", "200", "3.75", "750"],
            ["West", "Gizmo", "64", "21", "1344"],
            ["North", "Cog", "310", "1.2", "372"],
            ["South", "Widget", "150", "9.5", "1425"],
            ["East", "Gadget", "95", "14.25", "1353.75"],
        };

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                var text = rows[r][c];
                var value = r > 0 && c >= 2 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? new NumberValue(number)
                    : (ScalarValue)new TextValue(text);
                sheet.SetCell(new CellAddress(sheet.Id, (uint)(r + 1), (uint)(c + 1)), Cell.FromValue(value));
            }
        }
    }

    private void RestoreParityDrawingSelection(SelectionPaneObjectKind? previousKind, Guid? previousId)
    {
        _selectedDrawingObjectKind = previousKind;
        _selectedDrawingObjectId = previousId;
        if (previousKind is { } kind)
            _ribbonContextSource.OnDrawingObjectSelected(kind);
        else
            _ribbonContextSource.OnSelectionCleared();
        RefreshTableContextualTab();
        RefreshPivotContextualTab();
        RefreshShell(_statusText.Text ?? "Ready");
    }

    private async Task ShowWithParitySelectionAsync(
        CellAddress start,
        CellAddress end,
        Func<Task> showDialogAsync)
    {
        var previousSelection = _session.SelectedRange;
        _session.SelectRange(new GridRange(start, end));
        RefreshShell(_statusText.Text ?? "Ready");

        try
        {
            await showDialogAsync();
        }
        finally
        {
            _session.SelectRange(previousSelection);
            RefreshShell(_statusText.Text ?? "Ready");
        }
    }

    private void PrepareSheetTabsOverflowParityCapture()
    {
        while (_session.SheetTabs.Count < 20)
            AddNewSheet();

        _sheetTabsHost.Content = BuildSheetTabs();
        UpdateSheetTabNavigationVisibility();
        RefreshShell("Ready");

        // The WPF capture activates the last inserted sheet and brings it into view, so its overflow
        // surface shows the TAIL of the strip (Sheet12–Sheet20). Avalonia's scroller does not auto-scroll
        // to the active tab on rebuild, so without this it would show the HEAD (Demo–Sheet10). Force the
        // scroller to its end after a layout pass so both shells frame the same overflowed sheet range.
        LayoutWindow();
        ScrollSheetTabsToEndForParityCapture();
        LayoutWindow();
    }

    private void ScrollSheetTabsToEndForParityCapture()
    {
        var maxOffsetX = Math.Max(0, _sheetTabsScroller.Extent.Width - _sheetTabsScroller.Viewport.Width);
        _sheetTabsScroller.Offset = new Vector(maxOffsetX, _sheetTabsScroller.Offset.Y);
    }

    private static (string SurfaceId, string TabId)[] BuildStaticRibbonTabSurfaces()
    {
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        return definition.VisibleTabs
            // The File tab is a backstage trigger, not a renderable ribbon surface: selecting it
            // opens the backstage and the renderer bounces the strip back to the last content tab,
            // so "tab.File" could only ever photograph whichever tab was showing before. The
            // backstage has its own backstage.* surfaces.
            .Where(tab => !string.Equals(tab.Id, global::FreeX.Ribbon.Definitions.FreeXRibbonTabIds.File, StringComparison.Ordinal))
            .Select(tab => ("tab." + SurfaceName(tab), tab.Id))
            .ToArray();
    }

    private static (string SurfaceId, string TabId, string ActivationKey)[] BuildContextualRibbonTabSurfaces()
    {
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        return definition.ContextualTabs
            .Where(tab => tab.Context is not null)
            .Select(tab => ("contextual." + SurfaceName(tab), tab.Id, tab.Context!.ActivationKey))
            .ToArray();
    }

    private static string SurfaceName(RibbonTab tab) =>
        tab.Id.EndsWith("Tab", StringComparison.Ordinal)
            ? tab.Id[..^3]
            : tab.Id;

    /// <summary>
    /// Closes the backstage before rendering a ribbon tab.
    /// </summary>
    /// <remarks>
    /// Selecting the File tab opens the backstage: <c>AvaloniaRibbonRenderer</c> invokes
    /// <c>onFileTabSelected</c> from its SelectionChanged handler and then restores the previous
    /// content tab. The overlay stays open, and it is a sibling of the shell DockPanel covering the
    /// whole client area -- so every ribbon capture taken after <c>tab.File</c>, which is first in
    /// the loop, photographed the backstage instead of the ribbon. That is why sixteen of the
    /// seventeen ribbon PNGs came back byte-identical.
    /// </remarks>
    private void CloseBackstageBeforeRibbonCapture()
    {
        var closedAny = false;
        foreach (var backstage in global::Avalonia.VisualTree.VisualExtensions
                     .GetVisualDescendants(this)
                     .OfType<global::Free.Shared.Shell.Avalonia.AvaloniaBackstageFrame>())
        {
            if (!backstage.IsVisible)
                continue;

            backstage.Hide();
            closedAny = true;
        }

        if (closedAny)
            LayoutWindow();
    }
    private ParitySurfaceResult CaptureRibbonTab(
        string outputDirectory,
        TabControl? ribbonTabControl,
        string surfaceId,
        string tabId,
        ParitySurfaceKind kind)
    {
        if (ribbonTabControl is null)
            return new ParitySurfaceResult(surfaceId, kind, surfaceId + ".png", Captured: false, "Ribbon tab control not found in the shell visual tree.");

        if (!SelectParityRibbonTab(ribbonTabControl, tabId))
            return new ParitySurfaceResult(surfaceId, kind, surfaceId + ".png", Captured: false, $"Ribbon tab '{tabId}' is not present in the strip.");

        CloseBackstageBeforeRibbonCapture();
        return CaptureWindowSurface(outputDirectory, surfaceId, kind);
    }

    private ParitySurfaceResult CaptureNameBoxDropdownSurface(string outputDirectory)
    {
        const string surfaceId = "popup.nameBoxDropdown";
        const string pngName = surfaceId + ".png";
        const string provenance = "managed-popup-diagnostic";

        try
        {
            var stalePngPath = Path.Combine(outputDirectory, pngName);
            if (File.Exists(stalePngPath))
                File.Delete(stalePngPath);

            SeedNameBoxDropdownParityFixture();
            ShowCellAddressAutocompletePopup();
            LayoutWindow();

            if (_cellAddressAutocompletePopup?.Child is not Visual)
                throw new InvalidOperationException("The Avalonia Name Box popup did not expose its production child.");

            return new ParitySurfaceResult(
                surfaceId,
                ParitySurfaceKind.Overlay,
                pngName,
                Captured: false,
                "Managed popup opening is diagnostic only. Authoritative Avalonia parity requires the live native X11 popup crop from the name-box-dropdown-parity physical selector.",
                EvidenceProvenance: provenance);
        }
        catch (Exception ex)
        {
            return new ParitySurfaceResult(
                surfaceId,
                ParitySurfaceKind.Overlay,
                pngName,
                Captured: false,
                $"{ex.GetType().Name}: {ex.Message}",
                EvidenceProvenance: provenance);
        }
        finally
        {
            if (_cellAddressAutocompletePopup is { } popup)
                popup.IsOpen = false;
            LayoutWindow();
        }
    }

    /// <summary>
    /// Seeds the same five Name Box entries used by the WPF screenshot tour. This is capture-only data and
    /// deliberately has different ids from the Wave68 physical-selection fixture so the two contracts cannot
    /// accidentally change one another's object-selection behavior.
    /// </summary>
    private void SeedNameBoxDropdownParityFixture()
    {
        var sheet = _session.ActiveSheet;
        const string salesName = "Sales";
        foreach (var name in _session.Workbook.NamedRanges.Keys.ToArray())
            _session.Workbook.RemoveNamedRange(name);
        foreach (var scopedName in _session.Workbook.ScopedNamedRanges.Keys.ToArray())
            _session.Workbook.RemoveScopedNamedRange(scopedName.Name, scopedName.Sheet);
        foreach (var workbookSheet in _session.Workbook.Sheets)
        {
            workbookSheet.StructuredTables.Clear();
            workbookSheet.DrawingShapes.Clear();
            workbookSheet.Pictures.Clear();
            workbookSheet.TextBoxes.Clear();
            workbookSheet.Charts.Clear();
        }

        _session.Workbook.NamedRanges[salesName] = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));

        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Id = Guid.Parse("68000000-0000-0000-0000-000000000001"),
            Name = "Tour Name Box Shape",
            Anchor = new CellAddress(sheet.Id, 22, 8),
            Width = 96,
            Height = 48,
            IsVisible = true,
        });
        sheet.Pictures.Add(new PictureModel
        {
            Id = Guid.Parse("68000000-0000-0000-0000-000000000002"),
            Name = "Tour Name Box Picture",
            Anchor = new CellAddress(sheet.Id, 23, 8),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3, 4],
            ContentType = "image/png",
            Width = 96,
            Height = 48,
            IsVisible = true,
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Id = Guid.Parse("68000000-0000-0000-0000-000000000003"),
            Name = "Tour Name Box Text Box",
            Anchor = new CellAddress(sheet.Id, 24, 8),
            Text = "Tour Name Box text box",
            Width = 120,
            Height = 48,
            IsVisible = true,
        });
        sheet.Charts.Add(new ChartModel
        {
            Id = Guid.Parse("68000000-0000-0000-0000-000000000004"),
            Name = "Tour Name Box Chart",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 25, 8),
                new CellAddress(sheet.Id, 26, 9)),
            IsVisible = true,
        });

        _session.SelectRange(new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3)));
        RefreshShell(_statusText.Text ?? "Ready");
    }

    /// <summary>Renders the whole shell window to <c>&lt;surfaceId&gt;.png</c>.</summary>
    private ParitySurfaceResult CaptureWindowSurface(string outputDirectory, string surfaceId, ParitySurfaceKind kind)
    {
        var pngName = surfaceId + ".png";
        try
        {
            RenderWindowWithCapturedTitleBarToPng(this, ParityCaptureWindowWidth, ParityCaptureWindowHeight, Path.Combine(outputDirectory, pngName));
            return ParityCaptureOutputGuard.ResultForPng(surfaceId, kind, outputDirectory, pngName);
        }
        catch (Exception ex)
        {
            return new ParitySurfaceResult(surfaceId, kind, pngName, Captured: false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static ParitySurfaceResult CaptureBackstageSurface(
        string outputDirectory,
        FreeXBackstageCaptureSurfacePlan capture)
    {
        var pngName = capture.PngFileName;
        try
        {
            RenderVisualToPng(
                CreateParityCapturedBackstageSurface(capture.SurfaceId),
                (int)capture.Width,
                (int)capture.Height,
                Path.Combine(outputDirectory, pngName));
            return ParityCaptureOutputGuard.ResultForPng(capture.SurfaceId, ParitySurfaceKind.Backstage, outputDirectory, pngName);
        }
        catch (Exception ex)
        {
            return new ParitySurfaceResult(capture.SurfaceId, ParitySurfaceKind.Backstage, pngName, Captured: false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens a modal surface via <paramref name="opener"/> (fire-and-forget, since it blocks on
    /// <c>ShowDialog</c>), polls this window's owned-window list for the freshly opened dialog, renders it, then
    /// closes it so the opener's task completes. The actual content of the dialog (its fields) does not need to
    /// be inspected — the renderer captures whatever the shell laid out.
    /// </summary>
    private async Task<ParitySurfaceResult> CaptureModalSurfaceAsync(
        string outputDirectory,
        string surfaceId,
        ParitySurfaceKind kind,
        Func<Task> opener,
        bool render = true)
    {
        var pngName = surfaceId + ".png";
        if (!render)
            AppendInteractionDialogProgress(outputDirectory, surfaceId, "starting");
        var ownerFocusBeforeOpen = PrepareOwnerFocusForDialogContract();
        var preexisting = OwnedWindows.ToHashSet();

        Task openerTask;
        try
        {
            // Fire-and-forget: schedule the modal opener after this method starts polling for its owned window.
            openerTask = RunParityModalOpenerAsync(opener, suppressInspectionAutoClose: !render);
        }
        catch (Exception ex)
        {
            return new ParitySurfaceResult(surfaceId, kind, pngName, Captured: false, $"Opener threw: {ex.GetType().Name}: {ex.Message}");
        }

        var dialog = await WaitForOwnedDialogAsync(preexisting);
        if (!render)
            AppendInteractionDialogProgress(outputDirectory, surfaceId, dialog is null ? "not-opened" : "opened");
        if (dialog is null)
        {
            // The opener may have early-returned (e.g. a guard) without showing a window; record honestly.
            await AwaitOpenerQuietlyAsync(openerTask);
            RecordDialogInteractionOpenFailure(surfaceId, "Dialog window did not open within the wait window.");
            return new ParitySurfaceResult(surfaceId, kind, pngName, Captured: false, "Dialog window did not open within the wait window (guard or unavailable surface).");
        }

        ParitySurfaceResult result;
        try
        {
            if (!render)
            {
                AppendInteractionDialogProgress(outputDirectory, surfaceId, "ready-for-contract");
                result = new ParitySurfaceResult(
                    surfaceId,
                    kind,
                    "",
                    Captured: true,
                    "Opened through the production route for keyboard and focus validation; visual capture is a separate parity lane.");
                return result;
            }

            if (TryGetFixedModalCaptureSize(surfaceId, out var fixedWidth, out var fixedHeight))
            {
                dialog.SizeToContent = SizeToContent.Manual;
                dialog.Width = fixedWidth;
                dialog.Height = fixedHeight;
                dialog.MinWidth = fixedWidth;
                dialog.MinHeight = fixedHeight;
                try { dialog.UpdateLayout(); } catch { /* best-effort */ }
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            }

            var width = fixedWidth > 0
                ? (int)Math.Ceiling(fixedWidth)
                : (int)Math.Ceiling(dialog.Bounds.Width > 0 ? dialog.Bounds.Width : dialog.Width);
            var height = fixedHeight > 0
                ? (int)Math.Ceiling(fixedHeight)
                : (int)Math.Ceiling(dialog.Bounds.Height > 0 ? dialog.Bounds.Height : dialog.Height);
            RenderVisualToPng(dialog, width, height, Path.Combine(outputDirectory, pngName));
            result = ParityCaptureOutputGuard.ResultForPng(surfaceId, kind, outputDirectory, pngName);
        }
        catch (Exception ex)
        {
            result = new ParitySurfaceResult(surfaceId, kind, pngName, Captured: false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (!render)
            {
                AppendInteractionDialogProgress(outputDirectory, surfaceId, "contract-starting");
                try
                {
                    await RecordDialogInteractionContractAsync(
                        surfaceId,
                        dialog,
                        ownerFocusBeforeOpen,
                        openerTask,
                        opener);
                }
                catch (Exception ex)
                {
                    RecordDialogInteractionOpenFailure(surfaceId, $"Contract probe threw: {ex.GetType().Name}: {ex.Message}");
                }
                AppendInteractionDialogProgress(outputDirectory, surfaceId, "contract-complete");
            }
            try { if (dialog.IsVisible) dialog.Close(); } catch { /* closing best-effort */ }
            await AwaitOpenerQuietlyAsync(openerTask);
        }

        return result;
    }

    private static void AppendInteractionDialogProgress(
        string outputDirectory,
        string surfaceId,
        string stage)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            File.AppendAllText(
                Path.Combine(outputDirectory, "dialog-progress.log"),
                $"{DateTimeOffset.UtcNow:O}\t{surfaceId}\t{stage}{Environment.NewLine}");
        }
        catch
        {
            // Progress diagnostics must never alter validation behavior.
        }
    }

    private static bool TryGetFixedModalCaptureSize(string surfaceId, out double width, out double height)
    {
        (width, height) = surfaceId switch
        {
            "dialog.ExportOptions" => (ExportOptionsDialogSurfacePlanner.CaptureWidth, ExportOptionsDialogSurfacePlanner.CaptureHeight),
            "dialog.ProtectWorkbook" => (ProtectionDialogPlanner.ProtectWorkbookCaptureWidth, ProtectionDialogPlanner.ProtectWorkbookCaptureHeight),
            "dialog.Sparkline" => (SparklinePlanner.InsertDialogCaptureWidth, SparklinePlanner.InsertDialogCaptureHeight),
            _ => (0, 0)
        };

        return width > 0 && height > 0;
    }

    /// <summary>
    /// Like <see cref="CaptureModalSurfaceAsync"/>, but for a multi-tab / multi-category dialog: opens the
    /// dialog once, renders the default surface as <c>&lt;surfaceId&gt;.png</c>, then for each tab index sets
    /// the relevant <see cref="TabControl"/>'s <c>SelectedIndex</c> (or invokes the category-list selector
    /// for dialogs whose categories are not a <see cref="TabControl"/>, e.g. Options), pumps a layout pass so
    /// the swapped pane re-renders, and writes <c>&lt;surfaceId&gt;.&lt;tabName&gt;.png</c>. The dialog is
    /// closed once at the end. Returns one <see cref="ParitySurfaceResult"/> per emitted PNG.
    /// </summary>
    private async Task<IReadOnlyList<ParitySurfaceResult>> CaptureModalTabsAsync(
        string outputDirectory,
        string surfaceId,
        ParitySurfaceKind kind,
        Func<Task> opener,
        string[] tabNames,
        bool render = true)
    {
        var results = new List<ParitySurfaceResult>();
        var defaultPng = surfaceId + ".png";
        var ownerFocusBeforeOpen = PrepareOwnerFocusForDialogContract();
        var preexisting = OwnedWindows.ToHashSet();

        Task openerTask;
        try
        {
            openerTask = RunParityModalOpenerAsync(opener, suppressInspectionAutoClose: !render);
        }
        catch (Exception ex)
        {
            results.Add(new ParitySurfaceResult(surfaceId, kind, defaultPng, Captured: false, $"Opener threw: {ex.GetType().Name}: {ex.Message}"));
            return results;
        }

        var dialog = await WaitForOwnedDialogAsync(preexisting);
        if (dialog is null)
        {
            await AwaitOpenerQuietlyAsync(openerTask);
            RecordDialogInteractionOpenFailure(surfaceId, "Dialog window did not open within the wait window.");
            results.Add(new ParitySurfaceResult(surfaceId, kind, defaultPng, Captured: false, "Dialog window did not open within the wait window (guard or unavailable surface)."));
            return results;
        }

        try
        {
            if (!render)
            {
                results.Add(new ParitySurfaceResult(
                    surfaceId,
                    kind,
                    "",
                    Captured: true,
                    "Opened through the production route for keyboard and focus validation; tab rendering is a separate parity lane."));
                return results;
            }

            // Default surface first (whatever tab the dialog opens on). Pump once before
            // rendering so SizeToContent tab dialogs settle to the same frame the tab
            // captures use after changing SelectedIndex.
            await WaitForParityDialogLayoutAsync(dialog);
            results.Add(RenderParityDialogTab(outputDirectory, dialog, surfaceId, defaultPng, kind));

            // A real TabControl drives selection via SelectedIndex; the Options dialog instead carries an
            // Action<int> category selector on the category list's Tag (its categories are Border rows in a
            // StackPanel, not a TabControl). Prefer the TabControl when present.
            var tabControl = dialog.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
            var categorySelector = tabControl is null ? FindParityCategorySelector(dialog) : null;

            for (var i = 0; i < tabNames.Length; i++)
            {
                var pngName = $"{surfaceId}.{tabNames[i]}.png";
                if (tabControl is not null)
                {
                    if (i >= tabControl.ItemCount)
                    {
                        results.Add(new ParitySurfaceResult($"{surfaceId}.{tabNames[i]}", kind, pngName, Captured: false, $"Tab index {i} is out of range (dialog has {tabControl.ItemCount} tabs)."));
                        continue;
                    }
                    tabControl.SelectedIndex = i;
                }
                else if (categorySelector is not null)
                {
                    categorySelector(i);
                }
                else
                {
                    results.Add(new ParitySurfaceResult($"{surfaceId}.{tabNames[i]}", kind, pngName, Captured: false, "No TabControl or category selector found in the dialog visual tree."));
                    continue;
                }

                // Pump a layout pass so the newly-selected tab's pane is measured/arranged before render.
                await WaitForParityDialogLayoutAsync(dialog);

                results.Add(RenderParityDialogTab(outputDirectory, dialog, $"{surfaceId}.{tabNames[i]}", pngName, kind));
            }
        }
        finally
        {
            if (!render)
            {
                try
                {
                    await RecordDialogInteractionContractAsync(
                        surfaceId,
                        dialog,
                        ownerFocusBeforeOpen,
                        openerTask,
                        opener);
                }
                catch (Exception ex)
                {
                    RecordDialogInteractionOpenFailure(surfaceId, $"Contract probe threw: {ex.GetType().Name}: {ex.Message}");
                }
            }
            try { if (dialog.IsVisible) dialog.Close(); } catch { /* closing best-effort */ }
            await AwaitOpenerQuietlyAsync(openerTask);
        }

        return results;
    }

    private static ParitySurfaceResult RenderParityDialogTab(
        string outputDirectory, Window dialog, string surfaceId, string pngName, ParitySurfaceKind kind)
    {
        try
        {
            var captureSize = ResolveParityDialogCaptureSize(
                dialog.Width,
                dialog.Height,
                dialog.MinWidth,
                dialog.MinHeight,
                dialog.Bounds.Size);
            var width = Math.Max(1, (int)Math.Ceiling(captureSize.Width));
            var height = Math.Max(1, (int)Math.Ceiling(captureSize.Height));
            RenderVisualToPng(dialog, width, height, Path.Combine(outputDirectory, pngName));
            return ParityCaptureOutputGuard.ResultForPng(surfaceId, kind, outputDirectory, pngName);
        }
        catch (Exception ex)
        {
            return new ParitySurfaceResult(surfaceId, kind, pngName, Captured: false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task WaitForParityDialogLayoutAsync(Window dialog)
    {
        const int resizePollCount = 10;
        for (var attempt = 0; attempt < resizePollCount; attempt++)
        {
            await Task.Delay(ParityCaptureDialogPollMilliseconds);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            try { dialog.UpdateLayout(); } catch { /* best-effort */ }
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

            if (ParityDialogLayoutMatchesRequest(
                    dialog.Width,
                    dialog.Height,
                    dialog.MinWidth,
                    dialog.MinHeight,
                    dialog.Bounds.Size))
                return;
        }
    }

    internal static Size ResolveParityDialogCaptureSize(
        double requestedWidth,
        double requestedHeight,
        double minimumWidth,
        double minimumHeight,
        Size arrangedSize)
    {
        if (!ParityDialogLayoutMatchesRequest(
                requestedWidth,
                requestedHeight,
                minimumWidth,
                minimumHeight,
                arrangedSize))
        {
            throw new InvalidOperationException(
                $"Dialog layout is {arrangedSize.Width:0.##}x{arrangedSize.Height:0.##}, " +
                $"but requested {requestedWidth:0.##}x{requestedHeight:0.##}; refusing to pad an undersized capture.");
        }

        return arrangedSize;
    }

    private static bool ParityDialogLayoutMatchesRequest(
        double requestedWidth,
        double requestedHeight,
        double minimumWidth,
        double minimumHeight,
        Size arrangedSize)
    {
        const double layoutTolerance = 1;
        if (arrangedSize.Width <= 0 || arrangedSize.Height <= 0)
            return false;

        return arrangedSize.Width + layoutTolerance >= minimumWidth
            && arrangedSize.Height + layoutTolerance >= minimumHeight
            && (!double.IsFinite(requestedWidth)
                || requestedWidth <= 0
                || Math.Abs(requestedWidth - arrangedSize.Width) <= layoutTolerance)
            && (!double.IsFinite(requestedHeight)
                || requestedHeight <= 0
                || Math.Abs(requestedHeight - arrangedSize.Height) <= layoutTolerance);
    }

    /// <summary>
    /// Returns the Options-style category selector — an <c>Action&lt;int&gt;</c> stashed on the category list's
    /// <c>Tag</c> by the Options dialog — so the capture can switch left-list categories that are not backed by
    /// a <see cref="TabControl"/>. Returns <c>null</c> when no such selector is present.
    /// </summary>
    private static Action<int>? FindParityCategorySelector(Window dialog) =>
        dialog.GetVisualDescendants()
            .OfType<Control>()
            .Select(control => control.Tag as Action<int>)
            .FirstOrDefault(selector => selector is not null);

    private static Task RunParityModalOpenerAsync(
        Func<Task> opener,
        bool suppressInspectionAutoClose = false)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(
            async () =>
            {
                var previousSuppression = SuppressDialogInspectionAutoClose;
                SuppressDialogInspectionAutoClose = suppressInspectionAutoClose;
                try
                {
                    await opener();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
                finally
                {
                    SuppressDialogInspectionAutoClose = previousSuppression;
                }
            },
            DispatcherPriority.Background);
        return completion.Task;
    }

    private async Task<Window?> WaitForOwnedDialogAsync(HashSet<Window> preexisting)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(ParityCaptureDialogWaitMilliseconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var candidate = OwnedWindows.FirstOrDefault(w => !preexisting.Contains(w));
            if (candidate is not null && candidate.IsVisible && candidate.Bounds.Width > 0 && candidate.Bounds.Height > 0)
                return candidate;
            await Task.Delay(ParityCaptureDialogPollMilliseconds);
        }
        // Last chance: a window may be owned but not yet reporting bounds; take it anyway if present.
        return OwnedWindows.FirstOrDefault(w => !preexisting.Contains(w));
    }

    private static async Task AwaitOpenerQuietlyAsync(Task openerTask)
    {
        try
        {
            // Give the now-closed dialog's ShowDialog continuation a moment to unwind.
            var completed = await Task.WhenAny(openerTask, Task.Delay(1000));
            if (completed == openerTask)
                await openerTask;
        }
        catch
        {
            // The opener's post-dialog work (applying a result, etc.) is irrelevant to capture; swallow.
        }
    }

    /// <summary>Locates the ribbon's <see cref="TabControl"/> — the top-docked strip whose items carry tab-id tags.</summary>
    private TabControl? FindParityRibbonTabControl()
    {
        foreach (var tabControl in this.GetVisualDescendants().OfType<TabControl>())
        {
            if (tabControl.Items.OfType<TabItem>().Any(item => item.Tag is string tag && tag.EndsWith("Tab", StringComparison.Ordinal)))
                return tabControl;
        }
        return null;
    }

    private bool SelectParityRibbonTab(TabControl? ribbonTabControl, string tabId)
    {
        if (ribbonTabControl is null)
            return false;

        for (var i = 0; i < ribbonTabControl.Items.Count; i++)
        {
            if (ribbonTabControl.Items[i] is TabItem item && item.Tag is string tag && string.Equals(tag, tabId, StringComparison.Ordinal))
            {
                ribbonTabControl.SelectedIndex = i;
                LayoutWindow();
                return true;
            }
        }
        return false;
    }

    /// <summary>Forces a synchronous layout pass so a just-changed tab / selection is reflected before render.</summary>
    private void LayoutWindow()
    {
        Measure(new Size(ParityCaptureWindowWidth, ParityCaptureWindowHeight));
        Arrange(new Rect(0, 0, ParityCaptureWindowWidth, ParityCaptureWindowHeight));
        UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
    }

    private static void RenderWindowWithCapturedTitleBarToPng(MainWindow window, int width, int height, string path)
    {
        var pixelWidth = Math.Max(1, width);
        var pixelHeight = Math.Max(1, height);

        // The shared frame is part of the client visual tree, including its real QAT and title text.
        // Capture that frame directly so reports do not prepend a second synthetic title bar.
        using var bitmap = RenderWindowClientContentToBitmap(window, pixelWidth, pixelHeight);
        bitmap.Save(path);
    }

    private static RenderTargetBitmap RenderWindowClientContentToBitmap(MainWindow window, int width, int height)
    {
        var originalWidth = window.Width;
        var originalHeight = window.Height;

        try
        {
            window.Width = width;
            window.Height = height;
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

            var contentVisual = window.Content as Visual ?? window;
            return RenderVisualToBitmap(contentVisual, width, height);
        }
        finally
        {
            window.Width = originalWidth;
            window.Height = originalHeight;
            window.Measure(new Size(ParityCaptureWindowWidth, ParityCaptureWindowHeight));
            window.Arrange(new Rect(0, 0, ParityCaptureWindowWidth, ParityCaptureWindowHeight));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
        }
    }

    private static Control CreateParityCapturedTitleBar(string title)
    {
        var root = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(23, 50, 77)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 36, 58)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 1),
        };

        var dock = new DockPanel { LastChildFill = true };
        root.Child = dock;

        var systemButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
        };
        DockPanel.SetDock(systemButtons, Dock.Right);
        systemButtons.Children.Add(CreateParityCapturedTitleBarButton(RibbonCommandIconKind.WindowMinimize));
        systemButtons.Children.Add(CreateParityCapturedTitleBarButton(RibbonCommandIconKind.WindowMaximize));
        systemButtons.Children.Add(CreateParityCapturedTitleBarButton(RibbonCommandIconKind.WindowClose));
        dock.Children.Add(systemButtons);

        var appIcon = CreateParityCapturedAppIcon();
        DockPanel.SetDock(appIcon, Dock.Left);
        dock.Children.Add(appIcon);

        var qat = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        qat.Children.Add(CreateParityCapturedQatButton(RibbonCommandIconKind.Save, width: 26, iconSize: 16, isEnabled: true));
        qat.Children.Add(CreateParityCapturedQatButton(RibbonCommandIconKind.Undo, width: 24, iconSize: 16, isEnabled: false));
        qat.Children.Add(CreateParityCapturedQatButton(RibbonCommandIconKind.ChevronDown, width: 12, iconSize: 9, isEnabled: false));
        qat.Children.Add(CreateParityCapturedQatButton(RibbonCommandIconKind.Redo, width: 24, iconSize: 16, isEnabled: false));
        qat.Children.Add(CreateParityCapturedQatButton(RibbonCommandIconKind.ChevronDown, width: 12, iconSize: 9, isEnabled: false));
        DockPanel.SetDock(qat, Dock.Left);
        dock.Children.Add(qat);

        dock.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontFamily = ParityNarrowUiFontFamily,
            FontWeight = FontWeight.Normal,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        });

        return root;
    }

    private static string FormatParityCapturedWindowTitle(string title)
    {
        const string oldPrefix = "FreeX - ";
        return title.StartsWith(oldPrefix, StringComparison.Ordinal)
            ? title[oldPrefix.Length..] + " - FreeX"
            : title;
    }

    private static Control CreateParityCapturedAppIcon()
    {
        if (TryCreateParityCapturedAppIconFromResource() is { } resourceIcon)
            return resourceIcon;

        return CreateParityCapturedFallbackAppIcon();
    }

    private static Control? TryCreateParityCapturedAppIconFromResource()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "FreeX.ico");
        if (!File.Exists(iconPath))
            return null;

        try
        {
            var bitmap = TryDecodeParityCapturedIcoPngFrame(iconPath, desiredSize: 48)
                ?? DecodeParityCapturedIco(iconPath);
            return new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2, 0, 8, 0),
                Child = new Viewbox
                {
                    Width = 22,
                    Height = 22,
                    Stretch = Stretch.Uniform,
                    Child = new Image
                    {
                        Source = bitmap,
                        Width = 20,
                        Height = 20,
                        Stretch = Stretch.Uniform,
                    },
                },
            };
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? DecodeParityCapturedIco(string iconPath)
    {
        using var stream = File.OpenRead(iconPath);
        return Bitmap.DecodeToWidth(stream, 22);
    }

    private static Bitmap? TryDecodeParityCapturedIcoPngFrame(string iconPath, int desiredSize)
    {
        var bytes = File.ReadAllBytes(iconPath);
        if (bytes.Length < 6 || BitConverter.ToUInt16(bytes, 0) != 0 || BitConverter.ToUInt16(bytes, 2) != 1)
            return null;

        var count = BitConverter.ToUInt16(bytes, 4);
        var bestOffset = 0;
        var bestSize = 0;
        var bestDelta = int.MaxValue;
        for (var index = 0; index < count; index++)
        {
            var entryOffset = 6 + index * 16;
            if (entryOffset + 16 > bytes.Length)
                return null;

            var width = bytes[entryOffset] == 0 ? 256 : bytes[entryOffset];
            var height = bytes[entryOffset + 1] == 0 ? 256 : bytes[entryOffset + 1];
            var imageSize = (int)BitConverter.ToUInt32(bytes, entryOffset + 8);
            var imageOffset = (int)BitConverter.ToUInt32(bytes, entryOffset + 12);
            if (width != height || imageSize <= 8 || imageOffset < 0 || imageOffset + imageSize > bytes.Length)
                continue;
            if (!IsPngSignature(bytes, imageOffset))
                continue;

            var delta = Math.Abs(width - desiredSize);
            if (delta >= bestDelta)
                continue;

            bestDelta = delta;
            bestOffset = imageOffset;
            bestSize = imageSize;
            if (delta == 0)
                break;
        }

        if (bestSize == 0)
            return null;

        var frame = new byte[bestSize];
        Array.Copy(bytes, bestOffset, frame, 0, bestSize);
        using var stream = new MemoryStream(frame);
        return new Bitmap(stream);
    }

    private static bool IsPngSignature(byte[] bytes, int offset) =>
        offset + 8 <= bytes.Length
        && bytes[offset] == 0x89
        && bytes[offset + 1] == 0x50
        && bytes[offset + 2] == 0x4E
        && bytes[offset + 3] == 0x47
        && bytes[offset + 4] == 0x0D
        && bytes[offset + 5] == 0x0A
        && bytes[offset + 6] == 0x1A
        && bytes[offset + 7] == 0x0A;

    private static Control CreateParityCapturedFallbackAppIcon()
    {
        var iconCanvas = new Canvas
        {
            Width = 20,
            Height = 20,
        };

        iconCanvas.Children.Add(new global::Avalonia.Controls.Shapes.Rectangle
        {
            Width = 20,
            Height = 20,
            Fill = new SolidColorBrush(Color.FromRgb(23, 50, 77)),
            Stroke = new SolidColorBrush(Color.FromRgb(222, 244, 249)),
            StrokeThickness = 1,
        });
        iconCanvas.Children.Add(new global::Avalonia.Controls.Shapes.Rectangle
        {
            Width = 16,
            Height = 5,
            Fill = new SolidColorBrush(Color.FromRgb(15, 126, 155)),
        });
        Canvas.SetLeft(iconCanvas.Children[^1], 2);
        Canvas.SetTop(iconCanvas.Children[^1], 2);

        var f = CreateParityCapturedAppIconText("F", Brushes.White, new Thickness(0), zIndex: 1);
        f.FontSize = 10.5;
        f.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
        f.VerticalAlignment = AvaloniaVerticalAlignment.Top;
        iconCanvas.Children.Add(f);
        Canvas.SetLeft(f, 3);
        Canvas.SetTop(f, 3);

        var x = CreateParityCapturedAppIconText("X", Brushes.White, new Thickness(0), zIndex: 1);
        x.FontSize = 12.5;
        x.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
        x.VerticalAlignment = AvaloniaVerticalAlignment.Top;
        iconCanvas.Children.Add(x);
        Canvas.SetLeft(x, 9);
        Canvas.SetTop(x, 6);

        return new Border
        {
            Width = 20,
            Height = 20,
            Margin = new Thickness(0, 0, 8, 0),
            Child = iconCanvas,
        };
    }

    private static TextBlock CreateParityCapturedAppIconText(string text, IBrush foreground, Thickness margin, int zIndex)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 14.5,
            FontFamily = new FontFamily("Segoe UI, Arial, Liberation Sans, sans-serif"),
            FontWeight = FontWeight.Bold,
            Foreground = foreground,
            Margin = margin,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        block.ZIndex = zIndex;
        return block;
    }

    private static Control CreateParityCapturedQatButton(RibbonCommandIconKind kind, double width, double iconSize, bool isEnabled) =>
        new Border
        {
            Width = width,
            Height = 22,
            Opacity = isEnabled ? 1.0 : 0.42,
            Child = AvaloniaRibbonIcons.Build(new RibbonCommandIcon(kind), iconSize, Brushes.White),
        };

    private static Control CreateParityCapturedSaveQatButton()
    {
        var glyph = new AvaloniaGrid
        {
            Width = 14,
            Height = 14,
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(4) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
        };
        AddGridChild(glyph, new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(1, 1, 0, 0),
        }, 0, 0);
        AddGridChild(glyph, new Border
        {
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(1),
            Margin = new Thickness(0, 3, 0, 0),
        }, 0, 0);
        AvaloniaGrid.SetRowSpan(glyph.Children[^1], 2);
        AddGridChild(glyph, new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(23, 50, 77)),
            Height = 4,
            Margin = new Thickness(3, 7, 3, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
        }, 1, 0);

        return new Border
        {
            Width = 26,
            Height = 22,
            Child = glyph,
        };
    }

    private static Control CreateParityCapturedTitleBarButton(RibbonCommandIconKind kind) =>
        new Border
        {
            Width = 46,
            Height = 28,
            Child = AvaloniaRibbonIcons.Build(new RibbonCommandIcon(kind), 18, Brushes.White),
        };

    private static Control CreateParityCapturedBackstageSurface(string surfaceId)
    {
        var pane = surfaceId switch
        {
            { } id when id.EndsWith(".Info", StringComparison.Ordinal) => "Info",
            { } id when id.EndsWith(".Export", StringComparison.Ordinal) => "Export",
            { } id when id.EndsWith(".Account", StringComparison.Ordinal) => "Account",
            _ => "Home",
        };

        var root = new AvaloniaGrid
        {
            Width = ParityCaptureWindowWidth,
            Height = ParityCaptureWindowHeight,
            Background = Brushes.White,
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(ParityCaptureTitleBarHeight) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = new GridLength(28) },
            },
        };

        AddGridChild(root, CreateParityCapturedTitleBar("Parity Demo - FreeX"), 0, 0);

        var body = new AvaloniaGrid
        {
            Background = Brushes.White,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(190) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        AddGridChild(root, body, 1, 0);

        AddGridChild(body, CreateParityCapturedBackstageRail(pane), 0, 0);

        var content = string.Equals(pane, "Info", StringComparison.Ordinal)
            ? CreateParityCapturedBackstageInfoPane()
            : string.Equals(pane, "Account", StringComparison.Ordinal)
                ? CreateParityCapturedBackstageAccountPane()
                : CreateParityCapturedBackstageHomePane();
        AddGridChild(body, CreateParityCapturedBackstageContentScroll(content), 0, 1);
        AddGridChild(root, CreateParityCapturedStatusBarFooter(), 2, 0);
        ApplyParityBackstageTypography(root);
        return root;
    }

    private static Control CreateParityCapturedStatusBarFooter()
    {
        var grid = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        AddGridChild(grid, new TextBlock
        {
            Text = "Ready",
            FontSize = 12,
            Foreground = Brushes.White,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        }, 0, 0);

        var viewButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 24,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        viewButtons.Children.Add(CreateParityCapturedStatusBarIconButton(RibbonCommandIconKind.Grid, isChecked: true));
        viewButtons.Children.Add(CreateParityCapturedStatusBarIconButton(RibbonCommandIconKind.Page, isChecked: false));
        viewButtons.Children.Add(CreateParityCapturedStatusBarIconButton(RibbonCommandIconKind.PageBreak, isChecked: false));

        var zoomPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 24,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        zoomPanel.Children.Add(CreateParityCapturedStatusBarZoomText("-"));
        zoomPanel.Children.Add(CreateParityCapturedStatusZoomSlider());
        zoomPanel.Children.Add(CreateParityCapturedStatusBarZoomText("+"));
        zoomPanel.Children.Add(new TextBlock
        {
            Text = "100%",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            Width = 44,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        });

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        right.Children.Add(viewButtons);
        right.Children.Add(zoomPanel);
        AddGridChild(grid, right, 0, 2);

        return new Border
        {
            Background = Brush(23, 50, 77),
            BorderThickness = new Thickness(0),
            Height = 28,
            Padding = new Thickness(8, 3),
            Child = grid,
        };
    }

    private static Control CreateParityCapturedStatusBarIconButton(RibbonCommandIconKind kind, bool isChecked) =>
        new Border
        {
            Width = 24,
            Height = 24,
            Background = isChecked ? Brush(15, 109, 140) : Brushes.Transparent,
            Child = AvaloniaRibbonIcons.Build(new RibbonCommandIcon(kind), 15, Brushes.White),
        };

    private static Control CreateParityCapturedStatusBarZoomText(string text) =>
        new TextBlock
        {
            Text = text,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            Width = 20,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };

    private static Control CreateParityCapturedStatusZoomSlider()
    {
        var canvas = new Canvas
        {
            Width = 120,
            Height = 22,
        };
        canvas.Children.Add(new Border
        {
            Width = 104,
            Height = 4,
            Background = Brush(218, 222, 228),
            BorderBrush = Brush(175, 184, 193),
            BorderThickness = new Thickness(1),
        });
        Canvas.SetLeft(canvas.Children[^1], 8);
        Canvas.SetTop(canvas.Children[^1], 9);
        foreach (var left in new[] { 8d, 60d, 111d })
        {
            canvas.Children.Add(new Border
            {
                Width = 1,
                Height = 4,
                Background = Brush(232, 236, 240),
            });
            Canvas.SetLeft(canvas.Children[^1], left);
            Canvas.SetTop(canvas.Children[^1], 16);
        }
        canvas.Children.Add(new Border
        {
            Width = 9,
            Height = 16,
            Background = Brush(248, 249, 250),
            BorderBrush = Brush(124, 133, 143),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(1),
        });
        Canvas.SetLeft(canvas.Children[^1], 55.5);
        Canvas.SetTop(canvas.Children[^1], 3);
        return canvas;
    }

    private static Control CreateParityCapturedBackstageContentScroll(Control content)
    {
        var root = new AvaloniaGrid
        {
            Background = Brushes.White,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(14) },
            },
        };
        AddGridChild(root, content, 0, 0);
        AddGridChild(root, CreateParityCapturedBackstageScrollbar(), 0, 1);
        return root;
    }

    private static Control CreateParityCapturedBackstageScrollbar()
    {
        const int statusBarHeight = 28;
        var height = ParityCaptureWindowHeight - ParityCaptureTitleBarHeight - statusBarHeight;
        var canvas = new Canvas
        {
            Width = 14,
            Height = height,
            Background = Brushes.White,
        };
        canvas.Children.Add(new Border
        {
            Width = 14,
            Height = height,
            Background = Brush(245, 245, 245),
            BorderBrush = Brush(224, 224, 224),
            BorderThickness = new Thickness(1, 0, 0, 0),
        });
        Canvas.SetTop(canvas.Children[^1], 0);
        canvas.Children.Add(new Border
        {
            Width = 8,
            Height = 120,
            Background = Brush(197, 197, 197),
            CornerRadius = new CornerRadius(4),
        });
        Canvas.SetLeft(canvas.Children[^1], 3);
        Canvas.SetTop(canvas.Children[^1], 56);
        return canvas;
    }

    private static Control CreateParityCapturedBackstageRail(string selectedPane)
    {
        var rail = new AvaloniaGrid
        {
            Background = ParityBackstageSidebarBrush,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
            },
        };

        var top = new StackPanel { Spacing = 0 };
        top.Children.Add(CreateParityCapturedBackstageBackButton());
        AddGridChild(rail, top, 0, 0);

        var bottom = new StackPanel { Spacing = 0 };
        AddGridChild(rail, bottom, 2, 0);
        foreach (var entry in FreeXBackstageNavigationPlanner.Build())
        {
            var panel = entry.DockBottom ? bottom : top;
            if (entry.Kind == FreeXBackstageNavigationEntryKind.Divider)
            {
                panel.Children.Add(CreateParityCapturedBackstageRailSeparator());
                continue;
            }

            var text = UiText.Get(entry.LabelKey!);
            panel.Children.Add(CreateParityCapturedBackstageRailButton(
                MapBackstageIcon(entry.Icon),
                text,
                entry.IconCommandName ?? text,
                IsParityCapturedBackstageEntrySelected(entry, selectedPane)));
        }
        return rail;
    }

    private static bool IsParityCapturedBackstageEntrySelected(
        FreeXBackstageNavigationEntry entry,
        string selectedPane) =>
        selectedPane switch
        {
            "Home" => entry.Pane == FreeXBackstagePaneId.Home,
            "Info" => entry.Pane == FreeXBackstagePaneId.Info,
            "Export" => entry.Command == FreeXBackstageCommandId.Export,
            "Account" => entry.Command == FreeXBackstageCommandId.Account,
            _ => false
        };

    private static RibbonCommandIconKind MapBackstageIcon(BackstageIconKind? icon) =>
        icon switch
        {
            BackstageIconKind.Previous => RibbonCommandIconKind.Previous,
            BackstageIconKind.Grid => RibbonCommandIconKind.Grid,
            BackstageIconKind.Info => RibbonCommandIconKind.Info,
            BackstageIconKind.Insert => RibbonCommandIconKind.Insert,
            BackstageIconKind.GetData => RibbonCommandIconKind.GetData,
            BackstageIconKind.Share => RibbonCommandIconKind.Share,
            BackstageIconKind.Save => RibbonCommandIconKind.Save,
            BackstageIconKind.Print => RibbonCommandIconKind.Print,
            BackstageIconKind.View => RibbonCommandIconKind.View,
            BackstageIconKind.WindowClose => RibbonCommandIconKind.WindowClose,
            _ => RibbonCommandIconKind.Info
        };

    private static Control CreateParityCapturedBackstageBackButton()
    {
        var row = new Border
        {
            Width = 190,
            Height = 50,
            Background = Brushes.Transparent,
            Child = new Border
            {
                HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                Margin = new Thickness(24, 0, 0, 0),
                Child = CreateParityCapturedBackstageBackArrowGlyph(),
            },
        };
        AutomationProperties.SetName(row, "Back");
        return row;
    }

    private static Control CreateParityCapturedBackstageBackArrowGlyph() =>
        // Render the arrow at the geometry's natural ~14px size (Stretch.None) instead of stretching it
        // to fill an 18px box — the uniform stretch also scaled the 1.25 stroke up, making the Linux
        // arrow read much larger/heavier than the thin ~16px back arrow on the Windows backstage rail
        // (backstage.Account.win.png / backstage.Info.win.png). No stretch keeps the stroke crisp + thin.
        new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M12,4 L5,11 L12,18 M6,11 L19,11"),
            Width = 16,
            Height = 16,
            Stroke = Brushes.White,
            StrokeThickness = 1.1,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.None,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };

    private static Control CreateParityCapturedBackstageRailButton(RibbonCommandIconKind iconKind, string text, string commandName, bool isSelected)
    {
        // Strip WPF-style access-key markers (_Save → Save, Save _As → Save As) so underscores
        // are not rendered literally. WPF shows these as underlined mnemonics; Avalonia has no
        // AccessText equivalent, so we simply remove the marker character.
        var displayText = text.Replace("_", string.Empty, StringComparison.Ordinal);

        var content = new AvaloniaGrid
        {
            Width = 190,
            Height = 38,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(22) },
                new ColumnDefinition { Width = new GridLength(22) },
                new ColumnDefinition { Width = new GridLength(12) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        AddGridChild(content, AvaloniaRibbonIcons.BuildMonochrome(iconKind, 22, commandName, Brushes.White), 0, 1);
        AddGridChild(content, new TextBlock
        {
            Text = displayText,
            FontSize = 13,
            FontFamily = ParityNarrowUiFontFamily,
            Foreground = Brushes.White,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        }, 0, 3);

        var row = new Border
        {
            Height = 38,
            Background = isSelected
                ? ParityBackstageSelectedBrush
                : Brushes.Transparent,
            Child = content,
        };
        AutomationProperties.SetName(row, text);
        return row;
    }

    private static Control CreateParityCapturedBackstageRailSeparator() =>
        new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4),
            Background = ParityBackstageSeparatorBrush,
        };

    private static void ApplyParityBackstageTypography(Control control)
    {
        if (control is TextBlock text)
            text.FontFamily = ParityNarrowUiFontFamily;

        switch (control)
        {
            case Panel panel:
                foreach (var child in panel.Children.OfType<Control>())
                    ApplyParityBackstageTypography(child);
                break;
            case ContentControl { Content: Control child }:
                ApplyParityBackstageTypography(child);
                break;
            case Decorator { Child: Control child }:
                ApplyParityBackstageTypography(child);
                break;
        }
    }

    private static Control CreateParityCapturedBackstageHomePane()
    {
        var canvas = new Canvas
        {
            Background = Brushes.White,
        };

        PlaceBackstage(canvas, new TextBlock
        {
            Text = "Good evening",
            FontSize = 30,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        }, 40, 40);
        PlaceBackstage(canvas, new TextBlock
        {
            Text = "New",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        }, 40, 98);
        PlaceBackstage(canvas, CreateParityCapturedBlankWorkbookTile(), 44, 126);

        var homePane = FreeXBackstageHomePanePlanner.Build();
        var recentHeader = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        AddGridChild(recentHeader, new TextBlock
        {
            Text = UiText.Get(homePane.RecentTab.LabelKey),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 96, 128)),
            Margin = new Thickness(0, 0, 28, 0),
        }, 0, 0);
        AddGridChild(recentHeader, new TextBlock
        {
            Text = UiText.Get(homePane.PinnedTab.LabelKey),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
        }, 0, 1);
        PlaceBackstage(canvas, recentHeader, 40, 248);
        PlaceBackstage(canvas, new Border
        {
            Width = 64,
            Height = 2,
            Background = new SolidColorBrush(Color.FromRgb(0, 96, 128)),
        }, 40, 272);
        PlaceBackstage(canvas, new Border
        {
            Width = 198,
            Height = 24,
            Background = new SolidColorBrush(Color.FromRgb(246, 246, 246)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
            BorderThickness = new Thickness(1),
        }, 692, 244);
        PlaceBackstage(canvas, CreateParityCapturedRecentHeaderRow(homePane), 40, 286);
        PlaceBackstage(canvas, CreateParityCapturedBackstageRecentFile(), 40, 310);

        return new Border
        {
            Background = Brushes.White,
            Child = canvas,
        };
    }

    private static Control CreateParityCapturedBackstageAccountPane()
    {
        var projection = FreeXBackstagePaneProjectionPlanner.BuildAccountDialog(
            BuildParityCapturedBackstageAccountPanePlan());
        var heading = projection.Elements.OfType<FreeXBackstageHeadingProjectionElement>().Single();
        var sectionHeader = projection.Elements.OfType<FreeXBackstageSectionHeaderProjectionElement>().First();
        var detailRows = projection.Elements.OfType<FreeXBackstageDetailRowsProjectionElement>().Single();
        var root = new StackPanel
        {
            Margin = new Thickness(44, 34, 46, 0),
            Spacing = 18,
        };
        root.Children.Add(new TextBlock
        {
            Text = UiText.Get(heading.TextKey),
            FontSize = 30,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        });
        // Windows shows an "Account" page subtitled "Local account information" (not "Product
        // information"): a two-column label/value table of the local app + OS identity, version, and
        // local workbook/sharing/export readiness — and no cloud-account note. Mirror that here.
        root.Children.Add(new TextBlock
        {
            Text = BackstageAccountText(sectionHeader.TextKey),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        });

        var details = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(180) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        var rows = BuildParityCapturedBackstageAccountRows(detailRows.Rows);
        for (var i = 0; i < rows.Length; i++)
        {
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddGridChild(details, new TextBlock
            {
                Text = rows[i].Label,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                Margin = new Thickness(0, 0, 18, 10),
                HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                TextAlignment = TextAlignment.Left,
            }, i, 0);
            AddGridChild(details, new TextBlock
            {
                Text = rows[i].Value,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
                Margin = new Thickness(0, 0, 0, 10),
                MaxWidth = 560,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                TextAlignment = TextAlignment.Left,
            }, i, 1);
        }
        root.Children.Add(details);

        return new Border
        {
            Background = Brushes.White,
            Child = root,
        };
    }

    private static FreeXBackstageAccountPanePlan BuildParityCapturedBackstageAccountPanePlan()
    {
        var accountInfo = LocalAccountInfoPlanner.Build(
            typeof(MainWindow).Assembly,
            deviceName: Environment.MachineName,
            userName: Environment.UserName,
            optionsAvailable: true);

        return FreeXBackstageAccountPanePlanner.Build(
            LocalAccountInfoPlanner.CreateBackstageAccountPaneRequest(
                accountInfo,
                currentWorkbookPath: null,
                currentWorkbookName: "Parity Demo (not saved yet)"));
    }

    private static (string Label, string Value)[] BuildParityCapturedBackstageAccountRows(
        IReadOnlyList<FreeXBackstageDetailRowProjection> details)
    {
        var rows = new (string Label, string Value)[details.Count];
        for (var i = 0; i < details.Count; i++)
        {
            var detail = details[i];
            rows[i] = (
                BackstageAccountText(detail.LabelKey),
                ResolveParityCapturedBackstageAccountValue(detail.Value));
        }

        return rows;
    }

    private static string ResolveParityCapturedBackstageAccountValue(FreeXBackstageTextValue value) =>
        value.Resolve(BackstageAccountText);

    /// <summary>
    /// Resolves a backstage Account string, falling back to the canonical English text when the
    /// localization key has not yet been authored (so the page renders at parity before the resx
    /// keys land). These labels are colon-free to match the Windows backstage page styling, so they
    /// use fresh keys rather than the existing colon-suffixed Backstage_Account_*Label keys.
    /// </summary>
    private static string BackstageAccountText(string resourceKey)
    {
        var localized = UiText.Get(resourceKey);
        if (!string.IsNullOrEmpty(localized) &&
            !(localized.StartsWith("[[", StringComparison.Ordinal) && localized.EndsWith("]]", StringComparison.Ordinal)) &&
            !string.Equals(localized, resourceKey, StringComparison.Ordinal))
        {
            return localized;
        }

        return resourceKey switch
        {
            "Backstage_Account_LocalInfoHeading" => "Local account information",
            "Backstage_Account_FreeXUserNameLabel" => "FreeX user name",
            "Backstage_Account_LocalOSAccountLabel" => "Local OS account",
            "Backstage_Account_DeviceRowLabel" => "Device",
            "Backstage_Account_AppVersionLabel" => "App version",
            "Backstage_Account_OptionsFileLabel" => "Options file",
            "Backstage_Account_OptionsFileLocalProfile" => "Local profile settings",
            "Backstage_Account_CurrentWorkbookLabel" => "Current workbook",
            "Backstage_Account_SharingLabel" => "Sharing",
            "Backstage_Account_SharingSaveAsRequired" => "Save As is required before local share can send the workbook.",
            "Backstage_Account_ExportLabel" => "Export",
            "Backstage_Account_ExportReadyLocal" => "Ready for local PDF/XPS export to a chosen local path.",
            _ => resourceKey,
        };
    }

    private static void PlaceBackstage(Canvas canvas, Control child, double left, double top)
    {
        Canvas.SetLeft(child, left);
        Canvas.SetTop(child, top);
        canvas.Children.Add(child);
    }

    private static Control CreateParityCapturedBlankWorkbookTile()
    {
        var canvas = new Canvas
        {
            Width = 108,
            Height = 80,
        };
        canvas.Children.Add(CreateParityCapturedThumbnailRect(0, 0, 18, 80, fill: Color.FromRgb(0xF0, 0xF0, 0xF0)));
        canvas.Children.Add(CreateParityCapturedThumbnailRect(0, 0, 108, 14, fill: Color.FromRgb(0xF0, 0xF0, 0xF0)));
        foreach (var y in new[] { 27d, 40d, 53d, 66d })
            canvas.Children.Add(CreateParityCapturedThumbnailLine(0, y, 108, y));
        foreach (var x in new[] { 42d, 66d, 90d })
            canvas.Children.Add(CreateParityCapturedThumbnailLine(x, 14, x, 80));
        canvas.Children.Add(CreateParityCapturedThumbnailRect(18, 14, 24, 13, fill: Color.FromRgb(0xE6, 0xF6, 0xFA)));
        canvas.Children.Add(CreateParityCapturedThumbnailRect(18, 14, 24, 13, stroke: Color.FromRgb(0x0F, 0x6D, 0x8C), strokeThickness: 1.5));

        var preview = new Border
        {
            Width = 108,
            Height = 80,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = canvas,
        };

        return new StackPanel
        {
            Width = 108,
            Children =
            {
                preview,
                new TextBlock
                {
                    Text = "Blank workbook",
                    FontSize = 12,
                    Margin = new Thickness(0, 6, 0, 2),
                    TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                },
            },
        };
    }

    private static Control CreateParityCapturedThumbnailRect(
        double left,
        double top,
        double width,
        double height,
        Color? fill = null,
        Color? stroke = null,
        double strokeThickness = 0.5)
    {
        var rect = new global::Avalonia.Controls.Shapes.Rectangle
        {
            Width = width,
            Height = height,
            Fill = fill is { } fillColor ? new SolidColorBrush(fillColor) : Brushes.Transparent,
            Stroke = stroke is { } strokeColor ? new SolidColorBrush(strokeColor) : null,
            StrokeThickness = stroke is null ? 0 : strokeThickness,
        };
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        return rect;
    }

    private static Control CreateParityCapturedThumbnailLine(double x1, double y1, double x2, double y2)
    {
        return new global::Avalonia.Controls.Shapes.Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC)),
            StrokeThickness = 0.5,
        };
    }

    private static Control CreateParityCapturedRecentHeaderRow(FreeXBackstageHomePanePlan homePane)
    {
        var grid = new AvaloniaGrid
        {
            Width = 850,
            Height = 28,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(150) },
                new ColumnDefinition { Width = new GridLength(36) },
            },
        };
        AddGridChild(grid, new TextBlock
        {
            Text = ResolveParityCapturedRecentColumnLabel(homePane, FreeXBackstageRecentColumnId.Name),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        }, 0, 0);
        AddGridChild(grid, new TextBlock
        {
            Text = ResolveParityCapturedRecentColumnLabel(homePane, FreeXBackstageRecentColumnId.DateModified),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        }, 0, 1);
        return new Border
        {
            Width = 850,
            Height = 28,
            BorderBrush = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Child = grid,
        };
    }

    private static string ResolveParityCapturedRecentColumnLabel(
        FreeXBackstageHomePanePlan homePane,
        FreeXBackstageRecentColumnId id)
    {
        var column = homePane.Columns.Single(column => column.Id == id);
        return UiText.Get(column.LabelKey);
    }

    private static Control CreateParityCapturedBackstageRecentFile()
    {
        var grid = new AvaloniaGrid
        {
            Width = 850,
            Height = 44,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(150) },
                new ColumnDefinition { Width = new GridLength(36) },
            },
        };
        var nameColumn = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        nameColumn.Children.Add(new Border
        {
            Width = 26,
            Height = 30,
            Background = new SolidColorBrush(Color.FromRgb(15, 109, 140)),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "X",
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            },
        });

        var text = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        text.Children.Add(new TextBlock
        {
            Text = "01_pivot-tables_customer-products.xlsx",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        });
        text.Children.Add(new TextBlock
        {
            Text = @"C:\Users\quinn\OneDrive\Documents\FreeX\FreeX\test-corpus\public\contextures",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        nameColumn.Children.Add(text);
        AddGridChild(grid, nameColumn, 0, 0);
        AddGridChild(grid, new TextBlock
        {
            Text = "Yesterday at 1:43 AM",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        }, 0, 1);
        AddGridChild(grid, AvaloniaRibbonIcons.Build(RibbonCommandIconKind.Pin, 22, "Pin to list"), 0, 2);
        return grid;
    }

    private static Control CreateParityCapturedBackstageInfoPane()
    {
        var projection = FreeXBackstagePaneProjectionPlanner.BuildInfoPane(
            BuildParityCapturedBackstageInfoPanePlan());
        var heading = projection.Elements.OfType<FreeXBackstageHeadingProjectionElement>().Single();
        var sectionHeaders = projection.Elements.OfType<FreeXBackstageSectionHeaderProjectionElement>().ToArray();
        var actionRow = projection.Elements.OfType<FreeXBackstageInfoActionRowProjectionElement>().Single();
        var detailRows = projection.Elements.OfType<FreeXBackstageDetailRowsProjectionElement>().Single();
        var root = new AvaloniaGrid
        {
            Margin = new Thickness(44, 34, 46, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(420) },
                new ColumnDefinition { Width = new GridLength(1) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        var actions = new StackPanel { Spacing = 14 };
        actions.Children.Add(new TextBlock
        {
            Text = UiText.Get(heading.TextKey),
            FontSize = 30,
            FontWeight = FontWeight.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        });
        actions.Children.Add(new TextBlock
        {
            Text = UiText.Get(sectionHeaders[0].TextKey),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        });
        foreach (var action in actionRow.Actions)
        {
            actions.Children.Add(CreateParityCapturedBackstageInfoAction(
                action.Icon,
                UiText.Get(action.LabelKey),
                ResolveParityCapturedBackstageTextValue(action.Detail)));
        }
        AddGridChild(root, actions, 0, 0);
        AddGridChild(root, new Border
        {
            Width = 1,
            Background = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
            Margin = new Thickness(0, 52, 0, 0),
        }, 0, 1);

        var properties = new StackPanel
        {
            Margin = new Thickness(28, 52, 0, 0),
            Spacing = 10,
        };
        properties.Children.Add(new TextBlock
        {
            Text = UiText.Get(sectionHeaders[1].TextKey),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        });
        foreach (var detail in detailRows.Rows)
        {
            properties.Children.Add(CreateParityCapturedBackstageProperty(
                UiText.Get(detail.LabelKey),
                ResolveParityCapturedBackstageTextValue(detail.Value)));
        }
        AddGridChild(root, properties, 0, 2);

        return new Border
        {
            Background = Brushes.White,
            Child = root,
        };
    }

    private static FreeXBackstageInfoPanePlan BuildParityCapturedBackstageInfoPanePlan()
    {
        var workbook = ParityDemoWorkbookFactory.Create();
        var activeSheet = workbook.Sheets[workbook.ActiveSheetIndex ?? 0];
        var info = BackstageInfoPlanner.Build(
            workbook,
            null,
            AvaloniaPlannerTextResources.Text,
            activeSheet,
            CultureInfo.CurrentCulture);

        return global::FreeX.ParityCapture.Avalonia.BackstageInfoParityProjection.Build(
            BackstageInfoPlanner.CreatePaneRequest(info));
    }

    private static Control CreateParityCapturedBackstageInfoAction(RibbonCommandIconKind iconKind, string title, string detail)
    {
        _ = iconKind;
        return new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            Children =
            {
                new Border
                {
                    Width = 220,
                    Height = 30,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromRgb(221, 221, 221)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = title,
                        FontSize = 12,
                        Foreground = Brushes.Black,
                        HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                        VerticalAlignment = AvaloniaVerticalAlignment.Center,
                    },
                },
                new TextBlock
                {
                    Text = detail,
                    FontSize = 11,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                    Foreground = new SolidColorBrush(Color.FromRgb(64, 64, 64)),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 292,
                },
            },
        };
    }

    private static string ResolveParityCapturedBackstageTextValue(FreeXBackstageTextValue? value) =>
        value?.Resolve(UiText.Get) ?? string.Empty;

    private static Control CreateParityCapturedBackstageProperty(string name, string value) =>
        new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            Children =
            {
                new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                    TextAlignment = TextAlignment.Left,
                },
                new TextBlock
                {
                    Text = value,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 340,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                    TextAlignment = TextAlignment.Left,
                },
            },
        };

    /// <summary>
    /// Renders <paramref name="visual"/> into an off-screen <see cref="RenderTargetBitmap"/> at the given
    /// pixel size and writes it as a PNG. The visual is measured/arranged first so an off-screen or
    /// not-yet-shown window still produces a populated bitmap.
    /// </summary>
    private static void RenderVisualToPng(Visual visual, int width, int height, string path)
    {
        using var bitmap = RenderVisualToBitmap(visual, width, height);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        using var stream = File.Create(path);
        bitmap.Save(stream);
    }

    /// <summary>
    /// Variant of <see cref="RenderVisualToPng"/> used for the parity-grid capture.  When
    /// <paramref name="visual"/> is a composite <c>AvaloniaGrid</c> that contains a
    /// <c>Canvas</c> overlay child (the drawing-object layer), <c>RenderTargetBitmap.Render</c>
    /// in Avalonia's headless platform does not reliably paint the Canvas sibling.
    /// <para/>
    /// This method works around that by using a two-pass render: first the cell-grid child is
    /// rendered into the primary bitmap; then any remaining children (the overlay Canvas) are
    /// rendered into a scratch bitmap and blitted on top via
    /// <c>RenderTargetBitmap.CreateDrawingContext</c> (which is additive, not clearing).
    /// </summary>
    private static void RenderVisualToPngWithOverlay(Visual visual, int width, int height, string path)
    {
        var pixelWidth  = Math.Max(1, width);
        var pixelHeight = Math.Max(1, height);
        var pixelSize   = new PixelSize(pixelWidth, pixelHeight);
        var dpi         = new Vector(96, 96);
        var fullRect    = new Rect(0, 0, pixelWidth, pixelHeight);

        // ── Layout pass ──────────────────────────────────────────────────────────────────────────
        if (visual is Layoutable root)
        {
            root.Measure(new Size(pixelWidth, pixelHeight));
            root.Arrange(fullRect);
        }
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        // ── Check whether we have a composite Grid with a Canvas overlay sibling ───────────────
        // If so, identify the cell-grid child and the overlay-canvas children separately.
        var compositeGrid = visual as AvaloniaGrid;
        var layerSeparation = compositeGrid is not null &&
                              compositeGrid.Children.Count >= 2 &&
                              compositeGrid.Children[compositeGrid.Children.Count - 1] is Canvas;

        if (!layerSeparation)
        {
            // Plain visual — render directly as before.
            using var bitmap = new RenderTargetBitmap(pixelSize, dpi);
            bitmap.Render(visual);
            SaveBitmap(bitmap, path);
            return;
        }

        // ── Two-pass composite render ─────────────────────────────────────────────────────────
        // Pass 1: render the cell grid (first child) into the primary bitmap.
        var cellGrid = compositeGrid!.Children[0];
        if (cellGrid is Layoutable cellLayoutable)
        {
            cellLayoutable.Measure(new Size(pixelWidth, pixelHeight));
            cellLayoutable.Arrange(fullRect);
        }

        using var primaryBitmap = new RenderTargetBitmap(pixelSize, dpi);
        primaryBitmap.Render(cellGrid);

        // Pass 2: render each overlay child onto a scratch bitmap and blit.
        // CreateDrawingContext is additive (does not clear the primary bitmap).
        for (var i = 1; i < compositeGrid.Children.Count; i++)
        {
            var overlay = compositeGrid.Children[i];
            if (overlay is Layoutable overlayLayoutable)
            {
                overlayLayoutable.Measure(new Size(pixelWidth, pixelHeight));
                overlayLayoutable.Arrange(fullRect);
            }

            using var overlayBitmap = new RenderTargetBitmap(pixelSize, dpi);
            overlayBitmap.Render(overlay);

            using var ctx = primaryBitmap.CreateDrawingContext();
            ctx.DrawImage(overlayBitmap, fullRect, fullRect);
        }

        SaveBitmap(primaryBitmap, path);
    }

    private static void SaveBitmap(RenderTargetBitmap bitmap, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        using var stream = File.Create(path);
        bitmap.Save(stream);
    }

    private static RenderTargetBitmap RenderVisualToBitmap(Visual visual, int width, int height)
    {
        var pixelWidth = Math.Max(1, width);
        var pixelHeight = Math.Max(1, height);

        if (visual is Layoutable layoutable)
        {
            layoutable.Measure(new Size(pixelWidth, pixelHeight));
            layoutable.Arrange(new Rect(0, 0, pixelWidth, pixelHeight));
        }
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        var bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96));
        bitmap.Render(visual);
        return bitmap;
    }

    // ── Grid-range capture (--parity-grid) ──────────────────────────────────────────────────────────

    /// <summary>
    /// Headless grid-range capture. Loads <paramref name="workbookPath"/>, sets the viewport origin to
    /// the top-left of <paramref name="rangeText"/>, builds the sheet-grid sub-tree (no ribbon/chrome),
    /// sizes the render canvas to the exact pixel extent of the range at zoom=1 with no row/column header
    /// gutter, renders to a PNG in <paramref name="outputDirectory"/>, and returns a <see cref="GridCaptureResult"/>
    /// whose <see cref="GridCaptureResult.JsonLog"/> summarises the outcome on one line.
    ///
    /// Mirroring Excel's <c>CopyPicture-of-range</c> and the WPF <c>--capture-range</c> harness:
    /// <list type="bullet">
    ///   <item>ShowHeadings = false — no row numbers or column letters in the output image.</item>
    ///   <item>Zoom = 1.0 — the pixel extents of the range cells are used verbatim.</item>
    ///   <item>ShowGridlines = true — matches Excel CopyPicture default.</item>
    /// </list>
    /// The output file is named <c>&lt;sheetName&gt;_&lt;range&gt;.png</c> with characters unsafe for file
    /// names replaced by underscores.
    /// </summary>
    internal Task<GridCaptureResult> CaptureGridRangeAsync(
        string workbookPath,
        string rangeText,
        string outputDirectory,
        string? worksheetName = null)
    {
        // All rendering must happen on the UI thread; we are called from the coordinator which already
        // runs on it via the Opened event, so there is nothing to marshal. Return a completed task so the
        // coordinator can await us uniformly.
        return Task.FromResult(CaptureGridRangeCore(workbookPath, rangeText, outputDirectory, worksheetName));
    }

    private GridCaptureResult CaptureGridRangeCore(
        string workbookPath,
        string rangeText,
        string outputDirectory,
        string? worksheetName)
    {
        // ── 1. Load the workbook ───────────────────────────────────────────────────────────────────
        // StartupWorkbookLoader silently falls back to the sample workbook for a missing/unsupported
        // path (it filters its arguments through File.Exists), so a non-existent fixture would otherwise
        // be "captured" against sample content. Fail explicitly here so the caller gets a real error.
        if (!File.Exists(workbookPath))
            return GridCaptureFailure(workbookPath, rangeText, outputDirectory,
                $"Workbook file not found: {workbookPath}");

        StartupWorkbookLoadResult source;
        try
        {
            source = new StartupWorkbookLoader().Load([workbookPath]);
        }
        catch (Exception ex)
        {
            return GridCaptureFailure(workbookPath, rangeText, outputDirectory,
                $"Failed to load workbook: {ex.GetType().Name}: {ex.Message}");
        }

        var workbook = source.Workbook;
        var sheet = string.IsNullOrWhiteSpace(worksheetName)
            ? workbook.Sheets.FirstOrDefault(s => !s.IsHidden && !s.IsVeryHidden)
            : workbook.Sheets.FirstOrDefault(s =>
                !s.IsHidden &&
                !s.IsVeryHidden &&
                string.Equals(s.Name, worksheetName, StringComparison.OrdinalIgnoreCase));
        if (sheet is null)
        {
            var message = string.IsNullOrWhiteSpace(worksheetName)
                ? "Workbook has no visible sheet."
                : $"Workbook has no visible worksheet named '{worksheetName}'.";
            return GridCaptureFailure(workbookPath, rangeText, outputDirectory, message);
        }

        // ── 2. Parse the cell range ────────────────────────────────────────────────────────────────
        GridRange range;
        try
        {
            var normalizedRange = rangeText.Replace("$", "", StringComparison.Ordinal).Trim();
            range = GridRange.ParseCellOrRange(normalizedRange, sheet.Id);
        }
        catch (Exception ex)
        {
            return GridCaptureFailure(workbookPath, rangeText, outputDirectory,
                $"Could not parse range '{rangeText}': {ex.GetType().Name}: {ex.Message}");
        }

        // ── 3. Size the session viewport to exactly cover the range ───────────────────────────────
        // Create a temporary session whose viewport is bounded to the requested range's own extent.
        //
        // IMPORTANT: we must NOT use a giant sentinel viewport here.  The ViewportService materializes
        // a RowMetric/ColMetric (and BuildSheetGrid then a cell visual) for every row/col that fits in
        // AvailableHeight/Width — a 32 000-DIP viewport produces ~1 600 rows × ~500 cols ≈ 800 000 cell
        // visuals, which deadlocks / crashes the headless render.  Instead we size the viewport in two
        // passes: first a bound just large enough to span the requested range (rowCount × a generous
        // per-row cap, colCount × per-col cap), measure the range's true extent from those metrics, then
        // resize the viewport to exactly that extent so only the range cells are materialised.
        const double ZoomFactor = 1.0;
        const double MaxRowHeightDip = 409.5;   // Excel's max row height in points ≈ DIP at 96 DPI
        const double MaxColWidthDip = 2000.0;   // generous upper bound for a single column's width

        var rangeRowCount = (int)(range.End.Row - range.Start.Row + 1);
        var rangeColCount = (int)(range.End.Col - range.Start.Col + 1);

        // Add one extra cell of slack so the metrics loop (which breaks once the offset exceeds the
        // available extent) emits the final range row/col rather than stopping one short.
        var measureHeight = (rangeRowCount + 1) * MaxRowHeightDip;
        var measureWidth = (rangeColCount + 1) * MaxColWidthDip;

        // Use the shared WorkbookSessionFactory so the session is wired identically to the live app.
        var sessionFactory = new WorkbookSessionFactory();
        // includeObjects: true so that drawing shapes, pictures and text boxes appear in the
        // captured grid image — this is the whole point of the --parity-grid harness for shapes.
        var tempSession = sessionFactory.Create(source, measureHeight, measureWidth, includeObjects: true);

        // The range's sheet may not be the workbook's default-active sheet (e.g. an Excel file may
        // mark a source-data sheet as active when saved, while the capture target is another sheet).
        // Navigate to the range's sheet so the session viewport and BuildSheetGrid both see the
        // correct sheet — without this, adornments (pivot dropdowns, sparklines, etc.) that read
        // _session.ActiveSheet would silently resolve against the wrong sheet and produce no output.
        if (tempSession.ActiveSheet.Id != sheet.Id)
            tempSession.SelectSheet(sheet.Id);

        // BuildSheetGrid reads the session view state, not Sheet.ShowHeadings directly. Set this
        // before positioning the viewport because the view-state mutation rebuilds its metrics.
        tempSession.SetShowHeadings(false);

        // Scroll viewport to the range origin so the range cells are the first ones materialised.
        tempSession.SetViewportOrigin(range.Start.Row, range.Start.Col);

        var viewport = tempSession.Viewport;

        // ── 4. Compute pixel size of the range ────────────────────────────────────────────────────
        // Filter the viewport metrics to only the rows/cols inside the requested range.
        var rangeRowMetrics = viewport.RowMetrics
            .Where(m => m.Row >= range.Start.Row && m.Row <= range.End.Row)
            .ToList();
        var rangeColMetrics = viewport.ColMetrics
            .Where(m => m.Col >= range.Start.Col && m.Col <= range.End.Col)
            .ToList();

        // Use the same display-width/height helpers as BuildSheetGrid so sizes are consistent.
        var pixelWidth = (int)Math.Ceiling(
            rangeColMetrics.Sum(m => Math.Max(MinimumDisplayedColumnWidth, m.Width) * ZoomFactor));
        var pixelHeight = (int)Math.Ceiling(
            rangeRowMetrics.Sum(m => Math.Max(MinimumDisplayedRowHeight, m.Height) * ZoomFactor));

        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);

        // Resize the viewport so it materialises just past the range (range extent + one row/col of
        // slack). This keeps BuildSheetGrid's cell count tiny while guaranteeing every range cell is
        // present; cells beyond the range render outside the exact-extent canvas and are cropped away.
        // (We deliberately do NOT resize to the EXACT extent: the metrics break-condition can then drop
        // the final range row/col, leaving the grid one cell short.)
        var slackHeight = pixelHeight + MaxRowHeightDip;
        var slackWidth = pixelWidth + MaxColWidthDip;
        tempSession.UpdateViewportSize(slackHeight, slackWidth);
        tempSession.SetViewportOrigin(range.Start.Row, range.Start.Col);

        // ── 5. Build a standalone grid control for the range ──────────────────────────────────────
        // Temporarily swap the main window's session to the range-loaded session, configure
        // ShowHeadings=false + ShowGridlines=true, rebuild the grid, then restore.
        var previousSession = _session;
        try
        {
            _session = tempSession;

            // Rebuild the grid sub-tree using the existing BuildSheetGrid() path — this produces
            // exactly the same cell rendering the live app uses.
            var gridControl = BuildSheetGrid();

            // A tiny range viewport can omit an otherwise valid embedded picture from its projected
            // DrawingObjects collection even though the source sheet anchors it inside the requested
            // range.  Add only those missing Image pictures to this capture-only overlay, reusing the
            // production image visual so crop and future renderer changes remain observable here.
            var missingPictureOverlayBounds = new List<GridCaptureOverlayBounds>();
            gridControl = AddMissingEmbeddedPictureCaptureOverlay(
                gridControl,
                viewport,
                sheet,
                range,
                pixelWidth,
                pixelHeight,
                missingPictureOverlayBounds);

            // ── 6. Render to PNG ───────────────────────────────────────────────────────────────────
            Directory.CreateDirectory(outputDirectory);

            var safeName = MakeSafeFileName($"{sheet.Name}_{rangeText}");
            var pngFileName = $"{safeName}.png";
            var pngPath = Path.Combine(outputDirectory, pngFileName);

            // Attach the range grid to the live shell before rendering it. The Win32+Skia backend can
            // encode a syntactically valid but transparent-black PNG when RenderTargetBitmap is asked to
            // paint a detached grid subtree. Keeping the capture-only grid rooted under the visible host
            // gives it a valid visual root without changing the production renderer or the cropped extent.
            //
            // Drawing-object overlay (shapes, pictures, text boxes): RenderTargetBitmap.Render on a
            // composite AvaloniaGrid doesn't always paint the Canvas sibling in headless mode.  We
            // use a two-pass approach: render the cell grid first, then render the Canvas overlay into
            // a second bitmap and blit it on top via CreateDrawingContext (additive draw).
            var chartPixelBounds = GetVisibleChartPixelBounds(sheet, range, pixelWidth, pixelHeight);
            var drawingObjectPixelBounds = GetVisibleNonChartDrawingPixelBounds(
                sheet,
                range,
                pixelWidth,
                pixelHeight);
            var overlayPixelBounds = chartPixelBounds
                .Concat(drawingObjectPixelBounds)
                .Concat(missingPictureOverlayBounds)
                .ToArray();
            var expectedShapeFillRegions = GetVisibleDrawingShapeFillRegions(
                workbook.Theme,
                sheet,
                range,
                pixelWidth,
                pixelHeight);
            var expectedPictureColorRegions = GetVisibleEmbeddedPictureColorRegions(
                sheet,
                range,
                pixelWidth,
                pixelHeight);
            RenderAttachedGridCaptureToPng(
                gridControl,
                pixelWidth,
                pixelHeight,
                pngPath,
                includeDrawingOverlays: overlayPixelBounds.Length > 0,
                overlayPixelBounds);
            if (ParityCaptureOutputGuard.ValidateGridPngOutput(
                    pngPath,
                    overlayPixelBounds,
                    expectedShapeFillRegions.Concat(expectedPictureColorRegions).ToArray()) is { } captureValidationError)
                return GridCaptureFailure(workbookPath, rangeText, outputDirectory, captureValidationError);

            // ── 7. Write the JSON log file alongside the PNG ──────────────────────────────────────
            var result = new GridCaptureResult(
                Captured: true,
                PngPath: pngPath,
                PngFileName: pngFileName,
                WidthPx: pixelWidth,
                HeightPx: pixelHeight,
                SheetName: sheet.Name,
                RangeText: rangeText,
                Note: "");

            File.WriteAllText(
                Path.Combine(outputDirectory, Path.ChangeExtension(pngFileName, ".json")),
                result.JsonLog);

            return result;
        }
        catch (Exception ex)
        {
            return GridCaptureFailure(workbookPath, rangeText, outputDirectory,
                $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _session = previousSession;
        }
    }

    /// <summary>
    /// Computes the pixel extent (width × height) of a cell range using the same display-width/height
    /// helpers as the live sheet grid, at zoom=1 with no header gutter. This is the sizing helper unit
    /// tests exercise to verify the formula before any full headless render is needed.
    /// </summary>
    internal static (int WidthPx, int HeightPx) ComputeRangePixelExtent(
        ViewportModel viewport, GridRange range, double zoomFactor = 1.0)
    {
        var w = viewport.ColMetrics
            .Where(m => m.Col >= range.Start.Col && m.Col <= range.End.Col)
            .Sum(m => Math.Max(MinimumDisplayedColumnWidth, m.Width) * zoomFactor);
        var h = viewport.RowMetrics
            .Where(m => m.Row >= range.Start.Row && m.Row <= range.End.Row)
            .Sum(m => Math.Max(MinimumDisplayedRowHeight, m.Height) * zoomFactor);
        return (Math.Max(1, (int)Math.Ceiling(w)), Math.Max(1, (int)Math.Ceiling(h)));
    }

    /// <summary>
    /// Determines whether a visible chart's sheet-absolute drawing rectangle intersects a captured
    /// cell range. Chart anchors use the same width × 8 / row-height geometry as the XLSX drawing
    /// anchor applier, so the check is independent of the temporary capture viewport.
    /// </summary>
    private static bool HasVisibleChartOverlappingRange(Sheet sheet, GridRange range)
        => GetVisibleChartPixelBounds(sheet, range, int.MaxValue, int.MaxValue).Count > 0;

    private static IReadOnlyList<GridCaptureOverlayBounds> GetVisibleChartPixelBounds(
        Sheet sheet,
        GridRange range,
        int captureWidth,
        int captureHeight)
    {
        var left = SumSheetColumnPixels(sheet, 1, range.Start.Col - 1);
        var top = SumSheetRowPixels(sheet, 1, range.Start.Row - 1);
        var right = left + SumSheetColumnPixels(sheet, range.Start.Col, range.End.Col - range.Start.Col + 1);
        var bottom = top + SumSheetRowPixels(sheet, range.Start.Row, range.End.Row - range.Start.Row + 1);

        return sheet.Charts
            .Where(chart => chart.IsVisible &&
                            chart.Left < right && chart.Left + chart.Width > left &&
                            chart.Top < bottom && chart.Top + chart.Height > top)
            .Select(chart => GridCaptureOverlayBounds.FromAbsoluteBounds(
                chart.Left - left,
                chart.Top - top,
                chart.Width,
                chart.Height,
                captureWidth,
                captureHeight))
            .Where(bounds => !bounds.IsEmpty)
            .ToArray();
    }

    private static IReadOnlyList<GridCaptureOverlayBounds> GetVisibleNonChartDrawingPixelBounds(
        Sheet sheet,
        GridRange range,
        int captureWidth,
        int captureHeight)
    {
        var bounds = new List<GridCaptureOverlayBounds>();
        AddVisibleAnchoredObjectPixelBounds(
            bounds,
            sheet,
            range,
            captureWidth,
            captureHeight,
            sheet.DrawingShapes.Where(shape => shape.IsVisible)
                .Select(shape => (shape.Anchor, shape.AnchorOffsetX, shape.AnchorOffsetY, shape.Width, shape.Height)));
        AddVisibleAnchoredObjectPixelBounds(
            bounds,
            sheet,
            range,
            captureWidth,
            captureHeight,
            sheet.Pictures.Where(picture => picture.IsVisible)
                .Select(picture => (picture.Anchor, picture.AnchorOffsetX, picture.AnchorOffsetY, picture.Width, picture.Height)));
        AddVisibleAnchoredObjectPixelBounds(
            bounds,
            sheet,
            range,
            captureWidth,
            captureHeight,
            sheet.TextBoxes.Where(textBox => textBox.IsVisible)
                .Select(textBox => (textBox.Anchor, textBox.AnchorOffsetX, textBox.AnchorOffsetY, textBox.Width, textBox.Height)));
        return bounds;
    }

    private static IReadOnlyList<GridCaptureFillColorRegion> GetVisibleDrawingShapeFillRegions(
        WorkbookTheme theme,
        Sheet sheet,
        GridRange range,
        int captureWidth,
        int captureHeight)
    {
        var regions = new List<GridCaptureFillColorRegion>();
        foreach (var shape in sheet.DrawingShapes)
        {
            if (!shape.IsVisible ||
                shape.GradientFillEndColor is not null ||
                shape.ResolveFillColor(theme, DrawingShapeModel.ResolveDefaultFillColor(theme)) is not { } fillColor)
                continue;

            if (TryGetAnchoredObjectPixelBounds(
                    sheet,
                    range,
                    captureWidth,
                    captureHeight,
                    shape.Anchor,
                    shape.AnchorOffsetX,
                    shape.AnchorOffsetY,
                    shape.Width,
                    shape.Height,
                    out var bounds))
            {
                regions.Add(new GridCaptureFillColorRegion(bounds, fillColor));
            }
        }

        return regions;
    }

    private static IReadOnlyList<GridCaptureFillColorRegion> GetVisibleEmbeddedPictureColorRegions(
        Sheet sheet,
        GridRange range,
        int captureWidth,
        int captureHeight)
    {
        var regions = new List<GridCaptureFillColorRegion>();
        foreach (var picture in sheet.Pictures)
        {
            if (!picture.IsVisible ||
                picture.Kind != PictureKind.Image ||
                picture.ImageBytes is not { Length: > 0 } imageBytes ||
                !TryGetRepresentativeChromaticColor(imageBytes, out var color) ||
                !TryGetAnchoredObjectPixelBounds(
                    sheet,
                    range,
                    captureWidth,
                    captureHeight,
                    picture.Anchor,
                    picture.AnchorOffsetX,
                    picture.AnchorOffsetY,
                    picture.Width,
                    picture.Height,
                    out var bounds))
            {
                continue;
            }

            regions.Add(new GridCaptureFillColorRegion(bounds, color));
        }

        return regions;
    }

    private static bool TryGetRepresentativeChromaticColor(byte[] imageBytes, out CellColor color)
    {
        color = default;
        using var bitmap = SKBitmap.Decode(imageBytes);
        if (bitmap is null)
            return false;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var maximum = Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue));
                var minimum = Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue));
                if (pixel.Alpha > 0 && maximum - minimum >= 32)
                {
                    color = new CellColor(pixel.Red, pixel.Green, pixel.Blue);
                    return true;
                }
            }
        }

        return false;
    }

    private static Control AddMissingEmbeddedPictureCaptureOverlay(
        Control gridControl,
        ViewportModel viewport,
        Sheet sheet,
        GridRange range,
        int captureWidth,
        int captureHeight,
        ICollection<GridCaptureOverlayBounds> addedBounds)
    {
        var projectedPictureIds = viewport.DrawingObjects
            .Where(item => item.Kind == SelectionPaneObjectKind.Picture)
            .Select(item => item.Id)
            .ToHashSet();
        var overlay = new Canvas
        {
            Width = captureWidth,
            Height = captureHeight,
            ClipToBounds = true,
            IsHitTestVisible = false,
        };

        foreach (var picture in sheet.Pictures)
        {
            if (!picture.IsVisible ||
                picture.Kind != PictureKind.Image ||
                picture.ImageBytes is not { Length: > 0 } imageBytes ||
                projectedPictureIds.Contains(picture.Id) ||
                !TryGetAnchoredObjectPixelBounds(
                    sheet,
                    range,
                    captureWidth,
                    captureHeight,
                    picture.Anchor,
                    picture.AnchorOffsetX,
                    picture.AnchorOffsetY,
                    picture.Width,
                    picture.Height,
                    out var bounds))
            {
                continue;
            }

            var renderPlan = DrawingObjectRenderPlanner.Plan(new DrawingObjectBounds(
                SelectionPaneObjectKind.Picture,
                picture.Id,
                picture.Name ?? "Picture",
                picture.Anchor.Row,
                picture.Anchor.Col,
                Left: bounds.Left,
                Top: bounds.Top,
                Width: bounds.Width,
                Height: bounds.Height,
                RotationDegrees: picture.RotationDegrees,
                FlipHorizontal: picture.FlipHorizontal,
                FlipVertical: picture.FlipVertical,
                PictureKind: picture.Kind,
                ImageBytes: imageBytes.ToArray(),
                ImageContentType: picture.ContentType,
                CropLeft: picture.CropLeft,
                CropTop: picture.CropTop,
                CropRight: picture.CropRight,
                CropBottom: picture.CropBottom));
            var visual = CreateDrawingImageVisual(renderPlan, bounds.Width, bounds.Height);
            Canvas.SetLeft(visual, bounds.Left);
            Canvas.SetTop(visual, bounds.Top);
            overlay.Children.Add(visual);
            addedBounds.Add(bounds);
        }

        if (overlay.Children.Count == 0)
            return gridControl;

        var composite = new AvaloniaGrid
        {
            Width = captureWidth,
            Height = captureHeight,
            ClipToBounds = true,
        };
        composite.Children.Add(gridControl);
        composite.Children.Add(overlay);
        return composite;
    }

    private static void AddVisibleAnchoredObjectPixelBounds(
        ICollection<GridCaptureOverlayBounds> destination,
        Sheet sheet,
        GridRange range,
        int captureWidth,
        int captureHeight,
        IEnumerable<(CellAddress Anchor, double OffsetX, double OffsetY, double Width, double Height)> objects)
    {
        foreach (var drawingObject in objects)
        {
            if (TryGetAnchoredObjectPixelBounds(
                    sheet,
                    range,
                    captureWidth,
                    captureHeight,
                    drawingObject.Anchor,
                    drawingObject.OffsetX,
                    drawingObject.OffsetY,
                    drawingObject.Width,
                    drawingObject.Height,
                    out var bounds))
            {
                destination.Add(bounds);
            }
        }
    }

    private static bool TryGetAnchoredObjectPixelBounds(
        Sheet sheet,
        GridRange range,
        int captureWidth,
        int captureHeight,
        CellAddress anchor,
        double offsetX,
        double offsetY,
        double width,
        double height,
        out GridCaptureOverlayBounds bounds)
    {
        bounds = default;
        if (anchor.Sheet != sheet.Id ||
            !double.IsFinite(offsetX) ||
            !double.IsFinite(offsetY) ||
            !double.IsFinite(width) ||
            !double.IsFinite(height) ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        var rangeLeft = SumSheetColumnPixels(sheet, 1, range.Start.Col - 1);
        var rangeTop = SumSheetRowPixels(sheet, 1, range.Start.Row - 1);
        var absoluteLeft = SumSheetColumnPixels(sheet, 1, anchor.Col - 1) + offsetX;
        var absoluteTop = SumSheetRowPixels(sheet, 1, anchor.Row - 1) + offsetY;
        bounds = GridCaptureOverlayBounds.FromAbsoluteBounds(
            absoluteLeft - rangeLeft,
            absoluteTop - rangeTop,
            width,
            height,
            captureWidth,
            captureHeight);
        return !bounds.IsEmpty;
    }

    private static double SumSheetColumnPixels(Sheet sheet, uint firstColumn, uint count)
    {
        double width = 0;
        for (var offset = 0u; offset < count; offset++)
        {
            var column = firstColumn + offset;
            if (!sheet.IsColEffectivelyHidden(column))
                width += sheet.ColumnWidths.GetValueOrDefault(column, sheet.DefaultColumnWidth) * 8;
        }

        return width;
    }

    private static double SumSheetRowPixels(Sheet sheet, uint firstRow, uint count)
    {
        double height = 0;
        for (var offset = 0u; offset < count; offset++)
        {
            var row = firstRow + offset;
            if (!sheet.IsRowEffectivelyHidden(row))
                height += sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight);
        }

        return height;
    }

    /// <summary>
    /// Temporarily roots a capture-only grid under the displayed worksheet host before painting it.
    /// Avalonia's desktop Skia renderer requires that visual-root relationship for a range grid to
    /// paint its cell children into a <see cref="RenderTargetBitmap"/>.
    /// </summary>
    private void RenderAttachedGridCaptureToPng(
        Control gridControl,
        int width,
        int height,
        string path,
        bool includeDrawingOverlays,
        IReadOnlyList<GridCaptureOverlayBounds> overlayPixelBounds)
    {
        var originalContent = _sheetGridHost.Content;
        var originalWidth = _sheetGridHost.Width;
        var originalHeight = _sheetGridHost.Height;
        var captureSize = new Size(Math.Max(1, width), Math.Max(1, height));
        var composite = gridControl as AvaloniaGrid;
        GridCaptureOverlayLayer[] overlays = !includeDrawingOverlays || composite is null
            ? []
            : composite.Children
                .Skip(1)
                .Select((child, index) => new GridCaptureOverlayLayer(index + 1, child))
                .ToArray();
        var basePath = path + "." + Guid.NewGuid().ToString("N") + ".base.png";
        var overlayPath = path + "." + Guid.NewGuid().ToString("N") + ".overlay.png";
        var overlaysDetached = false;

        try
        {
            _sheetGridHost.Width = captureSize.Width;
            _sheetGridHost.Height = captureSize.Height;
            _sheetGridHost.Content = gridControl;
            _sheetGridHost.Measure(captureSize);
            _sheetGridHost.Arrange(new Rect(0, 0, captureSize.Width, captureSize.Height));
            _sheetGridHost.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

            if (overlays.Length == 0)
            {
                // Preserve the established single-layer host path exactly for ordinary cell-only
                // captures. The two-layer composition below is only needed when a drawing overlay
                // exists, and the headless backend is sensitive to that extra bitmap path.
                RenderVisualToPngWithOverlay(_sheetGridHost, width, height, path);
                return;
            }

            // Render the established, host-rooted frame before touching the composite.  In the
            // headless backend this is the only proven path that paints the range grid reliably.
            // The overlay Canvas is composited separately below because it can be omitted from
            // that otherwise valid host render.
            RenderVisualToPngWithOverlay(_sheetGridHost, width, height, basePath);

            // The base image is now stable. Detach the overlays so each can be rooted under the
            // host and rendered independently without disturbing the cell-grid capture.
            for (var index = composite!.Children.Count - 1; index >= 1; index--)
                composite.Children.RemoveAt(index);
            overlaysDetached = true;

            using var baseBitmap = new Bitmap(basePath);
            var overlayComposite = new AvaloniaGrid
            {
                Width = captureSize.Width,
                Height = captureSize.Height,
                ClipToBounds = true,
            };
            // The bare Image deliberately serves as the first pass: in the headless backend it
            // leaves a black scratch frame but allows the final Canvas child to paint. The scratch
            // is then alpha-key composed over the separately captured, valid cell-grid base PNG.
            overlayComposite.Children.Add(new global::Avalonia.Controls.Image
            {
                Width = captureSize.Width,
                Height = captureSize.Height,
                Source = baseBitmap,
                Stretch = Stretch.Fill,
            });
            foreach (var overlay in overlays)
                overlayComposite.Children.Add(overlay.Control);

            // Render the chart Canvas into a scratch PNG, then combine it with the independently
            // valid base. The headless renderer encodes transparent Canvas background as opaque
            // black, so black pixels are retained only within known chart rectangles.
            _sheetGridHost.Content = overlayComposite;
            _sheetGridHost.Measure(captureSize);
            _sheetGridHost.Arrange(new Rect(0, 0, captureSize.Width, captureSize.Height));
            _sheetGridHost.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            RenderVisualToPngWithOverlay(overlayComposite, width, height, overlayPath);
            overlayComposite.Children.Clear();
            CompositeDrawingOverlayPng(basePath, overlayPath, path, overlayPixelBounds);
        }
        finally
        {
            try
            {
                if (File.Exists(basePath))
                    File.Delete(basePath);
                if (File.Exists(overlayPath))
                    File.Delete(overlayPath);
            }
            catch (IOException)
            {
                // Capture cleanup must not replace a valid artifact with a teardown failure.
            }

            if (composite is not null && overlaysDetached)
            {
                // Detach the final overlay from the host before restoring it to the composite.
                // Avalonia rejects inserting a control that still belongs to the host's ContentPresenter.
                _sheetGridHost.Content = gridControl;
                foreach (var overlay in overlays)
                    composite.Children.Insert(overlay.Index, overlay.Control);
            }

            _sheetGridHost.Content = originalContent;
            _sheetGridHost.Width = originalWidth;
            _sheetGridHost.Height = originalHeight;
            _sheetGridHost.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
        }
    }

    private static void CompositeDrawingOverlayPng(
        string basePath,
        string overlayPath,
        string outputPath,
        IReadOnlyList<GridCaptureOverlayBounds> overlayPixelBounds)
    {
        using var baseBitmap = SKBitmap.Decode(basePath)
            ?? throw new InvalidDataException("The captured grid base PNG could not be decoded.");
        using var overlayBitmap = SKBitmap.Decode(overlayPath)
            ?? throw new InvalidDataException("The captured drawing-overlay PNG could not be decoded.");
        if (baseBitmap.Width != overlayBitmap.Width || baseBitmap.Height != overlayBitmap.Height)
            throw new InvalidDataException("The captured grid base and chart overlay dimensions differ.");

        using var composed = new SKBitmap(baseBitmap.Width, baseBitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(composed))
            canvas.DrawBitmap(baseBitmap, 0, 0);

        for (var y = 0; y < composed.Height; y++)
        {
            for (var x = 0; x < composed.Width; x++)
            {
                var overlayPixel = overlayBitmap.GetPixel(x, y);
                var inOverlayBounds = overlayPixelBounds.Any(bounds => bounds.Contains(x, y));
                if (overlayPixel.Alpha == 0 ||
                    (!inOverlayBounds && overlayPixel.Red == 0 && overlayPixel.Green == 0 && overlayPixel.Blue == 0))
                {
                    continue;
                }

                composed.SetPixel(x, y, overlayPixel);
            }
        }

        using var image = SKImage.FromBitmap(composed);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        if (encoded is null)
            throw new InvalidDataException("The composited chart PNG could not be encoded.");

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(outputPath, encoded.ToArray());
    }

    private sealed record GridCaptureOverlayLayer(int Index, Control Control);

    private static GridCaptureResult GridCaptureFailure(
        string workbookPath,
        string rangeText,
        string outputDirectory,
        string note)
    {
        var pngFileName = MakeSafeFileName($"capture_{rangeText}") + ".png";
        return new GridCaptureResult(
            Captured: false,
            PngPath: Path.Combine(outputDirectory, pngFileName),
            PngFileName: pngFileName,
            WidthPx: 0,
            HeightPx: 0,
            SheetName: Path.GetFileNameWithoutExtension(workbookPath),
            RangeText: rangeText,
            Note: note);
    }

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        return sb.ToString();
    }
}
