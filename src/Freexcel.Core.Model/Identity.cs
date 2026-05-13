namespace Freexcel.Core.Model;

/// <summary>
/// Strongly-typed identifier for a workbook instance.
/// </summary>
public readonly record struct WorkbookId(Guid Value)
{
    public static WorkbookId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N")[..8];
}

/// <summary>
/// Strongly-typed identifier for a worksheet within a workbook.
/// </summary>
public readonly record struct SheetId(Guid Value)
{
    public static SheetId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N")[..8];
}

/// <summary>
/// Strongly-typed identifier for a style definition.
/// </summary>
public readonly record struct StyleId(int Value)
{
    public static readonly StyleId Default = new(0);
}
