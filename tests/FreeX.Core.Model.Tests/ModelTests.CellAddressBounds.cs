using FreeX.Core.Model;
using FluentAssertions;
using System.Diagnostics;

namespace FreeX.Core.Model.Tests;

public partial class CellAddressBoundsTests
{
    [Theory]
    [InlineData(1u, 1u)]    // "A"
    [InlineData(26u, 1u)]   // "Z"
    [InlineData(27u, 2u)]   // "AA"
    [InlineData(702u, 2u)]  // "ZZ"
    [InlineData(703u, 3u)]  // "AAA"
    [InlineData(16384u, 3u)] // "XFD"
    public void GetColumnNameLength_ReturnsCorrectLength(uint col, uint expectedLength)
    {
        CellAddress.GetColumnNameLength(col).Should().Be(expectedLength);
    }

    [Theory]
    [InlineData(1u, "A")]
    [InlineData(26u, "Z")]
    [InlineData(27u, "AA")]
    [InlineData(16384u, "XFD")]
    public void WriteColumnName_WritesCorrectChars(uint col, string expected)
    {
        Span<char> buffer = stackalloc char[(int)CellAddress.GetColumnNameLength(col)];
        CellAddress.WriteColumnName(col, buffer);
        new string(buffer).Should().Be(expected);
    }

    [Theory]
    [InlineData(1u, 1u)]
    [InlineData(9u, 1u)]
    [InlineData(10u, 2u)]
    [InlineData(999999u, 6u)]
    [InlineData(1048576u, 7u)]
    public void GetRowDigitCount_ReturnsCorrectCount(uint row, uint expectedCount)
    {
        CellAddress.GetRowDigitCount(row).Should().Be(expectedCount);
    }


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

    [Theory]
    [InlineData("AAAAAAA1")]   // 7-letter column, within uint but beyond MaxCol
    [InlineData("ZZZZZZZ1")]   // 7-letter column, would overflow uint in old checked arithmetic
    [InlineData("ZZZZZZZZ1")]  // 8-letter column, guaranteed overflow in old code
    public void TryParse_OverlongColumnName_ReturnsFalseNotThrow(string input)
    {
        // Regression: the old hand-rolled parsers in XlsxFileAdapter.SourcePackageSnapshot used
        // checked((col * 26) + ...) which threw OverflowException on malformed 7+-letter column
        // references in a crafted XLSX. CellAddress.TryParse must return false gracefully.
        var act = () => CellAddress.TryParse(input, SheetId.New(), out _);
        act.Should().NotThrow<OverflowException>("parsers must never throw on malformed input");
        CellAddress.TryParse(input, SheetId.New(), out _).Should().BeFalse("column exceeds MaxCol");
    }
}
