using FluentAssertions;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.SlicerTimeline;

/// <summary>
/// Tests for <see cref="PivotFieldItemsReader.ReadItems"/>: distinct values, (blank) sentinel for
/// blank/whitespace cells, sort order (current-culture, case-insensitive), and culture-insensitive
/// deduplication.
/// </summary>
public sealed class PivotFieldItemsReaderTests
{
    // A passthrough formatter that returns the text value directly (mirrors the simplest caller).
    private static readonly Func<ScalarValue?, string> PassthroughFormatter = value => value switch
    {
        TextValue t => t.Value,
        _ => string.Empty,
    };

    [Fact]
    public void ReadItems_ReturnsDistinctOrderedItems_ExcludingHeaderRow()
    {
        var sheet = MakeSheet(out var pivot,
            headerRow: ["Region", "Sales"],
            dataRows:
            [
                ["West", "100"],
                ["East", "200"],
                ["West", "150"],   // duplicate — must be deduped
            ]);

        var items = PivotFieldItemsReader.ReadItems(sheet, pivot, sourceFieldIndex: 0, PassthroughFormatter);

        items.Should().Equal("East", "West");
    }

    [Fact]
    public void ReadItems_MapsBlankFormattedValue_ToBlankSentinel()
    {
        var sheet = MakeSheet(out var pivot,
            headerRow: ["Region"],
            dataRows:
            [
                ["North"],
                [""],          // blank cell (TextValue with empty string) → "(blank)"
                ["South"],
            ]);

        var items = PivotFieldItemsReader.ReadItems(sheet, pivot, sourceFieldIndex: 0, PassthroughFormatter);

        items.Should().Contain("(blank)");
        items.Should().Contain("North");
        items.Should().Contain("South");
        items.Should().HaveCount(3);
    }

    [Fact]
    public void ReadItems_WhitespaceOnlyFormattedValue_MapsToBlankSentinel()
    {
        // If the caller's formatter returns whitespace-only, ReadItems maps it to "(blank)".
        var whitespaceFormatter = (ScalarValue? _) => "   ";

        var sheet = MakeSheet(out var pivot,
            headerRow: ["X"],
            dataRows: [["anything"]]);

        var items = PivotFieldItemsReader.ReadItems(sheet, pivot, sourceFieldIndex: 0, whitespaceFormatter);

        items.Should().Equal("(blank)");
    }

    [Fact]
    public void ReadItems_SortIsCaseInsensitive()
    {
        // "banana" and "Banana" dedup to one item; "apple" < "Cherry" in case-insensitive order.
        var sheet = MakeSheet(out var pivot,
            headerRow: ["Fruit"],
            dataRows:
            [
                ["Cherry"],
                ["apple"],
                ["banana"],
                ["Banana"],   // duplicate (case-insensitive) — collapses to one item
            ]);

        var items = PivotFieldItemsReader.ReadItems(sheet, pivot, sourceFieldIndex: 0, PassthroughFormatter);

        // Exactly 3 unique items in case-insensitive alphabetical order.
        items.Should().HaveCount(3);
        items[0].Should().BeOneOf("apple", "Apple");
        items[1].Should().BeOneOf("banana", "Banana");
        items[2].Should().BeOneOf("Cherry", "cherry");
    }

    [Fact]
    public void ReadItems_EmptyDataRange_ReturnsEmpty()
    {
        var sheet = MakeSheet(out var pivot,
            headerRow: ["Region"],
            dataRows: []);

        var items = PivotFieldItemsReader.ReadItems(sheet, pivot, sourceFieldIndex: 0, PassthroughFormatter);

        items.Should().BeEmpty();
    }

    [Fact]
    public void ReadItems_NullFormatterResult_MapsToBlankSentinel()
    {
        // A formatter that returns null: GetValue may return null ScalarValue.
        // PassthroughFormatter returns "" for non-TextValue (e.g. null cell) → blank sentinel.
        var sheet = MakeSheet(out var pivot,
            headerRow: ["X"],
            dataRows: [["text"], [null!]]);  // null cell → formatter returns ""

        var items = PivotFieldItemsReader.ReadItems(sheet, pivot, sourceFieldIndex: 0, PassthroughFormatter);

        items.Should().Contain("(blank)");
        items.Should().Contain("text");
    }

    [Fact]
    public void ReadItems_BlankSentinelSortsFirst_InCaseInsensitiveOrder()
    {
        // "(blank)" starts with '(' which is ASCII 40, before letters, so it sorts first.
        var sheet = MakeSheet(out var pivot,
            headerRow: ["Col"],
            dataRows:
            [
                ["Zebra"],
                [""],
                ["Apple"],
            ]);

        var items = PivotFieldItemsReader.ReadItems(sheet, pivot, sourceFieldIndex: 0, PassthroughFormatter);

        items[0].Should().Be("(blank)");
    }

    /// <summary>
    /// Builds a <see cref="Sheet"/> with a pivot table covering header + data rows, all in column A.
    /// Null entries in <paramref name="dataRows"/> are left as blank cells.
    /// </summary>
    private static Sheet MakeSheet(
        out PivotTableModel pivot,
        string[] headerRow,
        string?[][] dataRows)
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Data");

        // Write header row (row 1).
        for (var c = 0; c < headerRow.Length; c++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(c + 1)), new TextValue(headerRow[c]));

        // Write data rows (row 2 onwards).
        for (var r = 0; r < dataRows.Length; r++)
        {
            var row = dataRows[r];
            for (var c = 0; c < row.Length; c++)
            {
                if (row[c] is not null)
                    sheet.SetCell(new CellAddress(sheet.Id, (uint)(r + 2), (uint)(c + 1)), new TextValue(row[c]!));
                // null → leave cell blank (no SetCell call)
            }
        }

        var totalRows = (uint)(1 + dataRows.Length);
        var totalCols = (uint)headerRow.Length;

        pivot = new PivotTableModel
        {
            Name = "Pivot1",
            SourceRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, totalRows, totalCols)),
        };

        return sheet;
    }
}
