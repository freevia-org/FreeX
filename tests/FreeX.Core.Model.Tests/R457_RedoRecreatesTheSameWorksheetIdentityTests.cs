using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r457: redoing "PivotTable on a new worksheet" must recreate that worksheet with the SAME id.
///
/// <para>Found by extending the r417 driver to check REDO. Undo is only half of what a user relies
/// on -- Ctrl+Y after Ctrl+Z must put back exactly what Ctrl+Z removed -- and nothing tested the
/// second half, across any of the three apps.</para>
///
/// <para><c>Workbook.AddSheet</c> mints a brand-new <see cref="SheetId"/> every call, so redo gave the
/// pivot's worksheet a different identity than the first Apply produced. Anything holding the
/// original id -- a later command on the redo stack, a formula, a chart's source, a defined name --
/// then refers to a sheet that no longer exists.</para>
///
/// <para><see cref="AddSheetCommand"/> already solved this: it caches its id and re-creates the sheet
/// with it, under a comment naming R16 and the exact consequence. This command creates a worksheet
/// the same way and never got the same treatment -- the "fixed once, siblings left" shape this review
/// keeps meeting (r438, r441, r451, r455).</para>
/// </summary>
public sealed class R457_RedoRecreatesTheSameWorksheetIdentityTests
{
    private static (Workbook Workbook, TestCommandContext Context) Setup()
    {
        var workbook = new Workbook("redo");
        var source = workbook.AddSheet("Data");

        source.SetCell(new CellAddress(source.Id, 1, 1), new TextValue("Region"));
        source.SetCell(new CellAddress(source.Id, 1, 2), new TextValue("Amount"));
        for (uint row = 2; row <= 5; row++)
        {
            source.SetCell(new CellAddress(source.Id, row, 1), new TextValue(row % 2 == 0 ? "North" : "South"));
            source.SetCell(new CellAddress(source.Id, row, 2), new NumberValue(row * 10));
        }

        return (workbook, new TestCommandContext(workbook));
    }

    private static AddPivotTableToNewWorksheetCommand Command(Workbook workbook) =>
        new(
            GridRange.Parse("A1:B5", workbook.Sheets[0].Id),
            "Pivot1",
            [0],
            [1]);

    [Fact]
    public void RedoGivesTheWorksheetBackItsOriginalId()
    {
        var (workbook, context) = Setup();
        var command = Command(workbook);

        command.Apply(context).Success.Should().BeTrue("the fixture must actually create the pivot sheet");
        var originalId = command.CreatedSheetId;
        originalId.Should().NotBeNull();

        command.Revert(context);
        command.Apply(context);

        command.CreatedSheetId.Should().Be(
            originalId,
            "a redone sheet with a new identity orphans every reference to the original -- later " +
            "redo-stack commands, formulas, chart sources and scoped names all resolve by id");
    }

    [Fact]
    public void TheRedoneSheetIsActuallyInTheWorkbookUnderThatId()
    {
        // The id being remembered is not enough: the workbook must really contain a sheet with it,
        // or references would resolve to nothing just the same.
        var (workbook, context) = Setup();
        var command = Command(workbook);

        command.Apply(context);
        var originalId = command.CreatedSheetId!.Value;

        command.Revert(context);
        workbook.Sheets.Should().NotContain(sheet => sheet.Id == originalId, "undo removed it");

        command.Apply(context);

        workbook.Sheets.Should().Contain(sheet => sheet.Id == originalId, "and redo put it back");
    }

    [Fact]
    public void UndoStillRemovesTheWorksheetEntirely()
    {
        // Keeping the id across undo must not keep the SHEET: the point of undo is that the pivot
        // and its worksheet are gone.
        var (workbook, context) = Setup();
        var sheetCountBefore = workbook.Sheets.Count;
        var command = Command(workbook);

        command.Apply(context);
        workbook.Sheets.Should().HaveCount(sheetCountBefore + 1);

        command.Revert(context);

        workbook.Sheets.Should().HaveCount(sheetCountBefore, "undo removes the worksheet it created");
        workbook.PivotCaches.Should().BeEmpty("along with the cache the pivot registered");
    }

    [Fact]
    public void ARedoneSheetCarriesThePivotAgain()
    {
        // Identity alone is not the contract -- the redone sheet must hold the pivot the user redid.
        var (workbook, context) = Setup();
        var command = Command(workbook);

        command.Apply(context);
        command.Revert(context);
        command.Apply(context);

        var created = workbook.Sheets.Single(sheet => sheet.Id == command.CreatedSheetId!.Value);
        created.PivotTables.Should().ContainSingle("redo restores the pivot, not just its worksheet");
    }

    [Fact]
    public void ItMatchesHowAddSheetCommandAlreadyBehaved()
    {
        // The finding was a divergence from a sibling, so this pins the consistency: both commands
        // that create a worksheet keep its identity across undo/redo.
        var (workbook, context) = Setup();
        var addSheet = new AddSheetCommand("Extra");

        addSheet.Apply(context);
        var firstId = workbook.Sheets.Single(sheet => sheet.Name == "Extra").Id;

        addSheet.Revert(context);
        addSheet.Apply(context);

        workbook.Sheets.Single(sheet => sheet.Name == "Extra").Id
            .Should().Be(firstId, "AddSheetCommand's R16 behaviour, which r457 brought to its sibling");
    }
}
