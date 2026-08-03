namespace FreeP.Core.Model;

/// <summary>
/// The subset of OMML <c>m:mathPr</c> values currently consumed by FreeP's
/// shared parser and layout engine. A null member means that the source did
/// not author that property; no Office default is synthesized here.
/// </summary>
public sealed record OmmlMathProperties(
    string? BinaryBreak = null,
    string? BinarySubtraction = null,
    string? MathFontFamily = null,
    bool? SmallFraction = null)
{
    public bool HasValues =>
        !string.IsNullOrWhiteSpace(BinaryBreak) ||
        !string.IsNullOrWhiteSpace(BinarySubtraction) ||
        !string.IsNullOrWhiteSpace(MathFontFamily) ||
        SmallFraction.HasValue;

    /// <summary>
    /// Applies authored values from <paramref name="overriding"/> one property
    /// at a time, preserving lower-precedence values that were not authored.
    /// </summary>
    public OmmlMathProperties Overlay(OmmlMathProperties? overriding) => overriding is null
        ? this
        : new OmmlMathProperties(
            overriding.BinaryBreak ?? BinaryBreak,
            overriding.BinarySubtraction ?? BinarySubtraction,
            overriding.MathFontFamily ?? MathFontFamily,
            overriding.SmallFraction ?? SmallFraction);
}
