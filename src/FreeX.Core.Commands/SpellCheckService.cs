using System.Text;
using System.Text.RegularExpressions;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum SpellingIssueSource
{
    CellText,
    Note,
    ThreadedComment,
    ThreadedCommentReply
}

public sealed record SpellingIssue(
    CellAddress Address,
    string Word,
    string Suggestion,
    string CellText,
    int StartIndex = -1,
    int Length = 0,
    SpellingIssueSource Source = SpellingIssueSource.CellText,
    int ReplyIndex = -1);

public sealed record SpellingCorrectionEdit(
    CellAddress Address,
    string OriginalText,
    string CorrectedText,
    int ReplacementCount);

public sealed record SpellingCorrectionPlan(
    IReadOnlyList<SpellingCorrectionEdit> Edits,
    int IssueCount);

public static partial class SpellCheckService
{
    public static IReadOnlyList<SpellingIssue> FindIssues(
        Workbook workbook,
        SheetId? sheetId = null,
        IReadOnlySet<string>? customDictionary = null)
    {
        List<SpellingIssue>? result = null;

        foreach (var sheet in EnumerateTargetSheets(workbook, sheetId))
        {
            List<SpellingIssue>? sheetIssues = null;
            foreach (var (address, cell) in sheet.EnumerateCells())
            {
                if (cell.HasFormula || cell.Value is not TextValue textValue)
                    continue;

                AddIssuesForText(
                    ref sheetIssues,
                    address,
                    textValue.Value,
                    customDictionary,
                    SpellingIssueSource.CellText);
            }

            foreach (var (address, noteText) in sheet.Comments)
            {
                AddIssuesForText(
                    ref sheetIssues,
                    address,
                    noteText,
                    customDictionary,
                    SpellingIssueSource.Note);
            }

            foreach (var (address, threadedComment) in sheet.ThreadedComments)
            {
                AddIssuesForText(
                    ref sheetIssues,
                    address,
                    threadedComment.Text,
                    customDictionary,
                    SpellingIssueSource.ThreadedComment);

                for (var replyIndex = 0; replyIndex < threadedComment.Replies.Count; replyIndex++)
                {
                    AddIssuesForText(
                        ref sheetIssues,
                        address,
                        threadedComment.Replies[replyIndex].Text,
                        customDictionary,
                        SpellingIssueSource.ThreadedCommentReply,
                        replyIndex);
                }
            }

            if (sheetIssues is null)
                continue;

            sheetIssues.Sort(CompareIssues);
            result ??= [];
            result.AddRange(sheetIssues);
        }

        return result ?? [];
    }

    /// <summary>
    /// Returns every known-correction issue found in one literal text cell.
    /// Formula cells are intentionally handled by callers and are not edited as text.
    /// </summary>
    public static IReadOnlyList<SpellingIssue> FindIssuesInCell(
        CellAddress address,
        string text,
        IReadOnlySet<string>? customDictionary = null) =>
        FindIssuesInText(address, text, customDictionary, SpellingIssueSource.CellText);

    private static IReadOnlyList<SpellingIssue> FindIssuesInText(
        CellAddress address,
        string text,
        IReadOnlySet<string>? customDictionary,
        SpellingIssueSource source,
        int replyIndex = -1)
    {
        List<SpellingIssue>? issues = null;
        var ignoredSpans = FindIgnoredSpans(text);
        WordToken? previousWord = null;
        var index = 0;
        while (TryReadNextWord(text, index, out var word))
        {
            index = word.End;
            if (OverlapsIgnoredSpan(word.Start, word.Length, ignoredSpans))
            {
                previousWord = null;
                continue;
            }

            var wordSpan = text.AsSpan(word.Start, word.Length);
            var isKnownCorrection = TryGetKnownCorrection(wordSpan, out var suggestion);
            if (isKnownCorrection && !ContainsCustomDictionaryEntry(customDictionary, wordSpan))
            {
                var wordText = wordSpan.ToString();
                issues ??= [];
                issues.Add(new SpellingIssue(
                    address,
                    wordText,
                    MatchCapitalization(wordSpan, suggestion),
                    text,
                    word.Start,
                    word.Length,
                    source,
                    replyIndex));
            }

            if (previousWord is { } previous &&
                previous.Length >= 2 &&
                word.Length >= 2 &&
                IsWhitespaceOnly(text, previous.End, word.Start) &&
                EqualWordsIgnoreCase(text.AsSpan(previous.Start, previous.Length), wordSpan) &&
                !isKnownCorrection &&
                !ContainsCustomDictionaryEntry(customDictionary, text.AsSpan(previous.Start, word.End - previous.Start)))
            {
                issues ??= [];
                issues.Add(new SpellingIssue(
                    address,
                    text.Substring(previous.Start, word.End - previous.Start),
                    text.Substring(previous.Start, previous.Length),
                    text,
                    previous.Start,
                    word.End - previous.Start,
                    source,
                    replyIndex));
            }

            previousWord = word;
        }

        return issues ?? [];
    }

