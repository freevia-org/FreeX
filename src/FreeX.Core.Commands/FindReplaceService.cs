using System.Globalization;
using System.Text.RegularExpressions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Identifies which cell-owned text surface matched a search.</summary>
public enum FindResultTarget
{
    Cell,
    Note,
    ThreadedComment,
    ThreadedCommentReply
}

/// <summary>Represents a cell-owned text surface that matched a search.</summary>
public sealed record FindResult(
    CellAddress Address,
    string MatchedText,
    FindResultTarget Target = FindResultTarget.Cell,
    int? ReplyIndex = null);

public enum FindWithin
{
    Workbook,
    Sheet
}

public enum FindSearchOrder
{
    ByRows,
    ByColumns
}

public enum FindLookIn
{
    Formulas,
    Values,
    Notes,
    Comments
}

public sealed record FindOptions(
    FindWithin Within = FindWithin.Workbook,
    SheetId? CurrentSheetId = null,
    FindSearchOrder SearchOrder = FindSearchOrder.ByRows,
    FindLookIn LookIn = FindLookIn.Values,
    StyleDiff? RequiredFormat = null,
    // Excel: when more than one cell is selected before Find & Replace is opened, Replace All
    // (and Find All) is automatically restricted to that selection instead of the whole
    // sheet/workbook. Null (the default) means "no selection constraint" — every existing caller
    // that never sets this keeps searching the whole Within-scoped sheet/workbook, unchanged. A
    // non-null list is a set of ranges (Excel's "sqref" — a selection can be multiple
    // non-contiguous areas); a candidate must fall inside at least one of them to match.
    IReadOnlyList<GridRange>? SelectionScope = null);

public sealed record ReplaceAllResult(int ReplacedCount, CommandOutcome? Failure);

/// <summary>Search and replace service. Replace goes through ICommandBus for undo support.</summary>
public static class FindReplaceService
{
    /// <summary>
    /// Find all cells in the workbook whose display text (or formula text) contains searchText.
    /// Results are ordered: sheet order, then row-major within each sheet.
    /// </summary>
    public static IReadOnlyList<FindResult> Find(
        Workbook workbook,
        string searchText,
        bool matchCase = false,
        bool matchEntireCell = false,
        bool searchFormulas = false)
        => Find(
            workbook,
            searchText,
            new FindOptions(LookIn: searchFormulas ? FindLookIn.Formulas : FindLookIn.Values),
            matchCase,
            matchEntireCell);

    public static IReadOnlyList<FindResult> Find(
        Workbook workbook,
        string searchText,
        FindOptions options,
        bool matchCase = false,
        bool matchEntireCell = false)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        // The number-skip optimisation is no longer applied:
        //  - Values mode: numbers are now rendered through their applied number format
        //    (e.g. "50%", "$1,000.00"), so any pattern may potentially match.
        //  - Formulas mode: formula cells are searched by formula text, not by the cached
        //    NumberValue — skipping by value type would silently drop formula matches.
        //  - Notes/Comments: those branches in EnumerateSearchTexts return before the cell loop
        //    so skipNumberValues has no effect there anyway.
        const bool skipNumbers = false;
        var results = new List<FindResult>();

