namespace FreeX.Core.Model;

/// <summary>Custom structured table style metadata loaded from XLSX stylesheet tableStyle definitions.</summary>
public sealed class StructuredTableStyleModel
{
    public string Name { get; init; } = "";
    public bool AppliesToTables { get; init; } = true;
    public bool AppliesToPivotTables { get; init; }
    public string? NativeXml { get; init; }
}
