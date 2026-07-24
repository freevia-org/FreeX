using System.Text.Json.Serialization;
using Free.Shared.AppServices;
using FreeX.App.Services;

namespace FreeX.App.Host;

public enum FreeXEnterDirection
{
    Down,
    Right,
    Up,
    Left
}

public enum FreeXObjectDisplay
{
    All,
    Placeholders,
    Nothing
}

public sealed class FreeXOptions : IStatusBarOptionVisibilityStore
{
    internal const string OptionsPathEnvironmentVariable = AppOptionsStore.OptionsPathEnvironmentVariable;
    internal const string DefaultFontNameFallback = AppOptions.DefaultFontNameFallback;
    internal const int DefaultFontSizeFallback = AppOptions.DefaultFontSizeFallback;
    internal const int MaxDefaultFontSize = AppOptions.MaxDefaultFontSize;
    internal const int MinDefaultSheetCount = AppOptions.MinDefaultSheetCount;
    internal const int MaxDefaultSheetCount = AppOptions.MaxDefaultSheetCount;
    public const string XlsxDefaultFormat = AppOptions.XlsxDefaultFormat;
    public const string FreeXWorkbookDefaultFormat = AppOptions.FreeXWorkbookDefaultFormat;

    // General — new workbooks
    public string DefaultFontName  { get; set; } = DefaultFontNameFallback;
    public int    DefaultFontSize  { get; set; } = DefaultFontSizeFallback;
    public int    DefaultSheetCount{ get; set; } = 1;
    public string UserName         { get; set; } = Environment.UserName;
    public bool CollapseRibbonAutomatically { get; set; }
    public bool ShowScreenTips { get; set; } = true;

    // Language
    public string AppLanguage { get; set; } = AppLanguageCatalog.SystemDefaultCultureName;

    // Proofing
    public List<string> SpellCheckCustomDictionaryWords { get; set; } = [];

    // Formulas
    public bool AutoCalculate { get; set; } = true;
    public bool UseR1C1ReferenceStyle { get; set; }
    public bool ErrorCheckingEnabled { get; set; } = true;

    // Proofing — ignore rules
    public bool ProofingIgnoreUppercase { get; set; } = true;
    public bool ProofingIgnoreNumbers { get; set; } = true;

    // View
    public bool ShowFormulaBar { get; set; } = true;
    public bool FormulaBarExpanded { get; set; }
    public bool MoveSelectionAfterEnter { get; set; } = true;
    public FreeXEnterDirection AfterEnterDirection { get; set; } = FreeXEnterDirection.Down;
    public bool EnableAutoCompleteForCellValues { get; set; } = true;
    public bool ShowGridlines { get; set; } = true;
    public bool ShowHeadings { get; set; } = true;
    public FreeXObjectDisplay ObjectsDisplay { get; set; } = FreeXObjectDisplay.All;

    // Status bar
    public bool StatusBarShowCellMode { get; set; } = true;
    public bool StatusBarShowEndMode { get; set; }
    public bool StatusBarShowSelectionMode { get; set; }
    public bool StatusBarShowPageNumber { get; set; }
    public bool StatusBarShowAverage { get; set; } = true;
    public bool StatusBarShowCount { get; set; } = true;
    public bool StatusBarShowNumericalCount { get; set; }
    public bool StatusBarShowMinimum { get; set; }
    public bool StatusBarShowMaximum { get; set; }
    public bool StatusBarShowSum { get; set; } = true;
    public bool StatusBarShowViewShortcuts { get; set; } = true;
    public bool StatusBarShowZoom { get; set; } = true;
    public bool StatusBarShowZoomSlider { get; set; } = true;

    // Save
    public string DefaultFormat { get; set; } = XlsxDefaultFormat;

    // Quick Access Toolbar
    public bool QuickAccessToolbarBelowRibbon { get; set; }
    public List<string> QuickAccessToolbarCommands { get; set; } =
        QuickAccessToolbarCatalog.DefaultCommandIds.ToList();

    // Diagnostics
    public bool CrashAnalyticsEnabled { get; set; }
    public bool CrashAnalyticsPrompted { get; set; }