    private static void AddIssuesForText(
        ref List<SpellingIssue>? issues,
        CellAddress address,
        string text,
        IReadOnlySet<string>? customDictionary,
        SpellingIssueSource source,
        int replyIndex = -1)
    {
        if (!HasSpellCheckIssueCandidate(text, customDictionary))
            return;

        var textIssues = FindIssuesInText(address, text, customDictionary, source, replyIndex);
        if (textIssues.Count == 0)
            return;

        issues ??= [];
        issues.AddRange(textIssues);
    }

    /// <summary>
    /// Plans text-cell edits that apply every known correction in workbook sheet order,
    /// then row-major order within each sheet. Formula cells are not planned as text edits.
    /// </summary>
    public static SpellingCorrectionPlan PlanKnownCorrections(Workbook workbook, SheetId? sheetId = null)
    {
        var edits = new List<SpellingCorrectionEdit>();
        var issueCount = 0;

        foreach (var sheet in EnumerateTargetSheets(workbook, sheetId))
        {
            var sheetEdits = new List<SpellingCorrectionEdit>();
            foreach (var (address, cell) in sheet.EnumerateCells())
            {
                if (cell.HasFormula || cell.Value is not TextValue textValue)
                    continue;

                if (!HasKnownCorrectionCandidate(textValue.Value))
                    continue;

                var corrected = ApplyKnownCorrections(textValue.Value, out var replacementCount);
                issueCount += replacementCount;
                if (replacementCount > 0 && corrected != textValue.Value)
                    sheetEdits.Add(new SpellingCorrectionEdit(address, textValue.Value, corrected, replacementCount));
            }

            sheetEdits.Sort((a, b) =>
            {
                var rowCmp = a.Address.Row.CompareTo(b.Address.Row);
                return rowCmp != 0 ? rowCmp : a.Address.Col.CompareTo(b.Address.Col);
            });
            edits.AddRange(sheetEdits);
        }

        return new SpellingCorrectionPlan(edits, issueCount);
    }

    public static IReadOnlyList<(CellAddress Address, Cell NewCell)> BuildCorrectionCellEdits(SpellingCorrectionPlan plan) =>
        plan.Edits
            .Select(edit => (edit.Address, Cell.FromValue(new TextValue(edit.CorrectedText))))
            .ToList();

    public static string ApplyCorrection(SpellingIssue issue, string replacement)
    {
        if (IsValidIssueSpan(issue))
        {
            var original = issue.CellText.AsSpan(issue.StartIndex, issue.Length);
            var correctedReplacement = MatchCapitalization(original, replacement);
            return issue.CellText[..issue.StartIndex] + correctedReplacement + issue.CellText[(issue.StartIndex + issue.Length)..];
        }

        return ApplyCorrectionOccurrences(issue, replacement, replaceAll: false);
    }

    public static string ApplyCorrectionToAllOccurrences(SpellingIssue issue, string replacement) =>
        ApplyCorrectionOccurrences(issue, replacement, replaceAll: true);

    private static bool IsValidIssueSpan(SpellingIssue issue)
    {
        if (issue.StartIndex < 0 ||
            issue.Length <= 0 ||
            issue.StartIndex > issue.CellText.Length - issue.Length)
        {
            return false;
        }

        return issue.CellText.AsSpan(issue.StartIndex, issue.Length).Equals(issue.Word, StringComparison.OrdinalIgnoreCase);
    }

