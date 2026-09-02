using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r224: the Remove Hyperlinks twin of r220's Clear Hyperlinks guard, in the command next door.
/// Both are reachable from the same right-click menu over a selection that carries no link.
/// </summary>
public sealed class R224_RemoveHyperlinksNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static GridRange Range(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));

    [Fact]
    public void RemovingHyperlinksFromASelectionThatHasNone_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new RemoveHyperlinksCommand(sheet.Id, Range(sheet)).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void RemovingHyperlinksOutsideTheOnlyLinkedCell_ReportsNoOpAndLeavesItAlone()
    {
        var (sheet, ctx) = Fixture();
        var far = new CellAddress(sheet.Id, 1, 9);
        sheet.Hyperlinks[far] = "https://example.invalid";

        new RemoveHyperlinksCommand(sheet.Id, Range(sheet)).Apply(ctx).IsNoOp.Should().BeTrue();

        sheet.Hyperlinks.Should().ContainKey(far);
    }

    [Fact]
    public void RemovingALinkThatIsThere_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.Hyperlinks[address] = "https://example.invalid";

        var outcome = new RemoveHyperlinksCommand(sheet.Id, Range(sheet)).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.Hyperlinks.Should().NotContainKey(address);
    }
}
