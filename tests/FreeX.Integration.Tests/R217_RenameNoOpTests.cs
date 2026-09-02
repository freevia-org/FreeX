using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r217: the rename family, the first commands paid down from the population r217 made visible.
/// Every rename surface in FreeX edits in place with the current name pre-filled -- double-click a
/// sheet tab, Table Design &gt; Table Name, the PivotTable name box, the Selection pane's label --
/// so pressing Enter without typing is an ordinary gesture, not a contrived one.
/// <para>
/// Each of these did real work for that gesture and then pushed an undo entry, and the push clears
/// redo. Two did more than waste an entry: the sheet rename is
/// <c>IWholeWorkbookRecalcCommand</c> and rewrote every formula in the workbook through a
/// <c>RenameSheetOp</c> whose halves were identical, and the Selection-pane rename cleared
/// <c>IsSourceLoaded</c>, throwing away a loaded object's original anchor XML for a name that never
/// changed.
/// </para>
/// <para>
/// The case-only tests are the other half of the contract. Every guard here is ordinal, because
/// "Sheet1" -&gt; "sheet1" IS a rename; a case-insensitive guard would have suppressed a real edit,
/// which is the more dangerous direction to be wrong in.
/// </para>
/// </summary>
public sealed class R217_RenameNoOpTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    [Fact]
    public void RenamingASheetToItsOwnName_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();

        new RenameSheetCommand(sheet.Id, sheet.Name).Apply(ctx)
            .IsNoOp.Should().BeTrue("the tab edits in place with the current name selected");
    }

    [Fact]
    public void RenamingASheetToItsOwnName_LeavesEveryFormulaAlone()
    {
        var (workbook, sheet, ctx) = Fixture();
        var other = workbook.AddSheet("Report");
        other.SetFormula(new CellAddress(other.Id, 1, 1), "=Data!A1");

        new RenameSheetCommand(sheet.Id, "Data").Apply(ctx);

        other.GetCell(1, 1)!.FormulaText.Should().Be("=Data!A1");
        sheet.Name.Should().Be("Data");
    }

    [Fact]
    public void ChangingOnlyTheCaseOfASheetName_IsARealRename()
    {
        var (_, sheet, ctx) = Fixture();

        var outcome = new RenameSheetCommand(sheet.Id, "data").Apply(ctx);

        outcome.IsNoOp.Should().BeFalse("a case-only rename is a rename");
        sheet.Name.Should().Be("data");
    }

    [Fact]
    public void RenamingASheetToADifferentName_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();

        var outcome = new RenameSheetCommand(sheet.Id, "Figures").Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.Name.Should().Be("Figures");
    }

    [Fact]
    public void RenamingAStructuredTableToItsOwnName_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var table = AddTable(sheet, "Sales");

        new RenameStructuredTableCommand(sheet.Id, table.Id, "Sales").Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void RenamingAStructuredTableToItsOwnNameWithSurroundingSpace_ReportsNoOp()
    {
        // The command trims before writing, so " Sales " and "Sales" are the same request. Comparing
        // against the raw text would have called this a change and done the whole rewrite.
        var (_, sheet, ctx) = Fixture();
        var table = AddTable(sheet, "Sales");

        new RenameStructuredTableCommand(sheet.Id, table.Id, "  Sales  ").Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void RenamingAStructuredTableToADifferentName_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var table = AddTable(sheet, "Sales");

        var outcome = new RenameStructuredTableCommand(sheet.Id, table.Id, "Revenue").Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.StructuredTables[0].Name.Should().Be("Revenue");
    }

    [Fact]
    public void RenamingASelectionPaneObjectToItsOwnName_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var picture = NamedPicture(sheet, "Logo");

        new RenameSelectionPaneObjectCommand(
                sheet.Id, SelectionPaneObjectKind.Picture, picture.Id, "Logo")
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void RenamingASelectionPaneObjectToItsOwnName_KeepsItsLoadedAnchor()
    {
        // The point of this one. Clearing IsSourceLoaded is correct for a real rename -- the writer
        // has to regenerate the anchor under the new name -- but for a name that did not change it
        // discards the original anchor XML of a loaded object for nothing.
        var (_, sheet, ctx) = Fixture();
        var picture = NamedPicture(sheet, "Logo");
        picture.IsSourceLoaded = true;

        new RenameSelectionPaneObjectCommand(
                sheet.Id, SelectionPaneObjectKind.Picture, picture.Id, "Logo").Apply(ctx);

        picture.IsSourceLoaded.Should().BeTrue();
    }

    [Fact]
    public void RenamingASelectionPaneObjectForReal_StillClearsItsLoadedAnchor()
    {
        var (_, sheet, ctx) = Fixture();
        var picture = NamedPicture(sheet, "Logo");
        picture.IsSourceLoaded = true;

        var outcome = new RenameSelectionPaneObjectCommand(
            sheet.Id, SelectionPaneObjectKind.Picture, picture.Id, "Banner").Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        picture.Name.Should().Be("Banner");
        picture.IsSourceLoaded.Should().BeFalse(
            "R124's reason for clearing it is untouched for a rename that renames something");
    }

    private static StructuredTableModel AddTable(Sheet sheet, string name)
    {
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = name,
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
        };
        sheet.StructuredTables.Add(table);
        return table;
    }

    private static PictureModel NamedPicture(Sheet sheet, string name)
    {
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Name = name,
        };
        sheet.Pictures.Add(picture);
        return picture;
    }
}
