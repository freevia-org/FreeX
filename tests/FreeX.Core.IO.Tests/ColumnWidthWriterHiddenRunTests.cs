using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

// R39-io-worksheet-dimension-cols-2-1: XlsxWorksheetColumnWidthWriter expanded each <col> run to
// per-column entries, but then clamped the run's max to whichever explicitly-widthed model column was
// widest (`Math.Min(max, Math.Max(min, maxModelColumn))`), with no regard for whether the run itself
// carried real hidden/outlineLevel/collapsed state. A hidden+grouped run whose min lay at or before the
// widest modelled column (e.g. cols 5-10 hidden, only col 3 has an explicit width) was silently
// truncated to a single column (5), losing the hidden/outline state on columns 6-10. The fix skips the
// cap for any run that has meaningful attributes (hidden/collapsed/outlineLevel), preserving its full
// min..max span regardless of where modelled widths fall.
public sealed class ColumnWidthWriterHiddenRunTests
{
    [Fact]
    public void HiddenOutlinedColumnRun_BeforeWidenedColumn_PreservesFullSpanNotJustFirstColumn()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // A hidden+grouped run spanning columns 5-10.
        for (uint c = 5; c <= 10; c++)
        {
            sheet.HiddenCols.Add(c);
            sheet.ColOutlineLevels[c] = 1;
        }

        // An explicitly-widthed column (3) whose position is before the hidden run's min (5) --
        // this is exactly the condition that previously drove maxModelColumn (3) below the run's
        // min (5), clamping the run's max down to 5 and destroying columns 6-10's state.
        sheet.ColumnWidths[3] = 25.0;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(wb, ms);
        ms.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(ms);
        var reloadedSheet = reloaded.Sheets[0];

        for (uint c = 5; c <= 10; c++)
        {
            reloadedSheet.HiddenCols.Should().Contain(c, $"column {c} was part of the hidden run and must stay hidden");
            reloadedSheet.ColOutlineLevels.Should().ContainKey(c).WhoseValue.Should().Be(1, $"column {c}'s outline level must survive");
        }

        // The explicitly-widthed column must still round-trip its exact width alongside the run.
        reloadedSheet.ColumnWidths.TryGetValue(3, out var got).Should().BeTrue();
        got.Should().BeApproximately(25.0, 1e-6);
    }

    // Sibling no-regression case: the hidden/outlined run overlaps an explicitly-widthed column inside
    // its own span, rather than lying entirely before it. The full run must still expand, the interior
    // modelled width must apply exactly to its own column, and the rest of the run keeps its
    // hidden/outline state without picking up a spurious width.
    [Fact]
    public void HiddenOutlinedColumnRun_OverlappingWidenedColumn_KeepsFullSpanAndExactInteriorWidth()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        for (uint c = 5; c <= 10; c++)
        {
            sheet.HiddenCols.Add(c);
            sheet.ColOutlineLevels[c] = 1;
        }

        // Explicit width on a column inside the hidden run itself.
        sheet.ColumnWidths[7] = 14.5;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(wb, ms);
        ms.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(ms);
        var reloadedSheet = reloaded.Sheets[0];

        for (uint c = 5; c <= 10; c++)
        {
            reloadedSheet.HiddenCols.Should().Contain(c, $"column {c} was part of the hidden run and must stay hidden");
            reloadedSheet.ColOutlineLevels.Should().ContainKey(c).WhoseValue.Should().Be(1, $"column {c}'s outline level must survive");
        }

        reloadedSheet.ColumnWidths.TryGetValue(7, out var got).Should().BeTrue();
        got.Should().BeApproximately(14.5, 1e-6);
    }
}
