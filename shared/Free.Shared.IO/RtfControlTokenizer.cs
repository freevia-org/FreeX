using System.Numerics;

namespace Free.Shared.IO;

/// <summary>
/// One RTF control word as lexed by <see cref="RtfControlTokenizer.TryReadControlWord{T}"/>.
/// </summary>
/// <param name="NameStart">Index of the first letter of the control word.</param>
/// <param name="NameLength">Number of ASCII letters in the control word.</param>
/// <param name="Parameter">The signed 32-bit parameter, or null when none was written (a bare <c>-</c> is no parameter).</param>
/// <param name="ParameterOutOfRange">True when the digits did not fit a signed 32-bit integer; <paramref name="Parameter"/> is then clamped.</param>
/// <param name="End">Index of the first element after the control word, its parameter and its single delimiter space.</param>
public readonly record struct RtfControlWord(int NameStart, int NameLength, int? Parameter, bool ParameterOutOfRange, int End);

/// <summary>
/// The single RTF control-word / parameter / delimiter / escape tokenizer shared by every RTF reader in the
/// product (FreeW file and clipboard import, FreeP clipboard import). It works on <see cref="ReadOnlySpan{T}"/>
/// of <see cref="byte"/> (raw RTF) or <see cref="char"/> (RTF already widened one byte per char, for example
/// read as Latin-1), so it never decodes text: callers keep their own text-run decoding and group/destination
/// handling and only share the lexing.
/// <para>
/// Rules (RTF specification, as read tolerantly by Microsoft Word): a control word is one or more ASCII letters
/// a-z/A-Z only (non-ASCII letters end it), followed by an optional signed decimal parameter, followed by an
/// optional single space that is the delimiter and is consumed. A <c>-</c> that is not followed by a digit is no
/// parameter at all (the control word has no value rather than the value 0). A parameter that does not fit a
/// signed 32-bit integer is clamped to <see cref="int.MinValue"/>/<see cref="int.MaxValue"/> and flagged rather
/// than thrown. <c>\uN</c> takes N as a signed 16-bit value, and each unit skipped for the <c>\ucN</c> fallback
/// is one character, one <c>\'hh</c> escape, one control symbol or one whole control word.
/// </para>
/// </summary>
public static class RtfControlTokenizer
{
    /// <summary>True for ASCII <c>a-z</c>/<c>A-Z</c>.</summary>
    public static bool IsAsciiLetter(uint value) => (value | 0x20u) - 'a' <= 'z' - 'a';

    /// <summary>True for ASCII <c>0-9</c>.</summary>
    public static bool IsAsciiDigit(uint value) => value - '0' <= 9u;

    /// <summary>The value of an ASCII hex digit, or -1.</summary>
    public static int HexDigitValue(uint value) => value switch
    {
        >= '0' and <= '9' => (int)(value - '0'),
        >= 'a' and <= 'f' => (int)(value - 'a') + 10,
        >= 'A' and <= 'F' => (int)(value - 'A') + 10,
        _ => -1,
    };

    /// <summary>
    /// Lexes the control word whose first letter is at <paramref name="position"/> (the index just after the
    /// backslash). Returns false when there is no ASCII letter there.
    /// </summary>
    public static bool TryReadControlWord<T>(ReadOnlySpan<T> source, int position, out RtfControlWord word)
        where T : unmanaged, IBinaryInteger<T>
    {
        word = default;
        if ((uint)position >= (uint)source.Length || !IsAsciiLetter(Code(source[position])))
            return false;

        var start = position;
        while (position < source.Length && IsAsciiLetter(Code(source[position])))
            position++;
        var nameLength = position - start;

        int? parameter = null;
        var outOfRange = false;
        if (position < source.Length)
        {
            var negative = false;
            var digitsAt = position;
            if (Code(source[digitsAt]) == '-')
            {
                negative = true;
                digitsAt++;
            }

            if (digitsAt < source.Length && IsAsciiDigit(Code(source[digitsAt])))
            {
                ulong magnitude = 0;
                var overflow = false;
                while (digitsAt < source.Length && IsAsciiDigit(Code(source[digitsAt])))
                {
                    if (!overflow)
                    {
                        magnitude = magnitude * 10 + (Code(source[digitsAt]) - '0');
                        if (magnitude > 2147483648UL)
                            overflow = true;
                    }
                    digitsAt++;
                }

                if (negative)
                {
                    if (overflow || magnitude > 2147483648UL)
                    {
                        parameter = int.MinValue;
                        outOfRange = true;
                    }
                    else
                    {
                        parameter = magnitude == 2147483648UL ? int.MinValue : -(int)magnitude;
                    }
                }
                else if (overflow || magnitude > int.MaxValue)
                {
                    parameter = int.MaxValue;
                    outOfRange = true;
                }
                else
                {
                    parameter = (int)magnitude;
                }
                position = digitsAt;
            }
            else if (negative)
            {
                // A bare '-' is no parameter; the sign character is still part of the control word token.
                position = digitsAt;
            }
        }

        // A single space is the delimiter and is consumed (it is not document text).
        if (position < source.Length && Code(source[position]) == ' ')
            position++;

        word = new RtfControlWord(start, nameLength, parameter, outOfRange, position);
        return true;
    }

    /// <summary>
    /// Reads the two hex digits of a <c>\'hh</c> escape starting at <paramref name="position"/> (just after the
    /// quote). Up to two elements are always consumed; returns false (no byte) unless both are hex digits.
    /// </summary>
    public static bool TryReadHexEscape<T>(ReadOnlySpan<T> source, int position, out byte value, out int end)
        where T : unmanaged, IBinaryInteger<T>
    {
        value = 0;
        end = Math.Min(source.Length, position + 2);
        if (position + 1 >= source.Length)
            return false;

        var high = HexDigitValue(Code(source[position]));
        var low = HexDigitValue(Code(source[position + 1]));
        if (high < 0 || low < 0)
            return false;

        value = (byte)((high << 4) | low);
        return true;
    }

    /// <summary>The UTF-16 code unit for a <c>\uN</c> parameter: N is a signed 16-bit value, so negatives wrap.</summary>
    public static char ToUtf16CodeUnit(int parameter) => unchecked((char)(ushort)parameter);

    /// <summary>
    /// Returns the index after one <c>\ucN</c> fallback unit starting at <paramref name="position"/>: a
    /// <c>\'hh</c> escape (4 elements), a control symbol (2), a whole control word with its parameter and
    /// delimiter, or one plain element. Group braces are never fallback content: the position is returned
    /// unchanged for <c>{</c>/<c>}</c> and at the end of the input.
    /// </summary>
    public static int SkipFallbackUnit<T>(ReadOnlySpan<T> source, int position)
        where T : unmanaged, IBinaryInteger<T>
    {
        if (position >= source.Length)
            return position;

        var current = Code(source[position]);
        if (current == '{' || current == '}')
            return position;
        if (current != '\\')
            return position + 1;

        if (position + 1 >= source.Length)
            return position + 1;
        if (Code(source[position + 1]) == '\'')
            return Math.Min(source.Length, position + 4);
        if (TryReadControlWord(source, position + 1, out var word))
            return word.End;
        return Math.Min(source.Length, position + 2);
    }

    private static uint Code<T>(T value) where T : unmanaged, IBinaryInteger<T> => uint.CreateTruncating(value);
}
