namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Portable layout for a data-bar fill within a single cell. Fractions are normalized to
/// [0, 1] of the cell's drawable content width. <see cref="StartFraction"/> is the left edge
/// of the bar and <see cref="EndFraction"/> the right edge; for a positive value anchored at
/// the left axis the start is the axis position and the end extends rightward, while a negative
/// value extends leftward from the axis. Renderers turn these into pixels.
/// </summary>
public readonly record struct DataBarLayout(
    double StartFraction,
    double EndFraction,
    double AxisFraction,
    bool IsNegative,
    PresentationRgb FillColor,
    bool Gradient,
    bool Border,
    bool ShowValue)
{
    /// <summary>Signed extent of the bar (<see cref="EndFraction"/> − <see cref="StartFraction"/>), always ≥ 0.</summary>
    public double Length => EndFraction - StartFraction;
}

/// <summary>Interpolated color-scale fill for a cell.</summary>
public readonly record struct ColorScaleResult(PresentationRgb Fill);

/// <summary>
/// Selected icon for an icon-set rule. <see cref="BucketIndex"/> is the zero-based bucket from
/// lowest (0) to highest (<see cref="IconCount"/> − 1) after any reverse has been applied.
/// </summary>
public readonly record struct IconSetResult(
    string Style,
    int BucketIndex,
    int IconCount,
    bool ShowValue);
