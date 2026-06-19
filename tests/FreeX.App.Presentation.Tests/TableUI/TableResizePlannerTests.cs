using FluentAssertions;
using FreeX.App.Presentation.TableUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.TableUI;

public sealed class TableResizePlannerTests
{
    private static readonly SheetId Sheet = new(Guid.NewGuid());
    private static readonly SheetId OtherSheet = new(Guid.NewGuid());

    private static GridRange Range(uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(Sheet, r1, c1), new CellAddress(Sheet, r2, c2));

    private static StructuredTableModel Table(GridRange range) =>
        new() { Id = 1, Name = "Table1", DisplayName = "Table1", Range = range };

    [Fact]
    public void FormatRange_FormatsRectangleAndCollapsesSingleCell()
    {
        TableResizePlanner.FormatRange(Range(1, 1, 5, 3)).Should().Be("A1:C5");
        TableResizePlanner.FormatRange(Range(2, 2, 2, 2)).Should().Be("B2");
    }

    [Fact]
    public void Capture_ReadsTheCurrentRange()
    {
        TableResizePlanner.Capture(Table(Range(1, 1, 4, 2))).Should().Be("A1:B4");
    }

    [Fact]
    public void TryCreateResize_RejectsEmptyReference()
    {
        var ok = TableResizePlanner.TryCreateResize(
            Table(Range(1, 1, 4, 2)), "  ", Resolve(Range(1, 1, 6, 2)), out var change, out var error);
        ok.Should().BeFalse();
        change.Should().BeNull();
        error.Should().Be(TableResizePlanner.EmptyReferenceMessage);
    }

    [Fact]
    public void TryCreateResize_RejectsUnresolvableReference()
    {
        var ok = TableResizePlanner.TryCreateResize(
            Table(Range(1, 1, 4, 2)), "NotARange", FailResolve, out var change, out var error);
        ok.Should().BeFalse();
        change.Should().BeNull();
        error.Should().Be(TableResizePlanner.InvalidReferenceMessage);
    }

    [Fact]
    public void TryCreateResize_RejectsMovedTopLeftCell()
    {
        var ok = TableResizePlanner.TryCreateResize(
            Table(Range(1, 1, 4, 2)), "B2:C6", Resolve(Range(2, 2, 6, 3)), out _, out var error);
        ok.Should().BeFalse();
        error.Should().Be(TableResizePlanner.MovedHeaderMessage);
    }

    [Fact]
    public void TryCreateResize_RejectsDifferentSheet()
    {
        var moved = new GridRange(new CellAddress(OtherSheet, 1, 1), new CellAddress(OtherSheet, 6, 2));
        var ok = TableResizePlanner.TryCreateResize(
            Table(Range(1, 1, 4, 2)), "Sheet2!A1:B6", Resolve(moved), out _, out var error);
        ok.Should().BeFalse();
        error.Should().Be(TableResizePlanner.DifferentSheetMessage);
    }

    [Fact]
    public void TryCreateResize_RejectsSingleRow()
    {
        var ok = TableResizePlanner.TryCreateResize(
            Table(Range(1, 1, 4, 2)), "A1:B1", Resolve(Range(1, 1, 1, 2)), out _, out var error);
        ok.Should().BeFalse();
        error.Should().Be(TableResizePlanner.TooFewRowsMessage);
    }

    [Fact]
    public void TryCreateResize_ResolvesValidRangeAndTrimsText()
    {
        var resolved = Range(1, 1, 12, 4);
        var ok = TableResizePlanner.TryCreateResize(
            Table(Range(1, 1, 4, 2)), "  A1:D12  ", Resolve(resolved), out var change, out var error);
        ok.Should().BeTrue();
        error.Should().BeNull();
        change!.NewRange.Should().Be(resolved);
        change.NewRangeText.Should().Be("A1:D12");
    }

    private static TableResizePlanner.ReferenceResolver Resolve(GridRange range) =>
        (string _, out GridRange r) =>
        {
            r = range;
            return true;
        };

    private static bool FailResolve(string reference, out GridRange range)
    {
        range = default;
        return false;
    }
}
