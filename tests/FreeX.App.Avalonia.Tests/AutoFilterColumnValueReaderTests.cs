using System;
using System.Linq;

using FluentAssertions;

using FreeX.App.Avalonia;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the UI-free <see cref="AutoFilterColumnValueReader"/>: the canonical filter-text mapping
/// (which must mirror Core's FilterValueFormatter so checklist values match what the filter hides) and the
/// distinct data-row values of a column. No running shell required.
/// </summary>
public sealed class AutoFilterColumnValueReaderTests
{
    private static Sheet CreateSheet() => new Workbook("Book").AddSheet("Sheet1");

    [Fact]
    public void ToFilterText_MatchesCanonicalFormatting()
    {
        AutoFilterColumnValueReader.ToFilterText(new TextValue("Hi")).Should().Be("Hi");
        AutoFilterColumnValueReader.ToFilterText(new NumberValue(10)).Should().Be("10");
        AutoFilterColumnValueReader.ToFilterText(new BoolValue(true)).Should().Be("TRUE");
        AutoFilterColumnValueReader.ToFilterText(new BoolValue(false)).Should().Be("FALSE");
        AutoFilterColumnValueReader.ToFilterText(DateTimeValue.FromDateTime(new DateTime(2026, 1, 2)))
            .Should().Be("2026-01-02");
        AutoFilterColumnValueReader.ToFilterText(BlankValue.Instance).Should().Be("");
    }

    [Fact]
    public void ToFilterText_AgreesWith_CoreFilterValueFormatter()
    {
        // The Avalonia checklist reader must produce exactly the canonical text Core's FilterCommand
        // matches against — they now share the single source of truth, so every value type must agree.
        ScalarValue[] values =
        [
            new TextValue("Hi"),
            new TextValue(""),
            new NumberValue(10),
            new NumberValue(1234.5),
            new BoolValue(true),
            new BoolValue(false),
            DateTimeValue.FromDateTime(new DateTime(2026, 1, 2)),
            BlankValue.Instance,
            new ErrorValue("#DIV/0!"),
        ];

        foreach (var value in values)
        {
            AutoFilterColumnValueReader.ToFilterText(value)
                .Should().Be(FilterValueFormatter.ToText(value));
        }
    }

    [Fact]
    public void DistinctColumnValues_ExcludesHeader_AndDeduplicates()
    {
        var sheet = CreateSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));

        var values = AutoFilterColumnValueReader.DistinctColumnValues(sheet, range, columnOffset: 0);

        // Header "Region" excluded; "West" deduplicated; first-seen order.
        values.Should().Equal("West", "East");
    }

    [Fact]
    public void DistinctColumnValues_HonorsColumnOffset_AndFormatsNumbers()
    {
        var sheet = CreateSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(10));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));

        var values = AutoFilterColumnValueReader.DistinctColumnValues(sheet, range, columnOffset: 1);

        values.Should().Equal("20", "10");
    }
}
