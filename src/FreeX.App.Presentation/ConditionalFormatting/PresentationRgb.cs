using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// A framework-neutral 8-bit RGB color produced by the portable conditional-formatting
/// evaluator. The renderers map this to their own native color type. Kept distinct from
/// <see cref="RgbColor"/> / <see cref="CellColor"/> so the portable layer never leaks a
/// model-styling type through its result contracts, while still interoperating with both.
/// </summary>
public readonly record struct PresentationRgb(byte R, byte G, byte B)
{
    /// <summary>Create from a model <see cref="RgbColor"/> (used by CF rule definitions).</summary>
    public static PresentationRgb FromRgbColor(RgbColor c) => new(c.R, c.G, c.B);

    /// <summary>Create from a model <see cref="CellColor"/>.</summary>
    public static PresentationRgb FromCellColor(CellColor c) => new(c.R, c.G, c.B);

    /// <summary>Convert to a model <see cref="CellColor"/> for callers that need one.</summary>
    public CellColor ToCellColor() => new(R, G, B);
}
