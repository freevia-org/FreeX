using System.Collections;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class CommandGuardsEnumerationTests
{
    [Fact]
    public void GroupedEditValidation_SkipsValidationEnumerationOnOrdinaryUnprotectedSheets()
    {
        var groupedEditSource = ModelSourceTestSupport.ReadCommandsSource("GroupedEditCellsCommand.cs");
        var guardsSource = ModelSourceTestSupport.ReadCommandsSource("CommandGuards.cs");

        groupedEditSource.Should().Contain("if (!requiresProtectionValidation && !requiresArraySplitValidation)");
        groupedEditSource.Should().Contain("new CellAddress[_sourceEdits.Count]");
        groupedEditSource.Should().NotContain("new List<CellAddress>(_sourceEdits.Count)");
        groupedEditSource.Should().NotContain("EnumerateRemappedAddresses");
        guardsSource.Should().Contain("internal static bool RequiresArraySplitValidation(Sheet sheet)");
        guardsSource.Should().Contain("if (!RequiresArraySplitValidation(sheet))");
    }

    [Fact]
    public void GroupedEditValidation_VisitsOrdinaryEditsOnlyDuringApplication()
    {
        var workbook = new Workbook("Book");
        var sourceSheet = workbook.AddSheet("Source");
        var groupedSheet = workbook.AddSheet("Grouped");
        var sourceAddress = new CellAddress(sourceSheet.Id, 1, 1);
        var edits = new CountingReadOnlyList<(CellAddress Address, Cell NewCell)>(
        [
            (sourceAddress, Cell.FromValue(new NumberValue(42))),
        ]);
        var command = new GroupedEditCellsCommand(
            [sourceSheet.Id, groupedSheet.Id],
            sourceSheet.Id,
            edits);

        var outcome = command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        edits.IndexerAccessCount.Should().Be(2,
            "each ordinary target should visit its edit once for application and never for validation");
        edits.EnumerationCount.Should().Be(0,
            "the indexed command path should not allocate an interface enumerator");
    }

    [Fact]
    public void RejectIfSplitsArray_DoesNotEnumerateInputWhenSheetHasNoArrayLikeRanges()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");

        var outcome = CommandGuards.RejectIfSplitsArray(sheet, new ThrowingAddresses());

        outcome.Should().BeNull();
    }

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

    private sealed class CountingReadOnlyList<T>(IReadOnlyList<T> values) : IReadOnlyList<T>
    {
        public int Count => values.Count;
        public int EnumerationCount { get; private set; }
        public int IndexerAccessCount { get; private set; }
        public T this[int index]
        {
            get
            {
                IndexerAccessCount++;
                return values[index];
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            return values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ThrowingAddresses : IEnumerable<CellAddress>
    {
        public IEnumerator<CellAddress> GetEnumerator() =>
            throw new InvalidOperationException("Ordinary sheets must not enumerate addresses.");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
