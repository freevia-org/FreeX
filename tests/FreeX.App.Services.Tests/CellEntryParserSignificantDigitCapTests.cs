using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R24-number-precision-edge-3: a typed literal number must be capped to Excel's 15
/// significant-digit storage precision, matching DelimitedTextWorkbookReader's own cap for the
/// CSV-import path (RoundToSignificantDigits).
/// </summary>
public sealed class CellEntryParserSignificantDigitCapTests
{
    private static readonly CellAddress Anchor = new(SheetId.New(), 2, 2);

    [Fact]
    public void CreateCell_CapsATypedLiteralNumberToFifteenSignificantDigits()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("1.234567890123456789", Anchor, useR1C1ReferenceStyle: false);

        var value = cell.Value.Should().BeOfType<NumberValue>().Which.Value;

        // The raw IEEE-754 nearest double for this literal (1.2345678901234567) differs from
        // Excel's 15-significant-digit-capped storage value (1.23456789012346) at the 15th/16th
        // digit; the cell must store the capped value, not the uncapped raw double.
        value.Should().Be(1.23456789012346);
        value.Should().NotBe(double.Parse("1.234567890123456789"));
    }

    [Fact]
    public void CreateCell_LeavesAnAlreadyShortNumberUnaffectedByTheCap()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("12.5", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(12.5);
    }
}
