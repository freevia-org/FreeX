using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public sealed class VacatedSpillFrontierTests
{
    [Fact]
    public void CaptureVacatedSpillCells_EnumeratesOnlyOldMinusRetainedExtentInRowMajorOrder()
    {
        Capture(3, 4, 2, 2).Should().Equal(
            (0u, 2u), (0u, 3u),
            (1u, 2u), (1u, 3u),
            (2u, 0u), (2u, 1u), (2u, 2u), (2u, 3u));

        Capture(3, 4, 4, 2).Should().Equal(
            (0u, 2u), (0u, 3u),
            (1u, 2u), (1u, 3u),
            (2u, 2u), (2u, 3u));

        Capture(3, 4, 2, 5).Should().Equal(
            (2u, 0u), (2u, 1u), (2u, 2u), (2u, 3u));
        Capture(3, 4, 4, 5).Should().BeEmpty();
        Capture(3, 4, 3, 4).Should().BeEmpty();
    }

    [Fact]
    public void CaptureVacatedSpillCells_ExcludesAnchorWhenAnAxisOrEntireSpillIsCleared()
    {
        Capture(2, 3, 0, 0).Should().Equal(
            (0u, 1u), (0u, 2u),
            (1u, 0u), (1u, 1u), (1u, 2u));
        Capture(2, 3, 0, 2).Should().Equal(
            (0u, 1u), (0u, 2u),
            (1u, 0u), (1u, 1u), (1u, 2u));
        Capture(2, 3, 2, 0).Should().Equal(
            (0u, 1u), (0u, 2u),
            (1u, 0u), (1u, 1u), (1u, 2u));
        Capture(1, 1, 0, 0).Should().BeEmpty();
        Capture(2, 3, -1, -1).Should().Equal(Capture(2, 3, 0, 0));
    }

    [Fact]
    public void CaptureVacatedSpillCells_LargeRowShrinkProducesOnlyFrontierCells()
    {
        Capture(1_000_000, 10, 999_999, 10).Should().HaveCount(10);
    }

    private static (uint Row, uint Col)[] Capture(uint priorRows, uint priorCols, int newRows, int newCols)
    {
        var anchor = new CellAddress(SheetId.New(), 1, 1);
        List<CellAddress>? cells = null;

        RecalcEngine.CaptureVacatedSpillCells(
            anchor,
            priorRows,
            priorCols,
            newRows,
            newCols,
            ref cells);

        return cells?.Select(cell => (cell.Row - anchor.Row, cell.Col - anchor.Col)).ToArray() ?? [];
    }
}
