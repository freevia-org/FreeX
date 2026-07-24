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
}

public static class PresentationListMarkerPlanner
{
    public static string FormatAutoNumber(AutoNumType type, int value)
    {
        int normalizedValue = Math.Max(1, value);
        return type switch
        {
            AutoNumType.ArabicPeriod => $"{normalizedValue}.",
            AutoNumType.ArabicParenR => $"{normalizedValue})",
            AutoNumType.ArabicParenBoth => $"({normalizedValue})",
            AutoNumType.RomanUcPeriod => $"{ToRoman(normalizedValue, upper: true)}.",
            AutoNumType.RomanLcPeriod => $"{ToRoman(normalizedValue, upper: false)}.",
            AutoNumType.RomanUcParenR => $"{ToRoman(normalizedValue, upper: true)})",
            AutoNumType.RomanLcParenR => $"{ToRoman(normalizedValue, upper: false)})",
            AutoNumType.AlphaUcPeriod => $"{ToAlpha(normalizedValue, upper: true)}.",
            AutoNumType.AlphaLcPeriod => $"{ToAlpha(normalizedValue, upper: false)}.",
            AutoNumType.AlphaUcParenR => $"{ToAlpha(normalizedValue, upper: true)})",
            AutoNumType.AlphaLcParenR => $"{ToAlpha(normalizedValue, upper: false)})",
            AutoNumType.AlphaUcParenBoth => $"({ToAlpha(normalizedValue, upper: true)})",
            AutoNumType.AlphaLcParenBoth => $"({ToAlpha(normalizedValue, upper: false)})",
            _ => $"{normalizedValue}.",
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
