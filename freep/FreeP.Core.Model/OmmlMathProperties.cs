namespace FreeP.Core.Model;

/// <summary>
/// The subset of OMML <c>m:mathPr</c> values currently consumed by FreeP's
/// shared parser and layout engine. A null member means that the source did
/// not author that property; <see cref="DefaultJustification"/> retains the
/// authored <c>m:defJc</c> value so the shared parser can apply its Open XML
/// default and precedence rules.
/// </summary>
public sealed record OmmlMathProperties(
    string? BinaryBreak = null,
    string? BinarySubtraction = null,
    string? MathFontFamily = null,
    bool? SmallFraction = null,
    string? DefaultJustification = null)
{
    public bool HasValues =>
        !string.IsNullOrWhiteSpace(BinaryBreak) ||
        !string.IsNullOrWhiteSpace(BinarySubtraction) ||
        !string.IsNullOrWhiteSpace(MathFontFamily) ||
        SmallFraction.HasValue ||
        !string.IsNullOrWhiteSpace(DefaultJustification);

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
            overriding.SmallFraction ?? SmallFraction,
            overriding.DefaultJustification ?? DefaultJustification);
}
