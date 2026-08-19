using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class CellEntryParser
{
    /// <summary>
    /// Parses a typed cell-entry string into a <see cref="Cell"/>. When <paramref name="workbook"/>
    /// is supplied, the target <paramref name="address"/>'s current number format is honored the
    /// same way real Excel does: a Text ("@") formatted destination keeps the literal input
    /// verbatim (no numeric/date/formula coercion, matching PasteCommandFactory
    /// .IsDestinationTextFormatted's identical rule for the paste path), and a General-formatted
    /// destination that receives a percent/currency/fraction/time-shaped literal has that shape's
    /// matching number format auto-applied, mirroring Excel's own typed-entry auto-formatting
    /// (R87-formula-number-parse-locale-5-2, R87-formula-number-parse-locale-5-3). Callers that
    /// omit <paramref name="workbook"/> keep the original workbook-agnostic coercion behavior.
    /// </summary>
    public static Cell CreateCell(string text, CellAddress address, bool useR1C1ReferenceStyle, Workbook? workbook = null)
    {
        if (workbook is not null && IsTargetTextFormatted(workbook, address))
        {
            return Cell.FromValue(new TextValue(TruncateToExcelCellTextLimit(text)));
        }

        if (text.StartsWith("=", StringComparison.Ordinal))
        {
            // Real Excel's formula bar refuses to leave edit mode for a formula longer than its
            // documented 8,192-character limit, rejecting the entry outright rather than
            // committing (or silently truncating) the oversized text. Checked against the raw
            // typed text (before any R1C1-to-A1 conversion) since that's what the user actually
            // typed and what Excel's own formula-bar length check would see
            // (R120-formula-entry-nesting-length-validation).
            FormulaEvaluator.ValidateFormulaEntryLength(text);

            var formula = text[1..];
            if (useR1C1ReferenceStyle)
                formula = FormulaReferenceStyleService.ToA1(formula, address);

            // Real Excel refuses to leave edit mode for genuinely malformed formula syntax (e.g.
            // an unbalanced "=SUM(A1"), offering its "we found an error in this formula" correction
            // prompt instead of committing the broken text. Validate the syntax up front so a
            // parse failure rejects the entry outright (FormulaParseException propagates to the
            // caller, matching the DataValidation-block contract those callers already implement)
            // rather than silently committing unparseable formula text that would otherwise only
            // ever surface as a #VALUE! error later, during recalculation -- RecalcEngine.cs's own
            // FormulaParseException catches exist for the DIFFERENT case of a cell whose formula
            // text was already committed (e.g. loaded from a file whose formula this parser can't
            // handle, such as an external-workbook reference), not for a fresh interactive entry.
            var parsedFormula = FormulaEvaluator.ParseFormula(formula);

            // Real Excel additionally refuses to leave edit mode for a well-known built-in
            // function called with the wrong number of arguments -- e.g. "=IF(A1>0)" (1 argument;
            // IF requires 2 or 3) or "=LEFT(\"x\",1,2,3)" (4 arguments; LEFT allows at most 2) --
            // even though that text is otherwise syntactically valid. Excel's entry-time compiler
            // checks the argument count against the function's known signature and pops its
            // "too few/too many arguments" dialog instead of committing. FreeX previously enforced
            // this arity only during recalculation (see FormulaEvaluator
            // .ValidateBuiltInFunctionArity's own doc comment for the exact call sites), silently
            // committing the malformed shape and only ever surfacing it later as a #VALUE!.
            // Walking the freshly parsed AST here rejects it at entry instead, matching Excel
            // (R120-formula-entry-arity-validation).
            FormulaEvaluator.ValidateBuiltInFunctionArity(parsedFormula);

            // Real Excel also refuses to leave edit mode for a formula whose deepest chain of one
            // function nested inside another exceeds its documented 64-level function-nesting
            // limit (e.g. 100 nested IF() calls), popping its "too many levels of nesting" error
            // instead of committing. The parser's own EnterNesting/EnterParseFrame checks
            // (FormulaSafetyLimits.MaxParseNesting/MaxParseDepth = 256/512) are purely internal
            // recursion/stack-depth DoS guards -- much larger than Excel's real limit and not a
            // substitute for it -- so a formula built with, say, 100 nested IFs sailed through
            // those unchallenged even though real Excel's formula bar would reject that exact text
            // at entry. Walking the already-parsed AST here closes that gap
            // (R120-formula-entry-nesting-length-validation).
            FormulaEvaluator.ValidateFunctionNestingDepth(parsedFormula);

            return Cell.FromFormula(formula);
        }

        var value = ParseScalarValue(text, out var inferredNumberFormat);
        var cell = Cell.FromValue(value);

        if (inferredNumberFormat is not null && workbook is not null && IsTargetFormatGeneral(workbook, address))
        {
            var style = workbook.GetStyle(GetTargetStyleId(workbook, address)).Clone();
            style.NumberFormat = inferredNumberFormat;
            cell.StyleId = workbook.RegisterStyle(style);
        }

        return cell;
    }

    private static StyleId GetTargetStyleId(Workbook workbook, CellAddress address)
    {
        var sheet = workbook.GetSheet(address.Sheet);
        if (sheet is null)
            return StyleId.Default;

        return sheet.GetCell(address)?.StyleId ??
            sheet.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
    }

    // Mirrors PasteCommandFactory.IsDestinationTextFormatted / FindReplaceService's identical
    // "@"-format check for the paste and find/replace-re-parse paths respectively.
    private static bool IsTargetTextFormatted(Workbook workbook, CellAddress address) =>
        workbook.GetStyle(GetTargetStyleId(workbook, address)).NumberFormat == "@";

    private static bool IsTargetFormatGeneral(Workbook workbook, CellAddress address) =>
        workbook.GetStyle(GetTargetStyleId(workbook, address)).NumberFormat == "General";

    public static ScalarValue ParseScalarValue(string text) => ParseScalarValue(text, out _);

    private static ScalarValue ParseScalarValue(string text, out string? inferredNumberFormat)
    {
        inferredNumberFormat = null;

        if (text.Length == 0)
        {
            return BlankValue.Instance;
        }

        // Excel's text-escape convention: a leading apostrophe forces the typed entry to be kept
        // as text (apostrophe stripped), e.g. '007 -> "007". Mirrors PasteCommandFactory
        // .ParseClipboardValue's identical rule for the paste path; must be checked before any
        // numeric/boolean/date coercion below.
        if (text.StartsWith('\''))
        {
            return new TextValue(TruncateToExcelCellTextLimit(text[1..]));
        }

        if (TryParseFiniteNumber(text, out var number))
        {
            return new NumberValue(number);
        }

        if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new BoolValue(text.Equals("TRUE", StringComparison.OrdinalIgnoreCase));
        }

        // Excel auto-converts a handful of other common typed literal shapes into a numeric/date
        // value even though the plain-number parse above never sees '%', '$', or a date/fraction
        // separator. Fraction is checked before date because a strict "<int> <int>/<int>" shape
        // (e.g. "1 1/2") is otherwise also accepted by DateTime.TryParse below as a nonsensical
        // date - claim the unambiguous fraction shape first.
        if (TryParsePercent(text, out var percentValue))
        {
            inferredNumberFormat = "0%";
            return new NumberValue(percentValue);
        }

        if (TryParseCurrency(text, out var currencyValue))
        {
            inferredNumberFormat = NumberFormatShortcutService.GetFormat(NumberFormatShortcut.Currency);
            return new NumberValue(currencyValue);
        }

        if (TryParseMixedFraction(text, out var fractionValue))
        {
            inferredNumberFormat = "# ?/?";
            return new NumberValue(fractionValue);
        }

        // Excel's fictitious 1900 leap day ("2/29/1900" / "1900-02-29") cannot be represented as
        // a real .NET DateTime (1900 is not a leap year), so it must be special-cased directly
        // to serial 60 before the general DateTime-based parse below - which would otherwise
        // fail for this literal and leave the cell as plain text. Mirrors the same special-case
        // already applied to DATEVALUE()/TIMEVALUE() in BuiltInFunctions.
        if (BuiltInFunctions.TryParseExcelFakeLeapDayText(text, out var fakeLeapDaySerial))
        {
            return new DateTimeValue(fakeLeapDaySerial);
        }

        if (ExcelDateEntryParser.TryParseCurrentCulture(text, allowTimeOnly: true, out var dateTime))
        {
            // A time-only literal (e.g. "15:30", "3:30 PM") has no date component;
            // ExcelDateEntryParser's NoCurrentDateDefault parse synthesizes
            // DateTime.MinValue's date (0001-01-01) for the missing date part rather than
            // today's date. Such a value is an Excel time-of-day serial (< 1, e.g. 0.645833...
            // for 15:30), not a real date, so it must bypass DateTimeValue.FromDateTime's OADate
            // conversion entirely -- DateTime.ToOADate() throws for years before 100 AD, and even
            // if it didn't, the corrected serial math only applies to genuine dates.
            if (dateTime.Date == DateTime.MinValue.Date)
            {
                inferredNumberFormat = NumberFormatShortcutService.GetFormat(NumberFormatShortcut.Time);
                return new DateTimeValue(dateTime.TimeOfDay.TotalDays);
            }

            return DateTimeValue.FromDateTime(dateTime);
        }

        return new TextValue(TruncateToExcelCellTextLimit(text));
    }

    // Real Excel's hard cap on how much literal text a single cell can hold (see
    // PasteCommandFactory.TruncateToExcelCellTextLimit's identical rule for the external-clipboard
    // paste path, which this mirrors). A typed entry that exceeds this was previously accepted
    // uncapped -- the cell would carry text longer than a real XLSX cell can validly hold, so the
    // workbook saves fine here but real Excel then truncates it on open, silently losing data the
    // user believed was saved intact. Real Excel truncates the typed text to this limit rather than
    // rejecting the entry outright, so mirror that here rather than erroring the cell out.
    private const int ExcelCellTextLimit = 32767;

    private static string TruncateToExcelCellTextLimit(string text) =>
        text.Length > ExcelCellTextLimit ? text[..ExcelCellTextLimit] : text;

    // Float + AllowThousands so a comma-decimal locale's grouped integer (e.g. de-DE "1.234"
    // meaning 1234, '.' as thousands separator) is honored, not silently misread as a decimal.
    // AllowParentheses recognizes Excel's Lotus-style negative notation (e.g. "(123)" -> -123)
    // for a plain (no currency-symbol) numeric literal, not just the currency-marked form
    // ("($123)") that NumberStyles.Currency already handled via TryParseCurrency below.
    private const NumberStyles NumberEntryStyles =
        NumberStyles.Float | NumberStyles.AllowThousands | NumberStyles.AllowParentheses;

    private static bool TryParseFiniteNumber(string text, out double number)
    {
        // A culture whose thousands separator is itself a whitespace character (e.g. fr-FR's
        // U+202F narrow no-break space) is typed/pasted using whatever whitespace code point the
        // keyboard or source app actually produced -- an ordinary U+0020 or a plain non-breaking
        // U+00A0 -- not necessarily CultureInfo's exact reported separator. double.TryParse alone
        // accepts U+0020 as interchangeable with fr-FR's U+202F but rejects U+00A0, so without this
        // normalization a typed "1<U+00A0>234,56" was misread as text while the visually identical
        // Ctrl+V of the same string (PasteCommandFactory.TryParseCultureGroupedNumber) already
        // parsed correctly. Shares ExcelTextNumberParser's normalizer -- the same rule the paste
        // path and formula-text coercion already use -- rather than growing a fourth hand-written
        // copy of it. No-op for any culture whose separator isn't a single whitespace character
        // (en-US ',', de-DE '.'), so those cultures are untouched.
        text = ExcelTextNumberParser.NormalizeGroupSeparatorSpaceVariants(
            text, CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator);

        if (double.TryParse(text, NumberEntryStyles, CultureInfo.CurrentCulture, out number) &&
            double.IsFinite(number))
        {
            number = ExcelNumericPrecision.CapSignificantDigits(number);
            return true;
        }

        // Only reinterpret using invariant separators when the current-culture attempt failed
        // and the text doesn't contain the current culture's own (non-'.') decimal separator -
        // otherwise a locale-typed value that merely failed to parse could be misread as an
        // invariant-formatted one.
        var currentDecimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (currentDecimalSeparator != "." && text.Contains(currentDecimalSeparator, StringComparison.Ordinal))
        {
            number = 0;
            return false;
        }

        if (double.TryParse(text, NumberEntryStyles, CultureInfo.InvariantCulture, out number) &&
            double.IsFinite(number))
        {
            number = ExcelNumericPrecision.CapSignificantDigits(number);
            return true;
        }

        return false;
    }

    // Trailing '%' (e.g. "50%") -> Excel stores the underlying fraction (0.5), not the literal 50.
    private static bool TryParsePercent(string text, out double value)
    {
        value = default;
        if (text.Length < 2 || text[^1] != '%')
            return false;

        if (!TryParseFiniteNumber(text[..^1], out var number))
            return false;

        value = number / 100d;
        return true;
    }

    // A '$' sign is Excel's ASCII currency-entry marker (e.g. "$5") regardless of the current
    // culture's own currency symbol, so it is always parsed against en-US currency formatting.
    private static bool TryParseCurrency(string text, out double value)
    {
        value = default;
        if (!text.Contains('$'))
            return false;

        if (!double.TryParse(text, NumberStyles.Currency, CultureInfo.GetCultureInfo("en-US"), out value) ||
            !double.IsFinite(value))
        {
            value = default;
            return false;
        }

        value = ExcelNumericPrecision.CapSignificantDigits(value);
        return true;
    }

    // Mixed-number fraction entry (e.g. "1 1/2" -> 1.5, "0 1/2" -> 0.5). Requires a whole part
    // plus a space before the "n/d", matching Excel's own typed-entry convention - a bare "n/d"
    // with no leading whole part/space (e.g. "1/2") is a date to Excel, not a fraction.
    private static bool TryParseMixedFraction(string text, out double value)
    {
        value = default;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        var slashIndex = parts[1].IndexOf('/');
        if (slashIndex <= 0 || slashIndex == parts[1].Length - 1)
            return false;

        if (!long.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
            return false;

        if (!long.TryParse(parts[1].AsSpan(0, slashIndex), NumberStyles.None, CultureInfo.InvariantCulture, out var numerator) ||
            !long.TryParse(parts[1].AsSpan(slashIndex + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var denominator) ||
            denominator == 0)
        {
            return false;
        }

        var fraction = (double)numerator / denominator;
        value = whole < 0 ? whole - fraction : whole + fraction;
        return true;
    }

}
