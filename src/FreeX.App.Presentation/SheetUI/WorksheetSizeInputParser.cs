using System.Globalization;

namespace FreeX.App.Presentation.SheetUI;

public static class WorksheetSizeInputParser
{
    public static bool TryParsePositiveSize(string input, out double size)
        => TryParseSizeInRange(input, minInclusive: double.Epsilon, maxInclusive: double.MaxValue, out size);

    /// <summary>
    /// r604: current culture first, invariant as the fallback -- the shape FreeX already uses in
    /// <see cref="NumericInputParser"/>'s parameterless overloads, in <c>ChartDialogValueParser</c>
    /// and in <c>FormatCellsInputParser</c>. This one read the CURRENT CULTURE ONLY, so the ribbon
    /// font-size box rejected a value spelled the invariant way, while FreeX's own Avalonia host
    /// parsed the same box INVARIANT ONLY and rejected the locale spelling instead: one box, two
    /// platforms, opposite halves of the same defect.
    ///
    /// <para>AllowThousands is gone with it, and that removes a worse failure than either: '.' is
    /// the THOUSANDS separator on a de-DE machine, so a box showing "10.5" parsed back as 105 --
    /// pressing Enter without editing multiplied the size by ten. Nobody types a grouped font size
    /// or row height, and the chart dialogs had already excluded it.</para>
    /// </summary>
    public static bool TryParseSizeInRange(string input, double minInclusive, double maxInclusive, out double size)
    {
        if (NumericInputParser.TryParseFiniteDouble(
                input,
                CultureInfo.CurrentCulture,
                CultureInfo.InvariantCulture,
                out var parsed) &&
            parsed >= minInclusive &&
            parsed <= maxInclusive)
        {
            size = parsed;
            return true;
        }

        size = 0;
        return false;
    }
}
