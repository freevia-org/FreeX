using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class SheetSpillOwnershipTests
{
    [Fact]
    public void TryGetArrayExtent_ResolvesEachLiveMemberToItsOwningAnchor()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var verticalAnchor = new CellAddress(sheet.Id, 2, 2);
        var horizontalAnchor = new CellAddress(sheet.Id, 20, 8);

        sheet.SetSpillRange(verticalAnchor, CreateNumberRange(3, 1));
        sheet.SetSpillRange(horizontalAnchor, CreateNumberRange(1, 4));

        sheet.TryGetArrayExtent(new CellAddress(sheet.Id, 4, 2), out var verticalOwner, out var verticalRows, out var verticalCols)
            .Should().BeTrue();
        verticalOwner.Should().Be(verticalAnchor);
        verticalRows.Should().Be(3);
        verticalCols.Should().Be(1);

        sheet.TryGetArrayExtent(new CellAddress(sheet.Id, 20, 11), out var horizontalOwner, out var horizontalRows, out var horizontalCols)
            .Should().BeTrue();
        horizontalOwner.Should().Be(horizontalAnchor);
        horizontalRows.Should().Be(1);
        horizontalCols.Should().Be(4);
    }

    [Fact]
    public void TryGetArrayExtent_RefreshesMemberOwnershipWhenAnchorRespills()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var anchor = new CellAddress(sheet.Id, 5, 5);
        var oldMember = new CellAddress(sheet.Id, 7, 5);
        var newMember = new CellAddress(sheet.Id, 5, 7);

        sheet.SetSpillRange(anchor, CreateNumberRange(3, 1));
        sheet.SetSpillRange(anchor, CreateNumberRange(1, 3));

        sheet.TryGetArrayExtent(oldMember, out _, out _, out _).Should().BeFalse();
        sheet.TryGetArrayExtent(newMember, out var owner, out var rows, out var cols).Should().BeTrue();
        owner.Should().Be(anchor);
        rows.Should().Be(1);
        cols.Should().Be(3);
    }

    [Fact]
    public void Clone_PreservesDirectSpillMemberOwnership()
    {
        var source = new Sheet(SheetId.New(), "S");
        var sourceAnchor = new CellAddress(source.Id, 3, 4);
        source.SetSpillRange(sourceAnchor, CreateNumberRange(2, 2));

        var clone = source.Clone(SheetId.New(), "Copy");
        var cloneMember = new CellAddress(clone.Id, 4, 5);

        clone.TryGetArrayExtent(cloneMember, out var owner, out var rows, out var cols).Should().BeTrue();
        owner.Should().Be(new CellAddress(clone.Id, 3, 4));
        rows.Should().Be(2);
        cols.Should().Be(2);
        clone.GetValue(cloneMember).Should().Be(new NumberValue(4));
    }

    private static RangeValue CreateNumberRange(int rows, int cols)
    {
        var cells = new ScalarValue[rows, cols];
        var value = 1d;
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
                cells[row, col] = new NumberValue(value++);
        }

        return new RangeValue(cells);
    }
}
