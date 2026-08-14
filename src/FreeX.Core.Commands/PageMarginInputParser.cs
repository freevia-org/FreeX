using System.Globalization;
using Free.Shared.PageSetup;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Parses FreeX's four-value "left, right, top, bottom" margin text (inches). The per-value numeric
/// rules come from the cross-app <see cref="PageMarginTextPolicy"/>; the field layout and the
/// FreeX-specific error wording stay here.
/// </summary>
public static class PageMarginInputParser
{
    public static bool TryParse(string input, out WorksheetPageMargins margins, out string? error)
    {
        margins = default;

        var parts = input.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            error = "Enter four comma-separated margins: left, right, top, bottom.";
            return false;
        }

        var values = new double[4];
        for (var i = 0; i < parts.Length; i++)
        {
            // The joined field is invariant by construction (see PageSetupDialogPlanner's margin-token
            // normalization), so this list is always parsed with InvariantCulture.
            if (!PageMarginTextPolicy.TryParseNonNegative(
                    parts[i],
                    CultureInfo.InvariantCulture,
                    out var value,
                    out var failure))
            {
                error = failure == PageMeasureParseFailure.Negative
                    ? "Margins cannot be negative."
                    : "Margins must be numbers in inches.";
                return false;
            }

            values[i] = value;
        }

        margins = new WorksheetPageMargins(values[0], values[1], values[2], values[3]);
        error = null;
        return true;
    }
}
