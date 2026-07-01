using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotDataSourcePlannerTests
{
    private static readonly SheetId Sheet = new(Guid.NewGuid());

    private static GridRange Range(uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(Sheet, r1, c1), new CellAddress(Sheet, r2, c2));

    [Fact]
    public void FormatSourceRange_FormatsRectangleAndCollapsesSingleCell()
    {
        PivotDataSourcePlanner.FormatSourceRange(Range(1, 1, 5, 3)).Should().Be("A1:C5");
        PivotDataSourcePlanner.FormatSourceRange(Range(2, 2, 2, 2)).Should().Be("B2");
    }

    [Fact]
    public void Capture_ReadsTheCurrentSourceRange()
    {
        var pivot = new PivotTableModel { Name = "P", SourceRange = Range(1, 1, 4, 2) };
        PivotDataSourcePlanner.Capture(pivot).Should().Be("A1:B4");
    }

    [Fact]
    public void NormalizeReferenceText_TrimsDialogAndRequestText()
    {
        PivotDataSourcePlanner.NormalizeReferenceText("  Sheet1!A1:D10  ").Should().Be("Sheet1!A1:D10");
        PivotDataSourcePlanner.NormalizeReferenceText(null).Should().BeEmpty();
    }

    [Fact]
    public void TryCreateChange_RejectsEmptyReference()
    {
        var ok = PivotDataSourcePlanner.TryCreateChange("  ", Resolve(Range(1, 1, 5, 3)), out var change, out var error);
        ok.Should().BeFalse();
        change.Should().BeNull();
        error.Should().Be(PivotDataSourcePlanner.EmptyReferenceMessage);
    }

    [Fact]
    public void TryCreateChange_RejectsUnresolvableReference()
    {
        var ok = PivotDataSourcePlanner.TryCreateChange(
            "NotARange", FailResolve, out var change, out var error);
        ok.Should().BeFalse();
        change.Should().BeNull();
        error.Should().Be(PivotDataSourcePlanner.InvalidReferenceMessage);
    }

    [Fact]
    public void TryCreateChange_RejectsRangeWithoutHeaderAndDataRows()
    {
        var ok = PivotDataSourcePlanner.TryCreateChange(
            "A1:C1", Resolve(Range(1, 1, 1, 3)), out var change, out var error);
        ok.Should().BeFalse();
        change.Should().BeNull();
        error.Should().Be(PivotDataSourcePlanner.MissingHeadersMessage);
    }

    [Fact]
    public void TryCreateChange_ResolvesValidReferenceAndTrimsText()
    {
        var resolved = Range(1, 1, 10, 4);
        var ok = PivotDataSourcePlanner.TryCreateChange(
            "  Sheet1!A1:D10  ", Resolve(resolved), out var change, out var error);
        ok.Should().BeTrue();
        error.Should().BeNull();
        change!.SourceRange.Should().Be(resolved);
        change.SourceRangeText.Should().Be("Sheet1!A1:D10");
    }

    private static PivotDataSourcePlanner.ReferenceResolver Resolve(GridRange range) =>
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
