using FreeX.Core.Model;
using FluentAssertions;
using System.Diagnostics;

namespace FreeX.Core.Model.Tests;

public partial class CellAddressBoundsTests
{
    [Fact]
    public void CellAddress_Parse_ThrowsForRowZero()
    {
        var sheet = SheetId.New();
        Action act = () => CellAddress.Parse("A0", sheet);
        act.Should().Throw<FormatException>("row 0 is below the valid range");
    }

    [Fact]
    public void CellAddress_Parse_ThrowsForRowAboveMax()
    {
        var sheet = SheetId.New();
        Action act = () => CellAddress.Parse("A1048577", sheet);
        act.Should().Throw<FormatException>("row 1048577 exceeds MaxRow");
    }

    [Fact]
    public void CellAddress_Parse_ThrowsForColumnAboveMax()
    {
        var sheet = SheetId.New();
        // XFE is column 16385, one past the maximum XFD (16384)
        Action act = () => CellAddress.Parse("XFE1", sheet);
        act.Should().Throw<FormatException>("column XFE exceeds MaxCol");
    }

    [Fact]
    public void ColumnNameToNumber_SevenLetters_DoesNotOverflow()
    {
        // Seven-letter column names (e.g. ZZZZZZZ) would overflow uint without
        // the early-exit guard; the result must exceed MaxCol but not wrap.
        var result = CellAddress.ColumnNameToNumber("ZZZZZZZ");
        result.Should().BeGreaterThan(CellAddress.MaxCol, "long column names must return a value > MaxCol, not an overflow-wrapped one");
    }
}
