using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round-14 bucket T8 regression tests (App.Services half). One focused test per finding.
/// </summary>
public sealed class FreeXR14T8Tests
{
    // R14-cell-styles-themes-2: Clear Formats must also remove diagonal borders, matching Excel's
    // Home > Clear > Clear Formats, which strips ALL formatting including diagonal up/down borders.
    [Fact]
    public void ClearFormatsDiff_RemovesDiagonalBorders()
    {
        var styled = new CellStyle
        {
            BorderDiagonalDown = new CellBorder(BorderStyle.Thin, new CellColor(255, 0, 0)),
            BorderDiagonalUp = new CellBorder(BorderStyle.Thin, new CellColor(255, 0, 0)),
        };

        var diff = CellStyleDiffPlanner.ClearFormatsDiff();
        var result = diff.ApplyTo(styled);

        result.BorderDiagonalDown.Should().Be(new CellBorder(BorderStyle.None),
            "Excel's Clear Formats removes diagonal borders along with the four edge borders");
        result.BorderDiagonalUp.Should().Be(new CellBorder(BorderStyle.None),
            "Excel's Clear Formats removes diagonal borders along with the four edge borders");
    }
}
