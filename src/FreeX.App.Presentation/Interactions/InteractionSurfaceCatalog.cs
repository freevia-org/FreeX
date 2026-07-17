using Free.Shared.AppServices;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Presentation.Interactions;

public enum InteractionSurfaceKind
{
    Dialog,
    ContextMenu
}

public enum InteractionSurfaceModality
{
    Modal,
    Modeless,
    Transient
}

public enum InteractionPlatform
{
    Wpf,
    PortableDesktop
}

public enum InteractionImplementationCapability
{
    NotApplicable,
    Missing,
    Unverified,
    ManagedSurface,
    NativeSurface
}

public enum InteractionNativeBoundary
{
    None,
    NativeApplicationMenu
}

public enum InteractionExpectation
{
    Required,
    WhenActionExists,
    NotApplicable
}

public enum DialogSurfaceFamily
{
    Application,
    Workbook,
    Worksheet,
    Formatting,
    Data,
    Formula,
    Chart,
    Pivot,
    Drawing,
    Review,
    PageLayout,
    Protection,
    View
}

public enum ContextMenuFamily
{
    WorksheetTargetsAndStateVariants,
    SheetTabs,
    StatusBar,
    PivotField,
    PivotHeader,
    PivotChart,
    RecentFiles,
    QuickAccessToolbar,
    WaterfallPoint,
    AutoFilterCriteria,
    NativeApplicationMenu
}

public sealed record InteractionExpectations(
    InteractionExpectation Open,
    InteractionExpectation InitialFocus,
    InteractionExpectation TabTraversal,
    InteractionExpectation EnterSubmit,
    InteractionExpectation EscapeCancel,
    InteractionExpectation FocusReturn);

public sealed record InteractionPlatformCapability(
    bool? IsApplicable,
    InteractionImplementationCapability Implementation,
    InteractionNativeBoundary NativeBoundary = InteractionNativeBoundary.None);

public sealed record InteractionPlatformCapabilities(
    InteractionPlatformCapability Wpf,
    InteractionPlatformCapability PortableDesktop)
{
    public InteractionPlatformCapability For(InteractionPlatform platform) =>
        platform == InteractionPlatform.Wpf ? Wpf : PortableDesktop;
}

public sealed record InteractionSurfaceSource(
    string CatalogOrPlanner,
    IReadOnlyList<string>? VariantSources = null)
{
    public IReadOnlyList<string> VariantSources { get; init; } = VariantSources ?? [];
}

public sealed record InteractionSurfaceVariant(
    string Id,
    string Name,
    IReadOnlyList<string>? Prerequisites = null)
{
    public IReadOnlyList<string> Prerequisites { get; init; } = Prerequisites ?? [];
}

public sealed record InteractionSurfaceCatalogRow(
    string Id,
    string Name,
    InteractionSurfaceKind Kind,
    string Owner,
    string Family,
    IReadOnlyList<string> Prerequisites,
    InteractionSurfaceModality Modality,
    InteractionExpectations Expectations,
    InteractionPlatformCapabilities Platforms,
    InteractionSurfaceSource Source,
    DialogSurfaceFamily? DialogFamily = null,
    ContextMenuFamily? ContextFamily = null,
    IReadOnlyList<InteractionSurfaceVariant>? Variants = null)
{
    public IReadOnlyList<InteractionSurfaceVariant> Variants { get; init; } = Variants ?? [];

    public bool IsApplicableTo(InteractionPlatform platform) => Platforms.For(platform).IsApplicable == true;
}

/// <summary>
/// Authoritative, renderer-neutral inventory of FreeX logical dialogs and context-menu families.
/// Shell validators consume the stable ids and platform capabilities; command membership remains owned
/// by the planner or catalog named in <see cref="InteractionSurfaceCatalogRow.Source"/>.
/// </summary>
public static class InteractionSurfaceCatalog
{
    private static readonly InteractionPlatformCapability ManagedCapability =
        new(IsApplicable: true, InteractionImplementationCapability.ManagedSurface);

    private static readonly InteractionPlatformCapabilities ManagedOnBothPlatforms =
        new(ManagedCapability, ManagedCapability);

