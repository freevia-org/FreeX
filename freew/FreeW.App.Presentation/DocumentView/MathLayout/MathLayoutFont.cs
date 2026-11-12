namespace FreeW.App.Presentation.DocumentView.MathLayout;

/// <summary>
/// The OpenType MATH constants the layout engine needs, in em units (a value of 1 is one em at the
/// current font size). <see cref="CambriaMath"/> holds the values of Cambria Math — the font Word lays its
/// equations out with — as read from its MATH table (unitsPerEm 2048), so the engine reproduces Word's
/// geometry even where the real font cannot be opened (tests, non-Windows hosts).
/// </summary>
public sealed record MathFontConstants
{
    public double ScriptPercentScaleDown { get; init; }
    public double ScriptScriptPercentScaleDown { get; init; }
    public double DelimitedSubFormulaMinHeight { get; init; }
    public double DisplayOperatorMinHeight { get; init; }
    public double MathLeading { get; init; }
    public double AxisHeight { get; init; }
    public double AccentBaseHeight { get; init; }
    public double FlattenedAccentBaseHeight { get; init; }
    public double SubscriptShiftDown { get; init; }
    public double SubscriptTopMax { get; init; }
    public double SubscriptBaselineDropMin { get; init; }
    public double SuperscriptShiftUp { get; init; }
    public double SuperscriptShiftUpCramped { get; init; }
    public double SuperscriptBottomMin { get; init; }
    public double SuperscriptBaselineDropMax { get; init; }
    public double SubSuperscriptGapMin { get; init; }
    public double SuperscriptBottomMaxWithSubscript { get; init; }
    public double SpaceAfterScript { get; init; }
    public double UpperLimitGapMin { get; init; }
    public double UpperLimitBaselineRiseMin { get; init; }
    public double LowerLimitGapMin { get; init; }
    public double LowerLimitBaselineDropMin { get; init; }
    public double StackTopShiftUp { get; init; }
    public double StackTopDisplayStyleShiftUp { get; init; }
    public double StackBottomShiftDown { get; init; }
    public double StackBottomDisplayStyleShiftDown { get; init; }
    public double StackGapMin { get; init; }
    public double StackDisplayStyleGapMin { get; init; }
    public double StretchStackTopShiftUp { get; init; }
    public double StretchStackBottomShiftDown { get; init; }
    public double StretchStackGapAboveMin { get; init; }
    public double StretchStackGapBelowMin { get; init; }
    public double FractionNumeratorShiftUp { get; init; }
    public double FractionNumeratorDisplayStyleShiftUp { get; init; }
    public double FractionDenominatorShiftDown { get; init; }
    public double FractionDenominatorDisplayStyleShiftDown { get; init; }
    public double FractionNumeratorGapMin { get; init; }
    public double FractionNumDisplayStyleGapMin { get; init; }
    public double FractionRuleThickness { get; init; }
    public double FractionDenominatorGapMin { get; init; }
    public double FractionDenomDisplayStyleGapMin { get; init; }
    public double SkewedFractionHorizontalGap { get; init; }
    public double SkewedFractionVerticalGap { get; init; }
    public double OverbarVerticalGap { get; init; }
    public double OverbarRuleThickness { get; init; }
    public double OverbarExtraAscender { get; init; }
    public double UnderbarVerticalGap { get; init; }
    public double UnderbarRuleThickness { get; init; }
    public double UnderbarExtraDescender { get; init; }
    public double RadicalVerticalGap { get; init; }
    public double RadicalDisplayStyleVerticalGap { get; init; }
    public double RadicalRuleThickness { get; init; }
    public double RadicalExtraAscender { get; init; }
    public double RadicalKernBeforeDegree { get; init; }
    public double RadicalKernAfterDegree { get; init; }
    public double RadicalDegreeBottomRaisePercent { get; init; }

    /// <summary>Cambria Math's MATH-table constants (font units / 2048; percentages kept as percentages).</summary>
    public static MathFontConstants CambriaMath { get; } = FromFontUnits(
        unitsPerEm: 2048,
        scriptPercentScaleDown: 73,
        scriptScriptPercentScaleDown: 60,
        radicalDegreeBottomRaisePercent: 65,
        [
            3000, 2500, 300, 585, 976, 1250, 418, 760, 320, 750, 615, 239, 460, 300, 765, 85, 133, 500, 133, 1240,
            940, 1550, 770, 1370, 400, 660, 1600, 1180, 133, 133, 1200, 1550, 1030, 1370, 133, 260, 133, 133, 260,
            800, 133, 345, 133, 133, 345, 133, 133, 166, 345, 133, 133, 133, -640
        ]);

