namespace FreeX.Ribbon.Definitions;

/// <summary>
/// Semantic command ids for ribbon actions whose display labels are not unique. These ids describe
/// the command route and deliberately contain no WPF handler implementation details.
/// </summary>
public static class FreeXRibbonCommandIds
{
    public const string DrawingSelectionPane = "drawing.selectionPane";
    public const string DrawingCrop = "drawing.crop";
    public const string DrawingResetCrop = "drawing.crop.reset";
    public const string DrawingShapeEffectNone = "drawing.shapeEffect.none";
    public const string DrawingShapeEffectShadow = "drawing.shapeEffect.shadow";
    public const string DrawingShapeEffectInnerShadow = "drawing.shapeEffect.innerShadow";
    public const string DrawingShapeEffectReflection = "drawing.shapeEffect.reflection";
    public const string DrawingShapeEffectGlow = "drawing.shapeEffect.glow";
    public const string DrawingShapeEffectSoftEdges = "drawing.shapeEffect.softEdges";
    public const string DrawingShapeEffectBevel = "drawing.shapeEffect.bevel";
    public const string DrawingShapeEffectThreeDRotation = "drawing.shapeEffect.threeDRotation";

    public const string PageLayoutThemeOffice = "pageLayout.theme.office";
    public const string PageLayoutThemeColorful = "pageLayout.theme.colorful";
    public const string PageLayoutThemeGrayscale = "pageLayout.theme.grayscale";
    public const string PageLayoutThemeColorsOffice = "pageLayout.themeColors.office";
    public const string PageLayoutThemeColorsColorful = "pageLayout.themeColors.colorful";
    public const string PageLayoutThemeColorsGrayscale = "pageLayout.themeColors.grayscale";
    public const string PageLayoutThemeFontsOffice = "pageLayout.themeFonts.office";
    public const string PageLayoutThemeFontsArial = "pageLayout.themeFonts.arial";
    public const string PageLayoutThemeFontsTimesNewRoman = "pageLayout.themeFonts.timesNewRoman";
    public const string PageLayoutThemeFontsCustomize = "pageLayout.themeFonts.customize";
    public const string PageLayoutThemeEffectsOffice = "pageLayout.themeEffects.office";
    public const string PageLayoutThemeEffectsSubtle = "pageLayout.themeEffects.subtle";
    public const string PageLayoutThemeEffectsRefined = "pageLayout.themeEffects.refined";
    public const string PageLayoutThemeEffectsCustomize = "pageLayout.themeEffects.customize";
    public const string PageLayoutThemeCustomize = "pageLayout.theme.customize";
    public const string PageLayoutThemeColorsCustomize = "pageLayout.themeColors.customize";
    public const string PageLayoutMarginsNormal = "pageLayout.margins.normal";
    public const string PageLayoutMarginsWide = "pageLayout.margins.wide";
    public const string PageLayoutMarginsNarrow = "pageLayout.margins.narrow";
    public const string PageLayoutMarginsCustom = "pageLayout.margins.custom";
    public const string PageLayoutOrientationPortrait = "pageLayout.orientation.portrait";
    public const string PageLayoutOrientationLandscape = "pageLayout.orientation.landscape";
    public const string PageLayoutPaperSizeLetter = "pageLayout.paperSize.letter";
    public const string PageLayoutPaperSizeLegal = "pageLayout.paperSize.legal";
    public const string PageLayoutPaperSizeExecutive = "pageLayout.paperSize.executive";
    public const string PageLayoutPaperSizeStatement = "pageLayout.paperSize.statement";
    public const string PageLayoutPaperSizeTabloid = "pageLayout.paperSize.tabloid";
    public const string PageLayoutPaperSizeA4 = "pageLayout.paperSize.a4";
    public const string PageLayoutPaperSizeA3 = "pageLayout.paperSize.a3";
    public const string PageLayoutPaperSizeA5 = "pageLayout.paperSize.a5";
    public const string PageLayoutPaperSizeB4Jis = "pageLayout.paperSize.b4Jis";
    public const string PageLayoutPaperSizeB5Jis = "pageLayout.paperSize.b5Jis";
    public const string PageLayoutPrintAreaSet = "pageLayout.printArea.set";
    public const string PageLayoutPrintAreaClear = "pageLayout.printArea.clear";
    public const string PageLayoutBreakInsert = "pageLayout.break.insert";
    public const string PageLayoutBreakRemove = "pageLayout.break.remove";
    public const string PageLayoutBreakResetAll = "pageLayout.break.resetAll";
    public const string PageLayoutBackgroundChoose = "pageLayout.background.choose";
    public const string PageLayoutBackgroundDelete = "pageLayout.background.delete";

