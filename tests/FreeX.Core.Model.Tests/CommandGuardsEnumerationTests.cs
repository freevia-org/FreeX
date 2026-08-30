using System.Collections;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class CommandGuardsEnumerationTests
{
    [Fact]
    public void RejectIfSplitsArray_EnumeratesSingleUseInputOnlyOnce()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(2,2)"));
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[,]
        {
            { new NumberValue(1), new NumberValue(2) },
            { new NumberValue(3), new NumberValue(4) },
        }));
        var addresses = new SingleUseAddresses(
        [
            anchor,
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 2, 2),
        ]);

        var outcome = CommandGuards.RejectIfSplitsArray(sheet, addresses);

        outcome.Should().BeNull();
        addresses.EnumerationCount.Should().Be(1);
    }

    private sealed class SingleUseAddresses(IReadOnlyList<CellAddress> values) : IEnumerable<CellAddress>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<CellAddress> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
                throw new InvalidOperationException("Sequence was enumerated more than once.");

            return values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