    /// <summary>
    /// Builds constants from raw font units in MATH-table order: DelimitedSubFormulaMinHeight,
    /// DisplayOperatorMinHeight, then the 51 MathValueRecords from MathLeading to RadicalKernAfterDegree.
    /// </summary>
    public static MathFontConstants FromFontUnits(
        double unitsPerEm,
        double scriptPercentScaleDown,
        double scriptScriptPercentScaleDown,
        double radicalDegreeBottomRaisePercent,
        IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < ValueCount)
            throw new ArgumentException($"Expected {ValueCount} MATH constant values.", nameof(values));
        if (unitsPerEm <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerEm));

        var index = 0;
        double Next() => values[index++] / unitsPerEm;
        return new MathFontConstants
        {
            ScriptPercentScaleDown = scriptPercentScaleDown <= 0 ? 70 : scriptPercentScaleDown,
            ScriptScriptPercentScaleDown = scriptScriptPercentScaleDown <= 0 ? 50 : scriptScriptPercentScaleDown,
            DelimitedSubFormulaMinHeight = Next(),
            DisplayOperatorMinHeight = Next(),
            MathLeading = Next(),
            AxisHeight = Next(),
            AccentBaseHeight = Next(),
            FlattenedAccentBaseHeight = Next(),
            SubscriptShiftDown = Next(),
            SubscriptTopMax = Next(),
            SubscriptBaselineDropMin = Next(),
            SuperscriptShiftUp = Next(),
            SuperscriptShiftUpCramped = Next(),
            SuperscriptBottomMin = Next(),
            SuperscriptBaselineDropMax = Next(),
            SubSuperscriptGapMin = Next(),
            SuperscriptBottomMaxWithSubscript = Next(),
            SpaceAfterScript = Next(),
            UpperLimitGapMin = Next(),
            UpperLimitBaselineRiseMin = Next(),
            LowerLimitGapMin = Next(),
            LowerLimitBaselineDropMin = Next(),
            StackTopShiftUp = Next(),
            StackTopDisplayStyleShiftUp = Next(),
            StackBottomShiftDown = Next(),
            StackBottomDisplayStyleShiftDown = Next(),
            StackGapMin = Next(),
            StackDisplayStyleGapMin = Next(),
            StretchStackTopShiftUp = Next(),
            StretchStackBottomShiftDown = Next(),
            StretchStackGapAboveMin = Next(),
            StretchStackGapBelowMin = Next(),
            FractionNumeratorShiftUp = Next(),
            FractionNumeratorDisplayStyleShiftUp = Next(),
            FractionDenominatorShiftDown = Next(),
            FractionDenominatorDisplayStyleShiftDown = Next(),
            FractionNumeratorGapMin = Next(),
            FractionNumDisplayStyleGapMin = Next(),
            FractionRuleThickness = Next(),
            FractionDenominatorGapMin = Next(),
            FractionDenomDisplayStyleGapMin = Next(),
            SkewedFractionHorizontalGap = Next(),
            SkewedFractionVerticalGap = Next(),
            OverbarVerticalGap = Next(),
            OverbarRuleThickness = Next(),
            OverbarExtraAscender = Next(),
            UnderbarVerticalGap = Next(),
            UnderbarRuleThickness = Next(),
            UnderbarExtraDescender = Next(),
            RadicalVerticalGap = Next(),
            RadicalDisplayStyleVerticalGap = Next(),
            RadicalRuleThickness = Next(),
            RadicalExtraAscender = Next(),
            RadicalKernBeforeDegree = Next(),
            RadicalKernAfterDegree = Next(),
            RadicalDegreeBottomRaisePercent = radicalDegreeBottomRaisePercent
        };
    }

    /// <summary>Number of FWORD/MathValueRecord values <see cref="FromFontUnits"/> consumes.</summary>
    public const int ValueCount = 53;
}

/// <summary>Ink and advance metrics of a run of math text, in em units.</summary>
/// <param name="Width">Advance width.</param>
/// <param name="Ascent">Ink extent above the baseline.</param>
/// <param name="Descent">Ink extent below the baseline (positive downwards).</param>
/// <param name="ItalicCorrection">Italic correction of the last glyph (how far its ink leans past the advance).</param>
/// <param name="InkLeft">Left edge of the ink relative to the origin.</param>
/// <param name="InkRight">Right edge of the ink relative to the origin.</param>
public readonly record struct MathTextMetrics(
    double Width,
    double Ascent,
    double Descent,
    double ItalicCorrection,
    double InkLeft,
    double InkRight);

