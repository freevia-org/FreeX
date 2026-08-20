using System.Text;
using System.Text.RegularExpressions;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum SpellingIssueSource
{
    CellText,
    Note,
    ThreadedComment,
    ThreadedCommentReply,
    TextBox
}

public sealed record SpellingIssue(
    CellAddress Address,
    string Word,
    string Suggestion,
    string CellText,
    int StartIndex = -1,
    int Length = 0,
    SpellingIssueSource Source = SpellingIssueSource.CellText,
    int ReplyIndex = -1,
    Guid? TextBoxId = null);

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

            foreach (var textBox in sheet.TextBoxes)
            {
                AddIssuesForText(
                    ref sheetIssues,
                    textBox.Anchor,
                    textBox.Text,
                    customDictionary,
                    SpellingIssueSource.TextBox,
                    textBoxId: textBox.Id);
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
        int replyIndex = -1,
        Guid? textBoxId = null)
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
                    replyIndex,
                    textBoxId));
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
                    replyIndex,
                    textBoxId));
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
        int replyIndex = -1,
        Guid? textBoxId = null)
    {
        if (!HasSpellCheckIssueCandidate(text, customDictionary))
            return;

        var textIssues = FindIssuesInText(address, text, customDictionary, source, replyIndex, textBoxId);
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
        if (TryGetRepeatedWordReplacement(issue, replacement, out var repeatedWord))
        {
            return ApplyRepeatedWordRunCorrection(issue.CellText, repeatedWord, replacement, replaceAll);
        }

        var ignoredSpans = FindIgnoredSpans(issue.CellText);
        StringBuilder? builder = null;
        var appendStart = 0;
        var replaced = false;
        var index = 0;
        while (TryReadNextWord(issue.CellText, index, out var word))
        {
            index = word.End;
            if (OverlapsIgnoredSpan(word.Start, word.Length, ignoredSpans) ||
                !EqualWordsIgnoreCase(issue.CellText.AsSpan(word.Start, word.Length), issue.Word.AsSpan()))
            {
                continue;
            }

            builder ??= new StringBuilder(issue.CellText.Length);
            builder.Append(issue.CellText, appendStart, word.Start - appendStart);
            builder.Append(MatchCapitalization(issue.CellText.AsSpan(word.Start, word.Length), replacement));
            appendStart = word.End;
            replaced = true;

            if (!replaceAll)
                break;
        }

        if (!replaced || builder is null)
            return issue.CellText;

        builder.Append(issue.CellText, appendStart, issue.CellText.Length - appendStart);
        return builder.ToString();
    }

    private static bool TryGetRepeatedWordReplacement(SpellingIssue issue, string replacement, out string repeatedWord)
    {
        repeatedWord = string.Empty;
        if (!TryReadNextWord(issue.Word, 0, out var first) ||
            !TryReadNextWord(issue.Word, first.End, out var second) ||
            TryReadNextWord(issue.Word, second.End, out _) ||
            !IsWhitespaceOnly(issue.Word, first.End, second.Start))
        {
            return false;
        }

        var firstWord = issue.Word.AsSpan(first.Start, first.Length);
        var secondWord = issue.Word.AsSpan(second.Start, second.Length);
        if (!EqualWordsIgnoreCase(firstWord, secondWord) ||
            !EqualWordsIgnoreCase(firstWord, replacement.AsSpan()))
        {
            return false;
        }

        repeatedWord = firstWord.ToString();
        return true;
    }

    private static string ApplyRepeatedWordRunCorrection(string text, string repeatedWord, string replacement, bool replaceAll)
    {
        var ignoredSpans = FindIgnoredSpans(text);
        StringBuilder? builder = null;
        var appendStart = 0;
        var replaced = false;
        WordToken? runFirst = null;
        WordToken? runLast = null;
        var runCount = 0;
        var index = 0;

        while (TryReadNextWord(text, index, out var word))
        {
            index = word.End;
            if (OverlapsIgnoredSpan(word.Start, word.Length, ignoredSpans) ||
                !EqualWordsIgnoreCase(text.AsSpan(word.Start, word.Length), repeatedWord.AsSpan()))
            {
                FlushRepeatedRun();
                if (replaced && !replaceAll)
                    break;
                continue;
            }

            if (runLast is { } previous &&
                IsWhitespaceOnly(text, previous.End, word.Start))
            {
                runLast = word;
                runCount++;
                continue;
            }

            FlushRepeatedRun();
            if (replaced && !replaceAll)
                break;
            runFirst = word;
            runLast = word;
            runCount = 1;
        }

        FlushRepeatedRun();
        if (builder is null)
            return text;

        builder.Append(text, appendStart, text.Length - appendStart);
        return builder.ToString();

        void FlushRepeatedRun()
        {
            if ((!replaced || replaceAll) &&
                runCount >= 2 &&
                runFirst is { } first &&
                runLast is { } last)
            {
                builder ??= new StringBuilder(text.Length);
                builder.Append(text, appendStart, first.Start - appendStart);
                builder.Append(MatchCapitalization(text.AsSpan(first.Start, first.Length), replacement));
                appendStart = last.End;
                replaced = true;
            }

            runFirst = null;
            runLast = null;
            runCount = 0;
        }
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
            SpellingIssueSource.TextBox => 4,
            _ => 5
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
        if (TryGetCommonProofingCorrection(word, out suggestion))
            return true;

        var first = word.Length > 0 ? ToAsciiLowerInvariant(word[0]) : '\0';
        switch (word.Length)
        {
            case 3:
                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "axs"))
                {
                    suggestion = "axis";
                    return true;
                }

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

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "audt"))
                {
                    suggestion = "audit";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "chrt"))
                {
                    suggestion = "chart";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fild"))
                {
                    suggestion = "field";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "isue"))
                {
                    suggestion = "issue";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "ordr"))
                {
                    suggestion = "order";
                    return true;
                }

                if (first == 'q' && EqualAsciiWordIgnoreCase(word, "queu"))
                {
                    suggestion = "queue";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "rang"))
                {
                    suggestion = "range";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "riks"))
                {
                    suggestion = "risk";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "srot"))
                {
                    suggestion = "sorting";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "tabl"))
                {
                    suggestion = "table";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "totl"))
                {
                    suggestion = "total";
                    return true;
                }

                break;
            case 5:
                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "acion"))
                {
                    suggestion = "action";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "seasn"))
                {
                    suggestion = "season";
                    return true;
                }

                if (first == 'h' && EqualAsciiWordIgnoreCase(word, "hazrd"))
                {
                    suggestion = "hazard";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "agnda"))
                {
                    suggestion = "agenda";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "amout"))
                {
                    suggestion = "amount";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "axies"))
                {
                    suggestion = "axis";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "buton"))
                {
                    suggestion = "button";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "chrat"))
                {
                    suggestion = "chart";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "cliam"))
                {
                    suggestion = "claim";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "feild"))
                {
                    suggestion = "field";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "grnad"))
                {
                    suggestion = "grand";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "lable"))
                {
                    suggestion = "label";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "legnd"))
                {
                    suggestion = "legend";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "ledgr"))
                {
                    suggestion = "ledger";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "metrc"))
                {
                    suggestion = "metric";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "manul"))
                {
                    suggestion = "manual";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "mockp"))
                {
                    suggestion = "mockup";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "menuu"))
                {
                    suggestion = "menu";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "veneu"))
                {
                    suggestion = "venue";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "odrer"))
                {
                    suggestion = "order";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "owenr"))
                {
                    suggestion = "owner";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "ownre"))
                {
                    suggestion = "owner";
                    return true;
                }

                if (first == 'n' && EqualAsciiWordIgnoreCase(word, "noets"))
                {
                    suggestion = "notes";
                    return true;
                }

                if (first == 'n' && EqualAsciiWordIgnoreCase(word, "notse"))
                {
                    suggestion = "notes";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "colum"))
                {
                    suggestion = "column";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "vlaue"))
                {
                    suggestion = "value";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "valus"))
                {
                    suggestion = "values";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "pviot"))
                {
                    suggestion = "pivot";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "pivto"))
                {
                    suggestion = "pivot";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "polcy"))
                {
                    suggestion = "policy";
                    return true;
                }

                if (first == 'q' && EqualAsciiWordIgnoreCase(word, "qoute"))
                {
                    suggestion = "quote";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "ribbn"))
                {
                    suggestion = "ribbon";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "risck"))
                {
                    suggestion = "risk";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "tbale"))
                {
                    suggestion = "table";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sorce"))
                {
                    suggestion = "source";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "thier"))
                {
                    suggestion = "their";
                    return true;
                }

                if (first == 'u' && EqualAsciiWordIgnoreCase(word, "uptme"))
                {
                    suggestion = "uptime";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "wierd"))
                {
                    suggestion = "weird";
                    return true;
                }

                break;
            case 6:
                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sampel"))
                {
                    suggestion = "sample";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fitnes"))
                {
                    suggestion = "fitness";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "focuss"))
                {
                    suggestion = "focus";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "alertt"))
                {
                    suggestion = "alert";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "drilll"))
                {
                    suggestion = "drill";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "regimn"))
                {
                    suggestion = "regimen";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sheltr"))
                {
                    suggestion = "shelter";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sirenn"))
                {
                    suggestion = "siren";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "smokee"))
                {
                    suggestion = "smoke";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "scorng"))
                {
                    suggestion = "scoring";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "stormm"))
                {
                    suggestion = "storm";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "weathr"))
                {
                    suggestion = "weather";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "workot"))
                {
                    suggestion = "workout";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "arival"))
                {
                    suggestion = "arrival";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "bagage"))
                {
                    suggestion = "baggage";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "bookng"))
                {
                    suggestion = "booking";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "bootth"))
                {
                    suggestion = "booth";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "cultre"))
                {
                    suggestion = "culture";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "exhbit"))
                {
                    suggestion = "exhibit";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fligth"))
                {
                    suggestion = "flight";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "lugage"))
                {
                    suggestion = "luggage";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "speker"))
                {
                    suggestion = "speaker";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "adress"))
                {
                    suggestion = "address";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "actoin"))
                {
                    suggestion = "action";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "artcle"))
                {
                    suggestion = "article";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "acount"))
                {
                    suggestion = "account";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "acural"))
                {
                    suggestion = "accrual";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "accrul"))
                {
                    suggestion = "accrual";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "acrued"))
                {
                    suggestion = "accrued";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "auditt"))
                {
                    suggestion = "audit";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "budjet"))
                {
                    suggestion = "budget";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "bankng"))
                {
                    suggestion = "banking";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "billng"))
                {
                    suggestion = "billing";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "packge"))
                {
                    suggestion = "package";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "publsh"))
                {
                    suggestion = "publish";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "licnse"))
                {
                    suggestion = "license";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "renewl"))
                {
                    suggestion = "renewal";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "seatss"))
                {
                    suggestion = "seats";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "triall"))
                {
                    suggestion = "trial";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "tooltp"))
                {
                    suggestion = "tooltip";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "catlog"))
                {
                    suggestion = "catalog";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "carier"))
                {
                    suggestion = "carrier";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "cliams"))
                {
                    suggestion = "claims";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "climte"))
                {
                    suggestion = "climate";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fomula"))
                {
                    suggestion = "formula";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "formla"))
                {
                    suggestion = "formula";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "feilds"))
                {
                    suggestion = "fields";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fundng"))
                {
                    suggestion = "funding";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "featre"))
                {
                    suggestion = "feature";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "freigt"))
                {
                    suggestion = "freight";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fuelng"))
                {
                    suggestion = "fueling";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "grantt"))
                {
                    suggestion = "grant";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "filtre"))
                {
                    suggestion = "filter";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fliter"))
                {
                    suggestion = "filter";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fitler"))
                {
                    suggestion = "filter";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "cloumn"))
                {
                    suggestion = "column";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "colomn"))
                {
                    suggestion = "column";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "custmr"))
                {
                    suggestion = "customer";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "custms"))
                {
                    suggestion = "customs";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "churnn"))
                {
                    suggestion = "churn";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "cloudd"))
                {
                    suggestion = "cloud";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "cohrot"))
                {
                    suggestion = "cohort";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "datset"))
                {
                    suggestion = "dataset";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "desgin"))
                {
                    suggestion = "design";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "foruma"))
                {
                    suggestion = "formula";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "insigt"))
                {
                    suggestion = "insight";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "invoce"))
                {
                    suggestion = "invoice";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "agneda"))
                {
                    suggestion = "agenda";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "isssue"))
                {
                    suggestion = "issue";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "meetng"))
                {
                    suggestion = "meeting";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "mesage"))
                {
                    suggestion = "message";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "manger"))
                {
                    suggestion = "manager";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "metirc"))
                {
                    suggestion = "metric";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "messge"))
                {
                    suggestion = "message";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "onbord"))
                {
                    suggestion = "onboard";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "minuts"))
                {
                    suggestion = "minutes";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "lables"))
                {
                    suggestion = "labels";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "legned"))
                {
                    suggestion = "legend";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "leasng"))
                {
                    suggestion = "leasing";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "piviot"))
                {
                    suggestion = "pivot";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "pendng"))
                {
                    suggestion = "pending";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "paymnt"))
                {
                    suggestion = "payment";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "payble"))
                {
                    suggestion = "payable";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "patint"))
                {
                    suggestion = "patient";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "premum"))
                {
                    suggestion = "premium";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "pricng"))
                {
                    suggestion = "pricing";
                    return true;
                }

                if (first == 'q' && EqualAsciiWordIgnoreCase(word, "quater"))
                {
                    suggestion = "quarter";
                    return true;
                }

                if (first == 'q' && EqualAsciiWordIgnoreCase(word, "querry"))
                {
                    suggestion = "query";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "revenu"))
                {
                    suggestion = "revenue";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "relase"))
                {
                    suggestion = "release";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "raange"))
                {
                    suggestion = "range";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "refesh"))
                {
                    suggestion = "refresh";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "reivew"))
                {
                    suggestion = "review";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "reveiw"))
                {
                    suggestion = "review";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "routng"))
                {
                    suggestion = "routing";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "recipt"))
                {
                    suggestion = "receipt";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "roadmp"))
                {
                    suggestion = "roadmap";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "griddd"))
                {
                    suggestion = "grid";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fiberr"))
                {
                    suggestion = "fiber";
                    return true;
                }

                if (first == 'h' && EqualAsciiWordIgnoreCase(word, "handof"))
                {
                    suggestion = "handoff";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "modemm"))
                {
                    suggestion = "modem";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "opticl"))
                {
                    suggestion = "optical";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sumary"))
                {
                    suggestion = "summary";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sereis"))
                {
                    suggestion = "series";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "serise"))
                {
                    suggestion = "series";
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

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "slicre"))
                {
                    suggestion = "slicer";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sampel"))
                {
                    suggestion = "sample";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "slcier"))
                {
                    suggestion = "slicer";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "servce"))
                {
                    suggestion = "service";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "segmnt"))
                {
                    suggestion = "segment";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "scopee"))
                {
                    suggestion = "scope";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sortng"))
                {
                    suggestion = "sorting";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sponsr"))
                {
                    suggestion = "sponsor";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sewerr"))
                {
                    suggestion = "sewer";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "stockk"))
                {
                    suggestion = "stock";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "stauts"))
                {
                    suggestion = "status";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "studnt"))
                {
                    suggestion = "student";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "scrapp"))
                {
                    suggestion = "scrap";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "shiftt"))
                {
                    suggestion = "shift";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "signng"))
                {
                    suggestion = "signing";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "suport"))
                {
                    suggestion = "support";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "testng"))
                {
                    suggestion = "testing";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "taxabl"))
                {
                    suggestion = "taxable";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "taxble"))
                {
                    suggestion = "taxable";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "tenent"))
                {
                    suggestion = "tenant";
                    return true;
                }

                if (first == 'y' && EqualAsciiWordIgnoreCase(word, "yieldd"))
                {
                    suggestion = "yield";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "vendro"))
                {
                    suggestion = "vendor";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "vlookp"))
                {
                    suggestion = "vlookup";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "waterr"))
                {
                    suggestion = "water";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fencng"))
                {
                    suggestion = "fencing";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "grainn"))
                {
                    suggestion = "grain";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "grazng"))
                {
                    suggestion = "grazing";
                    return true;
                }

                if (first == 'h' && EqualAsciiWordIgnoreCase(word, "harvst"))
                {
                    suggestion = "harvest";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "orchrd"))
                {
                    suggestion = "orchard";
                    return true;
                }

                if (first == 'x' && EqualAsciiWordIgnoreCase(word, "xlookp"))
                {
                    suggestion = "xlookup";
                    return true;
                }

                break;
            case 7:
                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "acheive"))
                {
                    suggestion = "achieve";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "athleet"))
                {
                    suggestion = "athlete";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "leaguee"))
                {
                    suggestion = "league";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "wellnes"))
                {
                    suggestion = "wellness";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "airlnie"))
                {
                    suggestion = "airline";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "advisry"))
                {
                    suggestion = "advisory";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "breachd"))
                {
                    suggestion = "breached";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "checkbx"))
                {
                    suggestion = "checkbox";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "dialogg"))
                {
                    suggestion = "dialog";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "expirng"))
                {
                    suggestion = "expiring";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "floodng"))
                {
                    suggestion = "flooding";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "incidnt"))
                {
                    suggestion = "incident";
                    return true;
                }

                if (first == 'k' && EqualAsciiWordIgnoreCase(word, "keybord"))
                {
                    suggestion = "keyboard";
                    return true;
                }

                if (first == 'k' && EqualAsciiWordIgnoreCase(word, "keytipp"))
                {
                    suggestion = "keytip";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "outbrek"))
                {
                    suggestion = "outbreak";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "outagee"))
                {
                    suggestion = "outage";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "rescuee"))
                {
                    suggestion = "rescue";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "respnse"))
                {
                    suggestion = "response";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "renewel"))
                {
                    suggestion = "renewal";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "localee"))
                {
                    suggestion = "locale";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "resorce"))
                {
                    suggestion = "resource";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "severty"))
                {
                    suggestion = "severity";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "shortct"))
                {
                    suggestion = "shortcut";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "triagee"))
                {
                    suggestion = "triage";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "verison"))
                {
                    suggestion = "version";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "boardng"))
                {
                    suggestion = "boarding";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "itinery"))
                {
                    suggestion = "itinerary";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "passprt"))
                {
                    suggestion = "passport";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sessoin"))
                {
                    suggestion = "session";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "shuttel"))
                {
                    suggestion = "shuttle";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "travell"))
                {
                    suggestion = "travel";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "ammount"))
                {
                    suggestion = "amount";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "analsys"))
                {
                    suggestion = "analysis";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "argment"))
                {
                    suggestion = "argument";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "aproval"))
                {
                    suggestion = "approval";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "approvl"))
                {
                    suggestion = "approval";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "approvr"))
                {
                    suggestion = "approver";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "alertng"))
                {
                    suggestion = "alerting";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "auditng"))
                {
                    suggestion = "auditing";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "acounts"))
                {
                    suggestion = "accounts";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "treasry"))
                {
                    suggestion = "treasury";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "trailor"))
                {
                    suggestion = "trailer";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "balence"))
                {
                    suggestion = "balance";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "basline"))
                {
                    suggestion = "baseline";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "backkup"))
                {
                    suggestion = "backup";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "benifit"))
                {
                    suggestion = "benefit";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "bugfixx"))
                {
                    suggestion = "bugfix";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "budgget"))
                {
                    suggestion = "budget";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "cashflw"))
                {
                    suggestion = "cashflow";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "couponn"))
                {
                    suggestion = "coupon";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "caterng"))
                {
                    suggestion = "catering";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "donaton"))
                {
                    suggestion = "donation";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "donorrr"))
                {
                    suggestion = "donor";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "campain"))
                {
                    suggestion = "campaign";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "employe"))
                {
                    suggestion = "employee";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "emploee"))
                {
                    suggestion = "employee";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "emplyee"))
                {
                    suggestion = "employee";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "escroww"))
                {
                    suggestion = "escrow";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "beleive"))
                {
                    suggestion = "believe";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "atendee"))
                {
                    suggestion = "attendee";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "commnet"))
                {
                    suggestion = "comment";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "comnent"))
                {
                    suggestion = "comment";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "expence"))
                {
                    suggestion = "expense";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "expenss"))
                {
                    suggestion = "expense";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "emisson"))
                {
                    suggestion = "emission";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "estimte"))
                {
                    suggestion = "estimate";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "formual"))
                {
                    suggestion = "formula";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "facilty"))
                {
                    suggestion = "facility";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "functon"))
                {
                    suggestion = "function";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "firewal"))
                {
                    suggestion = "firewall";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "forcast"))
                {
                    suggestion = "forecast";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fleettt"))
                {
                    suggestion = "fleet";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "intrest"))
                {
                    suggestion = "interest";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "maneger"))
                {
                    suggestion = "manager";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "maturty"))
                {
                    suggestion = "maturity";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "meterng"))
                {
                    suggestion = "metering";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "manifst"))
                {
                    suggestion = "manifest";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "metircs"))
                {
                    suggestion = "metrics";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "scedule"))
                {
                    suggestion = "schedule";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "securty"))
                {
                    suggestion = "security";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "serverr"))
                {
                    suggestion = "server";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sprintt"))
                {
                    suggestion = "sprint";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sponser"))
                {
                    suggestion = "sponsor";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "shedule"))
                {
                    suggestion = "schedule";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "resouce"))
                {
                    suggestion = "resource";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "refundd"))
                {
                    suggestion = "refund";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "backhal"))
                {
                    suggestion = "backhaul";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "brandng"))
                {
                    suggestion = "branding";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "exportt"))
                {
                    suggestion = "export";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "latancy"))
                {
                    suggestion = "latency";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "routerr"))
                {
                    suggestion = "router";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "signall"))
                {
                    suggestion = "signal";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "switchh"))
                {
                    suggestion = "switch";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "shpping"))
                {
                    suggestion = "shipping";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "capcity"))
                {
                    suggestion = "capacity";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "complte"))
                {
                    suggestion = "complete";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "coverge"))
                {
                    suggestion = "coverage";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "contrct"))
                {
                    suggestion = "contract";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "custmer"))
                {
                    suggestion = "customer";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "customr"))
                {
                    suggestion = "customer";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "dedline"))
                {
                    suggestion = "deadline";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "deadlne"))
                {
                    suggestion = "deadline";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "delivry"))
                {
                    suggestion = "delivery";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "defectt"))
                {
                    suggestion = "defect";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "databse"))
                {
                    suggestion = "database";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "documnt"))
                {
                    suggestion = "document";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "downtme"))
                {
                    suggestion = "downtime";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "expcted"))
                {
                    suggestion = "expected";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "insigts"))
                {
                    suggestion = "insights";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "invioce"))
                {
                    suggestion = "invoice";
                    return true;
                }

                if (first == 'j' && EqualAsciiWordIgnoreCase(word, "jounral"))
                {
                    suggestion = "journal";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "meating"))
                {
                    suggestion = "meeting";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "meetign"))
                {
                    suggestion = "meeting";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "meetnig"))
                {
                    suggestion = "meeting";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "thruput"))
                {
                    suggestion = "throughput";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "workbok"))
                {
                    suggestion = "workbook";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "payrole"))
                {
                    suggestion = "payroll";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "pallete"))
                {
                    suggestion = "palette";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "permitt"))
                {
                    suggestion = "permit";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "promotn"))
                {
                    suggestion = "promotion";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "paybles"))
                {
                    suggestion = "payables";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "privicy"))
                {
                    suggestion = "privacy";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "payemnt"))
                {
                    suggestion = "payment";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "paymetn"))
                {
                    suggestion = "payment";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "pipline"))
                {
                    suggestion = "pipeline";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "prospec"))
                {
                    suggestion = "prospect";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "purchse"))
                {
                    suggestion = "purchase";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "patints"))
                {
                    suggestion = "patients";
                    return true;
                }

                if (first == 'q' && EqualAsciiWordIgnoreCase(word, "quanity"))
                {
                    suggestion = "quantity";
                    return true;
                }

                if (first == 'q' && EqualAsciiWordIgnoreCase(word, "quantiy"))
                {
                    suggestion = "quantity";
                    return true;
                }

                if (first == 'q' && EqualAsciiWordIgnoreCase(word, "qaulity"))
                {
                    suggestion = "quality";
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

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "refered"))
                {
                    suggestion = "referred";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "occured"))
                {
                    suggestion = "occurred";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "outtage"))
                {
                    suggestion = "outage";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sroting"))
                {
                    suggestion = "sorting";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "subtoal"))
                {
                    suggestion = "subtotal";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "subtotl"))
                {
                    suggestion = "subtotal";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "shipmnt"))
                {
                    suggestion = "shipment";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "symptms"))
                {
                    suggestion = "symptoms";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "studnts"))
                {
                    suggestion = "students";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "safetey"))
                {
                    suggestion = "safety";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "syllbus"))
                {
                    suggestion = "syllabus";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "suppler"))
                {
                    suggestion = "supplier";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "suplier"))
                {
                    suggestion = "supplier";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "scenrio"))
                {
                    suggestion = "scenario";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "timline"))
                {
                    suggestion = "timeline";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "tickett"))
                {
                    suggestion = "ticket";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "vacaton"))
                {
                    suggestion = "vacation";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "variace"))
                {
                    suggestion = "variance";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "vectorr"))
                {
                    suggestion = "vector";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "rasterr"))
                {
                    suggestion = "raster";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "reserch"))
                {
                    suggestion = "research";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "samplng"))
                {
                    suggestion = "sampling";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "croping"))
                {
                    suggestion = "cropping";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "manuree"))
                {
                    suggestion = "manure";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "ripenes"))
                {
                    suggestion = "ripeness";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "seedlng"))
                {
                    suggestion = "seedling";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sprayng"))
                {
                    suggestion = "spraying";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "vinyard"))
                {
                    suggestion = "vineyard";
                    return true;
                }

                break;
            case 8:
                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "reagentt"))
                {
                    suggestion = "reagent";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "athelete"))
                {
                    suggestion = "athlete";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "alttextt"))
                {
                    suggestion = "alt text";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "emergncy"))
                {
                    suggestion = "emergency";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "equpment"))
                {
                    suggestion = "equipment";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "exercize"))
                {
                    suggestion = "exercise";
                    return true;
                }

                if (first == 'h' && EqualAsciiWordIgnoreCase(word, "hydraton"))
                {
                    suggestion = "hydration";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "licensng"))
                {
                    suggestion = "licensing";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "instaler"))
                {
                    suggestion = "installer";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "langauge"))
                {
                    suggestion = "language";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "manfiest"))
                {
                    suggestion = "manifest";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "overagee"))
                {
                    suggestion = "overage";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "packging"))
                {
                    suggestion = "packaging";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "previeww"))
                {
                    suggestion = "preview";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "resxfile"))
                {
                    suggestion = "resource file";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "prioroty"))
                {
                    suggestion = "priority";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "pracitce"))
                {
                    suggestion = "practice";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "schedual"))
                {
                    suggestion = "schedule";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "ticketng"))
                {
                    suggestion = "ticketing";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "toolbaar"))
                {
                    suggestion = "toolbar";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "warningg"))
                {
                    suggestion = "warning";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "wildifre"))
                {
                    suggestion = "wildfire";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "airfaire"))
                {
                    suggestion = "airfare";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "departue"))
                {
                    suggestion = "departure";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "acheived"))
                {
                    suggestion = "achieved";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "acturial"))
                {
                    suggestion = "actuarial";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "annuitiy"))
                {
                    suggestion = "annuity";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "animaton"))
                {
                    suggestion = "animation";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "buisness"))
                {
                    suggestion = "business";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "begining"))
                {
                    suggestion = "beginning";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "benifits"))
                {
                    suggestion = "benefits";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "backlogg"))
                {
                    suggestion = "backlog";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "backordr"))
                {
                    suggestion = "backorder";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "bandwith"))
                {
                    suggestion = "bandwidth";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "broadban"))
                {
                    suggestion = "broadband";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "ballance"))
                {
                    suggestion = "balance";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "beleived"))
                {
                    suggestion = "believed";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "atendees"))
                {
                    suggestion = "attendees";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "attendes"))
                {
                    suggestion = "attendees";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "agreemnt"))
                {
                    suggestion = "agreement";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "analytcs"))
                {
                    suggestion = "analytics";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "atribute"))
                {
                    suggestion = "attribute";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "asserton"))
                {
                    suggestion = "assertion";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "apporver"))
                {
                    suggestion = "approver";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "argments"))
                {
                    suggestion = "arguments";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "alergens"))
                {
                    suggestion = "allergens";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "campains"))
                {
                    suggestion = "campaigns";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "clinicly"))
                {
                    suggestion = "clinically";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "clasroom"))
                {
                    suggestion = "classroom";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "catlogue"))
                {
                    suggestion = "catalog";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "checkuot"))
                {
                    suggestion = "checkout";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "excelent"))
                {
                    suggestion = "excellent";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "expences"))
                {
                    suggestion = "expenses";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "estimete"))
                {
                    suggestion = "estimate";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "formular"))
                {
                    suggestion = "formula";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "functons"))
                {
                    suggestion = "functions";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "featuers"))
                {
                    suggestion = "features";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "flitered"))
                {
                    suggestion = "filtered";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fitlered"))
                {
                    suggestion = "filtered";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "formatng"))
                {
                    suggestion = "formatting";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "followup"))
                {
                    suggestion = "follow-up";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "guidline"))
                {
                    suggestion = "guideline";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "inventry"))
                {
                    suggestion = "inventory";
                    return true;
                }

                if (first == 'k' && EqualAsciiWordIgnoreCase(word, "knowlege"))
                {
                    suggestion = "knowledge";
                    return true;
                }

                if (first == 'k' && EqualAsciiWordIgnoreCase(word, "kerningg"))
                {
                    suggestion = "kerning";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "workshet"))
                {
                    suggestion = "worksheet";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "worsheet"))
                {
                    suggestion = "worksheet";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "warehous"))
                {
                    suggestion = "warehouse";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "voltagee"))
                {
                    suggestion = "voltage";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "milstone"))
                {
                    suggestion = "milestone";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "mitgiate"))
                {
                    suggestion = "mitigate";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "messsage"))
                {
                    suggestion = "message";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "migraton"))
                {
                    suggestion = "migration";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "municipl"))
                {
                    suggestion = "municipal";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "mileagee"))
                {
                    suggestion = "mileage";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "resouces"))
                {
                    suggestion = "resources";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "reagentt"))
                {
                    suggestion = "reagent";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "liabilty"))
                {
                    suggestion = "liability";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "liablity"))
                {
                    suggestion = "liability";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "liqudity"))
                {
                    suggestion = "liquidity";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "logstics"))
                {
                    suggestion = "logistics";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "princpal"))
                {
                    suggestion = "principal";
                    return true;
                }

                if (first == 'q' && EqualAsciiWordIgnoreCase(word, "quaterly"))
                {
                    suggestion = "quarterly";
                    return true;
                }

                if (first == 'q' && EqualAsciiWordIgnoreCase(word, "quotaton"))
                {
                    suggestion = "quotation";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "commited"))
                {
                    suggestion = "committed";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "calandar"))
                {
                    suggestion = "calendar";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "compelte"))
                {
                    suggestion = "complete";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "dashbord"))
                {
                    suggestion = "dashboard";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "databsae"))
                {
                    suggestion = "database";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "deducton"))
                {
                    suggestion = "deduction";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "dimenson"))
                {
                    suggestion = "dimension";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "recieved"))
                {
                    suggestion = "received";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "reorderd"))
                {
                    suggestion = "reordered";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "refrence"))
                {
                    suggestion = "reference";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "releasee"))
                {
                    suggestion = "release";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "relevent"))
                {
                    suggestion = "relevant";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "retenton"))
                {
                    suggestion = "retention";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "renovatn"))
                {
                    suggestion = "renovation";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "prototyp"))
                {
                    suggestion = "prototype";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "renderng"))
                {
                    suggestion = "rendering";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "registrr"))
                {
                    suggestion = "registrar";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "restoree"))
                {
                    suggestion = "restore";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "seperate"))
                {
                    suggestion = "separate";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sparklne"))
                {
                    suggestion = "sparkline";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "statment"))
                {
                    suggestion = "statement";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "statemnt"))
                {
                    suggestion = "statement";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "summarry"))
                {
                    suggestion = "summary";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "subttoal"))
                {
                    suggestion = "subtotal";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "supportt"))
                {
                    suggestion = "support";
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

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "varience"))
                {
                    suggestion = "variance";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "volunter"))
                {
                    suggestion = "volunteer";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "vloookup"))
                {
                    suggestion = "vlookup";
                    return true;
                }

                if (first == 'x' && EqualAsciiWordIgnoreCase(word, "xloookup"))
                {
                    suggestion = "xlookup";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "tommorow"))
                {
                    suggestion = "tomorrow";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "tiemline"))
                {
                    suggestion = "timeline";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "tranpose"))
                {
                    suggestion = "transpose";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "treatmnt"))
                {
                    suggestion = "treatment";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "transpos"))
                {
                    suggestion = "transpose";
                    return true;
                }

                if (first == 'n' && EqualAsciiWordIgnoreCase(word, "nutriton"))
                {
                    suggestion = "nutrition";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "occuring"))
                {
                    suggestion = "occurring";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "purcahse"))
                {
                    suggestion = "purchase";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "passwrod"))
                {
                    suggestion = "password";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "privlege"))
                {
                    suggestion = "privilege";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "occupncy"))
                {
                    suggestion = "occupancy";
                    return true;
                }

                if (first == 'u' && EqualAsciiWordIgnoreCase(word, "utilties"))
                {
                    suggestion = "utilities";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "emisions"))
                {
                    suggestion = "emissions";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "renewble"))
                {
                    suggestion = "renewable";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "recyling"))
                {
                    suggestion = "recycling";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "gatewayy"))
                {
                    suggestion = "gateway";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "roamingg"))
                {
                    suggestion = "roaming";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "walkthru"))
                {
                    suggestion = "walkthrough";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "workordr"))
                {
                    suggestion = "workorder";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "wirefram"))
                {
                    suggestion = "wireframe";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "livestok"))
                {
                    suggestion = "livestock";
                    return true;
                }

                if (first == 'n' && EqualAsciiWordIgnoreCase(word, "nurseryy"))
                {
                    suggestion = "nursery";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "pasturee"))
                {
                    suggestion = "pasture";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "pesticde"))
                {
                    suggestion = "pesticide";
                    return true;
                }

                break;
            case 9:
                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "analaysis"))
                {
                    suggestion = "analysis";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "artifactt"))
                {
                    suggestion = "artifact";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "callbackk"))
                {
                    suggestion = "callback";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "chatbottt"))
                {
                    suggestion = "chatbot";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "comboboxx"))
                {
                    suggestion = "combo box";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "competion"))
                {
                    suggestion = "competition";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "dispatchr"))
                {
                    suggestion = "dispatcher";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "escalaton"))
                {
                    suggestion = "escalation";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "expiraton"))
                {
                    suggestion = "expiration";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fallbackk"))
                {
                    suggestion = "fallback";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "evacuaton"))
                {
                    suggestion = "evacuation";
                    return true;
                }

                if (first == 'h' && EqualAsciiWordIgnoreCase(word, "helpdeskk"))
                {
                    suggestion = "helpdesk";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "confernce"))
                {
                    suggestion = "conference";
                    return true;
                }

                if (first == 'q' && EqualAsciiWordIgnoreCase(word, "quarantne"))
                {
                    suggestion = "quarantine";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sanitaton"))
                {
                    suggestion = "sanitation";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "membrship"))
                {
                    suggestion = "membership";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "manifestt"))
                {
                    suggestion = "manifest";
                    return true;
                }

                if (first == 'n' && EqualAsciiWordIgnoreCase(word, "navigaton"))
                {
                    suggestion = "navigation";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "prorateed"))
                {
                    suggestion = "prorated";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "publishng"))
                {
                    suggestion = "publishing";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "scorebord"))
                {
                    suggestion = "scoreboard";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "shortcutt"))
                {
                    suggestion = "shortcut";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "tournment"))
                {
                    suggestion = "tournament";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "timezonee"))
                {
                    suggestion = "time zone";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "workarond"))
                {
                    suggestion = "workaround";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "assignmnt"))
                {
                    suggestion = "assignment";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "assembely"))
                {
                    suggestion = "assembly";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "billablee"))
                {
                    suggestion = "billable";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "beveragee"))
                {
                    suggestion = "beverage";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "banquettt"))
                {
                    suggestion = "banquet";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "bluepritn"))
                {
                    suggestion = "blueprint";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "availible"))
                {
                    suggestion = "available";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "arguement"))
                {
                    suggestion = "argument";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "ammortize"))
                {
                    suggestion = "amortize";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "alocation"))
                {
                    suggestion = "allocation";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "allocaton"))
                {
                    suggestion = "allocation";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "automtion"))
                {
                    suggestion = "automation";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "activaton"))
                {
                    suggestion = "activation";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "credental"))
                {
                    suggestion = "credential";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "connecton"))
                {
                    suggestion = "connection";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "contracor"))
                {
                    suggestion = "contractor";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "cellularr"))
                {
                    suggestion = "cellular";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "curriculm"))
                {
                    suggestion = "curriculum";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "changereq"))
                {
                    suggestion = "change request";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "collaterl"))
                {
                    suggestion = "collateral";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "compliace"))
                {
                    suggestion = "compliance";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "deductble"))
                {
                    suggestion = "deductible";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "diagnosys"))
                {
                    suggestion = "diagnosis";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "existance"))
                {
                    suggestion = "existence";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "encrypton"))
                {
                    suggestion = "encryption";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "enrollmnt"))
                {
                    suggestion = "enrollment";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "expensses"))
                {
                    suggestion = "expenses";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "escallate"))
                {
                    suggestion = "escalate";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "esclation"))
                {
                    suggestion = "escalation";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "departmnt"))
                {
                    suggestion = "department";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "dashbaord"))
                {
                    suggestion = "dashboard";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "downtimee"))
                {
                    suggestion = "downtime";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "dispatchh"))
                {
                    suggestion = "dispatch";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "curbsidee"))
                {
                    suggestion = "curbside";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "deploymnt"))
                {
                    suggestion = "deployment";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fulfilmnt"))
                {
                    suggestion = "fulfillment";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "forcasted"))
                {
                    suggestion = "forecasted";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "formating"))
                {
                    suggestion = "formatting";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "flitering"))
                {
                    suggestion = "filtering";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "failoverr"))
                {
                    suggestion = "failover";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "facilties"))
                {
                    suggestion = "facilities";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "goverment"))
                {
                    suggestion = "government";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "generaton"))
                {
                    suggestion = "generation";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "greenhose"))
                {
                    suggestion = "greenhouse";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "gradution"))
                {
                    suggestion = "graduation";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "compostng"))
                {
                    suggestion = "composting";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "efficency"))
                {
                    suggestion = "efficiency";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "incidentt"))
                {
                    suggestion = "incident";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "inventroy"))
                {
                    suggestion = "inventory";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "ingredent"))
                {
                    suggestion = "ingredient";
                    return true;
                }

                if (first == 'j' && EqualAsciiWordIgnoreCase(word, "janitoral"))
                {
                    suggestion = "janitorial";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "mispelled"))
                {
                    suggestion = "misspelled";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "mitgation"))
                {
                    suggestion = "mitigation";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "medicaton"))
                {
                    suggestion = "medication";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "machinary"))
                {
                    suggestion = "machinery";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "occurence"))
                {
                    suggestion = "occurrence";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "onbording"))
                {
                    suggestion = "onboarding";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "permisson"))
                {
                    suggestion = "permission";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "prodction"))
                {
                    suggestion = "production";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "pipelinee"))
                {
                    suggestion = "pipeline";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "privleges"))
                {
                    suggestion = "privileges";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "referance"))
                {
                    suggestion = "reference";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "recoverey"))
                {
                    suggestion = "recovery";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "recieving"))
                {
                    suggestion = "receiving";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "remitance"))
                {
                    suggestion = "remittance";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "recruting"))
                {
                    suggestion = "recruiting";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "refernece"))
                {
                    suggestion = "reference";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "regulaton"))
                {
                    suggestion = "regulation";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "regresion"))
                {
                    suggestion = "regression";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "resoluton"))
                {
                    suggestion = "resolution";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "redundncy"))
                {
                    suggestion = "redundancy";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "resilienc"))
                {
                    suggestion = "resilience";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "resturant"))
                {
                    suggestion = "restaurant";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sucessful"))
                {
                    suggestion = "successful";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "subscrber"))
                {
                    suggestion = "subscriber";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "storybord"))
                {
                    suggestion = "storyboard";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "signiture"))
                {
                    suggestion = "signature";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "settlemnt"))
                {
                    suggestion = "settlement";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "substaton"))
                {
                    suggestion = "substation";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "schedulng"))
                {
                    suggestion = "scheduling";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "succesful"))
                {
                    suggestion = "successful";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sparklins"))
                {
                    suggestion = "sparklines";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "shippment"))
                {
                    suggestion = "shipment";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "transopse"))
                {
                    suggestion = "transpose";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "trainging"))
                {
                    suggestion = "training";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "typograpy"))
                {
                    suggestion = "typography";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "workboook"))
                {
                    suggestion = "workbook";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "wishlistt"))
                {
                    suggestion = "wishlist";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "tommorrow"))
                {
                    suggestion = "tomorrow";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "validaton"))
                {
                    suggestion = "validation";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "experment"))
                {
                    suggestion = "experiment";
                    return true;
                }

                if (first == 'h' && EqualAsciiWordIgnoreCase(word, "hypothsis"))
                {
                    suggestion = "hypothesis";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "incubaton"))
                {
                    suggestion = "incubation";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "labratory"))
                {
                    suggestion = "laboratory";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "microscop"))
                {
                    suggestion = "microscope";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sequencng"))
                {
                    suggestion = "sequencing";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fertilzer"))
                {
                    suggestion = "fertilizer";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "greenhous"))
                {
                    suggestion = "greenhouse";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "irrigaton"))
                {
                    suggestion = "irrigation";
                    return true;
                }

                break;
            case 10:
                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "definately"))
                {
                    suggestion = "definitely";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "officiatng"))
                {
                    suggestion = "officiating";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "deductable"))
                {
                    suggestion = "deductible";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "deliverble"))
                {
                    suggestion = "deliverable";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "dependancy"))
                {
                    suggestion = "dependency";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "departmant"))
                {
                    suggestion = "department";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "deployemnt"))
                {
                    suggestion = "deployment";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "discountng"))
                {
                    suggestion = "discounting";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "disclousre"))
                {
                    suggestion = "disclosure";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "excavatoin"))
                {
                    suggestion = "excavation";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "accomodate"))
                {
                    suggestion = "accommodate";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "acommodate"))
                {
                    suggestion = "accommodate";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "attendence"))
                {
                    suggestion = "attendance";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "caluclated"))
                {
                    suggestion = "calculated";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "calcuation"))
                {
                    suggestion = "calculation";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "convertion"))
                {
                    suggestion = "conversion";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "conversoin"))
                {
                    suggestion = "conversion";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "credentail"))
                {
                    suggestion = "credential";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "calculaton"))
                {
                    suggestion = "calculation";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "corelation"))
                {
                    suggestion = "correlation";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "complaince"))
                {
                    suggestion = "compliance";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "compositon"))
                {
                    suggestion = "composition";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "copywritng"))
                {
                    suggestion = "copywriting";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "conciergee"))
                {
                    suggestion = "concierge";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "aggregaton"))
                {
                    suggestion = "aggregation";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "enviroment"))
                {
                    suggestion = "environment";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "electricty"))
                {
                    suggestion = "electricity";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "certificat"))
                {
                    suggestion = "certificate";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "entitlment"))
                {
                    suggestion = "entitlement";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "encrpytion"))
                {
                    suggestion = "encryption";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "endorsemnt"))
                {
                    suggestion = "endorsement";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "forcasting"))
                {
                    suggestion = "forecasting";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "forecastng"))
                {
                    suggestion = "forecasting";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fundraisin"))
                {
                    suggestion = "fundraising";
                    return true;
                }

                if (first == 'f' && EqualAsciiWordIgnoreCase(word, "fulfillmnt"))
                {
                    suggestion = "fulfillment";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "witholding"))
                {
                    suggestion = "withholding";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "integraton"))
                {
                    suggestion = "integration";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "instrction"))
                {
                    suggestion = "instruction";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "inspeciton"))
                {
                    suggestion = "inspection";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "insulatoin"))
                {
                    suggestion = "insulation";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "iconogrphy"))
                {
                    suggestion = "iconography";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "ingredents"))
                {
                    suggestion = "ingredients";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "marketting"))
                {
                    suggestion = "marketing";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "monitering"))
                {
                    suggestion = "monitoring";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "maintnance"))
                {
                    suggestion = "maintenance";
                    return true;
                }

                if (first == 'n' && EqualAsciiWordIgnoreCase(word, "neccessary"))
                {
                    suggestion = "necessary";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "opportunty"))
                {
                    suggestion = "opportunity";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "occurrance"))
                {
                    suggestion = "occurrence";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "prospectve"))
                {
                    suggestion = "prospective";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "publically"))
                {
                    suggestion = "publicly";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "permisison"))
                {
                    suggestion = "permission";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "percenatge"))
                {
                    suggestion = "percentage";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "pivottabel"))
                {
                    suggestion = "pivot table";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "perfomance"))
                {
                    suggestion = "performance";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "procuremnt"))
                {
                    suggestion = "procurement";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "prescriptn"))
                {
                    suggestion = "prescription";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "performnce"))
                {
                    suggestion = "performance";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "photogrphy"))
                {
                    suggestion = "photography";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "punchlistt"))
                {
                    suggestion = "punchlist";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "recievable"))
                {
                    suggestion = "receivable";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "remitances"))
                {
                    suggestion = "remittances";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "requirment"))
                {
                    suggestion = "requirement";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "relability"))
                {
                    suggestion = "reliability";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "reliablity"))
                {
                    suggestion = "reliability";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "restaraunt"))
                {
                    suggestion = "restaurant";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "reservaton"))
                {
                    suggestion = "reservation";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "roomservce"))
                {
                    suggestion = "room service";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "liabilties"))
                {
                    suggestion = "liabilities";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "laborotory"))
                {
                    suggestion = "laboratory";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "linebalnce"))
                {
                    suggestion = "line balance";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "scafolding"))
                {
                    suggestion = "scaffolding";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "seperately"))
                {
                    suggestion = "separately";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "stakehlder"))
                {
                    suggestion = "stakeholder";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "stakeholer"))
                {
                    suggestion = "stakeholder";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "syncronize"))
                {
                    suggestion = "synchronize";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "transfered"))
                {
                    suggestion = "transferred";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "transacton"))
                {
                    suggestion = "transaction";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "translaton"))
                {
                    suggestion = "translation";
                    return true;
                }

                if (first == 'u' && EqualAsciiWordIgnoreCase(word, "utilzation"))
                {
                    suggestion = "utilization";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "vaccinaton"))
                {
                    suggestion = "vaccination";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "analysisis"))
                {
                    suggestion = "analysis";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "calibraton"))
                {
                    suggestion = "calibration";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "protocolll"))
                {
                    suggestion = "protocol";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "replicatee"))
                {
                    suggestion = "replicate";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "specimennt"))
                {
                    suggestion = "specimen";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "pollinaton"))
                {
                    suggestion = "pollination";
                    return true;
                }

                break;
            case 11:
                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "availablity"))
                {
                    suggestion = "availability";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "billngcycle"))
                {
                    suggestion = "billing cycle";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "cancelation"))
                {
                    suggestion = "cancellation";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "cancellaton"))
                {
                    suggestion = "cancellation";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "conditoning"))
                {
                    suggestion = "conditioning";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "servcelevel"))
                {
                    suggestion = "service level";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "screenreder"))
                {
                    suggestion = "screen reader";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "supportdesk"))
                {
                    suggestion = "support desk";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "subscrption"))
                {
                    suggestion = "subscription";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "registraton"))
                {
                    suggestion = "registration";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "delivarable"))
                {
                    suggestion = "deliverable";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "distributon"))
                {
                    suggestion = "distribution";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "instalation"))
                {
                    suggestion = "installation";
                    return true;
                }

                if (first == 'l' && EqualAsciiWordIgnoreCase(word, "localizaton"))
                {
                    suggestion = "localization";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "righttoleft"))
                {
                    suggestion = "right to left";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "centrifugee"))
                {
                    suggestion = "centrifuge";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "genotypingg"))
                {
                    suggestion = "genotyping";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "spectromtry"))
                {
                    suggestion = "spectrometry";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "caluclation"))
                {
                    suggestion = "calculation";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "comparision"))
                {
                    suggestion = "comparison";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "confidental"))
                {
                    suggestion = "confidential";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "confidntial"))
                {
                    suggestion = "confidential";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "credentails"))
                {
                    suggestion = "credentials";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "constituant"))
                {
                    suggestion = "constituent";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "depreciaton"))
                {
                    suggestion = "depreciation";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "disbursemnt"))
                {
                    suggestion = "disbursement";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "amortizaton"))
                {
                    suggestion = "amortization";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "benificiary"))
                {
                    suggestion = "beneficiary";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "conservaton"))
                {
                    suggestion = "conservation";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "constrction"))
                {
                    suggestion = "construction";
                    return true;
                }

                if (first == 'h' && EqualAsciiWordIgnoreCase(word, "housekeepng"))
                {
                    suggestion = "housekeeping";
                    return true;
                }

                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "illustation"))
                {
                    suggestion = "illustration";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "maintenence"))
                {
                    suggestion = "maintenance";
                    return true;
                }

                if (first == 'n' && EqualAsciiWordIgnoreCase(word, "notfication"))
                {
                    suggestion = "notification";
                    return true;
                }

                if (first == 'n' && EqualAsciiWordIgnoreCase(word, "notificaton"))
                {
                    suggestion = "notification";
                    return true;
                }

                if (first == 'n' && EqualAsciiWordIgnoreCase(word, "notifcation"))
                {
                    suggestion = "notification";
                    return true;
                }

                if (first == 'o' && EqualAsciiWordIgnoreCase(word, "oppertunity"))
                {
                    suggestion = "opportunity";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "percentatge"))
                {
                    suggestion = "percentage";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "provisoning"))
                {
                    suggestion = "provisioning";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "requirments"))
                {
                    suggestion = "requirements";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "reinsurence"))
                {
                    suggestion = "reinsurance";
                    return true;
                }

                if (first == 'u' && EqualAsciiWordIgnoreCase(word, "underwritng"))
                {
                    suggestion = "underwriting";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "witholdings"))
                {
                    suggestion = "withholdings";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "troubleshot"))
                {
                    suggestion = "troubleshoot";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "throughputt"))
                {
                    suggestion = "throughput";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "stewardhsip"))
                {
                    suggestion = "stewardship";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "transmision"))
                {
                    suggestion = "transmission";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "verfication"))
                {
                    suggestion = "verification";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "centrifugee"))
                {
                    suggestion = "centrifuge";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "genotypingg"))
                {
                    suggestion = "genotyping";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "spectromtry"))
                {
                    suggestion = "spectrometry";
                    return true;
                }

                break;
            case 12:
                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "advertisment"))
                {
                    suggestion = "advertisement";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "accomodation"))
                {
                    suggestion = "accommodation";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "accesibility"))
                {
                    suggestion = "accessibility";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "acessibility"))
                {
                    suggestion = "accessibility";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "authorizaton"))
                {
                    suggestion = "authorization";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "autorization"))
                {
                    suggestion = "authorization";
                    return true;
                }

                if (first == 'v' && EqualAsciiWordIgnoreCase(word, "visualizaton"))
                {
                    suggestion = "visualization";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "certificaton"))
                {
                    suggestion = "certification";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "subscribtion"))
                {
                    suggestion = "subscription";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "configration"))
                {
                    suggestion = "configuration";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "configuraton"))
                {
                    suggestion = "configuration";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "consolodated"))
                {
                    suggestion = "consolidated";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "communcation"))
                {
                    suggestion = "communication";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "comunication"))
                {
                    suggestion = "communication";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "communicaton"))
                {
                    suggestion = "communication";
                    return true;
                }

                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "enviromental"))
                {
                    suggestion = "environmental";
                    return true;
                }

                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "globalizaton"))
                {
                    suggestion = "globalization";
                    return true;
                }

                if (first == 'b' && EqualAsciiWordIgnoreCase(word, "biodiveristy"))
                {
                    suggestion = "biodiversity";
                    return true;
                }

                if (first == 'h' && EqualAsciiWordIgnoreCase(word, "hospitallity"))
                {
                    suggestion = "hospitality";
                    return true;
                }

                if (first == 'n' && EqualAsciiWordIgnoreCase(word, "notifciation"))
                {
                    suggestion = "notification";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "reimbursment"))
                {
                    suggestion = "reimbursement";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "pluralizaton"))
                {
                    suggestion = "pluralization";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "dependancies"))
                {
                    suggestion = "dependencies";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "documentaton"))
                {
                    suggestion = "documentation";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "distrubution"))
                {
                    suggestion = "distribution";
                    return true;
                }

                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "depriciation"))
                {
                    suggestion = "depreciation";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "maintainance"))
                {
                    suggestion = "maintenance";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "merchandisng"))
                {
                    suggestion = "merchandising";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sponsorshipp"))
                {
                    suggestion = "sponsorship";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "subcontracor"))
                {
                    suggestion = "subcontractor";
                    return true;
                }

                break;
            case 13:
                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "advertisments"))
                {
                    suggestion = "advertisements";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "rehabilitaton"))
                {
                    suggestion = "rehabilitation";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "ammortization"))
                {
                    suggestion = "amortization";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "autentication"))
                {
                    suggestion = "authentication";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "authentcation"))
                {
                    suggestion = "authentication";
                    return true;
                }

                if (first == 'a' && EqualAsciiWordIgnoreCase(word, "appropreation"))
                {
                    suggestion = "appropriation";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "reinbursement"))
                {
                    suggestion = "reimbursement";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "capitalizaton"))
                {
                    suggestion = "capitalization";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "chromatograpy"))
                {
                    suggestion = "chromatography";
                    return true;
                }

                if (first == 'c' && EqualAsciiWordIgnoreCase(word, "communciation"))
                {
                    suggestion = "communication";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "manufactruing"))
                {
                    suggestion = "manufacturing";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "reconcilation"))
                {
                    suggestion = "reconciliation";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sustainablity"))
                {
                    suggestion = "sustainability";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "transporation"))
                {
                    suggestion = "transportation";
                    return true;
                }

                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "prioritzation"))
                {
                    suggestion = "prioritization";
                    return true;
                }

                break;
            case 14:
                if (first == 'd' && EqualAsciiWordIgnoreCase(word, "decarbonizaton"))
                {
                    suggestion = "decarbonization";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "recomendations"))
                {
                    suggestion = "recommendations";
                    return true;
                }

                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "regionalseting"))
                {
                    suggestion = "regional setting";
                    return true;
                }

                break;
            case 15:
                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "reconcilliation"))
                {
                    suggestion = "reconciliation";
                    return true;
                }

                break;
            case 16:
                if (first == 'r' && EqualAsciiWordIgnoreCase(word, "releasecandidate"))
                {
                    suggestion = "release candidate";
                    return true;
                }

                break;
            case 17:
                if (first == 'p' && EqualAsciiWordIgnoreCase(word, "pseudolocalizaton"))
                {
                    suggestion = "pseudolocalization";
                    return true;
                }

                break;
            case 19:
                if (first == 'i' && EqualAsciiWordIgnoreCase(word, "internatonalization"))
                {
                    suggestion = "internationalization";
                    return true;
                }

                break;
        }

        suggestion = string.Empty;
        return false;
    }

    private static bool TryGetCommonProofingCorrection(ReadOnlySpan<char> word, out string suggestion)
    {
        var first = word.Length > 0 ? ToAsciiLowerInvariant(word[0]) : '\0';
        switch (word.Length)
        {
            case 4:
                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "wrod"))
                {
                    suggestion = "word";
                    return true;
                }

                break;
            case 5:
                if (first == 'e' && EqualAsciiWordIgnoreCase(word, "erors"))
                {
                    suggestion = "errors";
                    return true;
                }

                if (first == 't' && EqualAsciiWordIgnoreCase(word, "typoo"))
                {
                    suggestion = "typo";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "wrods"))
                {
                    suggestion = "words";
                    return true;
                }

                break;
            case 6:
                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "mistke"))
                {
                    suggestion = "mistake";
                    return true;
                }

                break;
            case 7:
                if (first == 'g' && EqualAsciiWordIgnoreCase(word, "grammer"))
                {
                    suggestion = "grammar";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "mispell"))
                {
                    suggestion = "misspell";
                    return true;
                }

                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "mistkae"))
                {
                    suggestion = "mistake";
                    return true;
                }

                if (first == 's' && EqualAsciiWordIgnoreCase(word, "speling"))
                {
                    suggestion = "spelling";
                    return true;
                }

                break;
            case 8:
                if (first == 's' && EqualAsciiWordIgnoreCase(word, "sentance"))
                {
                    suggestion = "sentence";
                    return true;
                }

                if (first == 'w' && EqualAsciiWordIgnoreCase(word, "writting"))
                {
                    suggestion = "writing";
                    return true;
                }

                break;
            case 10:
                if (first == 'm' && EqualAsciiWordIgnoreCase(word, "mispelling"))
                {
                    suggestion = "misspelling";
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
          | (?<![\p{L}\p{N}_])""(?:[A-Z]:[\\/]|\\\\|~?[\\/])[^""<>\r\n]+""
          | (?<![\p{L}\p{N}_])'(?:[A-Z]:[\\/]|\\\\|~?[\\/])[^'<>\r\n]+'
          | (?<![\p{L}\p{N}_])\[(?:[A-Z]:[\\/]|\\\\|~?[\\/])[^\]\r\n<>]+\]
          | (?<![\p{L}\p{N}_])(?:[A-Z]:[\\/]|\\\\)[^\s<>""']+
          | (?<![\p{L}\p{N}_])(?:~|/)[\w.-]+(?:/[\w .-]+)+
          | (?<![\p{L}\p{N}_])[\w.-]+\.(?:xlsx?|xlsm|csv|tsv|txt|pdf|docx?|pptx?|zip|json|xml|html?|png|jpe?g|gif|svg)\b
        )")]
    private static partial Regex IgnoredAddressSpanRegex();
}
