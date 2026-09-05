using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r438: undoing "Insert PivotTable" must put back the merged regions the pivot's first render
/// stripped from the target rectangle.
///
/// <para>Found by the r417 auto-driver once its value factory was widened to build
/// <c>AddPivotTableCommand</c> -- the only command in 228 whose Revert left the workbook visibly
/// changed. Rendering a pivot clears its target range through
/// <c>PivotTableRefreshService.ClearTargetRange</c>, which unconditionally drops every merge
/// overlapping that range; the undo path replays cell VALUES only, so the merge never came back.</para>
///
/// <para>This is the damage that still looks deliberate: after Undo the numbers and text are all
/// where the user left them, so the sheet reads as correctly restored -- while a merged report
/// heading has silently become four separate cells. <c>RefreshPivotTableCommand</c> (round154) and
/// <c>MovePivotTableCommand</c> (sweep92) had each already been fixed for their own paths. Creation
/// is the one command that necessarily lands on top of an existing layout, and so the one most
/// likely to meet a merged heading, and it was the one still holding the bug.</para>
/// </summary>
public sealed class R438_AddPivotTableUndoRestoresMergedRegionsTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Context) Setup()
    {
        var workbook = new Workbook("pivot-undo");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        for (uint row = 2; row <= 6; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(row % 2 == 0 ? "North" : "South"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static AddPivotTableCommand Command(Sheet sheet, string target) =>
        new(
            sheet.Id,
            GridRange.Parse("A1:B6", sheet.Id),
            GridRange.Parse(target, sheet.Id),
            "Pivot1",
            [0],
            [1]);

    [Fact]
    public void UndoingAnInsertedPivotBringsBackAMergedHeadingItCovered()
    {
        var (_, sheet, context) = Setup();
        var heading = GridRange.Parse("D2:E2", sheet.Id);
        sheet.AddMergedRegion(heading);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("Quarterly summary"));

        var command = Command(sheet, "D1:F8");
        command.Apply(context).Success.Should().BeTrue("the fixture must actually create a pivot");
        sheet.MergedRegions.Should().NotContain(heading, "rendering the pivot clears its target range");

        command.Revert(context);

        sheet.MergedRegions.Should().Contain(
            heading,
            "undo that leaves the heading split into separate cells has lost formatting the user " +
            "never asked to change, while the restored values make the sheet look correctly undone");
    }

    [Fact]
    public void TheRestoredMergeStillCarriesItsText()
    {
        // A merge re-added over cells whose values did not come back would be an empty box: the two
        // halves of the restore have to agree, so assert them together rather than trusting either.
        var (_, sheet, context) = Setup();
        sheet.AddMergedRegion(GridRange.Parse("D2:E2", sheet.Id));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("Quarterly summary"));

        var command = Command(sheet, "D1:F8");
        command.Apply(context);
        command.Revert(context);

        sheet.GetValue(2, 4).Should().BeOfType<TextValue>()
            .Which.Value.Should().Be("Quarterly summary", "the merge anchor's own text must return with it");
    }

    [Fact]
    public void AMergeOutsideTheTargetRectangleIsNeverTouched()
    {
        // The fix captures and re-adds; a capture scoped too widely, or a restore that replaced the
        // whole list, could resurrect a merge the user deleted elsewhere between apply and undo.
        var (_, sheet, context) = Setup();
        var elsewhere = GridRange.Parse("A9:B9", sheet.Id);
        sheet.AddMergedRegion(elsewhere);

        var command = Command(sheet, "D1:F8");
        command.Apply(context);
        sheet.MergedRegions.Should().Contain(elsewhere, "it never overlapped the pivot");

        command.Revert(context);

        sheet.MergedRegions.Should().ContainSingle("undo must not invent or duplicate merges")
            .Which.Should().Be(elsewhere);
    }

    [Fact]
    public void UndoAddsNoMergeToASheetThatHadNone()
    {
        // Every assertion above checks that something present is restored, so a Revert that merged
        // the target rectangle outright would satisfy them -- and would leave the user's data
        // welded together after a plain undo.
        var (_, sheet, context) = Setup();

        var command = Command(sheet, "D1:F8");
        command.Apply(context);
        command.Revert(context);

        sheet.MergedRegions.Should().BeEmpty("undo must return the sheet to having no merges at all");
    }
}
