using System.Text.RegularExpressions;

namespace FreeX.Core.Model;

/// <summary>
/// Canonical helper for quoting sheet names in Excel formula references.
/// </summary>
/// <remarks>
/// Excel's quoting rule: a sheet name reference does NOT need quoting if and only if
/// every character is an ASCII letter, digit, underscore or period AND the name does not
/// start with a digit or period AND the name is not a cell-address literal (A1 / R1C1 style)
/// AND the name is not the keyword TRUE or FALSE.
///
/// When quoting is needed the name is wrapped in single quotes and any embedded apostrophe
/// is doubled: <c>O'Brien</c> → <c>'O''Brien'</c>.
///
/// Period in non-first position is allowed unquoted; this matches Excel's own behaviour and
/// the existing <c>FormulaSerializer.RequiresQuoting</c> logic in this codebase.
/// </remarks>
public static class SheetNameFormatter
{
    // Matches R1C1 notation: R followed by optional digits, C followed by optional digits.
    // Also matches relative variants R[n]C[n] which are valid R1C1 forms.
    // The regex is anchored so it matches only complete strings.
    private static readonly Regex R1C1Pattern = new(
        @"^[Rr](\d+|\[\-?\d+\])?[Cc](\d+|\[\-?\d+\])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns the sheet name quoted with single quotes if Excel requires it, otherwise
    /// returns the name unchanged.
    /// </summary>
    public static string QuoteIfNeeded(string sheetName)
    {
        if (NeedsQuoting(sheetName))
        {
            var escaped = sheetName.Replace("'", "''", StringComparison.Ordinal);
            return $"'{escaped}'";
        }

        return sheetName;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="sheetName"/> must be wrapped in
    /// single quotes in an Excel formula reference.
    /// </summary>
    public static bool NeedsQuoting(string sheetName)
    {
        if (sheetName.Length == 0)
            return true;

        // Names starting with a digit look like numeric literals; starting with a period
        // is non-standard and Excel quotes such names. char.IsDigit (not IsAsciiDigit) matches
        // the Lexer's own number-literal dispatch (ReadNextToken: `char.IsDigit(c) || c == '.'`
        // routes to ReadNumber), which recognizes any Unicode digit here, not just ASCII 0-9.
        var first = sheetName[0];
        if (char.IsDigit(first) || first == '.')
            return true;

        // Every character must be a char an unquoted sheet-qualifier identifier can contain --
        // see IsUnquotedSheetNameChar, which mirrors the Lexer's own identifier-continuation
        // predicate so the two can never drift apart again.
        foreach (var ch in sheetName)
        {
            if (!IsUnquotedSheetNameChar(ch))
                return true;
        }

        // Names that look like cell addresses must be quoted so they are not parsed as
        // references rather than sheet names.  Use TryParse for A1-style detection.
        if (CellAddress.TryParse(sheetName, default, out _))
            return true;

        // R1C1-style cell references must also be quoted.
        if (R1C1Pattern.IsMatch(sheetName))
            return true;

        // TRUE / FALSE are boolean literals in Excel formula syntax.
        if (string.Equals(sheetName, "TRUE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sheetName, "FALSE", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// True when <paramref name="c"/> may appear -- in ANY position, including the first -- in an
    /// unquoted sheet-qualified formula reference, i.e. it is one of the identifier characters the
    /// formula Lexer's sheet-qualifier scan (in FreeX.Core.Formula: ReadIdentifierOrRef's
    /// continuation loop, <c>char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '.'</c>)
    /// accepts, minus '$' (a sheet name starting with, or containing, a literal '$' is edge-case
    /// enough -- and indistinguishable enough from an absolute-reference marker -- that it is
    /// always quoted here, matching this method's prior behavior).
    /// </summary>
    /// <remarks>
    /// This is the ONE shared predicate <see cref="NeedsQuoting"/> and the formula Lexer both
    /// derive their "does this character need quoting" answer from (the Lexer calls this method
    /// directly, ORed with its own '$' case), so the two can never again silently disagree about
    /// which sheet names round-trip unquoted. That is exactly the bug this replaced: this method
    /// used <c>char.IsAsciiLetterOrDigit</c> while the Lexer used Unicode-aware
    /// <c>char.IsLetterOrDigit</c>, so a Unicode-letter sheet name the Lexer accepted unquoted was
    /// nonetheless quoted here.
    /// </remarks>
    public static bool IsUnquotedSheetNameChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.';
}
