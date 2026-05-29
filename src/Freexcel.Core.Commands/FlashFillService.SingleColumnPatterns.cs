using System.Globalization;

namespace Freexcel.Core.Commands;

public static partial class FlashFillService
{
    // Delimiters tried in order for extract-by-delimiter and initials patterns.
    private static readonly char[] Delimiters = [' ', ',', ';', ':', '|', '-', '_', '@', '.', '/', '\\'];
    private static readonly string[] LabelValueSeparators = [":", "=", "->", "=>", "-", "/", "|"];
    private static readonly HashSet<string> KnownNameTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mr",
        "Mrs",
        "Ms",
        "Miss",
        "Dr",
        "Prof",
        "Professor",
        "Sir",
        "Dame"
    };
    private static readonly HashSet<string> KnownNameSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Jr",
        "Junior",
        "Sr",
        "Senior",
        "II",
        "III",
        "IV",
        "V",
        "PhD",
        "Ph.D",
        "MD",
        "M.D",
        "CPA",
        "C.P.A",
        "MBA",
        "M.B.A",
        "DDS",
        "D.D.S",
        "DVM",
        "D.V.M",
        "Esq"
    };

    private static readonly (char Open, char Close)[] PairedDelimiters =
        [('(', ')'), ('[', ']'), ('{', '}'), ('"', '"'), ('\'', '\''), ('<', '>')];

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
                var parts = source.Split(delimiter, StringSplitOptions.TrimEntries);
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
                    var parts = s.Split(d, StringSplitOptions.TrimEntries);
                    if (idx < parts.Length)
                        return parts[idx];

                    return idx == 0 ? s : null;
                };
            }
        }

        return null;
    }

    private static Func<string, string?>? TryExtractFinalDottedToken(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryGetFinalDottedToken(e.Source, out var token) && token == e.Expected))
            return null;

        return source => TryGetFinalDottedToken(source, out var token) ? token : null;
    }

    private static Func<string, string?>? TryRemoveFinalDottedToken(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryRemoveFinalDottedToken(e.Source, out var stem) && stem == e.Expected))
            return null;

        return source => TryRemoveFinalDottedToken(source, out var stem) ? stem : null;
    }

    private static bool TryRemoveFinalDottedToken(string source, out string stem)
    {
        stem = string.Empty;
        var lastDotIndex = source.LastIndexOf('.');
        if (lastDotIndex <= 0 || lastDotIndex == source.Length - 1)
            return false;

        stem = source[..lastDotIndex].Trim();
        return stem.Length > 0;
    }

    private static bool TryGetFinalDottedToken(string source, out string token)
    {
        var parts = source.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            token = string.Empty;
            return false;
        }

        token = parts[^1];
        return token.Length > 0;
    }

    private static Func<string, string?>? TryEmailDisplayName(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryFormatDottedEmailUserName(e.Source, out var displayName) && displayName == e.Expected))
            return null;

        return s => TryFormatDottedEmailUserName(s, out var displayName) ? displayName : null;
    }

    private static Func<string, string?>? TrySplitPascalCaseWords(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TrySplitPascalCaseWords(e.Source, out var words) && words == e.Expected))
            return null;

        return source => TrySplitPascalCaseWords(source, out var words) ? words : null;
    }

    private static bool TrySplitPascalCaseWords(string source, out string words)
    {
        words = string.Empty;
        if (source.Length < 2 || source.Any(char.IsWhiteSpace))
            return false;

        var split = new List<char>(source.Length + 4);
        var insertedSeparator = false;
        for (var i = 0; i < source.Length; i++)
        {
            var current = source[i];
            if (i > 0 &&
                char.IsUpper(current) &&
                char.IsLower(source[i - 1]))
            {
                split.Add(' ');
                insertedSeparator = true;
            }

            split.Add(current);
        }

        if (!insertedSeparator)
            return false;

        words = new string(split.ToArray());
        return words.Length > source.Length;
    }

    private static bool TryFormatDottedEmailUserName(string source, out string displayName)
    {
        displayName = string.Empty;
        var atIndex = source.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0)
            return false;

        var userName = source[..atIndex];
        var plusIndex = userName.IndexOf('+');
        if (plusIndex >= 0)
            userName = userName[..plusIndex];

        if (userName.Length == 0)
            return false;

        if (!userName.Contains('.', StringComparison.Ordinal) &&
            !userName.Contains('_', StringComparison.Ordinal) &&
            !userName.Contains('-', StringComparison.Ordinal))
        {
            return false;
        }

        var parts = userName.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts.Any(part => part.Any(char.IsDigit)))
            return false;

        displayName = string.Join(' ', parts.Select(ToProperCase));
        return true;
    }

    private static Func<string, string?>? TryEmailLocalPartWithoutPlusTag(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryExtractEmailLocalPartWithoutPlusTag(e.Source, out var localPart) && localPart == e.Expected))
            return null;

        return source => TryExtractEmailLocalPartWithoutPlusTag(source, out var localPart) ? localPart : null;
    }

    private static Func<string, string?>? TryEmailDomainStem(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryExtractEmailDomainStem(e.Source, out var domainStem) && domainStem == e.Expected))
            return null;

        return source => TryExtractEmailDomainStem(source, out var domainStem) ? domainStem : null;
    }

    private static bool TryExtractEmailLocalPartWithoutPlusTag(string source, out string localPart)
    {
        localPart = string.Empty;
        var atIndex = source.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0)
            return false;

        var plusIndex = source.IndexOf('+', 0, atIndex);
        if (plusIndex <= 0)
            return false;

        localPart = source[..plusIndex];
        return localPart.Length > 0;
    }

    private static bool TryExtractEmailDomainStem(string source, out string domainStem)
    {
        domainStem = string.Empty;

        var atIndex = source.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0 || atIndex == source.Length - 1)
            return false;

        var domain = source[(atIndex + 1)..].Trim();
        if (domain.Length == 0 || domain.Any(char.IsWhiteSpace))
            return false;

        var lastDotIndex = domain.LastIndexOf('.');
        if (lastDotIndex <= 0 || lastDotIndex == domain.Length - 1)
            return false;

        domainStem = domain[..lastDotIndex];
        return domainStem.Length > 0;
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

    private static Func<string, string?>? TryExtractFinalDigitRun(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var (source, expected) in examples)
        {
            if (!TryGetFinalDigitRun(source, out var digitRun) || digitRun != expected)
                return null;
        }

        return source => TryGetFinalDigitRun(source, out var digitRun) ? digitRun : null;
    }

    private static bool TryGetFinalDigitRun(string source, out string digitRun)
    {
        digitRun = string.Empty;
        var end = source.Length - 1;
        while (end >= 0 && !char.IsDigit(source[end]))
            end--;

        if (end < 0)
            return false;

        var start = end;
        while (start >= 0 && char.IsDigit(source[start]))
            start--;

        digitRun = source[(start + 1)..(end + 1)];
        return digitRun.Length > 0;
    }

    private static Func<string, string?>? TryDelimitedPartReorder(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var sourceDelimiter in Delimiters)
        {
            if (!TryDelimitedPartReorder(examples, sourceDelimiter, s => s[1] + ", " + s[0], out var commaFirstPattern))
                continue;

            return commaFirstPattern;
        }

        if (TryDelimitedPartReorder(examples, ',', s => s[1] + " " + s[0], out var firstLastPattern))
            return firstLastPattern;

        return null;
    }

    private static Func<string, string?>? TryThreeTokenNameDropMiddle(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (examples.All(e => TrySplitWhitespaceTokens(e.Source, out var tokens) && e.Expected == tokens[1] + " " + tokens[2]))
            return source => TrySplitWhitespaceTokens(source, out var tokens) ? tokens[1] + " " + tokens[2] : null;

        if (examples.All(e => TrySplitWhitespaceTokens(e.Source, out var tokens) && e.Expected == tokens[0] + " " + tokens[1]))
            return source => TrySplitWhitespaceTokens(source, out var tokens) ? tokens[0] + " " + tokens[1] : null;

        if (examples.All(e => TrySplitWhitespaceTokens(e.Source, out var tokens) && e.Expected == tokens[0] + " " + tokens[2]))
            return source => TrySplitWhitespaceTokens(source, out var tokens) ? tokens[0] + " " + tokens[2] : null;

        if (examples.All(e => TrySplitWhitespaceTokens(e.Source, out var tokens) && e.Expected == tokens[2] + ", " + tokens[0]))
            return source => TrySplitWhitespaceTokens(source, out var tokens) ? tokens[2] + ", " + tokens[0] : null;

        if (examples.All(e => TrySplitWhitespaceTokens(e.Source, out var tokens) && e.Expected == tokens[2] + ", " + tokens[0] + " " + tokens[1]))
            return source => TrySplitWhitespaceTokens(source, out var tokens) ? tokens[2] + ", " + tokens[0] + " " + tokens[1] : null;

        return null;
    }

    private static Func<string, string?>? TryThreeTokenNameInitial(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (examples.All(e => TrySplitWhitespaceTokens(e.Source, out var tokens) && e.Expected == GetFirstInitial(tokens[1]) + "."))
            return source => TrySplitWhitespaceTokens(source, out var tokens) ? GetFirstInitial(tokens[1]) + "." : null;

        return null;
    }

    private static Func<string, string?>? TryFinalWhitespaceToken(IReadOnlyList<(string Source, string Expected)> examples)
    {
        var exampleTokens = new List<string[]>(examples.Count);
        foreach (var (source, expected) in examples)
        {
            if (!TrySplitVariableWhitespaceTokens(source, out var tokens) || expected != tokens[^1])
                return null;

            exampleTokens.Add(tokens);
        }

        if (exampleTokens.Select(tokens => tokens[0]).Distinct(StringComparer.Ordinal).Count() == 1)
            return null;

        return source => TrySplitVariableWhitespaceTokens(source, out var tokens)
            ? tokens[^1]
            : null;
    }

    private static Func<string, string?>? TryKnownTitleRemoval(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryRemoveKnownNameTitle(e.Source, out var name) && name == e.Expected))
            return null;

        return source => TryRemoveKnownNameTitle(source, out var name) ? name : null;
    }

    private static Func<string, string?>? TryKnownTitleAndSuffixRemoval(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e =>
                TryRemoveKnownNameTitle(e.Source, out var withoutTitle) &&
                TryRemoveKnownNameSuffix(withoutTitle, out var name) &&
                name == e.Expected))
        {
            return null;
        }

        return source =>
            TryRemoveKnownNameTitle(source, out var withoutTitle) &&
            TryRemoveKnownNameSuffix(withoutTitle, out var name)
                ? name
                : null;
    }

    private static bool TryRemoveKnownNameTitle(string source, out string name)
    {
        name = string.Empty;
        var tokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            return false;

        var title = tokens[0].TrimEnd('.');
        if (!KnownNameTitles.Contains(title))
            return false;

        name = string.Join(' ', tokens.Skip(1));
        return name.Length > 0;
    }

    private static Func<string, string?>? TryKnownNameSuffixRemoval(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryRemoveKnownNameSuffix(e.Source, out var name) && name == e.Expected))
            return null;

        return source => TryRemoveKnownNameSuffix(source, out var name) ? name : null;
    }

    private static bool TryRemoveKnownNameSuffix(string source, out string name)
    {
        name = string.Empty;
        var tokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            return false;

        var suffix = tokens[^1].TrimEnd('.');
        if (!KnownNameSuffixes.Contains(suffix))
            return false;

        var nameTokens = tokens[..^1];
        nameTokens[^1] = nameTokens[^1].TrimEnd(',');
        name = string.Join(' ', nameTokens);
        return name.Length > 0;
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

    private static Func<string, string?>? TryPairedDelimiterExtraction(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var (open, close) in PairedDelimiters)
        {
            if (!examples.All(e => TryExtractBetweenPairedDelimiters(e.Source, open, close, out var extracted) && extracted == e.Expected))
                continue;

            return source => TryExtractBetweenPairedDelimiters(source, open, close, out var extracted)
                ? extracted
                : null;
        }

        return null;
    }

    private static bool TryExtractBetweenPairedDelimiters(string source, char open, char close, out string extracted)
    {
        extracted = string.Empty;
        var openIndex = source.IndexOf(open);
        if (openIndex < 0)
            return false;

        var closeIndex = source.IndexOf(close, openIndex + 1);
        if (closeIndex <= openIndex + 1)
            return false;

        extracted = source[(openIndex + 1)..closeIndex].Trim();
        return extracted.Length > 0;
    }

    private static Func<string, string?>? TryPairedDelimiterRemoval(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var (open, close) in PairedDelimiters)
        {
            if (!examples.All(e => TryRemovePairedDelimiterText(e.Source, open, close, out var removed) && removed == e.Expected))
                continue;

            return source => TryRemovePairedDelimiterText(source, open, close, out var removed)
                ? removed
                : null;
        }

        return null;
    }

    private static bool TryRemovePairedDelimiterText(string source, char open, char close, out string removed)
    {
        removed = string.Empty;
        var openIndex = source.IndexOf(open);
        if (openIndex < 0)
            return false;

        var closeIndex = source.IndexOf(close, openIndex + 1);
        if (closeIndex <= openIndex)
            return false;

        removed = (source[..openIndex] + source[(closeIndex + 1)..]).Trim();
        while (removed.Contains("  ", StringComparison.Ordinal))
            removed = removed.Replace("  ", " ", StringComparison.Ordinal);

        return removed.Length > 0 && !string.Equals(removed, source, StringComparison.Ordinal);
    }

    private static Func<string, string?>? TryLabelValueExtraction(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var separator in LabelValueSeparators)
        {
            if (!examples.All(e => TryExtractLabelValue(e.Source, separator, out var extracted) && extracted == e.Expected))
                continue;

            return source => TryExtractLabelValue(source, separator, out var extracted)
                ? extracted
                : null;
        }

        return null;
    }

    private static bool TryExtractLabelValue(string source, string separator, out string extracted)
    {
        extracted = string.Empty;
        if (!TryFindLabelValueSeparator(source, separator, out _, out var separatorEnd))
            return false;

        extracted = source[separatorEnd..].Trim();
        return extracted.Length > 0;
    }

    private static Func<string, string?>? TryLabelQualifierRemoval(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var separator in LabelValueSeparators)
        {
            if (!examples.All(e => TryRemoveLabelValue(e.Source, separator, out var removed) && removed == e.Expected))
                continue;

            return source => TryRemoveLabelValue(source, separator, out var removed)
                ? removed
                : null;
        }

        return null;
    }

    private static bool TryRemoveLabelValue(string source, string separator, out string removed)
    {
        removed = string.Empty;
        if (!TryFindLabelValueSeparator(source, separator, out var separatorStart, out _))
            return false;

        removed = source[..separatorStart].Trim();
        return removed.Length > 0;
    }

    private static bool TryFindLabelValueSeparator(
        string source,
        string separator,
        out int separatorStart,
        out int separatorEnd)
    {
        separatorStart = -1;
        separatorEnd = -1;

        var searchStart = 0;
        while (searchStart < source.Length)
        {
            var tokenIndex = source.IndexOf(separator, searchStart, StringComparison.Ordinal);
            if (tokenIndex < 0)
                return false;

            if ((separator == "-" || separator == "=") &&
                tokenIndex + 1 < source.Length &&
                source[tokenIndex + 1] == '>')
            {
                searchStart = tokenIndex + separator.Length;
                continue;
            }

            separatorStart = tokenIndex;
            while (separatorStart > 0 && char.IsWhiteSpace(source[separatorStart - 1]))
                separatorStart--;

            separatorEnd = tokenIndex + separator.Length;
            while (separatorEnd < source.Length && char.IsWhiteSpace(source[separatorEnd]))
                separatorEnd++;

            return separatorStart > 0 && separatorEnd < source.Length;
        }

        return false;
    }

    private static bool TrySplitWhitespaceTokens(string source, out string[] tokens)
    {
        tokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 3 && tokens.All(token => token.Length > 0);
    }

    private static bool TrySplitVariableWhitespaceTokens(string source, out string[] tokens)
    {
        tokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 2 && tokens.All(token => token.Length > 0);
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

            if (examples.All(e => TryGetDelimitedUpperInitials(e.Source, delimiter, out var initials) && initials == e.Expected))
            {
                return s => TryGetDelimitedUpperInitials(s, delimiter, out var initials) ? initials : null;
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

    private static bool TryGetDelimitedUpperInitials(string source, char delimiter, out string initials)
    {
        var parts = source.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            initials = string.Empty;
            return false;
        }

        initials = string.Concat(parts.Select(GetUpperInitial));
        return true;
    }

    private static Func<string, string?>? TryNameAbbreviations(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (TryNameAbbreviation(examples, 2, tokens => GetFirstInitial(tokens[0]) + ". " + tokens[1], out var firstInitialLast))
            return firstInitialLast;

        if (TryNameAbbreviation(examples, 2, tokens => GetFirstInitial(tokens[0]) + ". " + GetFirstInitial(tokens[1]) + ".", out var twoPartInitials))
            return twoPartInitials;

        if (TryNameAbbreviation(examples, 2, tokens => GetUpperInitial(tokens[0]) + ". " + GetUpperInitial(tokens[1]) + ".", out var twoPartUpperInitials))
            return twoPartUpperInitials;

        if (TryNameAbbreviation(examples, 2, tokens => tokens[0] + " " + GetFirstInitial(tokens[1]) + ".", out var firstLastInitial))
            return firstLastInitial;

        if (TryNameAbbreviation(examples, 2, tokens => tokens[1] + " " + GetFirstInitial(tokens[0]) + ".", out var lastFirstInitial))
            return lastFirstInitial;

        if (TryNameAbbreviation(examples, 2, tokens => tokens[1] + ", " + GetFirstInitial(tokens[0]) + ".", out var lastCommaFirstInitial))
            return lastCommaFirstInitial;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[0] + " " + GetFirstInitial(tokens[1]) + ". " + tokens[2], out var middleInitial))
            return middleInitial;

        if (TryNameAbbreviation(examples, 3, tokens => GetFirstInitial(tokens[0]) + ". " + tokens[2], out var firstInitialLastFromThreeParts))
            return firstInitialLastFromThreeParts;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[0] + " " + GetFirstInitial(tokens[2]) + ".", out var firstLastInitialFromThreeParts))
            return firstLastInitialFromThreeParts;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[2] + " " + GetFirstInitial(tokens[0]) + ".", out var lastFirstInitialFromThreeParts))
            return lastFirstInitialFromThreeParts;

        if (TryNameAbbreviation(examples, 3, tokens => GetFirstInitial(tokens[1]) + ". " + tokens[2], out var middleInitialLastFromThreeParts))
            return middleInitialLastFromThreeParts;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[1] + " " + GetFirstInitial(tokens[2]) + ".", out var middleLastInitialFromThreeParts))
            return middleLastInitialFromThreeParts;

        if (TryNameAbbreviation(examples, 3, tokens => GetFirstInitial(tokens[0]) + ". " + GetFirstInitial(tokens[1]) + ". " + tokens[2], out var firstMiddleInitialsLast))
            return firstMiddleInitialsLast;

        if (TryNameAbbreviation(examples, 3, tokens => GetFirstInitial(tokens[0]) + ". " + GetFirstInitial(tokens[1]) + ". " + GetFirstInitial(tokens[2]) + ".", out var threePartInitials))
            return threePartInitials;

        if (TryNameAbbreviation(examples, 3, tokens => GetUpperInitial(tokens[0]) + ". " + GetUpperInitial(tokens[1]) + ". " + GetUpperInitial(tokens[2]) + ".", out var threePartUpperInitials))
            return threePartUpperInitials;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[0] + " " + GetFirstInitial(tokens[1]) + ".", out var firstMiddleInitialOnly))
            return firstMiddleInitialOnly;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[0] + " " + tokens[1] + " " + GetFirstInitial(tokens[2]) + ".", out var firstMiddleLastInitial))
            return firstMiddleLastInitial;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[2] + ", " + tokens[0] + " " + GetFirstInitial(tokens[1]) + ".", out var lastCommaFirstMiddleInitial))
            return lastCommaFirstMiddleInitial;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[2] + ", " + GetFirstInitial(tokens[0]) + ". " + GetFirstInitial(tokens[1]) + ".", out var lastCommaFirstMiddleInitials))
            return lastCommaFirstMiddleInitials;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[2] + " " + GetFirstInitial(tokens[0]) + ". " + GetFirstInitial(tokens[1]) + ".", out var lastFirstMiddleInitials))
            return lastFirstMiddleInitials;

        return null;
    }

    private static bool TryNameAbbreviation(
        IReadOnlyList<(string Source, string Expected)> examples,
        int tokenCount,
        Func<string[], string> formatter,
        out Func<string, string?>? pattern)
    {
        pattern = null;
        foreach (var (source, expected) in examples)
        {
            if (!TrySplitWhitespaceTokens(source, tokenCount, out var tokens) || formatter(tokens) != expected)
                return false;
        }

        pattern = source =>
            TrySplitWhitespaceTokens(source, tokenCount, out var tokens)
                ? formatter(tokens)
                : null;
        return true;
    }

    private static bool TrySplitWhitespaceTokens(string source, int tokenCount, out string[] tokens)
    {
        tokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == tokenCount && tokens.All(token => token.Length > 0);
    }

    private static string GetFirstInitial(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value[0].ToString();

    private static string GetUpperInitial(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : char.ToUpperInvariant(value[0]).ToString();
}