        foreach (var sheet in FindReplaceSearchPlanner.SheetsForScope(workbook, options))
        {
            var sheetResults = new List<FindResult>();

            foreach (var candidate in FindReplaceSearchPlanner.EnumerateSearchTexts(sheet, options.LookIn,
                workbook: workbook,
                skipNumberValues: skipNumbers))
            {
                // A selection-scoped search (Excel: Replace All within an active multi-cell
                // selection) only considers candidates inside one of the scope's ranges.
                // Excel treats selection-scoping as a within-SHEET concept only: once the user
                // switches Within to Workbook, the search must cover every sheet, not just the
                // sheet the selection was captured on (GridRange.Contains requires
                // addr.Sheet == Start.Sheet, so an unconditional check here would silently drop
                // every match on other sheets).
                if (options.Within == FindWithin.Sheet &&
                    options.SelectionScope is { Count: > 0 } scope &&
                    !ContainsAddress(scope, candidate.Address))
                    continue;

                // Excel's "Look in: Formulas" match text is the formula-bar text, which always
                // includes the leading '=' that Cell.FormulaText intentionally omits (see
                // Cell.cs). candidate.Text (from FindReplaceSearchPlanner) is the bare,
                // '='-less FormulaText for a formula cell, so it must be re-prefixed here before
                // matching -- otherwise Match-entire-cell-contents in Formulas mode is inverted
                // relative to Excel (a search including '=' never matches; one omitting it always
                // does). A plain Contains/unanchored-wildcard match is unaffected by the extra
                // leading character, so this is safe to apply unconditionally.
                var matchText = options.LookIn == FindLookIn.Formulas && sheet.GetCell(candidate.Address) is { HasFormula: true } formulaCell
                    ? "=" + formulaCell.FormulaText
                    : candidate.Text;
                bool isMatch = IsTextMatch(matchText, searchText, comparison, matchEntireCell);

                if (isMatch && FindReplaceSearchPlanner.MatchesRequiredFormat(workbook, sheet, candidate.Address, options.RequiredFormat))
                {
                    sheetResults.Add(new FindResult(
                        candidate.Address,
                        candidate.Text,
                        candidate.Target,
                        candidate.ReplyIndex));
                }
            }

            FindReplaceSearchPlanner.SortResults(sheetResults, options.SearchOrder);
            results.AddRange(sheetResults);
        }

