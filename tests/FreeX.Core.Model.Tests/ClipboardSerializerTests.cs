using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class ClipboardSerializerTests
{
    [Fact]
    public void Serialize_WritesTabsRowsBlanksAndQuotedText()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [
                Cell(1, 1, "alpha"),
                Cell(1, 3, "bravo\tquoted"),
                Cell(2, 2, "charlie \"delta\""),
            ],
            [],
            []);

        var text = ClipboardSerializer.Serialize(
            viewport,
            new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 3)));

        text.Should().Be("alpha\t\t\"bravo\tquoted\"\r\n\t\"charlie \"\"delta\"\"\"\t");
    }

    [Fact]
    public void Deserialize_ReadsQuotedTabsRowsAndEscapedQuotes()
    {
        var rows = ClipboardSerializer.Deserialize("alpha\t\"bravo\tquoted\"\r\n\"charlie \"\"delta\"\"\"\t");

        rows.Should().BeEquivalentTo(
            [
                new[] { "alpha", "bravo\tquoted" },
                new[] { "charlie \"delta\"", "" },
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void Benchmark_SerializeDenseViewport_ReportsTimingAndAllocatedBytes()
    {
        const int rows = 250;
        const int cols = 80;
        const int steps = 8;

        var sheetId = SheetId.New();
        var cells = new List<DisplayCell>(rows * cols);
        for (uint row = 1; row <= rows; row++)
        {
            for (uint col = 1; col <= cols; col++)
            {
                var value = ((row + col) % 17) == 0
                    ? $"R{row} C{col} \"quoted\"\tfield"
                    : $"R{row}C{col}";
                cells.Add(Cell(row, col, value));
            }
        }

        var viewport = new ViewportModel(cells, [], []);
        var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, rows, cols));

        ClipboardSerializer.Serialize(viewport, range).Length.Should().BeGreaterThan(rows * cols);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var timings = new double[steps];
        var total = Stopwatch.StartNew();
        var checksum = 0;

        for (var i = 0; i < steps; i++)
        {
            var step = Stopwatch.StartNew();
            checksum += ClipboardSerializer.Serialize(viewport, range).Length;
            step.Stop();
            timings[i] = step.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        checksum.Should().BeGreaterThan(0);
        Console.WriteLine(
            "PERF CLIPBOARD_SERIALIZE_DENSE " +
            $"rows={rows} cols={cols} steps={steps} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} " +
            $"p95_ms={timings.OrderBy(x => x).ElementAt((int)Math.Ceiling(steps * 0.95) - 1):F2} " +
            $"max_ms={timings.Max():F2} " +
            $"allocated_bytes={allocatedBytes:N0}");
    }

    private static DisplayCell Cell(uint row, uint col, string text) =>
        new(row, col, new TextValue(text), text, null, StyleId.Default, null);
}
