using System.IO;
using System.Text.Json;

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

    private static readonly string StorePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Freexcel", "options.json");

    public static FreexcelOptions Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                return JsonSerializer.Deserialize<FreexcelOptions>(json) ?? new();
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[FreexcelOptions] Failed to load: {ex.Message}"); }
        return new FreexcelOptions();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[FreexcelOptions] Failed to save: {ex.Message}"); }
    }
}