    private static readonly InteractionPlatformCapabilities WpfManagedPortableUnverified =
        new(
            ManagedCapability,
            new InteractionPlatformCapability(
                IsApplicable: null,
                InteractionImplementationCapability.Unverified));

    private static readonly InteractionPlatformCapabilities WpfManagedPortableMissing =
        new(
            ManagedCapability,
            new InteractionPlatformCapability(
                IsApplicable: false,
                InteractionImplementationCapability.Missing));

    private static readonly InteractionExpectations DialogExpectations =
        new(
            InteractionExpectation.Required,
            InteractionExpectation.Required,
            InteractionExpectation.Required,
            InteractionExpectation.WhenActionExists,
            InteractionExpectation.Required,
            InteractionExpectation.Required);

    private static readonly InteractionExpectations ContextMenuExpectations =
        new(
            InteractionExpectation.Required,
            InteractionExpectation.Required,
            InteractionExpectation.Required,
            InteractionExpectation.Required,
            InteractionExpectation.Required,
            InteractionExpectation.Required);

    private static readonly HashSet<string> ModelessDialogNames = new(StringComparer.Ordinal)
    {
        "AutoFilterDialog",
        "CommentListWindow",
        "ErrorCheckingDialog",
        "FindReplaceDialog",
        "WatchWindowDialog"
    };

    public static IReadOnlyList<InteractionSurfaceCatalogRow> Dialogs => CatalogRows.Dialogs;

    public static IReadOnlyList<InteractionSurfaceCatalogRow> ContextMenus => CatalogRows.ContextMenus;

    public static IReadOnlyList<InteractionSurfaceCatalogRow> Rows => CatalogRows.All;

    public static IEnumerable<InteractionSurfaceCatalogRow> ForPlatform(InteractionPlatform platform) =>
        Rows.Where(row => row.IsApplicableTo(platform));

    public static string GetPlatformId(InteractionPlatform platform) =>
        platform switch
        {
            InteractionPlatform.Wpf => "wpf",
            InteractionPlatform.PortableDesktop => "avalonia",
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
        };

    public static bool TryGet(string id, out InteractionSurfaceCatalogRow? row)
    {
        row = Rows.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        return row is not null;
    }

    private static IReadOnlyList<InteractionSurfaceCatalogRow> BuildDialogs() =>
        Array.AsReadOnly(DialogDefinitions.Select(CreateDialogRow).ToArray());

    private static class CatalogRows
    {
        public static IReadOnlyList<InteractionSurfaceCatalogRow> Dialogs { get; } = BuildDialogs();

        public static IReadOnlyList<InteractionSurfaceCatalogRow> ContextMenus { get; } = BuildContextMenus();

        public static IReadOnlyList<InteractionSurfaceCatalogRow> All { get; } =
            Array.AsReadOnly(Dialogs.Concat(ContextMenus).ToArray());
    }

    private static InteractionSurfaceCatalogRow CreateDialogRow(DialogDefinition definition)
    {
        var modality = ModelessDialogNames.Contains(definition.Name)
            ? InteractionSurfaceModality.Modeless
            : InteractionSurfaceModality.Modal;
        var owner = definition.Name switch
        {
            "MergeCellsContentWarningDialog" => "WpfHost.MainWindow.ShowMergeCellsContentWarningDialog",
            _ => $"WpfHost.{definition.Name}"
        };

        return new InteractionSurfaceCatalogRow(
            Id: $"dialog.{definition.Name}",
            Name: definition.Name,
            Kind: InteractionSurfaceKind.Dialog,
            Owner: owner,
            Family: definition.Family.ToString(),
            Prerequisites: PrerequisitesFor(definition.Family),
            Modality: modality,
            Expectations: DialogExpectations,
            Platforms: WpfManagedPortableUnverified,
            Source: new InteractionSurfaceSource(owner),
            DialogFamily: definition.Family);
    }

