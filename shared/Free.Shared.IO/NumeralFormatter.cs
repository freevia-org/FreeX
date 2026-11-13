using System.Text;

namespace Free.Shared.IO;

/// <summary>
/// The roman-numeral and alphabetic list/page/note/field numbering schemes that FreeW and FreeP
/// each used to carry a private copy of (seven copies of the same <c>(1000, "M"), (900, "CM")...</c>
/// table and base-26 loop).
///
/// This class owns only the conversion. Every function is a <c>Try*</c> with an EXPLICIT inclusive
/// range and returns <c>false</c> (with an empty string) outside it, so no caller can generate an
/// unbounded string: a roman numeral emits one "M" per thousand once it runs out of larger symbols,
/// so an uncapped conversion of 2,000,000,000 is a 2,000,000-character string. What a caller does
/// out of range is ITS policy (decimal fallback, an error token, an empty field...), which is why
/// there is deliberately no "format or fall back" convenience here.
///
/// Two DIFFERENT letter schemes exist and must not be conflated:
/// <list type="bullet">
/// <item><see cref="TryToBijectiveLetters"/> is base-26 without a zero digit (a..z, aa, ab, ac, ...,
/// az, ba, ...) -- the Excel-column style FreeW's notes, page numbers, SEQ fields, multilevel lists
/// and FreeP's auto-numbering use.</item>
/// <item><see cref="TryToRepeatedLetters"/> repeats one letter once per cycle (a..z, aa, bb, cc, ...)
/// -- the Word <c>\* ALPHABETIC</c> field-picture style that mail-merge general formats use.</item>
/// </list>
/// </summary>
public static class NumeralFormatter
{
    /// <summary>Largest value the classic subtractive roman numeral covers (MMMCMXCIX).</summary>
    public const int MaxRoman = 3999;

    /// <summary>
    /// Largest value <see cref="TryToExtendedRoman"/> accepts: Word's <c>\* ROMAN</c> general format
    /// range, where thousands simply repeat "M" (so the longest numeral is 32 M's plus a tail).
    /// </summary>
    public const int MaxExtendedRoman = 32767;

    /// <summary>
    /// Largest value <see cref="TryToRepeatedLetters"/> accepts (30 full 26-letter cycles), the Word
    /// <c>\* ALPHABETIC</c> range; the longest result is 30 characters.
    /// </summary>
    public const int MaxRepeatedLetters = 780;

    private static readonly int[] RomanValues = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];

    private static readonly string[] RomanUpper = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];

    private static readonly string[] RomanLower = ["m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i"];

    /// <summary>
    /// Subtractive roman numeral for <paramref name="value"/> in 1..<see cref="MaxRoman"/>; false
    /// (and an empty <paramref name="result"/>) outside that range.
    /// </summary>
    public static bool TryToRoman(int value, bool upper, out string result) =>
        TryToRoman(value, upper, MaxRoman, out result);

    /// <summary>
    /// Roman numeral for <paramref name="value"/> in 1..<see cref="MaxExtendedRoman"/>; values above
    /// 3999 repeat "M" per thousand (Word's <c>\* ROMAN</c>). False outside that range.
    /// </summary>
    public static bool TryToExtendedRoman(int value, bool upper, out string result) =>
        TryToRoman(value, upper, MaxExtendedRoman, out result);

    /// <summary>
    /// Base-26 letters with no zero digit: 1=a ... 26=z, 27=aa, 28=ab, ... 702=zz, 703=aaa. Accepts
    /// every value from 1 to <see cref="int.MaxValue"/> (the output is at most 7 characters, so it is
    /// intrinsically bounded); false and empty for 0 and negatives.
    /// </summary>
    public static bool TryToBijectiveLetters(int value, bool upper, out string result)
    {
        if (value < 1)
        {
            result = string.Empty;
            return false;
        }

        Span<char> buffer = stackalloc char[8];
        var position = buffer.Length;
        var baseChar = upper ? 'A' : 'a';
        var remaining = value;
        while (remaining > 0)
        {
            remaining--;
            buffer[--position] = (char)(baseChar + remaining % 26);
            remaining /= 26;
        }

        result = new string(buffer[position..]);
        return true;
    }

    /// <summary>
    /// Repeated-letter numbering for <paramref name="value"/> in 1..<see cref="MaxRepeatedLetters"/>:
    /// 1=a ... 26=z, 27=aa, 28=bb, ... 52=zz, 53=aaa. False and empty outside that range.
    /// </summary>
    public static bool TryToRepeatedLetters(int value, bool upper, out string result)
    {
        if (value is < 1 or > MaxRepeatedLetters)
        {
            result = string.Empty;
            return false;
        }

        var letter = (char)((upper ? 'A' : 'a') + (value - 1) % 26);
        result = new string(letter, (value - 1) / 26 + 1);
        return true;
    }

    private static bool TryToRoman(int value, bool upper, int maximum, out string result)
    {
        if (value < 1 || value > maximum)
        {
            result = string.Empty;
            return false;
        }

        var symbols = upper ? RomanUpper : RomanLower;
        var builder = new StringBuilder();
        var remaining = value;
        for (var index = 0; index < RomanValues.Length; index++)
        {
            while (remaining >= RomanValues[index])
            {
                builder.Append(symbols[index]);
                remaining -= RomanValues[index];
            }
        }

        result = builder.ToString();
        return true;
    }
}
