using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Shared PowerPoint list-marker sequencing contract. Paragraph metadata owns the
/// marker; the marker is never part of editable text. A contiguous numbered list
/// continues through nested levels, while a non-numbered marker starts a new
/// numbering sequence.
/// </summary>
public sealed class PresentationListMarkerContinuationState
{
    private readonly int[] _counters = new int[9];
    private readonly AutoNumType?[] _types = new AutoNumType?[9];
    private readonly bool[] _active = new bool[9];

    public void Break()
    {
        Array.Clear(_counters);
        Array.Clear(_types);
        Array.Clear(_active);
    }

    public int Next(
        int level,
        AutoNumType type,
        int startAt,
        bool startAtSpecified = false)
    {
        int clampedLevel = Math.Clamp(level, 0, _counters.Length - 1);
        bool startsSequence = startAtSpecified
            || !_active[clampedLevel]
            || _types[clampedLevel] != type;
        int value = startsSequence
            ? Math.Max(1, startAt)
            : Math.Max(1, _counters[clampedLevel] + 1);

        _counters[clampedLevel] = value;
        _types[clampedLevel] = type;
        _active[clampedLevel] = true;

        // Moving to an outer level closes nested sequences. The outer level
        // remains active so a later sibling continues its numbering.
        for (int index = clampedLevel + 1; index < _counters.Length; index++)
        {
            _counters[index] = 0;
            _types[index] = null;
            _active[index] = false;
        }

        return value;
    }

    /// <summary>
    /// Expands an external level-text template after <see cref="Next"/> has advanced
    /// the current level. A template uses %1..%9 for list levels and keeps all other
    /// punctuation/literal text verbatim.
    /// </summary>
    public string FormatTemplate(
        int currentLevel,
        AutoNumType currentType,
        int currentValue,
        string? template)
    {
        if (string.IsNullOrEmpty(template))
            return PresentationListMarkerPlanner.FormatAutoNumber(currentType, currentValue);

        int clampedCurrentLevel = Math.Clamp(currentLevel, 0, _counters.Length - 1);
        var result = new System.Text.StringBuilder(template.Length + 8);
        for (int index = 0; index < template.Length; index++)
        {
            if (template[index] == '%' && index + 1 < template.Length
                && template[index + 1] is >= '1' and <= '9')
            {
                int level = template[++index] - '1';
                int value = level == clampedCurrentLevel
                    ? currentValue
                    : _active[level] ? _counters[level] : 1;
                AutoNumType type = level == clampedCurrentLevel
                    ? currentType
                    : _types[level] ?? currentType;
                result.Append(PresentationListMarkerPlanner.FormatNumberCore(type, value));
                continue;
            }

            result.Append(template[index]);
        }

        return result.ToString();
    }
}

/// <summary>
/// Portable list-marker decisions for one paragraph. Theme colors and theme-font tokens remain
/// unresolved so each renderer can realize them against its own effective text run and theme.
/// </summary>
public readonly record struct PresentationResolvedListMarkerPlan(
    BulletKind Kind,
    string Text,
    string? Character,
    ImagePart? Image,
    AutoNumType AutoNumType,
    ThemeAwareColor? Color,
    string? FontFamily,
    double? BulletSizePt,
    int? BulletSizePct)
{
    public static PresentationResolvedListMarkerPlan None { get; } = new(
        BulletKind.None,
        string.Empty,
        null,
        null,
        AutoNumType.ArabicPeriod,
        null,
        null,
        null,
        null);

    /// <summary>
    /// Resolves the portable absolute/percentage size inputs against the effective text size.
    /// Absolute marker sizes receive the renderer's text scale; percentage sizes use the already
    /// scaled effective text size.
    /// </summary>
    public double? ResolveFontSizePt(
        double? effectiveTextFontSizePt,
        double absoluteSizeScale = 1.0)
    {
        if (BulletSizePt is > 0)
            return BulletSizePt.Value * absoluteSizeScale;
        if (BulletSizePct is > 0 && effectiveTextFontSizePt is > 0)
            return effectiveTextFontSizePt.Value * BulletSizePct.Value / 100000.0;
        return effectiveTextFontSizePt;
    }
}

public static class PresentationListMarkerPlanner
{
    /// <summary>
    /// Resolves paragraph and inherited-style marker metadata, advances numbering state, and
    /// returns renderer-neutral typography inputs. Explicit <c>buNone</c> suppression blocks all
    /// inherited marker metadata and breaks the active numbering sequence.
    /// </summary>
    public static PresentationResolvedListMarkerPlan Resolve(
        Paragraph paragraph,
        TextStyleLevel? inheritedStyle,
        PresentationListMarkerContinuationState continuationState)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(continuationState);

        if (paragraph.BulletSuppressed)
        {
            continuationState.Break();
            return PresentationResolvedListMarkerPlan.None;
        }

        BulletKind kind = paragraph.BulletKind;
        string? character = paragraph.BulletChar;
        AutoNumType autoNumType = paragraph.AutoNumType;
        if (kind == BulletKind.None && inheritedStyle?.BulletKind is { } inheritedKind)
        {
            kind = inheritedKind;
            if (kind == BulletKind.Char && character is null)
                character = inheritedStyle.BulletChar;
            if (kind == BulletKind.Auto)
                autoNumType = inheritedStyle.AutoNumType;
        }