    private static IReadOnlyList<string> PrerequisitesFor(DialogSurfaceFamily family) =>
        family switch
        {
            DialogSurfaceFamily.Application => ["Application shell is running"],
            DialogSurfaceFamily.Workbook => ["Workbook is open"],
            DialogSurfaceFamily.Worksheet => ["Workbook is open", "Worksheet is active"],
            DialogSurfaceFamily.Formatting => ["Worksheet is active", "Cell or range selection is available"],
            DialogSurfaceFamily.Data => ["Worksheet is active", "Data command target is available"],
            DialogSurfaceFamily.Formula => ["Worksheet is active", "Formula command target is available"],
            DialogSurfaceFamily.Chart => ["Chart command target is available"],
            DialogSurfaceFamily.Pivot => ["PivotTable or PivotChart command target is available"],
            DialogSurfaceFamily.Drawing => ["Drawing object command target is available"],
            DialogSurfaceFamily.Review => ["Workbook review command target is available"],
            DialogSurfaceFamily.PageLayout => ["Worksheet is active", "Page-layout command target is available"],
            DialogSurfaceFamily.Protection => ["Workbook or worksheet protection command target is available"],
            DialogSurfaceFamily.View => ["Workbook view command target is available"],
            _ => ["Application shell is running"]
        };

    private static IReadOnlyList<InteractionSurfaceCatalogRow> BuildContextMenus() =>
        Array.AsReadOnly(new[]
        {
            ContextRow(
                "context-menu.worksheet",
                "Worksheet targets and state variants",
                ContextMenuFamily.WorksheetTargetsAndStateVariants,
                "FreeX.App.Services.Ribbon.WorksheetContextMenuPlanner",
                ["Worksheet is active", "Pointer or keyboard invocation target is resolved"],
                WorksheetVariants,
                [
                    "FreeX.App.Services.Ribbon.WorksheetContextMenuTargetKind",
                    "FreeX.App.Services.Ribbon.WorksheetContextMenuState"
                ]),
            ContextRow(
                "context-menu.sheet-tabs",
                "Sheet tabs",
                ContextMenuFamily.SheetTabs,
                "FreeX.App.Services.Ribbon.SheetTabContextMenuPlanner",
                ["Workbook is open", "Sheet tab is targeted"],
                Variants("default", "restricted-state"),
                ["FreeX.App.Services.Ribbon.SheetTabContextMenuState"]),
            CreateStatusBarRow(),
            ContextRow(
                "context-menu.pivot-field",
                "Pivot field list and area buckets",
                ContextMenuFamily.PivotField,
                "FreeX.App.Services.Ribbon.PivotFieldContextMenuPlanner",
                ["Pivot field is targeted"],
                Variants("available-fields", "filters-bucket", "columns-bucket", "rows-bucket", "values-bucket"),
                ["FreeX.App.Services.Ribbon.PivotFieldContextMenuPlanner.BuildPivotFieldCommands"],
                WpfManagedPortableMissing),
            CreatePivotHeaderRow(),
            ContextRow(
                "context-menu.pivot-chart",
                "PivotChart field button",
                ContextMenuFamily.PivotChart,
                "FreeX.App.Services.Ribbon.PivotChartFieldContextMenuPlanner",
                ["PivotChart field button or pivot header is targeted"],
                Variants("filter-state", "no-filter-state"),
                ["FreeX.App.Services.Ribbon.PivotChartFieldContextMenuState"],
                WpfManagedPortableMissing),
            ContextRow(
                "context-menu.recent-files",
                "Backstage recent files",
                ContextMenuFamily.RecentFiles,
                "FreeX.App.Services.Ribbon.BackstageRecentFileContextMenuPlanner",
                ["Backstage recent-file item is targeted"],
                Variants("recent", "pinned"),
                ["FreeX.App.Services.Ribbon.BackstageRecentFileContextMenuPlanner"],
                WpfManagedPortableMissing),
            ContextRow(
                "context-menu.quick-access-toolbar",
                "Quick Access Toolbar",
                ContextMenuFamily.QuickAccessToolbar,
                "FreeX.App.Services.Ribbon.QuickAccessToolbarContextMenuPlanner",
                ["Quick Access Toolbar command or history control is targeted"],
                Variants("customization", "undo-history", "redo-history"),
                [
                    "FreeX.App.Services.Ribbon.QuickAccessToolbarCustomizationMenuState",
                    "FreeX.App.Services.Ribbon.QuickAccessToolbarHistoryMenuState"
                ],
                WpfManagedPortableMissing),
            ContextRow(
                "context-menu.waterfall-point",
                "Waterfall chart point",
                ContextMenuFamily.WaterfallPoint,
                "FreeX.App.Services.Ribbon.WaterfallChartContextMenuPlanner",
                ["Waterfall chart data point is targeted"],
                Variants("regular-point", "total-point", "invalid-point"),
                ["FreeX.Core.Model.ChartModel", "pointIndex"],
                WpfManagedPortableMissing),
            CreateAutoFilterCriteriaRow(),
            CreateNativeApplicationMenuRow()
        });

