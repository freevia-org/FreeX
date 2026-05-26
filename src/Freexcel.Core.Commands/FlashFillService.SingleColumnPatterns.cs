using System.Globalization;

namespace Freexcel.Core.Commands;

public static partial class FlashFillService
{
    // Delimiters tried in order for extract-by-delimiter and initials patterns.
    private static readonly char[] Delimiters = [' ', ',', '-', '_', '@', '/', '\\'];

    private static Func<string, string?>? TryConstant(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (examples.Count < 2)
            return null;

        var first = examples[0].Expected;
        if (!examples.All(e => e.Expected == first))
            return null;

        bool allUpper = examples.All(e => e.Expected == e.Source.ToUpperInvariant());
        bool allLower = examples.All(e => e.Expected == e.Source.ToLowerInvariant());
        bool allProper = examples.All(e => e.Expected == ToProperCase(e.Source));
        if (allUpper || allLower || allProper)
            return null;

        return _ => first;
    }

    private static Func<string, string?>? TryCaseTransform(IReadOnlyList<(string Source, string Expected)> examples)
    {
        bool isUpper = examples.All(e => e.Expected == e.Source.ToUpperInvariant());
        if (isUpper)
            return s => s.ToUpperInvariant();

        bool isLower = examples.All(e => e.Expected == e.Source.ToLowerInvariant());
        if (isLower)
            return s => s.ToLowerInvariant();

        bool isProper = examples.All(e => e.Expected == ToProperCase(e.Source));
        if (isProper)
            return s => ToProperCase(s);

        return null;
    }

    private static Func<string, string?>? TryExtractByDelimiter(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var delimiter in Delimiters)
        {
            int? partIndex = null;

            bool allMatch = true;
            foreach (var (source, expected) in examples)
            {
                var parts = source.Split(delimiter);
                if (parts.Length < 2)
                {
                    allMatch = false;
                    break;
                }

                int foundIndex = -1;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == expected)
                    {
                        foundIndex = i;
                        break;
                    }
                }

                if (foundIndex < 0)
                {
                    allMatch = false;
                    break;
                }

