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

public static class PresentationListMarkerPlanner
{
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
