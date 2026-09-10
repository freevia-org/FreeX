using System.Globalization;

namespace Free.Shared.AppServices;

/// <summary>
/// r604: reads a number a USER TYPED, in the user's own locale.
///
/// <para>The mirror image of r603. A number coming out of a FILE must be culture-invariant, because
/// the format fixes its spelling; a number a user types into a ribbon box or dialog field must
/// follow their locale, because that is what they see everywhere else on their machine and what
/// Word, Excel and PowerPoint accept. Parsing typed input as invariant silently rejects
/// <c>1,5</c> for every comma-decimal user.</para>
///
/// <para>The shape is FreeX's, settled in <c>CellEntryParser</c> and repeated here rather than
/// reinvented: current culture first, then the invariant spelling as a fallback -- but only when the
/// text does NOT contain the current culture's own decimal separator. Without that guard a
/// locale-typed value that merely failed to parse could be re-read as an invariant one and mean
/// something different (<c>1,5</c> is one and a half in de-DE and fifteen under an
/// AllowThousands invariant parse).</para>
/// </summary>
public static class LocalizedNumberEntry
{
    /// <summary>
    /// Parses typed input, preferring the current culture. Non-finite results are rejected: an
    /// overflowing literal parses as ±Infinity since .NET Core (r486), and no typed measurement,
    /// size or duration can be infinite.
    /// </summary>
    public static bool TryParse(string? text, NumberStyles numberStyles, out double value)
    {
        if (double.TryParse(text, numberStyles, CultureInfo.CurrentCulture, out value)
            && double.IsFinite(value))
        {
            return true;
        }

        var currentDecimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (currentDecimalSeparator != "."
            && text is not null
            && text.Contains(currentDecimalSeparator, StringComparison.Ordinal))
        {
            value = 0;
            return false;
        }

        if (double.TryParse(text, numberStyles, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value))
        {
            return true;
        }

        value = 0;
        return false;
    }
}