    public const string FormulasAutoSum = "formulas.autoSum";
    public const string FormulasAutoSumSum = "formulas.autoSum.sum";
    public const string FormulasAutoSumAverage = "formulas.autoSum.average";
    public const string FormulasAutoSumCountNumbers = "formulas.autoSum.countNumbers";
    public const string FormulasAutoSumCountAll = "formulas.autoSum.countAll";
    public const string FormulasAutoSumMax = "formulas.autoSum.max";
    public const string FormulasAutoSumMin = "formulas.autoSum.min";
    public const string FormulasAutoSumMoreFunctions = "formulas.autoSum.moreFunctions";
    public const string FormulasMoreFunctions = "formulas.moreFunctions";
    public const string FormulasRemoveArrows = "formulas.removeArrows";
    public const string FormulasRemoveAllArrows = "formulas.removeArrows.all";
    public const string FormulasRemovePrecedentArrows = "formulas.removeArrows.precedent";
    public const string FormulasRemoveDependentArrows = "formulas.removeArrows.dependent";
    public const string FormulasErrorCheckingRun = "formulas.errorChecking.run";
    public const string FormulasErrorCheckingOptions = "formulas.errorChecking.options";
    public const string FormulasCalculationAutomatic = "formulas.calculation.automatic";
    public const string FormulasCalculationAutomaticExceptDataTables = "formulas.calculation.automaticExceptDataTables";
    public const string FormulasCalculationManual = "formulas.calculation.manual";

    public const string DataSortAscending = "data.sort.ascending";
    public const string DataSortDescending = "data.sort.descending";
    public const string DataFilter = "data.filter";
    public const string DataClearFilter = "data.filter.clear";
    public const string DataRemoveDuplicates = "data.removeDuplicates";
    public const string DataValidation = "data.validation";
    public const string DataValidationCircleInvalid = "data.validation.circleInvalid";
    public const string DataValidationClearCircles = "data.validation.clearCircles";
    public const string DataWhatIfGoalSeek = "data.whatIf.goalSeek";
    public const string DataWhatIfScenarioManager = "data.whatIf.scenarioManager";
    public const string DataWhatIfDataTable = "data.whatIf.dataTable";
    public const string DataOutlineGroup = "data.outline.group";
    public const string DataOutlineGroupRows = "data.outline.groupRows";
    public const string DataOutlineUngroup = "data.outline.ungroup";
    public const string DataOutlineUngroupRows = "data.outline.ungroupRows";
    public const string DataOutlineClear = "data.outline.clear";

    public const string ReviewProtectSheet = "review.protectSheet";
    public const string ReviewTranslate = "Translate";
    public const string ReviewCheckPerformance = "Check Performance";

    public const string ViewNormal = "view.normal";
    public const string ViewZoom100 = "view.zoom.100";
    public const string ViewZoomPreset100 = "view.zoom.preset.100";
    public const string ViewZoomPreset200 = "view.zoom.preset.200";
    public const string ViewZoomPreset75 = "view.zoom.preset.75";
    public const string ViewZoomPreset50 = "view.zoom.preset.50";
    public const string ViewZoomPreset25 = "view.zoom.preset.25";
    public const string ViewZoomCustom = "view.zoom.custom";
    public const string ViewArrangeTiled = "view.arrange.tiled";
    public const string ViewArrangeHorizontal = "view.arrange.horizontal";
    public const string ViewArrangeVertical = "view.arrange.vertical";
    public const string ViewArrangeCascade = "view.arrange.cascade";
    public const string ViewFreezePanes = "view.freezePanes";
    public const string ViewFreezeAtSelection = "view.freezePanes.selection";
    public const string ViewFreezeTopRow = "view.freezePanes.topRow";
    public const string ViewFreezeFirstColumn = "view.freezePanes.firstColumn";
    public const string ViewUnfreezePanes = "view.freezePanes.unfreeze";

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
    public const string PivotChartInsert = "pivot.chart.insert";
    public const string PivotChartChangeType = "pivot.chart.changeType";
    public const string PivotBandedRows = "pivot.bandedRows";
    public const string PivotBandedColumns = "pivot.bandedColumns";
}
