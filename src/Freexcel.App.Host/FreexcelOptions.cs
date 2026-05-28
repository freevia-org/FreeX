using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Freexcel.App.Host;

public enum FreexcelEnterDirection
{
    Down,
    Right,
    Up,
    Left
}

public enum FreexcelObjectDisplay
{
    All,
    Placeholders,
    Nothing
}

public sealed class FreexcelOptions
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
    public FreexcelEnterDirection AfterEnterDirection { get; set; } = FreexcelEnterDirection.Down;
    public bool ShowGridlines { get; set; } = true;
    public bool ShowHeadings { get; set; } = true;
    public FreexcelObjectDisplay ObjectsDisplay { get; set; } = FreexcelObjectDisplay.All;

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
        "Freexcel", "options.json");

    public static FreexcelOptions Load() => LoadFromPath(StorePath);

    internal static FreexcelOptions LoadFromPath(string storePath)
    {
        try
        {
            if (File.Exists(storePath))
            {
                var json = File.ReadAllText(storePath);
                return JsonSerializer.Deserialize<FreexcelOptions>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            return new FreexcelOptions
            {
                LastPersistenceError = $"Failed to load options from '{storePath}': {ex.Message}"
            };
        }

        return new FreexcelOptions();
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