        return results;
    }

    /// <summary>
    /// Replace all matches in cell values (not formulas). Returns the count of replacements made.
    /// Each replaced cell becomes an EditCellsCommand in a single transaction on the command bus.
    /// </summary>
    public static int ReplaceAll(
        Workbook workbook,
        ICommandBus commandBus,
        string searchText,
        string replaceText,
        bool matchCase = false,
        bool matchEntireCell = false,
        StyleDiff? replacementFormat = null)
        => TryReplaceAll(
            workbook,
            commandBus,
            searchText,
            replaceText,
            new FindOptions(LookIn: FindLookIn.Values),
            matchCase,
            matchEntireCell,
            replacementFormat).ReplacedCount;

    public static int ReplaceAll(
        Workbook workbook,
        ICommandBus commandBus,
        string searchText,
        string replaceText,
        FindOptions options,
        bool matchCase = false,
        bool matchEntireCell = false,
        StyleDiff? replacementFormat = null)
        => TryReplaceAll(
            workbook,
            commandBus,
            searchText,
            replaceText,
            options,
            matchCase,
            matchEntireCell,
            replacementFormat).ReplacedCount;

    public static ReplaceAllResult TryReplaceAll(
        Workbook workbook,
        ICommandBus commandBus,
        string searchText,
        string replaceText,
        bool matchCase = false,
        bool matchEntireCell = false,
        StyleDiff? replacementFormat = null)
        => TryReplaceAll(
            workbook,
            commandBus,
            searchText,
            replaceText,
            new FindOptions(LookIn: FindLookIn.Values),
            matchCase,
            matchEntireCell,
            replacementFormat);

    public static ReplaceAllResult TryReplaceAll(
        Workbook workbook,
        ICommandBus commandBus,
        string searchText,
        string replaceText,
        FindOptions options,
        bool matchCase = false,
        bool matchEntireCell = false,
        StyleDiff? replacementFormat = null)
    {
        var matches = Find(workbook, searchText, options, matchCase, matchEntireCell);
        if (matches.Count == 0)
            return new ReplaceAllResult(0, null);

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var editsBySheet = new Dictionary<SheetId, List<(CellAddress Address, Cell NewCell)>>();
        var commands = new List<IWorkbookCommand>();

        foreach (var result in matches)
        {
            var sheet = workbook.GetSheet(result.Address.Sheet);
            if (sheet is null) continue;

            if (TryCreateReplacementCell(
                    sheet,
                    result.Address,
                    searchText,
                    replaceText,
                    comparison,
                    matchEntireCell,
                    options.LookIn,
                    replacementFormat is not null,
                    out var newCell,
                    workbook))
            {
                if (!editsBySheet.TryGetValue(result.Address.Sheet, out var list))
                {
                    list = [];
                    editsBySheet[result.Address.Sheet] = list;
                }
                list.Add((result.Address, newCell));
                continue;
            }

            if (TryCreateReplacementCommentCommand(
                    sheet,
                    result,
                    searchText,
                    replaceText,
                    comparison,
                    matchEntireCell,
                    options.LookIn,
                    out var commentCommand))
                commands.Add(commentCommand);
        }

        foreach (var (sheetId, edits) in editsBySheet)
        {
            commands.Add(new EditCellsCommand(sheetId, edits));
            if (replacementFormat is not null)
            {
                commands.AddRange(edits.Select(edit => new ApplyStyleCommand(
                    sheetId,
                    new GridRange(edit.Address, edit.Address),
                    replacementFormat)));
            }
        }

        var replacedCount = editsBySheet.Values.Sum(static edits => edits.Count)
            + commands.Count(command => command is not EditCellsCommand and not ApplyStyleCommand);
        if (commands.Count == 0)
            return new ReplaceAllResult(0, null);

        var command = commands.Count == 1
            ? commands[0]
            : new CompositeWorkbookCommand("Replace All", commands);
        var outcome = commandBus.Execute(workbook.Id, command);
        if (!outcome.Success)
            return new ReplaceAllResult(0, outcome);

        return new ReplaceAllResult(replacedCount, null);
    }

    /// <param name="workbook">
    /// Optional owning workbook. Supply it so Values-mode matching against a formatted
    /// number/date cell (e.g. a currency or percent cell) uses the same number-format-aware
    /// display text that <see cref="Find"/> matched — see <see cref="TryCreateReplacementCell"/>.
    /// When omitted, formatted-number matches are skipped (unchanged legacy behavior).
    /// </param>
    public static bool TryCreateReplacementCommand(
        Sheet sheet,
        FindResult match,
        string searchText,
        string replaceText,
        bool matchCase,
        bool matchEntireCell,
        FindLookIn lookIn,
        StyleDiff? replacementFormat,
        out IWorkbookCommand command,
        Workbook? workbook = null)
    {
        command = null!;
        // A blank "Find what" is only meaningful here when it is paired with a Format criterion
        // on the Replace side (Excel's format-only Replace: reformat every Find-format match
        // without touching cell text) -- see TryCreateReplacementCell's allowFormatOnly handling
        // below. With no replacementFormat, an empty searchText can never build a text
        // substitution, so bail immediately exactly as before.
        if (string.IsNullOrEmpty(searchText) && replacementFormat is null)
            return false;

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (TryCreateReplacementCell(
                sheet,
                match.Address,
                searchText,
                replaceText,
                comparison,
                matchEntireCell,
                lookIn,
                replacementFormat is not null,
                out var newCell,
                workbook))
        {
            var editCommand = new EditCellsCommand(sheet.Id, [(match.Address, newCell)]);
            command = replacementFormat is null
                ? editCommand
                : new CompositeWorkbookCommand(
                    "Replace",
                    [
                        editCommand,
                        new ApplyStyleCommand(
                            sheet.Id,
                            new GridRange(match.Address, match.Address),
                            replacementFormat)
                    ]);
            return true;
        }

        return TryCreateReplacementCommentCommand(
            sheet,
            match,
            searchText,
            replaceText,
            comparison,
            matchEntireCell,
            lookIn,
            out command);
    }

    /// <summary>
    /// Builds the replacement cell for a Values/Formulas-mode match.
    /// </summary>
    /// <remarks>
    /// Excel semantics: Replace must operate on the very same text Find matched, so that a match
    /// only visible in the formatted display text (e.g. a currency cell showing "$1,000.00") is
    /// still replaceable, not silently skipped. When <paramref name="workbook"/> is supplied,
    /// Values-mode matching uses the cell's number-format-aware display text — identical to what
    /// <see cref="FindReplaceSearchPlanner.EnumerateSearchTexts"/> used to find it. The resulting
    /// replacement text is then re-parsed the same way Excel re-parses typed-in cell text
    /// (accepting "$", thousands separators, "%", and dates) so the new stored value round-trips
    /// through the same representation the user was editing; if it does not parse as a number or
    /// date, Excel stores the literal replacement text, so we do too. A match that exists only in
    /// the formatted rendering with no corresponding text in the stored value (not reachable via
    /// this round-trip) is therefore never silently applied — it is skipped, matching Excel.
    /// </remarks>
    private static bool TryCreateReplacementCell(
        Sheet sheet,
        CellAddress address,
        string searchText,
        string replaceText,
        StringComparison comparison,
        bool matchEntireCell,
        FindLookIn lookIn,
        bool allowFormatOnly,
        out Cell newCell,
        Workbook? workbook = null)
    {
        newCell = null!;
        var cell = sheet.GetCell(address);
        if (cell is null)
            return false;

        // Formulas-mode on a cell with no formula falls back to the same plain display text
        // FindReplaceSearchPlanner.EnumerateSearchTexts used to find the match (Excel's
        // "Look in: Formulas" replaces constants too — it is the ONLY replace mode Excel offers,
        // and it must not silently skip the very matches Find reported).
        //
        // A formula cell's currentText carries the leading '=' that Cell.FormulaText itself
        // omits (see Cell.cs) -- Excel's formula-bar text, which is what Look-in-Formulas
        // matches against, always starts with '='. Without it, Match-entire-cell-contents would
        // be inverted vs Excel (see Find(), which applies the same prefix before matching).
        var currentText = lookIn switch
        {
            FindLookIn.Formulas => cell.HasFormula ? "=" + cell.FormulaText : GetDisplayText(cell.Value),
            FindLookIn.Values => cell.HasFormula
                ? null
                : workbook is not null
                    ? GetDisplayTextFormatted(cell, workbook)
                    : GetDisplayText(cell.Value),
            _ => null
        };
        // True exactly when currentText came from the bare, invariant GetDisplayText helper
        // (as opposed to the '='-prefixed formula text or the number-format-aware
        // GetDisplayTextFormatted) -- see the DateTimeValue time-of-day preservation below.
        var usedInvariantDisplayText = lookIn switch
        {
            FindLookIn.Formulas => !cell.HasFormula,
            FindLookIn.Values => !cell.HasFormula && workbook is null,
            _ => false
        };
        if (currentText is null)
            return false;

        string newText;
        if (string.IsNullOrEmpty(searchText))
        {
            // Format-only replace (blank "Find what"/"Replace with" paired with a Format
            // criterion, see Find()'s RequiredFormat handling): there is no text to substitute --
            // Find() already matched this cell purely on its format, using an empty searchText
            // that matches every candidate's text. Pass currentText through unchanged rather than
            // failing, so the caller still gets a (no-op-value) edit for this address; that edit
            // is what TryReplaceAll/TryCreateReplacementCommand key their ApplyStyleCommand
            // (replacementFormat) emission off of. allowFormatOnly is only ever true when the
            // caller actually has a replacementFormat to apply (see call sites), so this can never
            // fire for a plain blank search with no format criterion.
            if (!allowFormatOnly)
                return false;
            newText = currentText;
        }
        else if (!TryCreateReplacementText(currentText, searchText, replaceText, comparison, matchEntireCell, out newText))
        {
            return false;
        }

        if (lookIn == FindLookIn.Formulas && cell.HasFormula)
        {
            newCell = cell.Clone();
            // FormulaText storage always omits the leading '=' (see Cell.cs), but currentText/
            // newText here carry it to match Excel's formula-bar semantics -- strip it back off
            // before storing.
            newCell.FormulaText = newText.StartsWith('=') ? newText[1..] : newText;
            // Clear the stale cached value so the cell shows blank rather than the old
            // result until the host triggers recalculation after the replace command.
            newCell.Value = BlankValue.Instance;
            return true;
        }

        // Re-parse the replacement text the same way Excel re-parses text typed into a cell
        // (accepts "$", thousands separators, "%", and dates) so a formatted numeric match
        // round-trips back into a NumberValue rather than becoming literal text. An empty
        // replacement result (e.g. Replace All with a blank "Replace with") must clear the cell
        // to BlankValue rather than storing an empty TextValue — Excel leaves the cell truly
        // blank (COUNTA excludes it, ISBLANK is TRUE), and a stored empty-string TextValue would
        // also round-trip into the saved .xlsx as a non-blank string cell.
        //
        // A destination cell whose number format is Text ("@") must never be re-parsed — Excel
        // keeps typed/replaced text as literal text there (e.g. a zip code "01234" kept as text
        // to preserve the leading zero), exactly like PasteCommandFactory.IsDestinationTextFormatted.
        var isDestinationTextFormatted =
            workbook is not null && workbook.GetStyle(cell.StyleId).NumberFormat == "@";
        ScalarValue newValue;
        if (newText.Length == 0)
        {
            newValue = BlankValue.Instance;
        }
        else if (isDestinationTextFormatted)
        {
            newValue = new TextValue(newText);
        }
        else if (ExcelTextNumberParser.TryParse(newText, out var number, workbook?.Uses1904DateSystem ?? false))
        {
            // GetDisplayText's DateTimeValue rendering ("yyyy-MM-dd") is date-only and drops any
            // time-of-day fraction, so a literal date/time cell matched/replaced through that
            // invariant text (Formulas-mode's constant-cell fallback, or Values-mode with no
            // workbook supplied) must not have its stored time silently zeroed out when the
            // replacement text itself carries no time component -- re-attach the cell's original
            // fractional day. A replacement that DOES specify its own time (contains ':' or an
            // AM/PM designator) is left alone and used as-is.
            if (usedInvariantDisplayText && cell.Value is DateTimeValue originalDateTime && !ContainsTimeComponent(newText))
            {
                var originalTimeFraction = originalDateTime.Value - Math.Floor(originalDateTime.Value);
                if (originalTimeFraction != 0)
                    number = Math.Floor(number) + originalTimeFraction;
            }

            newValue = new NumberValue(number);
        }
        else
        {
            newValue = new TextValue(newText);
        }

        newCell = cell.Clone();
        newCell.Value = newValue;
        newCell.FormulaText = null;
        return true;
    }

    private static bool TryCreateReplacementCommentCommand(
        Sheet sheet,
        FindResult match,
        string searchText,
        string replaceText,
        StringComparison comparison,
        bool matchEntireCell,
        FindLookIn lookIn,
        out IWorkbookCommand command)
    {
        command = null!;
        var currentText = lookIn switch
        {
            FindLookIn.Notes when
                match.Target == FindResultTarget.Note &&
                sheet.Comments.TryGetValue(match.Address, out var note) => note,
            FindLookIn.Comments when
                match.Target == FindResultTarget.ThreadedComment &&
                sheet.ThreadedComments.TryGetValue(match.Address, out var threadedComment) => threadedComment.Text,
            FindLookIn.Comments when
                match.Target == FindResultTarget.ThreadedCommentReply &&
                match.ReplyIndex is { } replyIndex &&
                sheet.ThreadedComments.TryGetValue(match.Address, out var threadedComment) &&
                replyIndex >= 0 &&
                replyIndex < threadedComment.Replies.Count => threadedComment.Replies[replyIndex].Text,
            _ => null
        };
        if (currentText is null ||
            !TryCreateReplacementText(currentText, searchText, replaceText, comparison, matchEntireCell, out var newText))
            return false;

        command = lookIn switch
        {
            FindLookIn.Notes when match.Target == FindResultTarget.Note =>
                new SetCommentCommand(sheet.Id, match.Address, newText),
            FindLookIn.Comments when match.Target == FindResultTarget.ThreadedComment =>
                new UpdateThreadedCommentTextCommand(sheet.Id, match.Address, newText),
            FindLookIn.Comments when
                match.Target == FindResultTarget.ThreadedCommentReply &&
                match.ReplyIndex is { } replyIndex =>
                new UpdateThreadedCommentReplyCommand(sheet.Id, match.Address, replyIndex, newText),
            _ => null!
        };

        return command is not null;
    }

    private static bool TryCreateReplacementText(
        string currentText,
        string searchText,
        string replaceText,
        StringComparison comparison,
        bool matchEntireCell,
        out string newText)
    {
        newText = "";

        // Format-only find/replace (blank "Find what" combined with a Format criterion, see
        // FindReplaceService.Find's RequiredFormat handling) reaches this helper via
        // TryReplaceAll's direct TryCreateReplacementCell call, which -- unlike the public
        // TryCreateReplacementCommand wrapper -- has no empty-searchText guard of its own.
        // Without this guard, the non-wildcard, non-entire-cell branch below would hit
        // string.Replace("", replaceText, comparison), which throws ArgumentException for any
        // StringComparison (oldValue must not be empty), crashing the WPF host. Matching the
        // wrapper's existing constraint, an empty search text simply cannot substitute text, so
        // report no match here instead of throwing.
        if (string.IsNullOrEmpty(searchText))
            return false;

        if (HasWildcard(searchText))
        {
            var regex = GetOrCreateSearchRegex(searchText, comparison, matchEntireCell);
            try
            {
                if (!regex.IsMatch(currentText))
                    return false;

                // Match Entire Cell replaces the whole cell text with the literal replacement.
                // Otherwise every non-overlapping wildcard match is replaced with the literal
                // replacement text — Excel does not expand wildcards in the replacement string, so a
                // manual match walk (see ReplaceNonOverlappingMatches) is used instead of
                // Regex.Replace: an all-wildcard pattern like "*" is unanchored and can match the
                // full text AND then an empty string at end-of-input, which Regex.Replace would
                // substitute twice (Regex.Replace("abc", ".*", "X") == "XX"); Excel produces "X".
                newText = matchEntireCell
                    ? replaceText
                    : ReplaceNonOverlappingMatches(regex, currentText, replaceText);
                return true;
            }
            catch (RegexMatchTimeoutException)
            {
                // Mirror every formula-side wildcard consumer (BuiltInFunctions.Criteria.cs,
                // BuiltInFunctions.TextCore.Search.cs, BuiltInFunctions.Regex.cs): a catastrophically
                // backtracking pattern must not crash the host — treat it as "no match" instead of
                // letting the exception escape into the Find/Replace UI click handler.
                return false;
            }
        }

        var isMatch = matchEntireCell
            ? currentText.Equals(searchText, comparison)
            : currentText.Contains(searchText, comparison);
        if (!isMatch)
            return false;

        newText = matchEntireCell
            ? replaceText
            : currentText.Replace(searchText, replaceText, comparison);
        return true;
    }

    /// <summary>
    /// Replaces every non-overlapping match of <paramref name="regex"/> in <paramref name="input"/>
    /// with the literal <paramref name="replacement"/>, skipping a zero-length match that starts
    /// exactly where the previous match ended. Plain <see cref="Regex.Replace(string, string)"/>
    /// does not skip that trailing empty match, so an unanchored, fully-wildcard pattern (e.g. the
    /// pattern built from a lone <c>*</c>) matches the whole input once and then matches an empty
    /// string at the end, causing the replacement text to be substituted twice.
    /// </summary>
    private static string ReplaceNonOverlappingMatches(Regex regex, string input, string replacement)
    {
        var sb = new System.Text.StringBuilder();
        var position = 0;
        var lastMatchEnd = -1;
        var match = regex.Match(input);
        while (match.Success)
        {
            if (match.Length == 0 && match.Index == lastMatchEnd)
            {
                // Zero-length match immediately abutting the previous (possibly non-empty) match:
                // skip it rather than substituting a second replacement, and advance by one
                // character (or stop) so the scan makes progress.
                if (match.Index >= input.Length)
                    break;
                match = regex.Match(input, match.Index + 1);
                continue;
            }

            sb.Append(input, position, match.Index - position);
            sb.Append(replacement);
            position = match.Index + match.Length;
            lastMatchEnd = position;

            match = match.Length == 0 && position < input.Length
                ? regex.Match(input, position + 1)
                : position <= input.Length
                    ? regex.Match(input, position)
                    : Match.Empty;
        }

        sb.Append(input, position, input.Length - position);
        return sb.ToString();
    }

    /// <summary>
    /// Text-match test shared by <see cref="Find"/> and the Replace path. Excel-style wildcards
    /// (<c>*</c> = any run of characters, <c>?</c> = exactly one character, <c>~</c> escapes the
    /// next wildcard character) are honored whenever <paramref name="searchText"/> contains one;
    /// otherwise a plain literal Equals/Contains is used, preserving the previous fast path so
    /// ordinary literal searches (the overwhelming majority) do not pay for regex construction.
    /// </summary>
    private static bool IsTextMatch(string text, string searchText, StringComparison comparison, bool matchEntireCell)
    {
        if (HasWildcard(searchText))
        {
            var regex = GetOrCreateSearchRegex(searchText, comparison, matchEntireCell);
            try
            {
                return regex.IsMatch(text);
            }
            catch (RegexMatchTimeoutException)
            {
                // A catastrophically backtracking wildcard pattern (e.g. many alternating "*x"
                // segments against long repetitive text) must not crash Find/Replace — every
                // formula-side wildcard consumer treats a timeout as "no match" and so do we.
                return false;
            }
        }

        return matchEntireCell
            ? text.Equals(searchText, comparison)
            : text.Contains(searchText, comparison);
    }

    /// <summary>True when <paramref name="address"/> falls inside any range of a selection scope.</summary>
    private static bool ContainsAddress(IReadOnlyList<GridRange> scope, CellAddress address)
    {
        foreach (var range in scope)
        {
            if (range.Contains(address))
                return true;
        }

        return false;
    }

    private static Regex GetOrCreateSearchRegex(string searchText, StringComparison comparison, bool anchored) =>
        FormulaWildcardHelper.GetOrCreateRegex(
            searchText,
            ignoreCase: comparison == StringComparison.OrdinalIgnoreCase,
            anchored: anchored);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="pattern"/> needs the wildcard/regex match path:
    /// it contains an unescaped <c>*</c> or <c>?</c> wildcard, or a <c>~</c> escape sequence
    /// (<c>~*</c>, <c>~?</c>, <c>~~</c>). The escape sequence case also requires the regex path
    /// even though it matches a single literal character, because the <c>~</c> prefix itself
    /// must be stripped — the plain literal Equals/Contains fast path would otherwise compare
    /// against the raw pattern text including the escaping <c>~</c>, which the cell text never
    /// contains. Matches <see cref="FormulaWildcardHelper"/>'s escaping rules exactly so
    /// detection and pattern-building never disagree.
    /// </summary>
    private static bool HasWildcard(string pattern)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (ch is '*' or '?' or '~')
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when the search pattern is a plain (non-wildcard) substring whose
    /// characters can potentially appear in an invariant numeric rendering, meaning a
    /// <see cref="NumberValue"/> cell might match and must not be skipped.
    /// Returns <c>false</c> only when the pattern is guaranteed never to match any number
    /// (contains at least one character outside [0-9eE+-.NanInfty] and no wildcards).
    /// </summary>
    /// <remarks>
    /// This is a conservative pre-filter: it returns <c>true</c> when in doubt so correctness
    /// is never compromised.  Callers may skip <see cref="NumberValue"/> cells when this returns
    /// <c>false</c>.
    /// </remarks>
    public static bool CanSearchTextMatchNumber(string searchText)
    {
        if (string.IsNullOrEmpty(searchText))
            return true;

        var hasNonNumericChar = false;
        foreach (var ch in searchText)
        {
            // Wildcards can match anything — bail out immediately (return true = may match).
            if (ch is '*' or '?')
                return true;

            // Characters that can appear in invariant number strings (digits, sign, decimal,
            // exponent marker, and the letters that make up "NaN" / "Infinity").
            if (ch is >= '0' and <= '9' or '.' or '-' or '+' or 'E' or 'e'
                    or 'N' or 'a' or 'n' or 'I' or 'f' or 'i' or 'y')
                continue;

            hasNonNumericChar = true;
        }

        // If every character was in the numeric set, the pattern might match a number.
        return !hasNonNumericChar;
    }

    private static string? GetDisplayText(ScalarValue value) => value switch
    {
        BlankValue => null,
        NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture),
        TextValue t => t.Value,
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        DateTimeValue dt => dt.ToDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ErrorValue err => err.Code,
        _ => null
    };

    /// <summary>
    /// Heuristic check for whether replacement text itself specifies a time-of-day component (a
    /// colon-separated time, or an AM/PM designator) -- used by <see cref="TryCreateReplacementCell"/>
    /// to decide whether a literal date/time cell's original fractional day should be re-attached
    /// after a replace whose text came from the date-only <see cref="GetDisplayText"/> rendering.
    /// </summary>
    private static bool ContainsTimeComponent(string text) =>
        text.Contains(':') ||
        text.Contains("AM", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("PM", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Number-format-aware display text for Values-mode replace, mirroring
    /// <c>FindReplaceSearchPlanner.GetDisplayTextFormatted</c> so Replace matches the exact text
    /// Find produced (e.g. "50%", "$1,000.00") instead of the unformatted invariant rendering.
    /// </summary>
    private static string? GetDisplayTextFormatted(Cell cell, Workbook workbook)
    {
        var value = cell.Value;

        if (value is BlankValue) return null;
        if (value is TextValue t) return t.Value;
        if (value is BoolValue b) return b.Value ? "TRUE" : "FALSE";
        if (value is ErrorValue e) return e.Code;

        var style = workbook.GetStyle(cell.StyleId);
        return NumberFormatter.Format(value, style.NumberFormat, workbook.Uses1904DateSystem);
    }
}