    private static string ApplyCorrectionOccurrences(SpellingIssue issue, string replacement, bool replaceAll)
    {
        var regex = new Regex(
            $@"\b{Regex.Escape(issue.Word)}\b",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));

        var ignoredSpans = FindIgnoredSpans(issue.CellText);
        StringBuilder? builder = null;
        var appendStart = 0;
        var replaced = false;
        foreach (Match match in regex.Matches(issue.CellText))
        {
            if (OverlapsIgnoredSpan(match.Index, match.Length, ignoredSpans))
                continue;

            builder ??= new StringBuilder(issue.CellText.Length);
            builder.Append(issue.CellText, appendStart, match.Index - appendStart);
            builder.Append(MatchCapitalization(match.Value, replacement));
            appendStart = match.Index + match.Length;
            replaced = true;

            if (!replaceAll)
                break;
        }

        if (!replaced || builder is null)
            return issue.CellText;

        builder.Append(issue.CellText, appendStart, issue.CellText.Length - appendStart);
        return builder.ToString();
    }

    private static string ApplyKnownCorrections(string text, out int replacementCount)
    {
        var count = 0;
        var ignoredSpans = FindIgnoredSpans(text);
        StringBuilder? builder = null;
        var appendStart = 0;
        var index = 0;
        while (TryReadNextWord(text, index, out var word))
        {
            index = word.End;
            if (OverlapsIgnoredSpan(word.Start, word.Length, ignoredSpans))
                continue;

            var wordSpan = text.AsSpan(word.Start, word.Length);
            if (!TryGetKnownCorrection(wordSpan, out var suggestion))
                continue;

            builder ??= new StringBuilder(text.Length);
            builder.Append(text, appendStart, word.Start - appendStart);
            builder.Append(MatchCapitalization(wordSpan, suggestion));
            appendStart = word.End;
            count++;
        }

        replacementCount = count;
        if (builder is null)
            return text;

        builder.Append(text, appendStart, text.Length - appendStart);
        return builder.ToString();
    }

    private static IReadOnlyList<Range> FindIgnoredSpans(string text)
    {
        if (!HasIgnoredSpanCandidate(text))
            return [];

        List<Range>? spans = null;
        foreach (Match match in IgnoredAddressSpanRegex().Matches(text))
        {
            spans ??= [];
            spans.Add(new Range(match.Index, match.Index + match.Length));
        }

        return spans ?? [];
    }

    private static bool HasIgnoredSpanCandidate(string text)
    {
        foreach (var value in text)
            if (value is ':' or '/' or '\\' or '@' or '.' or '~')
                return true;

        return false;
    }

    private static bool OverlapsIgnoredSpan(int index, int length, IReadOnlyList<Range> ignoredSpans)
    {
        var end = index + length;
        for (var spanIndex = 0; spanIndex < ignoredSpans.Count; spanIndex++)
        {
            var span = ignoredSpans[spanIndex];
            if (index < span.End.Value && end > span.Start.Value)
                return true;
        }

        return false;
    }

    private static IEnumerable<Sheet> EnumerateTargetSheets(Workbook workbook, SheetId? sheetId)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (!sheetId.HasValue || sheet.Id == sheetId.Value)
                yield return sheet;
        }
    }

    private static int CompareIssues(SpellingIssue left, SpellingIssue right)
    {
        var addressCmp = left.Address.CompareTo(right.Address);
        if (addressCmp != 0)
            return addressCmp;

        var sourceCmp = GetIssueSourceOrder(left.Source).CompareTo(GetIssueSourceOrder(right.Source));
        if (sourceCmp != 0)
            return sourceCmp;

        var replyCmp = left.ReplyIndex.CompareTo(right.ReplyIndex);
        if (replyCmp != 0)
            return replyCmp;

        return left.StartIndex.CompareTo(right.StartIndex);
    }

    private static int GetIssueSourceOrder(SpellingIssueSource source) =>
        source switch
        {
            SpellingIssueSource.CellText => 0,
            SpellingIssueSource.Note => 1,
            SpellingIssueSource.ThreadedComment => 2,
            SpellingIssueSource.ThreadedCommentReply => 3,
            _ => 4
        };

    private static string MatchCapitalization(string original, string replacement) =>
        MatchCapitalization(original.AsSpan(), replacement);

    private static string MatchCapitalization(ReadOnlySpan<char> original, string replacement)
    {
        if (original.Length == 0 || replacement.Length == 0)
            return replacement;

        var hasLetter = false;
        var allLettersAreUpper = true;
        foreach (var value in original)
        {
            if (!char.IsLetter(value))
                continue;

            hasLetter = true;
            if (!char.IsUpper(value))
                allLettersAreUpper = false;
        }

        if (hasLetter && allLettersAreUpper)
            return replacement.ToUpperInvariant();

        if (char.IsUpper(original[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];

        return replacement;
    }

    private readonly record struct WordToken(int Start, int End)
    {
        public int Length => End - Start;
    }

    private static bool TryReadNextWord(string text, int startIndex, out WordToken word)
    {
        var index = startIndex;
        while (index < text.Length)
        {
            while (index < text.Length && !char.IsLetter(text[index]))
                index++;

            if (index >= text.Length)
                break;

            var start = index;
            index++;
            while (index < text.Length && IsWordContinuation(text[index]))
                index++;

            if (IsWordBoundaryBefore(text, start) && IsWordBoundaryAfter(text, index))
            {
                word = new WordToken(start, index);
                return true;
            }
        }

        word = default;
        return false;
    }

    private static bool IsWordContinuation(char value) =>
        char.IsLetter(value) || value == '\'';

    private static bool IsWordBoundaryBefore(string text, int index) =>
        index == 0 || !IsLetterDigitOrUnderscore(text[index - 1]);

    private static bool IsWordBoundaryAfter(string text, int index) =>
        index >= text.Length || !IsLetterDigitOrUnderscore(text[index]);

    private static bool IsLetterDigitOrUnderscore(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

    private static bool IsWhitespaceOnly(string text, int start, int end)
    {
        if (start >= end)
            return false;

        for (var index = start; index < end; index++)
            if (text[index] is not (' ' or '\t' or '\r' or '\n' or '\f' or '\v' or '\u00A0'))
                return false;

        return true;
    }

    private static bool EqualWordsIgnoreCase(ReadOnlySpan<char> left, ReadOnlySpan<char> right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static bool HasSpellCheckIssueCandidate(string text, IReadOnlySet<string>? customDictionary)
    {
        WordToken? previousWord = null;
        var index = 0;
        while (TryReadNextWord(text, index, out var word))
        {
            index = word.End;
            var wordSpan = text.AsSpan(word.Start, word.Length);
            var isKnownCorrection = TryGetKnownCorrection(wordSpan, out _);
            if (isKnownCorrection && !ContainsCustomDictionaryEntry(customDictionary, wordSpan))
                return true;

            if (previousWord is { } previous &&
                previous.Length >= 2 &&
                word.Length >= 2 &&
                IsWhitespaceOnly(text, previous.End, word.Start) &&
                EqualWordsIgnoreCase(text.AsSpan(previous.Start, previous.Length), wordSpan) &&
                !isKnownCorrection &&
                !ContainsCustomDictionaryEntry(customDictionary, text.AsSpan(previous.Start, word.End - previous.Start)))
            {
                return true;
            }

            previousWord = word;
        }

        return false;
    }

    private static bool ContainsCustomDictionaryEntry(IReadOnlySet<string>? customDictionary, ReadOnlySpan<char> candidate)
    {
        if (customDictionary is null || customDictionary.Count == 0)
            return false;

        foreach (var entry in customDictionary)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            if (entry.AsSpan().Trim().Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool HasKnownCorrectionCandidate(string text)
    {
        var index = 0;
        while (TryReadNextWord(text, index, out var word))
        {
            index = word.End;
            if (TryGetKnownCorrection(text.AsSpan(word.Start, word.Length), out _))
                return true;
        }

        return false;
    }

    private static bool TryGetKnownCorrection(ReadOnlySpan<char> word, out string suggestion)
    {
        var first = word.Length > 0 ? ToAsciiLowerInvariant(word[0]) : '\0';
        switch (word.Length)
        {
            case 3:
                if (first == 't' && EqualAsciiWordIgnoreCase(word, "teh"))
                {
                    suggestion = "the";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "adn"))
                {
                    suggestion = "and";
                    return true;
                }

                break;
            case 4:
                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "alot"))
                {
                    suggestion = "a lot";
                    return true;
                }

                break;
            case 5:
                if (first == 't' && EqualAsciiWordIgnoreCase(word, "thier"))
                {
                    suggestion = "their";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "wierd"))
                {
                    suggestion = "weird";
                    return true;
                }

                break;
            case 6:
                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "adress"))
                {
                    suggestion = "address";
                    return true;
                }

                if (first == 'u' && EqualAsciiWordIgnoreCase(word, "untill"))
                {
                    suggestion = "until";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sucess"))
                {
                    suggestion = "success";
                    return true;
                }

                break;
            case 7:
                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "acheive"))
                {
                    suggestion = "achieve";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "beleive"))
                {
                    suggestion = "believe";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "recieve"))
                {
                    suggestion = "receive";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "reciept"))
                {
                    suggestion = "receipt";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "occured"))
                {
                    suggestion = "occurred";
                    return true;
                }

                break;
            case 8:
                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "commited"))
                {
                    suggestion = "committed";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "seperate"))
                {
                    suggestion = "separate";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "calender"))
                {
                    suggestion = "calendar";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "recomend"))
                {
                    suggestion = "recommend";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "tommorow"))
                {
                    suggestion = "tomorrow";
                    return true;
                }

                break;
            case 9:
                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "arguement"))
                {
                    suggestion = "argument";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "existance"))
                {
                    suggestion = "existence";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "goverment"))
                {
                    suggestion = "government";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "mispelled"))
                {
                    suggestion = "misspelled";
                    return true;
                }

                break;
            case 10:
                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "definately"))
                {
                    suggestion = "definitely";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "acommodate"))
                {
                    suggestion = "accommodate";
                    return true;
                }

                if (first == 'n' && EqualAsciiWordIgnoreCase(word, "neccessary"))
                {
                    suggestion = "necessary";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "publically"))
                {
                    suggestion = "publicly";
                    return true;
                }

                break;
            case 12:
                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "maintainance"))
                {
                    suggestion = "maintenance";
                    return true;
                }

                break;
            case 14:
                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "recomendations"))
                {
                    suggestion = "recommendations";
                    return true;
                }

                break;
        }

        suggestion = string.Empty;
        return false;
    }

    private static bool EqualAsciiWordIgnoreCase(ReadOnlySpan<char> word, string expected)
    {
        if (word.Length != expected.Length)
            return false;

        for (var index = 0; index < word.Length; index++)
        {
            if (ToAsciiLowerInvariant(word[index]) != expected[index])
                return false;
        }

        return true;
    }

    private static char ToAsciiLowerInvariant(char value) =>
        value is >= 'A' and <= 'Z' ? (char)(value | 0x20) : value;

    [GeneratedRegex(@"(?ix)
        (?:
            (?<![\p{L}\p{N}_])(?:[A-Z][A-Z0-9+.-]*://|mailto:)[^\s<>""']+
          | (?<![\p{L}\p{N}_])www\.[^\s<>""']+
          | (?<![\p{L}\p{N}_])[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b
          | (?<![\p{L}\p{N}_])(?:[A-Z]:[\\/]|\\\\)[^\s<>""']+
          | (?<![\p{L}\p{N}_])(?:~|/)[\w.-]+(?:/[\w .-]+)+
          | (?<![\p{L}\p{N}_])[\w.-]+\.(?:xlsx?|xlsm|csv|tsv|txt|pdf|docx?|pptx?|zip|json|xml|html?|png|jpe?g|gif|svg)\b
        )")]
    private static partial Regex IgnoredAddressSpanRegex();
}
