using System.Text.RegularExpressions;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed record SpellingIssue(
    CellAddress Address,
    string Word,
    string Suggestion,
    string CellText);

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
    private static readonly Dictionary<string, string> KnownCorrections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["teh"] = "the",
        ["adn"] = "and",
        ["recieve"] = "receive",
        ["seperate"] = "separate",
        ["occured"] = "occurred",
        ["definately"] = "definitely",
        ["adress"] = "address",
        ["untill"] = "until",
        ["acommodate"] = "accommodate",
        ["calender"] = "calendar",
        ["goverment"] = "government",
        ["publically"] = "publicly",
        ["recomend"] = "recommend",
        ["recomendations"] = "recommendations",
        ["sucess"] = "success",
        ["tommorow"] = "tomorrow",
        ["wierd"] = "weird",
    };

    public static IReadOnlyList<SpellingIssue> FindIssues(Workbook workbook, SheetId? sheetId = null)
    {
        var result = new List<SpellingIssue>();

        foreach (var sheet in EnumerateTargetSheets(workbook, sheetId))
        {
            var sheetIssues = new List<SpellingIssue>();
            foreach (var (address, cell) in sheet.EnumerateCells())
            {
                if (cell.HasFormula || cell.Value is not TextValue textValue)
                    continue;

                sheetIssues.AddRange(FindIssuesInCell(address, textValue.Value));
            }

            result.AddRange(sheetIssues
                .OrderBy(issue => issue.Address.Row)
                .ThenBy(issue => issue.Address.Col));
        }

        return result;
    }

    /// <summary>
    /// Returns every known-correction issue found in one literal text cell.
    /// Formula cells are intentionally handled by callers and are not edited as text.
    /// </summary>
    public static IReadOnlyList<SpellingIssue> FindIssuesInCell(CellAddress address, string text)
    {
        var issues = new List<(int Index, SpellingIssue Issue)>();
        var ignoredSpans = FindIgnoredSpans(text);
        foreach (Match match in WordRegex().Matches(text))
        {
            if (OverlapsIgnoredSpan(match.Index, match.Length, ignoredSpans))
                continue;

            if (KnownCorrections.TryGetValue(match.Value, out var suggestion))
                issues.Add((match.Index, new SpellingIssue(address, match.Value, MatchCapitalization(match.Value, suggestion), text)));
        }

        foreach (Match match in RepeatedWordRegex().Matches(text))
        {
            if (OverlapsIgnoredSpan(match.Index, match.Length, ignoredSpans))
                continue;

            var repeatedWord = match.Groups["word"].Value;
            if (KnownCorrections.ContainsKey(repeatedWord))
                continue;

            issues.Add((match.Index, new SpellingIssue(address, match.Value, repeatedWord, text)));
        }

        return issues
            .OrderBy(issue => issue.Index)
            .Select(issue => issue.Issue)
            .ToList();
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
        var correctedReplacement = MatchCapitalization(issue.Word, replacement);
        return Regex.Replace(
            issue.CellText,
            $@"\b{Regex.Escape(issue.Word)}\b",
            correctedReplacement,
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));
    }

    private static string ApplyKnownCorrections(string text, out int replacementCount)
    {
        var count = 0;
        var ignoredSpans = FindIgnoredSpans(text);
        var corrected = WordRegex().Replace(
            text,
            match =>
            {
                if (OverlapsIgnoredSpan(match.Index, match.Length, ignoredSpans))
                    return match.Value;

                if (!KnownCorrections.TryGetValue(match.Value, out var suggestion))
                    return match.Value;

                count++;
                return MatchCapitalization(match.Value, suggestion);
            },
            -1,
            0);
        replacementCount = count;
        return corrected;
    }

    private static IReadOnlyList<Range> FindIgnoredSpans(string text) =>
        IgnoredAddressSpanRegex()
            .Matches(text)
            .Select(match => new Range(match.Index, match.Index + match.Length))
            .ToList();

    private static bool OverlapsIgnoredSpan(int index, int length, IReadOnlyList<Range> ignoredSpans)
    {
        var end = index + length;
        return ignoredSpans.Any(span => index < span.End.Value && end > span.Start.Value);
    }

    private static IEnumerable<Sheet> EnumerateTargetSheets(Workbook workbook, SheetId? sheetId)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (!sheetId.HasValue || sheet.Id == sheetId.Value)
                yield return sheet;
        }
    }

    private static string MatchCapitalization(string original, string replacement)
    {
        if (original.Length == 0 || replacement.Length == 0)
            return replacement;

        if (original.Any(char.IsLetter) && original.Where(char.IsLetter).All(char.IsUpper))
            return replacement.ToUpperInvariant();

        if (char.IsUpper(original[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];

        return replacement;
    }

    [GeneratedRegex(@"\b[\p{L}']+\b")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\b(?<word>[\p{L}']{2,})\b[\s\u00A0]+\k<word>\b", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatedWordRegex();

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
