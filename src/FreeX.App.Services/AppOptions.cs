using System.Text.Json.Serialization;
using Free.Shared.AppServices;
using FreeX.Core.IO;

namespace FreeX.App.Services;

public enum AppOptionsEnterDirection
{
    Down,
    Right,
    Up,
    Left
}

public enum AppOptionsObjectDisplay
{
    All,
    Placeholders,
    Nothing
}

public sealed class AppOptions : INormalizableApplicationOptions, IStatusBarOptionVisibilityStore
{
    public const string DefaultFontNameFallback = "Calibri";
    public const int DefaultFontSizeFallback = 11;
    public const int MaxDefaultFontSize = 409;
    public const int MinDefaultSheetCount = 1;
    public const int MaxDefaultSheetCount = 255;
    public const string XlsxDefaultFormat = ".xlsx";
    public const string FreeXWorkbookDefaultFormat = ".fxl";
    public const string LegacyJsonDefaultFormat = ".json";
    public const string SystemDefaultCultureName = "";
    public const string DefaultPdfExportLanguage = "en-US";

    public static IReadOnlyList<string> DefaultQuickAccessToolbarCommands { get; } =
    [
        "Save",
        "Undo",
        "Redo"
    ];

    public string DefaultFontName { get; set; } = DefaultFontNameFallback;
    public int DefaultFontSize { get; set; } = DefaultFontSizeFallback;
    public int DefaultSheetCount { get; set; } = 1;
    public string UserName { get; set; } = Environment.UserName;
    public bool CollapseRibbonAutomatically { get; set; }
    public bool ShowScreenTips { get; set; } = true;

    public string AppLanguage { get; set; } = SystemDefaultCultureName;

    public List<string> SpellCheckCustomDictionaryWords { get; set; } = [];

    public bool AutoCalculate { get; set; } = true;
    public bool UseR1C1ReferenceStyle { get; set; }

    // Formulas — error checking. When off, the green error-checking triangles and the
    // background error scan are suppressed (parity with Excel's "Enable background error checking").
    public bool ErrorCheckingEnabled { get; set; } = true;

    // Proofing — spell-check ignore rules (parity with Excel's AutoCorrect / proofing options).
    public bool ProofingIgnoreUppercase { get; set; } = true;
    public bool ProofingIgnoreNumbers { get; set; } = true;

    public bool ShowFormulaBar { get; set; } = true;
    public bool FormulaBarExpanded { get; set; }
    public bool MoveSelectionAfterEnter { get; set; } = true;
    public AppOptionsEnterDirection AfterEnterDirection { get; set; } = AppOptionsEnterDirection.Down;
    public bool EnableFillHandleAndCellDragAndDrop { get; set; } = true;

    // Advanced — editing options. Parity with Excel's "Enable AutoComplete for cell values": while
    // typing a plain text entry, offers to complete it from a matching entry already in the same
    // column (see FreeX.Core.Commands.CellValueAutoCompleteSuggester).
    public bool EnableAutoCompleteForCellValues { get; set; } = true;
    public bool ShowGridlines { get; set; } = true;
    public bool ShowHeadings { get; set; } = true;
    public AppOptionsObjectDisplay ObjectsDisplay { get; set; } = AppOptionsObjectDisplay.All;

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

    public string DefaultFormat { get; set; } = XlsxDefaultFormat;

    public bool QuickAccessToolbarBelowRibbon { get; set; }
    public List<string> QuickAccessToolbarCommands { get; set; } =
        DefaultQuickAccessToolbarCommands.ToList();

    public bool CrashAnalyticsEnabled { get; set; }
    public bool CrashAnalyticsPrompted { get; set; }

    public string PdfExportLanguage { get; set; } = DefaultPdfExportLanguage;

    [JsonIgnore]
    public string? LastPersistenceError { get; private set; }

    /// <inheritdoc cref="INormalizableApplicationOptions.Normalize"/>
    public void Normalize() => NormalizePersistedCollections();

    public void NormalizePersistedCollections()
    {
        DefaultFontName = NormalizeDefaultFontName(DefaultFontName);
        DefaultFontSize = NormalizeDefaultFontSize(DefaultFontSize);
        DefaultFormat = NormalizeDefaultFormat(DefaultFormat);
        DefaultSheetCount = NormalizeDefaultSheetCount(DefaultSheetCount);
        UserName = NormalizeUserName(UserName);
        SpellCheckCustomDictionaryWords = NormalizeSpellCheckCustomDictionaryWords(SpellCheckCustomDictionaryWords);
        QuickAccessToolbarCommands = NormalizeQuickAccessToolbarCommands(QuickAccessToolbarCommands);
    }

    public static string NormalizeDefaultFontName(string? fontName)
    {
        var normalized = fontName?.Trim();
        return string.IsNullOrEmpty(normalized) ? DefaultFontNameFallback : normalized;
    }

    public static int NormalizeDefaultFontSize(int fontSize)
    {
        if (fontSize <= 0)
            return DefaultFontSizeFallback;

        return Math.Min(fontSize, MaxDefaultFontSize);
    }

    public static int NormalizeDefaultSheetCount(int sheetCount) =>
        Math.Clamp(sheetCount, MinDefaultSheetCount, MaxDefaultSheetCount);

    public static string NormalizeUserName(string? userName)
    {
        var normalized = userName?.Trim();
        return string.IsNullOrEmpty(normalized) ? Environment.UserName : normalized;
    }

    public static string NormalizeDefaultFormat(string? extension)
    {
        var normalized = string.IsNullOrWhiteSpace(extension)
            ? XlsxDefaultFormat
            : FileFormatResolver.NormalizeExtension(extension);

        if (string.Equals(normalized, LegacyJsonDefaultFormat, StringComparison.OrdinalIgnoreCase))
            return FreeXWorkbookDefaultFormat;

        return string.Equals(normalized, FreeXWorkbookDefaultFormat, StringComparison.OrdinalIgnoreCase)
            ? FreeXWorkbookDefaultFormat
            : XlsxDefaultFormat;
    }

    public static List<string> NormalizeSpellCheckCustomDictionaryWords(IEnumerable<string>? words)
    {
        if (words is null)
            return [];

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in words)
        {
            var value = NormalizeSpellCheckCustomDictionaryWord(word);
            if (value is null || !seen.Add(value))
                continue;

            normalized.Add(value);
        }

        normalized.Sort(StringComparer.OrdinalIgnoreCase);
        return normalized;
    }

    public static string? NormalizeSpellCheckCustomDictionaryWord(string? word)
    {
        var value = word?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static List<string> NormalizeQuickAccessToolbarCommands(IEnumerable<string>? commandIds) =>
        commandIds?.ToList() ?? DefaultQuickAccessToolbarCommands.ToList();

    internal void SetPersistenceError(string? error)
    {
        LastPersistenceError = error;
    }
}
