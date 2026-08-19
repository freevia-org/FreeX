using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Editing;

/// <summary>
/// R146-insert-copied-cells-hyperlink-1: sibling regression coverage to
/// PasteCommandFactory_CrossWorkbookHyperlinkTests, but through the "Insert Copied Cells"/"Insert Cut
/// Cells" context-menu path (<see cref="InsertCopiedCellsPlanner.CreateCommand"/>) rather than plain
/// Ctrl+V. The r146 fix wave added an optional <c>sourceSheetOverride</c> parameter to
/// <see cref="PasteCommandFactory.CreateInternalPasteCommand"/> so a cross-window paste still resolves
/// the copied cell's source Sheet for hyperlink carry, and wired it into
/// <c>MainWindow.ExecutePaste</c>'s <c>CreatePasteCommand</c> local function. It missed the sibling
/// call site: <c>MainWindow.ExecuteInsertCopiedCells</c>'s own local <c>CreateCommand</c> function
/// (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs) already has <c>clip.SourceSheet</c> in hand
/// (the identical clip snapshot the fixed ExecutePaste path uses) but never forwarded it into
/// <see cref="InsertCopiedCellsPlanner.CreateCommand"/>, which itself had no parameter to accept one --
/// so copying a hyperlink-bearing cell in one FreeX window and using "Insert Copied Cells" in another
/// window silently dropped the hyperlink with Success=true, exactly like the original defect.
/// </summary>
public sealed class InsertCopiedCellsPlanner_CrossWorkbookHyperlinkTests
{
    [Fact]
    public void CreateCommand_CrossWorkbookWithSourceSheetOverride_CarriesHyperlink()
    {
        // Models the two-window scenario exactly: sheetA and sheetB belong to two INDEPENDENT
        // Workbook instances (as two open FreeX windows would each have their own), so sheetA.Id can
        // never be found by workbookB.GetSheet(...).
        var workbookA = new Workbook("Book1");
        var sheetA = workbookA.AddSheet("Sheet1");
        var sourceAddress = new CellAddress(sheetA.Id, 1, 1); // A1
        var sourceCell = Cell.FromValue(new TextValue("Click me"));
        sheetA.SetCell(sourceAddress, sourceCell);
        sheetA.Hyperlinks[sourceAddress] = "https://example.com/report";
        sheetA.HyperlinkMetadata[sourceAddress] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            ScreenTip: "Report");

        var workbookB = new Workbook("Book2");
        var sheetB = workbookB.AddSheet("SheetOne");
        var ctx = new TestCommandContext(workbookB);
        var destinationAddress = new CellAddress(sheetB.Id, 5, 5); // F5
        sheetB.SetCell(new CellAddress(sheetB.Id, 6, 5), Cell.FromValue(new TextValue("below")));

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbookB,
            sheetB.Id,
            new GridRange(sourceAddress, sourceAddress),
            [(sourceAddress, sourceCell.Clone())],
            new GridRange(destinationAddress, destinationAddress),
            KeyboardInsertDeleteDialogChoice.ShiftDown,
            isCut: false,
            sourceAreas: null,
            sourceSheetOverride: sheetA);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue("the shift+paste composite must still succeed");

        sheetB.GetValue(destinationAddress).Should().Be(new TextValue("Click me"));
        sheetB.Hyperlinks.Should().ContainKey(destinationAddress,
            "a cross-window Insert Copied Cells with the source Sheet supplied via sourceSheetOverride " +
            "must carry the copied cell's hyperlink, exactly like same-window Insert Copied Cells and " +
            "exactly like the already-fixed plain Ctrl+V cross-window paste");
        sheetB.Hyperlinks[destinationAddress].Should().Be("https://example.com/report");
        sheetB.HyperlinkMetadata.Should().ContainKey(destinationAddress);
        sheetB.HyperlinkMetadata[destinationAddress].ScreenTip.Should().Be("Report");
        // The shifted-down neighbor proves the insert half of the composite still ran normally.
        sheetB.GetValue(new CellAddress(sheetB.Id, 7, 5)).Should().Be(new TextValue("below"));
    }

    /// <summary>
    /// Sibling no-regression check: a caller that does NOT supply <c>sourceSheetOverride</c> (every
    /// pre-existing call site) must keep its exact prior behavior for a genuine cross-workbook lookup
    /// miss -- silently paste the value with no hyperlink and no error, proving the new parameter is
    /// an additive opt-in seam.
    /// </summary>
    [Fact]
    public void CreateCommand_CrossWorkbookWithoutSourceSheetOverride_StillDropsHyperlinkButPastesValue()
    {
        var workbookA = new Workbook("Book1");
        var sheetA = workbookA.AddSheet("Sheet1");
        var sourceAddress = new CellAddress(sheetA.Id, 1, 1);
        var sourceCell = Cell.FromValue(new TextValue("Click me"));
        sheetA.SetCell(sourceAddress, sourceCell);
        sheetA.Hyperlinks[sourceAddress] = "https://example.com/report";

        var workbookB = new Workbook("Book2");
        var sheetB = workbookB.AddSheet("SheetOne");
        var ctx = new TestCommandContext(workbookB);
        var destinationAddress = new CellAddress(sheetB.Id, 5, 5);

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbookB,
            sheetB.Id,
            new GridRange(sourceAddress, sourceAddress),
            [(sourceAddress, sourceCell.Clone())],
            new GridRange(destinationAddress, destinationAddress),
            KeyboardInsertDeleteDialogChoice.ShiftDown);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        sheetB.GetValue(destinationAddress).Should().Be(new TextValue("Click me"));
        sheetB.Hyperlinks.Should().NotContainKey(destinationAddress,
            "without an explicit sourceSheetOverride this remains the pre-existing (unfixed) " +
            "cross-workbook lookup-miss behavior -- callers must opt in by supplying the source Sheet");
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
