using FreeX.Core.Model;
using FluentAssertions;
using System.Diagnostics;

namespace FreeX.Core.Model.Tests;

public partial class GridRangeTests
{
    [Fact]
    public void Constructor_NormalizesStartAndEnd()
    {
        var sheet = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheet, 10, 5),
            new CellAddress(sheet, 3, 2));

        range.Start.Should().Be(new CellAddress(sheet, 3, 2));
        range.End.Should().Be(new CellAddress(sheet, 10, 5));
    }

    [Fact]
    public void Constructor_RejectsEndpointsOnDifferentSheets()
    {
        var first = SheetId.New();
        var second = SheetId.New();

        var act = () => new GridRange(
            new CellAddress(first, 1, 1),
            new CellAddress(second, 2, 2));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*same sheet*");
    }

    [Fact]
    public void Parse_ReturnsNormalizedRange()
    {
        var sheet = SheetId.New();

        var range = GridRange.Parse("C4:A1", sheet);

        range.Start.Should().Be(new CellAddress(sheet, 1, 1));
        range.End.Should().Be(new CellAddress(sheet, 4, 3));
    }

    [Fact]
    public void Parse_InvalidRangeSeparator_ThrowsFormatException()
    {
        var sheet = SheetId.New();

        var act = () => GridRange.Parse("A1:B2:C3", sheet);

        act.Should().Throw<FormatException>()
            .WithMessage("Invalid range notation:*");
    }
}
