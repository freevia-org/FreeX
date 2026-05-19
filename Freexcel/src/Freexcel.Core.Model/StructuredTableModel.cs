namespace Freexcel.Core.Model;

/// <summary>Structured Excel table metadata loaded from XLSX packages.</summary>
public sealed class StructuredTableModel
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public GridRange Range { get; init; }
    public bool HasAutoFilter { get; init; }
    public bool TotalsRowShown { get; init; }
    public string? StyleName { get; init; }
    public bool ShowFirstColumn { get; init; }
    public bool ShowLastColumn { get; init; }
    public bool ShowRowStripes { get; init; }
    public bool ShowColumnStripes { get; init; }
    public string PackagePart { get; init; } = "";
    public List<StructuredTableColumnModel> Columns { get; } = [];
}

public sealed record StructuredTableColumnModel(
    int Id,
    string Name,
    string? TotalsRowLabel = null,
    string? TotalsRowFunction = null);
