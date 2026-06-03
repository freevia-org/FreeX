using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreeX.Core.IO;

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

public sealed class FreeXOptions
{
    internal const string OptionsPathEnvironmentVariable = "FREEX_OPTIONS_PATH";
    internal const string DefaultFontNameFallback = "Calibri";
    internal const int DefaultFontSizeFallback = 11;
    internal const int MaxDefaultFontSize = 409;
    internal const int MinDefaultSheetCount = 1;
    internal const int MaxDefaultSheetCount = 255;
    public const string XlsxDefaultFormat = ".xlsx";
    public const string FreeXWorkbookDefaultFormat = ".fxl";
    private const string LegacyJsonDefaultFormat = ".json";

    private static readonly JsonSerializerOptions StoreJsonOptions = new()
    {
        WriteIndented = true
    };

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

    // View
    public bool ShowFormulaBar { get; set; } = true;
    public bool FormulaBarExpanded { get; set; }
    public bool MoveSelectionAfterEnter { get; set; } = true;
    public FreeXEnterDirection AfterEnterDirection { get; set; } = FreeXEnterDirection.Down;
    public bool ShowGridlines { get; set; } = true;
    public bool ShowHeadings { get; set; } = true;
    public FreeXObjectDisplay ObjectsDisplay { get; set; } = FreeXObjectDisplay.All;

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

    private static string StorePath => ResolveStorePath();

    internal static string StorePathForDisplay => StorePath;

    public static FreeXOptions Load() => LoadFromPath(StorePath);

    private static string ResolveStorePath()
    {
        var overridePath = Environment.GetEnvironmentVariable(OptionsPathEnvironmentVariable);
        return string.IsNullOrWhiteSpace(overridePath)
            ? System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FreeX", "options.json")
            : overridePath;
    }

    internal static FreeXOptions LoadFromPath(string storePath)
    {
        try
        {
            if (File.Exists(storePath))
            {
                var json = File.ReadAllText(storePath);
                var options = JsonSerializer.Deserialize<FreeXOptions>(json) ?? new();
                options.NormalizePersistedCollections();
                return options;
            }
        }
        catch (Exception ex)
        {
            return new FreeXOptions
            {
                LastPersistenceError = $"Failed to load options from '{storePath}': {ex.Message}"
            };
        }

        return new FreeXOptions();
    }

    public bool Save() => SaveToPath(StorePath);

    internal bool SaveToPath(string storePath)
    {
        string? tempPath = null;
        try
        {
            NormalizePersistedCollections();
            var directory = System.IO.Path.GetDirectoryName(storePath)!;
            Directory.CreateDirectory(directory);

            tempPath = System.IO.Path.Combine(
                directory,
                $".{System.IO.Path.GetFileName(storePath)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, JsonSerializer.Serialize(this, StoreJsonOptions));
            File.Move(tempPath, storePath, overwrite: true);
            LastPersistenceError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastPersistenceError = $"Failed to save options to '{storePath}': {ex.Message}";
            return false;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    internal void NormalizePersistedCollections()
    {
        DefaultFontName = NormalizeDefaultFontName(DefaultFontName);
        DefaultFontSize = NormalizeDefaultFontSize(DefaultFontSize);
        DefaultFormat = NormalizeDefaultFormat(DefaultFormat);
        DefaultSheetCount = NormalizeDefaultSheetCount(DefaultSheetCount);
        SpellCheckCustomDictionaryWords = NormalizeSpellCheckCustomDictionaryWords(SpellCheckCustomDictionaryWords);
    }

    internal static string NormalizeDefaultFontName(string? fontName)
    {
        var normalized = fontName?.Trim();
        return string.IsNullOrEmpty(normalized) ? DefaultFontNameFallback : normalized;
    }

    internal static int NormalizeDefaultFontSize(int fontSize)
    {
        if (fontSize <= 0)
            return DefaultFontSizeFallback;

        return Math.Min(fontSize, MaxDefaultFontSize);
    }

    internal static int NormalizeDefaultSheetCount(int sheetCount) =>
        Math.Clamp(sheetCount, MinDefaultSheetCount, MaxDefaultSheetCount);

    internal static string NormalizeDefaultFormat(string? extension)
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

    internal static List<string> NormalizeSpellCheckCustomDictionaryWords(IEnumerable<string>? words)
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

    internal static string? NormalizeSpellCheckCustomDictionaryWord(string? word)
    {
        var value = word?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