    private static InteractionSurfaceCatalogRow CreateStatusBarRow()
    {
        var variants = StatusBarCustomizeContextMenuPlanner.BuildStatusBarCustomizeCommands()
            .Where(command => !command.IsSeparator && command.OptionTag.Length > 0)
            .Select(command => new InteractionSurfaceVariant(
                $"context-menu.status-bar.option.{command.OptionTag}",
                command.OptionTag))
            .ToArray();

        return ContextRow(
            "context-menu.status-bar",
            "Status bar customization",
            ContextMenuFamily.StatusBar,
            "Free.Shared.AppServices.StatusBarCustomizeContextMenuPlanner",
            ["Status bar is visible"],
            variants,
            ["Free.Shared.AppServices.StatusBarOptionTags"]);
    }

    private static InteractionSurfaceCatalogRow CreatePivotHeaderRow()
    {
        var variants = Enum.GetValues<PivotHeaderArea>()
            .Select(area => new InteractionSurfaceVariant(
                $"context-menu.pivot-header.area.{area.ToString().ToLowerInvariant()}",
                area.ToString()))
            .ToArray();

        return ContextRow(
            "context-menu.pivot-header",
            "Pivot header dropdown",
            ContextMenuFamily.PivotHeader,
            "FreeX.App.Presentation.PivotUI.PivotHeaderDropdownMenuBuilder",
            ["Pivot header dropdown target is resolved"],
            variants,
            [
                "FreeX.App.Presentation.PivotUI.PivotHeaderArea",
                "FreeX.App.Presentation.PivotUI.PivotHeaderDropdownTargetModel"
            ]);
    }

    private static InteractionSurfaceCatalogRow CreateAutoFilterCriteriaRow()
    {
        var variants = Enum.GetValues<AutoFilterMenuFilterKind>()
            .SelectMany(filterKind => AutoFilterMenuCatalog.GetCriteriaDescriptors(filterKind)
                .Select(criteria => new InteractionSurfaceVariant(
                    $"context-menu.auto-filter.{filterKind.ToString().ToLowerInvariant()}.{criteria.ResourceKey}",
                    criteria.ResourceKey,
                    [$"Detected filter kind is {filterKind}"])))
            .ToArray();

        return ContextRow(
            "context-menu.auto-filter-criteria",
            "AutoFilter criteria",
            ContextMenuFamily.AutoFilterCriteria,
            "FreeX.App.Presentation.Filtering.AutoFilterMenuCatalog",
            ["AutoFilter header is targeted"],
            variants,
            [
                "FreeX.App.Presentation.Filtering.AutoFilterMenuFilterKind",
                "FreeX.App.Presentation.Filtering.AutoFilterCriteriaDescriptor"
            ]);
    }

    private static InteractionSurfaceCatalogRow CreateNativeApplicationMenuRow()
    {
        var variants = NativeMenuCatalog.TopLevelMenus
            .Select(menu => new InteractionSurfaceVariant(
                $"context-menu.native-application.{menu.Id.ToString().ToLowerInvariant()}",
                menu.Id.ToString()))
            .ToArray();
        var platforms = new InteractionPlatformCapabilities(
            new InteractionPlatformCapability(
                IsApplicable: false,
                InteractionImplementationCapability.NotApplicable),
            new InteractionPlatformCapability(
                IsApplicable: true,
                InteractionImplementationCapability.NativeSurface,
                InteractionNativeBoundary.NativeApplicationMenu));

        return ContextRow(
            "context-menu.native-application",
            "Native application menu",
            ContextMenuFamily.NativeApplicationMenu,
            "FreeX.App.Presentation.Shell.NativeMenuCatalog",
            ["Portable desktop lifetime exposes a native application menu"],
            variants,
            ["FreeX.App.Presentation.Shell.NativeMenuTopLevelId"],
            platforms);
    }

