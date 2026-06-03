using FreeX.Core.Model;
using FluentAssertions;
using System.Diagnostics;

namespace FreeX.Core.Model.Tests;

[Collection("CellAddress performance")]
public partial class CellAddressTests
{
    [Fact]
    public void Parse_A1_ReturnsCorrectRowAndCol()
    {
        var sheet = SheetId.New();
        var addr = CellAddress.Parse("A1", sheet);
        addr.Row.Should().Be(1);
        addr.Col.Should().Be(1);
    }

    [Fact]
    public void Parse_B7_ReturnsCorrectRowAndCol()
    {
        var sheet = SheetId.New();
        var addr = CellAddress.Parse("B7", sheet);
        addr.Row.Should().Be(7);
        addr.Col.Should().Be(2);
    }

    [Fact]
    public void Parse_AA1_ReturnsColumn27()
    {
        var sheet = SheetId.New();
        var addr = CellAddress.Parse("AA1", sheet);
        addr.Col.Should().Be(27);
    }

    [Fact]
    public void Parse_LowercaseA1_ReturnsCorrectRowAndCol()
    {
        var sheet = SheetId.New();
        var addr = CellAddress.Parse("aa10", sheet);
        addr.Row.Should().Be(10);
        addr.Col.Should().Be(27);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("1")]
    [InlineData("A1B")]
    [InlineData("A999999999999999999999")]
    public void TryParse_InvalidA1Notation_ReturnsFalse(string input)
    {
        CellAddress.TryParse(input, SheetId.New(), out _).Should().BeFalse();
    }

    [Fact]
    public void ColumnNameToNumber_A_Returns1()
    {
        CellAddress.ColumnNameToNumber("A").Should().Be(1);
    }

    [Fact]
    public void ColumnNameToNumber_Z_Returns26()
    {
        CellAddress.ColumnNameToNumber("Z").Should().Be(26);
    }

    [Fact]
    public void ColumnNameToNumber_AA_Returns27()
    {
        CellAddress.ColumnNameToNumber("AA").Should().Be(27);
    }

    [Fact]
    public void ColumnNameToNumber_RepeatedLowercaseCalls_DoNotAllocate()
    {
        CellAddress.ColumnNameToNumber("xfd").Should().Be(CellAddress.MaxCol);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int repetitions = 100_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        uint result = 0;
        for (var i = 0; i < repetitions; i++)
            result = CellAddress.ColumnNameToNumber("xfd");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        result.Should().Be(CellAddress.MaxCol);
        Console.WriteLine(
            $"ColumnNameToNumber lowercase repeated {repetitions:N0}x: {allocated:N0} bytes allocated.");
        allocated.Should().BeLessThan(1_000);
    }

    [Fact]
    public void NumberToColumnName_RoundTrips()
    {
        for (uint i = 1; i <= 100; i++)
        {
            var name = CellAddress.NumberToColumnName(i);
            var number = CellAddress.ColumnNameToNumber(name);
            number.Should().Be(i);
        }
    }

    [Fact]
    public void NumberToColumnName_FormatsColumnsBeyondExcelBounds()
    {
        CellAddress.NumberToColumnName(18_279).Should().Be("AAAA");
    }

    [Fact]
    public void ToA1_FormatsCorrectly()
    {
        var sheet = SheetId.New();
        var addr = new CellAddress(sheet, 7, 2);
        addr.ToA1().Should().Be("B7");
    }

    [Theory]
    [InlineData(1u, 1u, "A1")]
    [InlineData(CellAddress.MaxRow, CellAddress.MaxCol, "XFD1048576")]
    public void ToA1_FormatsExcelBounds(uint row, uint col, string expected)
    {
        var sheet = SheetId.New();
        var addr = new CellAddress(sheet, row, col);

        addr.ToA1().Should().Be(expected);
    }

    [Fact]
    public void ToA1_FormatsRowsBeyondExcelBoundsWithoutTruncating()
    {
        var sheet = SheetId.New();
        var addr = new CellAddress(sheet, 1_000_000_000, 1);

        addr.ToA1().Should().Be("A1000000000");
    }

    [Fact]
    public void ToA1_RepeatedCalls_AllocateOnlyResultStrings()
    {
        var sheet = SheetId.New();
        var addr = new CellAddress(sheet, CellAddress.MaxRow, CellAddress.MaxCol);
        addr.ToA1().Should().Be("XFD1048576");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int repetitions = 100_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        string result = "";
        for (var i = 0; i < repetitions; i++)
            result = addr.ToA1();
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        result.Should().Be("XFD1048576");
        Console.WriteLine(
            $"ToA1 repeated {repetitions:N0}x: {stopwatch.Elapsed.TotalMilliseconds:F2} ms, {allocated:N0} bytes allocated.");
        allocated.Should().BeLessThan(6_500_000);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void NumberToColumnName_RepeatedCalls_ReusesCachedColumnName()
    {
        CellAddress.NumberToColumnName(CellAddress.MaxCol).Should().Be("XFD");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int repetitions = 100_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        string result = "";
        for (var i = 0; i < repetitions; i++)
            result = CellAddress.NumberToColumnName(CellAddress.MaxCol);
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        result.Should().Be("XFD");
        Console.WriteLine(
            $"NumberToColumnName cached repeated {repetitions:N0}x: {stopwatch.Elapsed.TotalMilliseconds:F2} ms, {allocated:N0} bytes allocated.");
        allocated.Should().BeLessThan(1_000);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void TryParse_RepeatedCalls_DoNotAllocateAndReportsTime()
    {
        var sheet = SheetId.New();
        CellAddress.TryParse("XFD1048576", sheet, out var warmup).Should().BeTrue();
        warmup.Should().Be(new CellAddress(sheet, CellAddress.MaxRow, CellAddress.MaxCol));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int repetitions = 100_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        CellAddress result = default;
        for (var i = 0; i < repetitions; i++)
        {
            if (!CellAddress.TryParse("XFD1048576", sheet, out result))
                throw new InvalidOperationException("Expected XFD1048576 to parse.");
        }
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        result.Should().Be(new CellAddress(sheet, CellAddress.MaxRow, CellAddress.MaxCol));
        Console.WriteLine(
            $"TryParse repeated {repetitions:N0}x: {stopwatch.Elapsed.TotalMilliseconds:F2} ms, {allocated:N0} bytes allocated.");
        allocated.Should().BeLessThan(1_000);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }
}

[CollectionDefinition("CellAddress performance", DisableParallelization = true)]
public sealed class CellAddressPerformanceCollection;