    // Export
    public string PdfExportLanguage { get; set; } = ExportPlanner.DefaultPdfLanguage;

    [JsonIgnore]
    public string? LastPersistenceError { get; private set; }

    private static string StorePath => AppOptionsStore.StorePath;

    internal static string StorePathForDisplay => StorePath;

    public static FreeXOptions Load() => LoadFromPath(StorePath);

    internal static string ResolveStorePath(IApplicationDataPathProvider pathProvider)
    {
        var overridePath = Environment.GetEnvironmentVariable(OptionsPathEnvironmentVariable);
        return AppOptionsStore.ResolveStorePath(pathProvider, overridePath);
    }

    internal static FreeXOptions LoadFromPath(string storePath) =>
        FromAppOptions(AppOptionsStore.LoadFromPath(storePath));

    public bool Save() => SaveToPath(StorePath);

    internal bool SaveToPath(string storePath)
    {
        var options = ToAppOptions();
        var saved = AppOptionsStore.SaveToPath(options, storePath);
        ApplyAppOptions(options, copyPersistenceError: true);
        return saved;
    }

    internal void NormalizePersistedCollections()
    {
        var options = ToAppOptions();
        options.NormalizePersistedCollections();
        ApplyAppOptions(options, copyPersistenceError: false);
    }

    internal static string NormalizeDefaultFontName(string? fontName) =>
        AppOptions.NormalizeDefaultFontName(fontName);

    internal static int NormalizeDefaultFontSize(int fontSize) =>
        AppOptions.NormalizeDefaultFontSize(fontSize);

    internal static int NormalizeDefaultSheetCount(int sheetCount) =>
        AppOptions.NormalizeDefaultSheetCount(sheetCount);

    internal static string NormalizeUserName(string? userName) =>
        AppOptions.NormalizeUserName(userName);

    internal static string NormalizeDefaultFormat(string? extension) =>
        AppOptions.NormalizeDefaultFormat(extension);

    internal static List<string> NormalizeSpellCheckCustomDictionaryWords(IEnumerable<string>? words) =>
        AppOptions.NormalizeSpellCheckCustomDictionaryWords(words);

    internal static string? NormalizeSpellCheckCustomDictionaryWord(string? word) =>
        AppOptions.NormalizeSpellCheckCustomDictionaryWord(word);

    internal AppOptions ToAppOptions() =>
        new()
        {
            DefaultFontName = DefaultFontName,
            DefaultFontSize = DefaultFontSize,
            DefaultSheetCount = DefaultSheetCount,
            UserName = UserName,
            CollapseRibbonAutomatically = CollapseRibbonAutomatically,
            ShowScreenTips = ShowScreenTips,
            AppLanguage = AppLanguage,
            SpellCheckCustomDictionaryWords = SpellCheckCustomDictionaryWords,
            AutoCalculate = AutoCalculate,
            UseR1C1ReferenceStyle = UseR1C1ReferenceStyle,
            ErrorCheckingEnabled = ErrorCheckingEnabled,
            ProofingIgnoreUppercase = ProofingIgnoreUppercase,
            ProofingIgnoreNumbers = ProofingIgnoreNumbers,
            ShowFormulaBar = ShowFormulaBar,
            FormulaBarExpanded = FormulaBarExpanded,
            MoveSelectionAfterEnter = MoveSelectionAfterEnter,
            AfterEnterDirection = (AppOptionsEnterDirection)AfterEnterDirection,
            EnableAutoCompleteForCellValues = EnableAutoCompleteForCellValues,
            ShowGridlines = ShowGridlines,
            ShowHeadings = ShowHeadings,
            ObjectsDisplay = (AppOptionsObjectDisplay)ObjectsDisplay,
            StatusBarShowCellMode = StatusBarShowCellMode,
            StatusBarShowEndMode = StatusBarShowEndMode,
            StatusBarShowSelectionMode = StatusBarShowSelectionMode,
            StatusBarShowPageNumber = StatusBarShowPageNumber,
            StatusBarShowAverage = StatusBarShowAverage,
            StatusBarShowCount = StatusBarShowCount,
            StatusBarShowNumericalCount = StatusBarShowNumericalCount,
            StatusBarShowMinimum = StatusBarShowMinimum,
            StatusBarShowMaximum = StatusBarShowMaximum,
            StatusBarShowSum = StatusBarShowSum,
            StatusBarShowViewShortcuts = StatusBarShowViewShortcuts,
            StatusBarShowZoom = StatusBarShowZoom,
            StatusBarShowZoomSlider = StatusBarShowZoomSlider,
            DefaultFormat = DefaultFormat,
            QuickAccessToolbarBelowRibbon = QuickAccessToolbarBelowRibbon,
            QuickAccessToolbarCommands = QuickAccessToolbarCommands,
            CrashAnalyticsEnabled = CrashAnalyticsEnabled,
            CrashAnalyticsPrompted = CrashAnalyticsPrompted,
            PdfExportLanguage = PdfExportLanguage
        };