    private static InteractionSurfaceCatalogRow ContextRow(
        string id,
        string name,
        ContextMenuFamily family,
        string owner,
        IReadOnlyList<string> prerequisites,
        IReadOnlyList<InteractionSurfaceVariant> variants,
        IReadOnlyList<string> variantSources,
        InteractionPlatformCapabilities? platforms = null) =>
        new(
            Id: id,
            Name: name,
            Kind: InteractionSurfaceKind.ContextMenu,
            Owner: owner,
            Family: family.ToString(),
            Prerequisites: prerequisites,
            Modality: InteractionSurfaceModality.Transient,
            Expectations: ContextMenuExpectations,
            Platforms: platforms ?? ManagedOnBothPlatforms,
            Source: new InteractionSurfaceSource(owner, variantSources),
            ContextFamily: family,
            Variants: variants);

    private static IReadOnlyList<InteractionSurfaceVariant> Variants(params string[] names) =>
        names.Select(name => new InteractionSurfaceVariant($"variant.{name}", name)).ToArray();

    private static readonly InteractionSurfaceVariant[] WorksheetVariants =
    [
        new("context-menu.worksheet.target.worksheet", "Worksheet"),
        new("context-menu.worksheet.target.picture", "Picture"),
        new("context-menu.worksheet.target.shape", "Shape"),
        new("context-menu.worksheet.target.text-box", "TextBox"),
        new("context-menu.worksheet.target.chart", "Chart"),
        new("context-menu.worksheet.target.row-selection", "RowSelection"),
        new("context-menu.worksheet.target.column-selection", "ColumnSelection"),
        new("context-menu.worksheet.state.threaded-comment", "HasThreadedComment"),
        new("context-menu.worksheet.state.resolved-comment", "IsThreadedCommentResolved"),
        new("context-menu.worksheet.state.note", "HasNote"),
        new("context-menu.worksheet.state.hyperlink", "HasHyperlink"),
        new("context-menu.worksheet.state.auto-filter-header", "HasAutoFilterHeaderTarget"),
        new("context-menu.worksheet.state.dropdown", "HasDropdownTarget"),
        new("context-menu.worksheet.state.pivot-table", "HasPivotTableTarget"),
        new("context-menu.worksheet.state.note-shown", "NoteIsShown")
    ];

    private sealed record DialogDefinition(string Name, DialogSurfaceFamily Family);

