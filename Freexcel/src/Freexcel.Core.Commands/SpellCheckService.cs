using System.Text.RegularExpressions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed record SpellingIssue(
    CellAddress Address,
    string Word,
    string Suggestion,
    string CellText);

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
    };

    public static IReadOnlyList<SpellingIssue> FindIssues(Workbook workbook, SheetId? sheetId = null)
    {
        var result = new List<SpellingIssue>();

        foreach (var sheet in workbook.Sheets)
        {
            if (sheetId.HasValue && sheet.Id != sheetId.Value)
                continue;

            var sheetIssues = new List<SpellingIssue>();
            foreach (var (address, cell) in sheet.EnumerateCells())
            {
                if (cell.Value is not TextValue textValue)
                    continue;

                foreach (Match match in WordRegex().Matches(textValue.Value))
                {
                    if (KnownCorrections.TryGetValue(match.Value, out var suggestion))
                    {
                        sheetIssues.Add(new SpellingIssue(address, match.Value, suggestion, textValue.Value));
                        break;
                    }
                }
            }

            sheetIssues.Sort((a, b) =>
            {
                var rowCmp = a.Address.Row.CompareTo(b.Address.Row);
                return rowCmp != 0 ? rowCmp : a.Address.Col.CompareTo(b.Address.Col);
            });
            result.AddRange(sheetIssues);
        }

        return result;
    }

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

    private static string MatchCapitalization(string original, string replacement)
    {
        if (original.Length == 0 || replacement.Length == 0)
            return replacement;

        if (char.IsUpper(original[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];

        return replacement;
    }

    [GeneratedRegex(@"\b[\p{L}']+\b")]
    private static partial Regex WordRegex();
}