/// <summary>One size variant of a stretchy glyph (OpenType MathGlyphVariantRecord), in em units.</summary>
public readonly record struct MathGlyphVariant(int GlyphId, double AdvanceMeasurement);

/// <summary>One part of an extensible glyph assembly (OpenType GlyphPart), in em units.</summary>
public readonly record struct MathGlyphPart(
    int GlyphId,
    double StartConnectorLength,
    double EndConnectorLength,
    double FullAdvance,
    bool IsExtender);

/// <summary>An extensible glyph assembly (OpenType GlyphAssembly). Vertical parts run bottom to top.</summary>
public sealed record MathGlyphAssembly(double ItalicCorrection, IReadOnlyList<MathGlyphPart> Parts);

/// <summary>
/// The font services the equation layout engine needs. Every length is in em units; the engine scales by
/// the current style's font size. An implementation without real glyph access (see
/// <see cref="EstimatedMathLayoutFont"/>) reports no glyphs, and the engine then draws stretchy characters
/// as scaled text.
/// </summary>
public interface IMathLayoutFont
{
    MathFontConstants Constants { get; }

    /// <summary>Minimum overlap between connecting assembly parts (OpenType MinConnectorOverlap).</summary>
    double MinConnectorOverlap { get; }
    /// <summary>Measures text drawn with the math font.</summary>
    MathTextMetrics MeasureText(string text);

    /// <summary>The nominal glyph of a code point, when the font maps it.</summary>
    bool TryGetGlyph(int codePoint, out int glyphId);

    /// <summary>Metrics of a single glyph (by glyph id).</summary>
    MathTextMetrics MeasureGlyph(int glyphId);

    /// <summary>The size variants of a glyph in the given direction, smallest first (may be empty).</summary>
    IReadOnlyList<MathGlyphVariant> GetVariants(int glyphId, bool vertical);

    /// <summary>The extensible assembly of a glyph in the given direction, or null.</summary>
    MathGlyphAssembly? GetAssembly(int glyphId, bool vertical);

    /// <summary>Horizontal top-accent attachment point of a glyph (em), or null to use the ink centre.</summary>
    double? GetTopAccentAttachment(int glyphId);
}

/// <summary>
/// A metric-only stand-in for Cambria Math used when the real font is unavailable (unit tests, hosts
/// without the font). It carries Cambria Math's MATH constants and approximates glyph advances and ink
/// extents per character class, so layouts keep Word's proportions; it maps no glyphs, so stretchy
/// characters fall back to scaled text.
/// </summary>
public sealed class EstimatedMathLayoutFont : IMathLayoutFont
{
    public static EstimatedMathLayoutFont Instance { get; } = new();

    public MathFontConstants Constants => MathFontConstants.CambriaMath;

    public double MinConnectorOverlap => 0.05;

    public MathTextMetrics MeasureText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return default;

        double width = 0, ascent = 0, descent = 0, italic = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var (advance, top, bottom, slant) = Estimate(rune.Value);
            width += advance;
            ascent = Math.Max(ascent, top);
            descent = Math.Max(descent, bottom);
            italic = slant;
        }

        return new MathTextMetrics(width, ascent, descent, italic, 0, width);
    }

    public bool TryGetGlyph(int codePoint, out int glyphId)
    {
        glyphId = 0;
        return false;
    }

    public MathTextMetrics MeasureGlyph(int glyphId) => default;

    public IReadOnlyList<MathGlyphVariant> GetVariants(int glyphId, bool vertical) => [];

    public MathGlyphAssembly? GetAssembly(int glyphId, bool vertical) => null;

    public double? GetTopAccentAttachment(int glyphId) => null;

    private static (double Advance, double Ascent, double Descent, double Italic) Estimate(int codePoint)
    {
        if (codePoint == ' ')
            return (0.22, 0, 0, 0);
        if (MathText.IsMathItalicCodePoint(codePoint))
            return (0.5, 0.47, 0.01, 0.04);
        if (codePoint is >= '0' and <= '9')
            return (0.5, 0.65, 0.01, 0);
        if (codePoint is >= 'A' and <= 'Z')
            return (0.62, 0.66, 0.01, 0);
        if (codePoint is '(' or ')' or '[' or ']' or '{' or '}' or '|')
            return (0.35, 0.75, 0.25, 0);
        if (MathText.IsLargeOperator(codePoint))
            return (0.8, 0.75, 0.25, 0);
        if (codePoint is '+' or '=' or '<' or '>' or 0x2212 or 0x00B1 or 0x00D7)
            return (0.65, 0.5, 0, 0);
        return (0.5, 0.5, 0.01, 0);
    }
}
