namespace FreeX.Ribbon.Definitions;

/// <summary>
/// Semantic command ids for ribbon actions whose display labels are not unique. These ids describe
/// the command route and deliberately contain no WPF handler implementation details.
/// </summary>
public static class FreeXRibbonCommandIds
{
    public const string DrawingSelectionPane = "drawing.selectionPane";

    public const string PageLayoutThemeOffice = "pageLayout.theme.office";
    public const string PageLayoutThemeColorful = "pageLayout.theme.colorful";
    public const string PageLayoutThemeGrayscale = "pageLayout.theme.grayscale";
    public const string PageLayoutThemeColorsOffice = "pageLayout.themeColors.office";
    public const string PageLayoutThemeColorsColorful = "pageLayout.themeColors.colorful";
    public const string PageLayoutThemeColorsGrayscale = "pageLayout.themeColors.grayscale";
    public const string PageLayoutThemeFontsOffice = "pageLayout.themeFonts.office";
    public const string PageLayoutThemeEffectsOffice = "pageLayout.themeEffects.office";
    public const string PageLayoutMarginsNormal = "pageLayout.margins.normal";

    public const string FormulasAutoSum = "formulas.autoSum";
    public const string FormulasAutoSumMoreFunctions = "formulas.autoSum.moreFunctions";
    public const string FormulasMoreFunctions = "formulas.moreFunctions";
    public const string FormulasRemoveArrows = "formulas.removeArrows";
    public const string FormulasRemoveAllArrows = "formulas.removeArrows.all";

    public const string DataSortAscending = "data.sort.ascending";
    public const string DataSortDescending = "data.sort.descending";
    public const string DataFilter = "data.filter";
    public const string DataClearFilter = "data.filter.clear";
    public const string DataRemoveDuplicates = "data.removeDuplicates";
    public const string DataValidation = "data.validation";
    public const string DataOutlineGroup = "data.outline.group";
    public const string DataOutlineGroupRows = "data.outline.groupRows";
    public const string DataOutlineUngroup = "data.outline.ungroup";
    public const string DataOutlineUngroupRows = "data.outline.ungroupRows";

    public const string ReviewProtectSheet = "review.protectSheet";

    public const string ViewNormal = "view.normal";
    public const string ViewZoom100 = "view.zoom.100";
    public const string ViewZoomPreset100 = "view.zoom.preset.100";
    public const string ViewArrangeHorizontal = "view.arrange.horizontal";
    public const string ViewFreezePanes = "view.freezePanes";
    public const string ViewFreezeAtSelection = "view.freezePanes.selection";

    public const string HelpOnline = "help.online";
    public const string HelpFeedback = "help.feedback";
    public const string HelpCopyDiagnostics = "help.copyDiagnostics";
    public const string HelpCheckForUpdates = "help.checkForUpdates";
    public const string HelpAbout = "help.about";
    public const string HelpLegalNotices = "help.legalNotices";

    public const string ChartChangeType = "chart.changeType";
    public const string TableRemoveDuplicates = "table.removeDuplicates";
    public const string TableBandedRows = "Banded Rows";
    public const string TableBandedColumns = "Banded Columns";
    public const string PivotUngroup = "pivot.ungroup";
    public const string PivotClear = "pivot.clear";
    public const string PivotChartChangeType = "pivot.chart.changeType";
    public const string PivotBandedRows = "pivot.bandedRows";
    public const string PivotBandedColumns = "pivot.bandedColumns";
}