    private static readonly DialogDefinition[] DialogDefinitions =
    [
        new("AboutDialog", DialogSurfaceFamily.Application),
        new("AccessibilityCheckerDialog", DialogSurfaceFamily.Review),
        new("ActivateSheetDialog", DialogSurfaceFamily.Workbook),
        new("AddWatchDialog", DialogSurfaceFamily.Formula),
        new("AdvancedFilterDialog", DialogSurfaceFamily.Data),
        new("AllowEditRangeDialog", DialogSurfaceFamily.Protection),
        new("AutoFilterDialog", DialogSurfaceFamily.Data),
        new("BookmarkDialog", DialogSurfaceFamily.Review),
        new("CellShiftDialog", DialogSurfaceFamily.Worksheet),
        new("ChangeChartTypeDialog", DialogSurfaceFamily.Chart),
        new("ChartAreaLegendDialog", DialogSurfaceFamily.Chart),
        new("ChartAxisFormatDialog", DialogSurfaceFamily.Chart),
        new("ChartBarFormatDialog", DialogSurfaceFamily.Chart),
        new("ChartBubbleFormatDialog", DialogSurfaceFamily.Chart),
        new("ChartDataLabelsDialog", DialogSurfaceFamily.Chart),
        new("ChartErrorBarsDialog", DialogSurfaceFamily.Chart),
        new("ChartPieFormatDialog", DialogSurfaceFamily.Chart),
        new("ChartSeriesFormatDialog", DialogSurfaceFamily.Chart),
        new("ChartStockFormatDialog", DialogSurfaceFamily.Chart),
        new("ChartStyleDialog", DialogSurfaceFamily.Chart),
        new("ChartTitlesDialog", DialogSurfaceFamily.Chart),
        new("ChartTrendlineOptionsDialog", DialogSurfaceFamily.Chart),
        new("ColorPickerDialog", DialogSurfaceFamily.Formatting),
        new("ColorScaleRuleDialog", DialogSurfaceFamily.Formatting),
        new("ColumnWidthDialog", DialogSurfaceFamily.Formatting),
        new("CommentListWindow", DialogSurfaceFamily.Review),
        new("ConditionalFormatDialog", DialogSurfaceFamily.Formatting),
        new("ConditionalFormatThresholdDialog", DialogSurfaceFamily.Formatting),
        new("ConfirmPasswordDialog", DialogSurfaceFamily.Protection),
        new("ConsolidateDialog", DialogSurfaceFamily.Data),
        new("CreateNamesFromSelectionDialog", DialogSurfaceFamily.Formula),
        new("CreateTableDialog", DialogSurfaceFamily.Data),
        new("CustomViewNameDialog", DialogSurfaceFamily.View),
        new("CustomViewsDialog", DialogSurfaceFamily.View),
        new("DataBarRuleDialog", DialogSurfaceFamily.Formatting),
        new("DataTableDialog", DialogSurfaceFamily.Data),
        new("DataValidationDialog", DialogSurfaceFamily.Data),
        new("ErrorCheckingDialog", DialogSurfaceFamily.Formula),
        new("EvaluateFormulaDialog", DialogSurfaceFamily.Formula),
        new("ExportOptionsDialog", DialogSurfaceFamily.PageLayout),
        new("FillSeriesStepDialog", DialogSurfaceFamily.Data),
        new("FindReplaceDialog", DialogSurfaceFamily.Worksheet),
        new("ForecastSheetDialog", DialogSurfaceFamily.Data),
        new("FormatCellsDialog", DialogSurfaceFamily.Formatting),
        new("FormatPictureDialog", DialogSurfaceFamily.Drawing),
        new("FunctionArgumentsDialog", DialogSurfaceFamily.Formula),
        new("GoalSeekDialog", DialogSurfaceFamily.Data),
        new("GoalSeekStatusDialog", DialogSurfaceFamily.Data),
        new("GoToDialog", DialogSurfaceFamily.Worksheet),
        new("GoToSpecialDialog", DialogSurfaceFamily.Worksheet),
        new("HeaderFooterDialog", DialogSurfaceFamily.PageLayout),
        new("HeaderFooterPictureFormatDialog", DialogSurfaceFamily.PageLayout),
        new("HighlightCellsRuleDialog", DialogSurfaceFamily.Formatting),
        new("HyperlinkDialog", DialogSurfaceFamily.Worksheet),
        new("IconSetRuleDialog", DialogSurfaceFamily.Formatting),
        new("InsertChartDialog", DialogSurfaceFamily.Chart),
        new("InsertFunctionDialog", DialogSurfaceFamily.Formula),
        new("InsertSlicerDialog", DialogSurfaceFamily.Pivot),
        new("InsertTimelineDialog", DialogSurfaceFamily.Pivot),
        new("LegalNoticesDialog", DialogSurfaceFamily.Application),
        new("ManageConditionalFormatsDialog", DialogSurfaceFamily.Formatting),
        new("MergeCellsContentWarningDialog", DialogSurfaceFamily.Formatting),
        new("MoveChartDialog", DialogSurfaceFamily.Chart),
        new("MoveOrCopySheetDialog", DialogSurfaceFamily.Worksheet),
        new("MovePivotTableDialog", DialogSurfaceFamily.Pivot),
        new("NameDefinitionDialog", DialogSurfaceFamily.Formula),
        new("NamedRangeDialog", DialogSurfaceFamily.Formula),
        new("NewConditionalFormatRuleDialog", DialogSurfaceFamily.Formatting),
        new("ObjectSizeDialog", DialogSurfaceFamily.Drawing),
        new("OptionsDialog", DialogSurfaceFamily.Application),
        new("OutlineGroupDialog", DialogSurfaceFamily.Data),
        new("PageBreakDialog", DialogSurfaceFamily.PageLayout),
        new("PageSetupDialog", DialogSurfaceFamily.PageLayout),
        new("PasswordProtectionDialog", DialogSurfaceFamily.Protection),
        new("PasteNamesDialog", DialogSurfaceFamily.Formula),
        new("PasteSpecialDialog", DialogSurfaceFamily.Worksheet),
        new("PictureCropDialog", DialogSurfaceFamily.Drawing),
        new("PivotCalculatedFieldDialog", DialogSurfaceFamily.Pivot),
        new("PivotCalculatedItemDialog", DialogSurfaceFamily.Pivot),
        new("PivotChartOptionsDialog", DialogSurfaceFamily.Pivot),
        new("PivotChartTypeDialog", DialogSurfaceFamily.Pivot),
        new("PivotFieldFilterDialog", DialogSurfaceFamily.Pivot),
        new("PivotFieldGroupingDialog", DialogSurfaceFamily.Pivot),
        new("PivotLabelFilterDialog", DialogSurfaceFamily.Pivot),
        new("PivotSortOptionsDialog", DialogSurfaceFamily.Pivot),
        new("PivotStyleGalleryDialog", DialogSurfaceFamily.Pivot),
        new("PivotTableDataSourceDialog", DialogSurfaceFamily.Pivot),
        new("PivotTableDialog", DialogSurfaceFamily.Pivot),
        new("PivotTableNameDialog", DialogSurfaceFamily.Pivot),
        new("PivotTableOptionsDialog", DialogSurfaceFamily.Pivot),
        new("PivotValueFieldSettingsDialog", DialogSurfaceFamily.Pivot),
        new("PivotValueFilterDialog", DialogSurfaceFamily.Pivot),
        new("PrintPreviewDialog", DialogSurfaceFamily.PageLayout),
        new("RecommendedPivotTablesDialog", DialogSurfaceFamily.Pivot),
        new("RemoveDuplicatesDialog", DialogSurfaceFamily.Data),
        new("RotationDialog", DialogSurfaceFamily.Drawing),
        new("RowHeightDialog", DialogSurfaceFamily.Formatting),
        new("ScenarioManagerDialog", DialogSurfaceFamily.Data),
        new("ScreenTipDialog", DialogSurfaceFamily.Review),
        new("SelectDataSourceDialog", DialogSurfaceFamily.Chart),
        new("SelectionPaneDialog", DialogSurfaceFamily.Drawing),
        new("ShapeEffectsDialog", DialogSurfaceFamily.Drawing),
        new("ShapeGradientDialog", DialogSurfaceFamily.Drawing),
        new("SheetNameDialog", DialogSurfaceFamily.Worksheet),
        new("SortDialog", DialogSurfaceFamily.Data),
        new("SortOptionsDialog", DialogSurfaceFamily.Data),
        new("SparklineDialog", DialogSurfaceFamily.Chart),
        new("SpellCheckDialog", DialogSurfaceFamily.Review),
        new("SubtotalDialog", DialogSurfaceFamily.Data),
        new("SymbolPickerDialog", DialogSurfaceFamily.Worksheet),
        new("TextEntryDialog", DialogSurfaceFamily.Worksheet),
        new("TextToColumnsDialog", DialogSurfaceFamily.Data),
        new("ThreadedCommentDialog", DialogSurfaceFamily.Review),
        new("TopBottomRuleDialog", DialogSurfaceFamily.Formatting),
        new("UnhideSheetDialog", DialogSurfaceFamily.Worksheet),
        new("UnhideWindowDialog", DialogSurfaceFamily.View),
        new("WatchWindowDialog", DialogSurfaceFamily.Formula),
        new("WorkbookStatisticsDialog", DialogSurfaceFamily.Workbook),
        new("WorkbookThemeDialog", DialogSurfaceFamily.Formatting),
        new("ZoomDialog", DialogSurfaceFamily.View)
    ];
}
