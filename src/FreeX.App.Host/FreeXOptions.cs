using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    private static readonly JsonSerializerOptions StoreJsonOptions = new()
    {
        WriteIndented = true
    };

    // General — new workbooks
    public string DefaultFontName  { get; set; } = "Calibri";
    public int    DefaultFontSize  { get; set; } = 11;
    public int    DefaultSheetCount{ get; set; } = 1;
    public string UserName         { get; set; } = Environment.UserName;

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
    public string DefaultFormat { get; set; } = ".xlsx";

    // Diagnostics
    public bool CrashAnalyticsEnabled { get; set; }
    public bool CrashAnalyticsPrompted { get; set; }

    // Export
    public string PdfExportLanguage { get; set; } = ExportPlanner.DefaultPdfLanguage;

    [JsonIgnore]
    public string? LastPersistenceError { get; private set; }

    private static readonly string StorePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FreeX", "options.json");

    public static FreeXOptions Load() => LoadFromPath(StorePath);

    internal static FreeXOptions LoadFromPath(string storePath)
    {
        try
        {
            if (File.Exists(storePath))
            {
                var json = File.ReadAllText(storePath);
                return JsonSerializer.Deserialize<FreeXOptions>(json) ?? new();
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

    public void Save() => SaveToPath(StorePath);

    internal bool SaveToPath(string storePath)
    {
        string? tempPath = null;
        try
        {
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
}
