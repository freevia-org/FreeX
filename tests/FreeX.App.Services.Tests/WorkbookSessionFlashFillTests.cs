using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionFlashFillTests
{
    [Fact]
    public void FlashFillSelectedRange_FillsFromPlannedRangeAndPreservesSelection()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SeedFirstNameData(sheet, "John Smith", "John", "Jane Doe", "Bob Brown");
        var selected = Address(sheet, 2, 2);
        var selectedRange = new GridRange(selected, selected);
        var session = CreateSession(workbook);
        session.SelectRange(selectedRange);

        var result = session.FlashFillSelectedRange();

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.AffectedCells.Should().Equal(Address(sheet, 2, 2), Address(sheet, 3, 2));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Jane"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Bob"));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(selected);
        session.SelectedRange.Should().Be(selectedRange);
    }

    [Fact]
    public void FlashFillSelectedRange_NoBlankCellsToFillReturnsNoMutation()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SeedFirstNameData(sheet, "John Smith", "John", "Jane Doe", "Bob Brown");
        SetText(sheet, 2, 2, "Jane");
        SetText(sheet, 3, 2, "Bob");
        var selected = Address(sheet, 2, 2);
        var selectedRange = new GridRange(selected, selected);
        var session = CreateSession(workbook);
        session.SelectRange(selectedRange);

        var result = session.FlashFillSelectedRange();

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.AffectedCells.Should().BeEmpty();
        sheet.GetValue(2, 2).Should().Be(new TextValue("Jane"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Bob"));
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.ActiveCell.Should().Be(selected);
        session.SelectedRange.Should().Be(selectedRange);
    }

    [Fact]
    public void FlashFillSelectedRange_PatternFailureDoesNotDirtyOrMoveSelection()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SetText(sheet, 1, 1, "Alice");
        SetText(sheet, 1, 2, "hello");
        SetText(sheet, 2, 1, "Bob");
        SetText(sheet, 2, 2, "world");
        SetText(sheet, 3, 1, "Carol");
        var selected = Address(sheet, 3, 2);
        var selectedRange = new GridRange(selected, selected);
        var session = CreateSession(workbook);
        session.SelectRange(selectedRange);

        var result = session.FlashFillSelectedRange();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("pattern");
        result.AffectedCells.Should().BeEmpty();
        sheet.GetValue(3, 2).Should().BeOfType<BlankValue>();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.ActiveCell.Should().Be(selected);
        session.SelectedRange.Should().Be(selectedRange);
    }

    [Fact]
    public void FlashFillSelectedRange_WithoutExamplesReturnsFailureWithoutDirtying()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var selected = Address(sheet, 2, 2);
        var selectedRange = new GridRange(selected, selected);
        var session = CreateSession(workbook);
        session.SelectRange(selectedRange);

        var result = session.FlashFillSelectedRange();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No examples found");
        result.AffectedCells.Should().BeEmpty();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.ActiveCell.Should().Be(selected);
        session.SelectedRange.Should().Be(selectedRange);
    }

    [Fact]
    public void FlashFillSelectedRange_RejectsProtectedTargetsWithoutDirtying()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SeedFirstNameData(sheet, "John Smith", "John", "Jane Doe");
        sheet.IsProtected = true;
        var selected = Address(sheet, 2, 2);
        var selectedRange = new GridRange(selected, selected);
        var session = CreateSession(workbook);
        session.SelectRange(selectedRange);

        var result = session.FlashFillSelectedRange();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        result.AffectedCells.Should().BeEmpty();
        sheet.GetValue(2, 2).Should().BeOfType<BlankValue>();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.ActiveCell.Should().Be(selected);
        session.SelectedRange.Should().Be(selectedRange);
    }

    [Fact]
    public void FlashFillSelectedRange_UndoRedoRestoresFilledCells()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SeedFirstNameData(sheet, "John Smith", "John", "Jane Doe", "Bob Brown");
        var selected = Address(sheet, 2, 2);
        var session = CreateSession(workbook);
        session.SelectCell(selected);

        var apply = session.FlashFillSelectedRange();
        var undo = session.UndoLastEdit();
        var redo = session.RedoLastEdit();

        apply.Success.Should().BeTrue();
        undo.Success.Should().BeTrue();
        redo.Success.Should().BeTrue();
        sheet.GetValue(2, 2).Should().Be(new TextValue("Jane"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Bob"));

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetValue(2, 2).Should().BeOfType<BlankValue>();
        sheet.GetValue(3, 2).Should().BeOfType<BlankValue>();
        session.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void FlashFillSelectedRange_PropagatesAcrossGroupedVisibleSheetsOnly()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        SeedFirstNameData(summary, "John Smith", "John", "Jane Doe", "Bob Brown");
        SeedFirstNameData(details, "Grace Hopper", "Grace", "Alan Turing", "Katherine Johnson");
        SeedFirstNameData(hidden, "Hidden One", "Hidden", "Hidden Two", "Hidden Three");
        var selected = Address(summary, 2, 2);
        var selectedRange = new GridRange(selected, selected);
        var session = CreateSession(workbook);
        session.SelectAllVisibleSheets();
        session.SelectRange(selectedRange);

        var result = session.FlashFillSelectedRange();

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Contain(Address(summary, 2, 2));
        result.AffectedCells.Should().Contain(Address(summary, 3, 2));
        result.AffectedCells.Should().Contain(Address(details, 2, 2));
        result.AffectedCells.Should().Contain(Address(details, 3, 2));
        session.IsWorkbookGrouped.Should().BeTrue();
        session.SelectedRange.Should().Be(selectedRange);
        summary.GetValue(2, 2).Should().Be(new TextValue("Jane"));
        summary.GetValue(3, 2).Should().Be(new TextValue("Bob"));
        details.GetValue(2, 2).Should().Be(new TextValue("Alan"));
        details.GetValue(3, 2).Should().Be(new TextValue("Katherine"));
        hidden.GetValue(2, 2).Should().BeOfType<BlankValue>();
        hidden.GetValue(3, 2).Should().BeOfType<BlankValue>();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.GetValue(2, 2).Should().BeOfType<BlankValue>();
        details.GetValue(2, 2).Should().BeOfType<BlankValue>();
        hidden.GetValue(2, 2).Should().BeOfType<BlankValue>();
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }

    private static void SeedFirstNameData(Sheet sheet, string exampleSource, string exampleOutput, params string[] remainingSources)
    {
        SetText(sheet, 1, 1, exampleSource);
        SetText(sheet, 1, 2, exampleOutput);

        for (var index = 0; index < remainingSources.Length; index++)
            SetText(sheet, (uint)index + 2, 1, remainingSources[index]);
    }

    private static void SetText(Sheet sheet, uint row, uint column, string value) =>
        sheet.SetCell(Address(sheet, row, column), new TextValue(value));

    private static CellAddress Address(Sheet sheet, uint row, uint column) =>
        new(sheet.Id, row, column);
}
