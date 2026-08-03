namespace FreeP.Core.Model;

/// <summary>
/// The subset of OMML <c>m:mathPr</c> values currently consumed by FreeP's
/// shared parser and layout engine. A null member means that the source did
/// not author that property; <see cref="DefaultJustification"/> retains the
/// authored <c>m:defJc</c> value so the shared parser can apply its Open XML
/// default and precedence rules. <see cref="DisplayDefaults"/> is nullable so
/// an absent <c>m:dispDef</c> remains distinct from an authored CT_OnOff value.
/// Limit-location values remain strings at the
/// package boundary so malformed authored values remain observable until the
/// shared parser applies the schema fallback.
/// <c>LeftMargin</c>, <c>RightMargin</c>, and <c>WrapIndent</c> retain the authored
/// twips text for
/// <c>m:lMargin</c>/<c>m:rMargin</c>; a null value means that the element was
/// absent, while a present val-less element is normalized to <c>1440</c> by
/// the package reader. <c>WrapRight</c> preserves the authored CT_OnOff value;
/// null means that the element was absent.
/// </summary>
public sealed record OmmlMathProperties(
    string? BinaryBreak = null,
    string? BinarySubtraction = null,
    string? MathFontFamily = null,
    bool? SmallFraction = null,
    string? DefaultJustification = null,
    string? IntegralLimitLocation = null,
    string? NaryLimitLocation = null,
    bool? DisplayDefaults = null,
    string? LeftMargin = null,
    string? RightMargin = null,
    string? WrapIndent = null,
    bool? WrapRight = null)
{
    public bool HasValues =>
        !string.IsNullOrWhiteSpace(BinaryBreak) ||
        !string.IsNullOrWhiteSpace(BinarySubtraction) ||
        !string.IsNullOrWhiteSpace(MathFontFamily) ||
        SmallFraction.HasValue ||
        !string.IsNullOrWhiteSpace(DefaultJustification) ||
        !string.IsNullOrWhiteSpace(IntegralLimitLocation) ||
        !string.IsNullOrWhiteSpace(NaryLimitLocation) ||
        DisplayDefaults.HasValue ||
        !string.IsNullOrWhiteSpace(LeftMargin) ||
        !string.IsNullOrWhiteSpace(RightMargin) ||
        !string.IsNullOrWhiteSpace(WrapIndent) ||
        WrapRight.HasValue;

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
            overriding.DefaultJustification ?? DefaultJustification,
            overriding.IntegralLimitLocation ?? IntegralLimitLocation,
            overriding.NaryLimitLocation ?? NaryLimitLocation,
            overriding.DisplayDefaults ?? DisplayDefaults,
            overriding.LeftMargin ?? LeftMargin,
            overriding.RightMargin ?? RightMargin,
            overriding.WrapIndent ?? WrapIndent,
            overriding.WrapRight ?? WrapRight);
}
