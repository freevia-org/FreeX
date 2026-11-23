using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Assertions for Excel's array-shape rule measured through COM: arguments whose lengths disagree on an axis (and
/// are not 1) do not make the formula a plain #VALUE!. The result grows to the longer length and the positions the
/// shorter array does not reach are #N/A (LEFT(C1:C3,{1;2}) is {"a";"Ba";#N/A}).
/// </summary>
internal static class ExcelParityRound8ShapeAssertions
{
    /// <summary>The result is an array that contains at least one #N/A (the padding).</summary>
    public static void ShouldPadMismatchedAxesWithNa(this ScalarValue value)
    {
        var range = value.Should().BeOfType<RangeValue>("mismatched array axes grow to the longer length").Subject;
        bool padded = false;
        for (int r = 0; r < range.RowCount && !padded; r++)
            for (int c = 0; c < range.ColCount && !padded; c++)
                padded = range.Cells[r, c] is ErrorValue { Code: "#N/A" };

        padded.Should().BeTrue("the positions the shorter array does not reach are #N/A");
    }
}
