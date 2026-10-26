using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r237: Fill Down over cells that already hold what the fill would write. r229 put this command on
/// the debt list because its target set is never empty, so the post-hoc "did we write anything" test
/// could not decide it; r234 built the cell comparison; r236 found that insufficient because this
/// command also writes hyperlinks, rich text, phonetic guides and comments.
/// <para>
/// The decision now walks all five of the snapshots it keeps for undo -- which is complete by
/// construction, because those snapshots are the record of what it writes. The companion cases below
/// are the ones a cell-only comparison would have got wrong.
/// </para>
/// </summary>
public sealed class R237_FillCellsNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static GridRange Column(Sheet sheet, uint fromRow, uint toRow) =>
        new(new CellAddress(sheet.Id, fromRow, 1), new CellAddress(sheet.Id, toRow, 1));

    [Fact]
    public void FillingDownOverCellsThatAlreadyMatch_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue("same"));

        new FillCellsCommand(sheet.Id, Column(sheet, 1, 3), FillCellsDirection.Down).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void FillingDownOverDifferentValues_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("source"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("other"));

        var outcome = new FillCellsCommand(sheet.Id, Column(sheet, 1, 2), FillCellsDirection.Down)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.GetValue(2, 1).Should().Be(new TextValue("source"));
    }

    [Fact]
    public void FillingDownWhenOnlyAHyperlinkDiffers_IsARealEdit()
    {
        // The case a cell-only comparison gets wrong, and the reason r236 refused to build one. The
        // values match; the target carries a link the source does not, and the fill removes it.
        var (sheet, ctx) = Fixture();
        for (uint row = 1; row <= 2; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue("same"));
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.Hyperlinks[target] = "https://example.invalid";

        var outcome = new FillCellsCommand(sheet.Id, Column(sheet, 1, 2), FillCellsDirection.Down)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.Hyperlinks.Should().NotContainKey(target);
    }

    [Fact]
    public void FillingDownWhenOnlyANoteDiffers_IsARealEdit()
    {
        // Same argument for the comment snapshot, which is the one CellEditCompanionSnapshot does
        // not carry -- so even r236's proposed remedy would have missed this without the extra list.
        var (sheet, ctx) = Fixture();
        for (uint row = 1; row <= 2; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue("same"));
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.Comments[target] = "a note";

        var outcome = new FillCellsCommand(sheet.Id, Column(sheet, 1, 2), FillCellsDirection.Down)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
    }
}
