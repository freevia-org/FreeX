namespace Freexcel.Core.Model;

public sealed class PivotTableStyleModel
{
    public string Name { get; init; } = "";
    public bool AppliesToPivotTables { get; init; } = true;
    public bool AppliesToTables { get; init; }
    public List<PivotTableStyleElementModel> Elements { get; } = [];
}

public sealed record PivotTableStyleElementModel(
    string Type,
    int? DifferentialFormatId = null,
    int? Size = null);