    internal static FreeXOptions FromAppOptions(AppOptions options)
    {
        var hostOptions = new FreeXOptions();
        hostOptions.ApplyAppOptions(options, copyPersistenceError: true);
        return hostOptions;
    }

    private void ApplyAppOptions(AppOptions options, bool copyPersistenceError)
    {
        DefaultFontName = options.DefaultFontName;
        DefaultFontSize = options.DefaultFontSize;
        DefaultSheetCount = options.DefaultSheetCount;
        UserName = options.UserName;
        CollapseRibbonAutomatically = options.CollapseRibbonAutomatically;
        ShowScreenTips = options.ShowScreenTips;
        AppLanguage = options.AppLanguage;
        SpellCheckCustomDictionaryWords = options.SpellCheckCustomDictionaryWords;
        AutoCalculate = options.AutoCalculate;
        UseR1C1ReferenceStyle = options.UseR1C1ReferenceStyle;
        ErrorCheckingEnabled = options.ErrorCheckingEnabled;
        ProofingIgnoreUppercase = options.ProofingIgnoreUppercase;
        ProofingIgnoreNumbers = options.ProofingIgnoreNumbers;
        ShowFormulaBar = options.ShowFormulaBar;
        FormulaBarExpanded = options.FormulaBarExpanded;
        MoveSelectionAfterEnter = options.MoveSelectionAfterEnter;
        AfterEnterDirection = (FreeXEnterDirection)options.AfterEnterDirection;
        EnableAutoCompleteForCellValues = options.EnableAutoCompleteForCellValues;
        ShowGridlines = options.ShowGridlines;
        ShowHeadings = options.ShowHeadings;
        ObjectsDisplay = (FreeXObjectDisplay)options.ObjectsDisplay;
        StatusBarShowCellMode = options.StatusBarShowCellMode;
        StatusBarShowEndMode = options.StatusBarShowEndMode;
        StatusBarShowSelectionMode = options.StatusBarShowSelectionMode;
        StatusBarShowPageNumber = options.StatusBarShowPageNumber;
        StatusBarShowAverage = options.StatusBarShowAverage;
        StatusBarShowCount = options.StatusBarShowCount;
        StatusBarShowNumericalCount = options.StatusBarShowNumericalCount;
        StatusBarShowMinimum = options.StatusBarShowMinimum;
        StatusBarShowMaximum = options.StatusBarShowMaximum;
        StatusBarShowSum = options.StatusBarShowSum;
        StatusBarShowViewShortcuts = options.StatusBarShowViewShortcuts;
        StatusBarShowZoom = options.StatusBarShowZoom;
        StatusBarShowZoomSlider = options.StatusBarShowZoomSlider;
        DefaultFormat = options.DefaultFormat;
        QuickAccessToolbarBelowRibbon = options.QuickAccessToolbarBelowRibbon;
        QuickAccessToolbarCommands = options.QuickAccessToolbarCommands;
        CrashAnalyticsEnabled = options.CrashAnalyticsEnabled;
        CrashAnalyticsPrompted = options.CrashAnalyticsPrompted;
        PdfExportLanguage = options.PdfExportLanguage;

        if (copyPersistenceError)
        {
            LastPersistenceError = options.LastPersistenceError;
        }
    }
}
