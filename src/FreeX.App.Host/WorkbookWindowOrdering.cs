using System.Collections.Generic;
using System.Globalization;

namespace FreeX.App.Host;

/// <summary>
/// Pure, WPF-free selection and numbering logic for the multi-window workbook registry.
/// Kept separate from <see cref="WorkbookWindowRegistry"/> so the ordering rules can be
/// unit-tested without standing up real windows.
/// </summary>
public static class WorkbookWindowOrdering
{
    /// <summary>Sentinel returned by <see cref="NextWindowIndex"/> when there is no window to switch to.</summary>
    public const int NoTarget = -1;

    /// <summary>
    /// Excel-style title suffix for a window viewing a shared workbook. A lone window over the
    /// workbook is not numbered (empty suffix); once a second window exists every window gains a
    /// " - {position}" suffix (Book1 - 1, Book1 - 2, ...). <paramref name="position"/> is 1-based.
    /// </summary>
    public static string FormatWindowTitleSuffix(int position, int totalWindowCount)
    {
        if (totalWindowCount <= 1)
            return string.Empty;
        if (position < 1 || position > totalWindowCount)
            return string.Empty;

        return string.Create(CultureInfo.InvariantCulture, $" - {position}");
    }

    /// <summary>
    /// Index of the next window to activate when cycling with Switch Windows. Wraps forward to
    /// the first window. Returns <see cref="NoTarget"/> when there are no windows, and falls back
    /// to the first window when the current index is out of range.
    /// </summary>
    public static int NextWindowIndex(int currentIndex, int count)
    {
        if (count <= 0)
            return NoTarget;
        if (currentIndex < 0 || currentIndex >= count)
            return 0;

        return (currentIndex + 1) % count;
    }

    /// <summary>
    /// Indices of the windows that must refresh after a workbook change, in registration order,
    /// excluding the originating window. When the origin index is out of range every window is
    /// notified (defensive: a change we cannot attribute should refresh everyone).
    /// </summary>
    public static IReadOnlyList<int> IndicesToNotify(int originIndex, int count)
    {
        if (count <= 0)
            return [];

        var result = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            if (i == originIndex)
                continue;
            result.Add(i);
        }

        return result;
    }
}