                if (partIndex is null)
                    partIndex = foundIndex;
                else if (partIndex != foundIndex)
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch && partIndex is not null)
            {
                var idx = partIndex.Value;
                var d = delimiter;
                return s =>
                {
                    var parts = s.Split(d);
                    return idx < parts.Length ? parts[idx] : s;
                };
            }
        }

        return null;
    }

    private static Func<string, string?>? TryEmailDisplayName(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryFormatDottedEmailUserName(e.Source, out var displayName) && displayName == e.Expected))
            return null;

        return s => TryFormatDottedEmailUserName(s, out var displayName) ? displayName : null;
    }

    private static bool TryFormatDottedEmailUserName(string source, out string displayName)
    {
        displayName = string.Empty;
        var atIndex = source.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0)
            return false;

        var userName = source[..atIndex];
        var separator = userName.Contains('.', StringComparison.Ordinal)
            ? '.'
            : userName.Contains('_', StringComparison.Ordinal)
                ? '_'
                : userName.Contains('-', StringComparison.Ordinal)
                    ? '-'
                    : '\0';
        if (separator == '\0')
            return false;

        var parts = userName.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts.Any(part => part.Any(char.IsDigit)))
            return false;

        displayName = string.Join(' ', parts.Select(ToProperCase));
        return true;
    }

    private static Func<string, string?>? TryStripThousandSeparators(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var (source, expected) in examples)
        {
            if (!source.Contains(',', StringComparison.Ordinal))
                return null;
            if (source.Replace(",", string.Empty) != expected)
                return null;
        }

        return s => s.Replace(",", string.Empty);
    }

    private static Func<string, string?>? TryExtractDigitsOnly(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var (source, expected) in examples)
        {
            if (source.All(char.IsDigit))
                return null;
            var digits = ExtractDigits(source);
            if (digits.Length == 0 || digits != expected)
                return null;
        }

        return s =>
        {
            var digits = ExtractDigits(s);
            return digits.Length > 0 ? digits : null;
        };
    }

    private static Func<string, string?>? TryDelimitedPartReorder(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var sourceDelimiter in Delimiters)
        {
            if (!TryDelimitedPartReorder(examples, sourceDelimiter, s => s[1] + ", " + s[0], out var commaFirstPattern))
                continue;

            return commaFirstPattern;
        }

        return null;
    }

    private static Func<string, string?>? TryDigitMask(IReadOnlyList<(string Source, string Expected)> examples)
    {
        string? mask = null;
        int? digitCount = null;

        foreach (var (source, expected) in examples)
        {
            if (source.Length == 0 || source.Any(c => !char.IsDigit(c)))
                return null;

            var expectedDigits = ExtractDigits(expected);
            if (source != expectedDigits)
                return null;

            var currentMask = CreateDigitMask(expected);
            if (currentMask == expected || string.IsNullOrWhiteSpace(currentMask))
                return null;

            if (mask is null)
            {
                mask = currentMask;
                digitCount = source.Length;
            }
            else if (mask != currentMask || digitCount != source.Length)
            {
                return null;
            }
        }

        if (mask is null || digitCount is null)
            return null;

        return source =>
        {
            if (source.Length != digitCount.Value || source.Any(c => !char.IsDigit(c)))
                return null;

            return ApplyDigitMask(source, mask);
        };
    }

    private static bool TryDelimitedPartReorder(
        IReadOnlyList<(string Source, string Expected)> examples,
        char sourceDelimiter,
        Func<string[], string> formatter,
        out Func<string, string?>? pattern)
    {
        pattern = null;
        foreach (var (source, expected) in examples)
        {
            if (!TrySplitTwoParts(source, sourceDelimiter, out var parts) ||
                formatter(parts) != expected)
            {
                return false;
            }
        }

        pattern = source =>
            TrySplitTwoParts(source, sourceDelimiter, out var parts)
                ? formatter(parts)
                : null;
        return true;
    }

    private static bool TrySplitTwoParts(string source, char delimiter, out string[] parts)
    {
        parts = source.Split(delimiter, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts.All(part => part.Length > 0);
    }

    private static string ExtractDigits(string value) =>
        string.Concat(value.Where(char.IsDigit));

    private static string CreateDigitMask(string value) =>
        new(value.Select(c => char.IsDigit(c) ? '#' : c).ToArray());

    private static string ApplyDigitMask(string digits, string mask)
    {
        var index = 0;
        var chars = new char[mask.Length];
        for (var i = 0; i < mask.Length; i++)
        {
            chars[i] = mask[i] == '#'
                ? digits[index++]
                : mask[i];
        }

        return new string(chars);
    }

    private static Func<string, string?>? TryInitials(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var delimiter in Delimiters)
        {
            if (examples.All(e => TryGetDelimitedInitials(e.Source, delimiter, out var initials) && initials == e.Expected))
            {
                return s => TryGetDelimitedInitials(s, delimiter, out var initials) ? initials : null;
            }
        }

        return null;
    }

    private static Func<string, string?>? TryPrefixTrim(IReadOnlyList<(string Source, string Expected)> examples)
    {
        var first = examples[0];
        if (first.Source.Length <= first.Expected.Length)
            return null;

        int prefixLen = first.Source.Length - first.Expected.Length;
        var prefix = first.Source[..prefixLen];
        if (first.Source[prefixLen..] != first.Expected)
            return null;

        if (!examples.Skip(1).All(e =>
                e.Source.Length > prefixLen &&
                e.Source[..prefixLen] == prefix &&
                e.Source[prefixLen..] == e.Expected))
            return null;

        return s => s.StartsWith(prefix, StringComparison.Ordinal)
            ? s[prefix.Length..]
            : s;
    }

    private static Func<string, string?>? TrySuffixTrim(IReadOnlyList<(string Source, string Expected)> examples)
    {
        var first = examples[0];
        if (first.Source.Length <= first.Expected.Length)
            return null;

        int suffixLen = first.Source.Length - first.Expected.Length;
        var suffix = first.Source[^suffixLen..];
        if (first.Source[..^suffixLen] != first.Expected)
            return null;

        if (!examples.Skip(1).All(e =>
                e.Source.Length > suffixLen &&
                e.Source[^suffixLen..] == suffix &&
                e.Source[..^suffixLen] == e.Expected))
            return null;

        return s => s.EndsWith(suffix, StringComparison.Ordinal)
            ? s[..^suffix.Length]
            : s;
    }

    private static Func<string, string?>? TryPrefixAdd(IReadOnlyList<(string Source, string Expected)> examples)
    {
        var first = examples[0];
        if (first.Expected.Length <= first.Source.Length)
            return null;

        if (!first.Expected.EndsWith(first.Source, StringComparison.Ordinal))
            return null;

        int prefixLen = first.Expected.Length - first.Source.Length;
        var prefix = first.Expected[..prefixLen];
        if (!examples.Skip(1).All(e =>
                e.Expected.Length > e.Source.Length &&
                e.Expected.Length - e.Source.Length == prefixLen &&
                e.Expected[..prefixLen] == prefix &&
                e.Expected[prefixLen..] == e.Source))
            return null;

        return s => prefix + s;
    }

    private static Func<string, string?>? TrySuffixAdd(IReadOnlyList<(string Source, string Expected)> examples)
    {
        var first = examples[0];
        if (!first.Expected.StartsWith(first.Source, StringComparison.Ordinal))
            return null;

        int suffixLen = first.Expected.Length - first.Source.Length;
        if (suffixLen <= 0)
            return null;

        var suffix = first.Expected[first.Source.Length..];
        if (!examples.Skip(1).All(e =>
                e.Expected.StartsWith(e.Source, StringComparison.Ordinal) &&
                e.Expected.Length - e.Source.Length == suffixLen &&
                e.Expected[e.Source.Length..] == suffix))
            return null;

        return s => s + suffix;
    }

    private static Func<string, string?>? TrySubstring(IReadOnlyList<(string Source, string Expected)> examples)
    {
        var first = examples[0];
        int sourceLen = first.Source.Length;
        int expectedLen = first.Expected.Length;
        if (expectedLen == 0 || expectedLen >= sourceLen)
            return null;

        int startIndex = first.Source.IndexOf(first.Expected, StringComparison.Ordinal);
        if (startIndex < 0)
            return null;

        if (!examples.Skip(1).All(e =>
        {
            if (e.Expected.Length != expectedLen)
                return false;
            if (e.Source.Length < startIndex + expectedLen)
                return false;
            return e.Source.Substring(startIndex, expectedLen) == e.Expected;
        }))
            return null;

        return s => s.Length >= startIndex + expectedLen
            ? s.Substring(startIndex, expectedLen)
            : null;
    }

    private static string ToProperCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return textInfo.ToTitleCase(s.ToLowerInvariant());
    }

    private static bool TryGetDelimitedInitials(string source, char delimiter, out string initials)
    {
        var parts = source.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            initials = string.Empty;
            return false;
        }

        initials = string.Concat(parts.Select(GetFirstInitial));
        return true;
    }

    private static string GetFirstInitial(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value[0].ToString();
}