        ThemeAwareColor? color = paragraph.BulletColorFollowsText
            ? null
            : paragraph.BulletColor
                ?? (inheritedStyle?.BulletColorFollowsText == true
                    ? null
                    : inheritedStyle?.BulletColor);
        string? fontFamily = paragraph.BulletFontFollowsText
            ? null
            : !string.IsNullOrEmpty(paragraph.BulletFontFamily)
                ? paragraph.BulletFontFamily
                : inheritedStyle?.BulletFontFollowsText == true
                    ? null
                    : inheritedStyle?.BulletFontFamily;

        double? bulletSizePt = null;
        int? bulletSizePct = null;
        if (!paragraph.BulletSizeFollowsText)
        {
            if (paragraph.BulletSizePt.HasValue)
                bulletSizePt = paragraph.BulletSizePt;
            else if (paragraph.BulletSizePct.HasValue)
                bulletSizePct = paragraph.BulletSizePct;
            else if (inheritedStyle?.BulletSizeFollowsText != true)
            {
                bulletSizePt = inheritedStyle?.BulletSizePt;
                if (!bulletSizePt.HasValue)
                    bulletSizePct = inheritedStyle?.BulletSizePct;
            }
        }

        string text;
        switch (kind)
        {
            case BulletKind.Char:
                text = character ?? "•";
                continuationState.Break();
                break;

            case BulletKind.Auto:
            {
                int value = continuationState.Next(
                    paragraph.Level,
                    autoNumType,
                    paragraph.AutoNumStartAt,
                    paragraph.AutoNumStartAtSpecified);
                text = continuationState.FormatTemplate(
                    paragraph.Level,
                    autoNumType,
                    value,
                    paragraph.AutoNumTextTemplate);
                break;
            }

            default:
                text = string.Empty;
                continuationState.Break();
                break;
        }

        return new PresentationResolvedListMarkerPlan(
            kind,
            text,
            character,
            kind == BulletKind.Image ? paragraph.BulletImage : null,
            autoNumType,
            color,
            fontFamily,
            bulletSizePt,
            bulletSizePct);
    }

    public static string FormatAutoNumber(AutoNumType type, int value)
    {
        int normalizedValue = Math.Max(1, value);
        return type switch
        {
            AutoNumType.ArabicPeriod => $"{FormatNumberCore(type, normalizedValue)}.",
            AutoNumType.ArabicParenR => $"{FormatNumberCore(type, normalizedValue)})",
            AutoNumType.ArabicParenBoth => $"({FormatNumberCore(type, normalizedValue)})",
            AutoNumType.RomanUcPeriod => $"{FormatNumberCore(type, normalizedValue)}.",
            AutoNumType.RomanLcPeriod => $"{FormatNumberCore(type, normalizedValue)}.",
            AutoNumType.RomanUcParenR => $"{FormatNumberCore(type, normalizedValue)})",
            AutoNumType.RomanLcParenR => $"{FormatNumberCore(type, normalizedValue)})",
            AutoNumType.AlphaUcPeriod => $"{FormatNumberCore(type, normalizedValue)}.",
            AutoNumType.AlphaLcPeriod => $"{FormatNumberCore(type, normalizedValue)}.",
            AutoNumType.AlphaUcParenR => $"{FormatNumberCore(type, normalizedValue)})",
            AutoNumType.AlphaLcParenR => $"{FormatNumberCore(type, normalizedValue)})",
            AutoNumType.AlphaUcParenBoth => $"({FormatNumberCore(type, normalizedValue)})",
            AutoNumType.AlphaLcParenBoth => $"({FormatNumberCore(type, normalizedValue)})",
            _ => $"{FormatNumberCore(type, normalizedValue)}.",
        };
    }

    internal static string FormatNumberCore(AutoNumType type, int value)
    {
        int normalizedValue = Math.Max(1, value);
        return type switch
        {
            AutoNumType.RomanUcPeriod or AutoNumType.RomanUcParenR => ToRoman(normalizedValue, upper: true),
            AutoNumType.RomanLcPeriod or AutoNumType.RomanLcParenR => ToRoman(normalizedValue, upper: false),
            AutoNumType.AlphaUcPeriod or AutoNumType.AlphaUcParenR or AutoNumType.AlphaUcParenBoth =>
                ToAlpha(normalizedValue, upper: true),
            AutoNumType.AlphaLcPeriod or AutoNumType.AlphaLcParenR or AutoNumType.AlphaLcParenBoth =>
                ToAlpha(normalizedValue, upper: false),
            _ => normalizedValue.ToString(),
        };
    }

    private static string ToRoman(int value, bool upper)
    {
        if (value > 3999)
            return value.ToString();

        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var symbols = upper
            ? new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" }
            : new[] { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" };
        var result = new System.Text.StringBuilder();
        for (int index = 0; index < values.Length; index++)
        {
            while (value >= values[index])
            {
                value -= values[index];
                result.Append(symbols[index]);
            }
        }

        return result.ToString();
    }

    private static string ToAlpha(int value, bool upper)
    {
        var result = new System.Text.StringBuilder();
        while (value > 0)
        {
            value--;
            result.Insert(0, (char)((upper ? 'A' : 'a') + value % 26));
            value /= 26;
        }

        return result.Length == 0 ? (upper ? "A" : "a") : result.ToString();
    }
}
