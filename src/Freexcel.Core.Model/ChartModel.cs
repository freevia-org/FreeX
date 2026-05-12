namespace Freexcel.Core.Model;

public enum ChartType { Column, Line, Pie }

/// <summary>Lightweight chart definition stored on a Sheet.</summary>
public sealed class ChartModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ChartType Type { get; set; } = ChartType.Column;
    public GridRange DataRange { get; set; }
    public bool FirstRowIsHeader { get; set; } = true;
    public bool FirstColIsCategories { get; set; } = true;
    public string? Title { get; set; }
    public double Left   { get; set; } = 50;
    public double Top    { get; set; } = 50;
    public double Width  { get; set; } = 400;
    public double Height { get; set; } = 300;
}
