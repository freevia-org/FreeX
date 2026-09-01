using System.Globalization;

namespace Free.Shared.IO;

/// <summary>
/// Length caps that cannot cut a character in half.
///
/// r194: four sheet-name sanitizers independently wrote <c>name[..31]</c> to enforce the 31-character
/// Excel sheet-name limit. That limit is in CHARACTERS, but the slice is in UTF-16 code units, so a
/// name whose 31st code unit falls inside a surrogate pair -- an emoji straddling that boundary, a
/// CJK Extension B ideograph -- was truncated to a trailing LONE HIGH SURROGATE. Nothing validated
/// the result (<c>Workbook.ValidateSheetNameStructure</c> checks length and the invalid-character
/// set, never surrogate well-formedness), so the workbook opened normally and then every save to
/// .xlsx threw from ClosedXML's <c>Worksheets.Add</c> -- "The surrogate pair is invalid" -- aborting
/// the write before any bytes were produced. The name never changes in memory, so the document
/// became permanently unsaveable in that format.
///
/// This is the same class as the r193 FreeW Drop Cap fix (splitting text by UTF-16 char rather than
/// by text element); the sweep that generalised that finding is what surfaced these. It lives here,
/// beside <see cref="XmlTextSanitizer"/>, so a fifth caller gets it for free rather than
/// reintroducing the slice.
/// </summary>
public static class SurrogateSafeTruncation
{
    /// <summary>
    /// <paramref name="value"/> limited to <paramref name="maxLength"/> UTF-16 code units, cut only
    /// on a text-element boundary so no surrogate pair or combining sequence is split. The result can
    /// therefore be SHORTER than <paramref name="maxLength"/> -- by up to one text element -- which is
    /// the point: a name one character under the cap is valid, a name ending in half a character is
    /// not.
    /// </summary>
    public static string LimitToTextElements(string? value, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);

        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value ?? string.Empty;

        // Walk whole text elements and stop before the one that would cross the cap. StringInfo is
        // culture-independent here: it segments by Unicode grapheme rules, not by locale.
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        var kept = 0;
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            if (kept + element.Length > maxLength)
                break;

            kept += element.Length;
        }

        return value[..kept];
    }
}
