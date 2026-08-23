using System.Globalization;
using Free.Shared.IO;

namespace FreeX.Core.Model;

/// <summary>
/// Single shared parse/format for a data-validation numeric bound (the text typed into
/// Formula1/Formula2 for a WholeNumber/Decimal/Date/Time rule).
/// <para>
/// Before this helper existed, the identical bound text was parsed with THREE different
/// <see cref="NumberStyles"/> across three independent call sites that all needed to agree on the
/// same number: the Data Validation dialog's entry gate
/// (FreeX.App.Presentation.Dialogs.DataValidationDialogModel, which used
/// <see cref="NumberStyles.Float"/> -- no thousands grouping at all, so a legitimately
/// thousands-grouped bound like "1,234" was rejected outright as invalid input), live enforcement
/// while the session runs (FreeX.Core.Commands.DataValidationBoundsParser, which used
/// <see cref="NumberStyles.Any"/> with a grouping-shape guard), and file-save canonicalization
/// (FreeX.Core.IO.XlsxDataValidationClosedXmlMapper, which used a hand-picked style set that also
/// omitted thousands grouping, so a bound that reached save with a grouping separator -- e.g.
/// loaded from a file, or entered through some path other than the gated dialog -- fell through
/// unparsed and was written to the XLSX verbatim, with its locale-specific grouping character
/// still embedded, instead of being canonicalized to invariant digits). A thousands-grouped bound
/// could therefore be accepted/rejected inconsistently by the dialog, enforced as one number while
/// the session ran, and end up persisted as a completely different (and locale-dependent) value on
/// disk. All three call sites now derive from this one helper so they can never drift apart again.
/// </para>
/// </summary>
public static class DataValidationNumericBoundText
{
    /// <summary>
    /// Deliberately NOT <see cref="NumberStyles.Any"/> (the style the pre-r134
    /// <c>DataValidationBoundsParser</c> used): <see cref="NumberStyles.AllowExponent"/> is included
    /// because it is required both for legitimate Excel DV bound syntax (e.g. "1E+10") and, more
    /// importantly, because <see cref="ToInvariantString"/> itself emits scientific notation for
    /// sufficiently extreme magnitudes (e.g. 1e21 formats as "1E+21") -- without it this parser could
    /// not read back text its own formatter produces, which is the self-inconsistency r134 shipped
    /// as a regression.
    /// <see cref="NumberStyles.AllowParentheses"/> (accounting-style negatives, e.g. "(5)") and
    /// <see cref="NumberStyles.AllowCurrencySymbol"/> / <see cref="NumberStyles.AllowTrailingSign"/>
    /// are deliberately excluded: none of these are valid Excel data-validation bound syntax, and
    /// <see cref="ToInvariantString"/> never emits them, so allowing them on the parse side would
    /// only widen the accepted grammar past what the app itself ever writes.
    /// </summary>
    private const NumberStyles Styles =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint |
        NumberStyles.AllowThousands | NumberStyles.AllowExponent;

    /// <summary>
    /// Parses a numeric bound the way a user actually types (or a file actually stores) it: first
    /// under the current UI culture, so a comma-decimal locale (e.g. de-DE "1,5") is read as
    /// intended, then falling back to invariant (dot-decimal) parsing for bounds authored/stored in
    /// invariant form. Rejects a grouping separator that doesn't fall on 3-digit boundaries --
    /// .NET's <see cref="NumberStyles.AllowThousands"/> parsing does not itself validate that
    /// shape, so under a '.'-grouping culture (de-DE, es-ES, it-IT, ...) an invariant dot-decimal
    /// literal like "1.5" would otherwise be silently misread as the grouped integer 15 by the
    /// current-culture attempt instead of falling through to the invariant attempt below.
    /// </summary>
    public static bool TryParse(string? text, out double value) =>
        TryParseCore(text, CultureInfo.CurrentCulture, out value) ||
        TryParseCore(text, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Formats a bound value back to the culture-invariant dot-decimal text Excel/OOXML always
    /// stores in Formula1/Formula2, regardless of the authoring or reloading UI culture. Persisted
    /// data-validation bounds must never carry a locale-dependent decimal or grouping separator --
    /// a file saved on a comma-decimal machine and reopened on a dot-decimal one (or vice versa)
    /// would otherwise silently change what the rule enforces.
    /// </summary>
    public static string ToInvariantString(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool TryParseCore(string? text, CultureInfo culture, out double value)
    {
        if (double.TryParse(text, Styles, culture, out value) &&
            NumericTextGroupingValidator.HasValidGroupingShape(text, culture))
            return true;

        value = default;
        return false;
    }

}
