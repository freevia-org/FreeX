using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class ExcelEditKeyPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();
    private static readonly CellAddress Current = new(SheetId, 10, 5);

    [Theory]
    [InlineData(Key.F4, ModifierKeys.None, Key.None, true)]
    [InlineData(Key.System, ModifierKeys.None, Key.F4, true)]
    [InlineData(Key.F4, ModifierKeys.Control, Key.None, false)]
    [InlineData(Key.F4, ModifierKeys.Shift, Key.None, false)]
    [InlineData(Key.F4, ModifierKeys.Alt, Key.None, false)]
    public void ShouldCycleFormulaReference_RequiresPlainF4(
        Key key,
        ModifierKeys modifiers,
        Key systemKey,
        bool expected)
    {
        ExcelEditKeyPlanner.ShouldCycleFormulaReference(key, modifiers, systemKey)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void ShouldCycleFormulaReference_DoesNotTreatSystemAltF4AsFormulaReferenceCycle()
    {
        ExcelEditKeyPlanner.ShouldCycleFormulaReference(Key.System, ModifierKeys.Alt, Key.F4)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(Key.Enter, ModifierKeys.None, 11, 5)]
    [InlineData(Key.Enter, ModifierKeys.Shift, 9, 5)]
    [InlineData(Key.Tab, ModifierKeys.None, 10, 6)]
    [InlineData(Key.Tab, ModifierKeys.Shift, 10, 4)]
    public void GetIntent_CommitsEntryAndMovesLikeExcel(Key key, ModifierKeys modifiers, uint expectedRow, uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(key, modifiers, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(FreeXEnterDirection.Right, ModifierKeys.None, 10, 6)]
    [InlineData(FreeXEnterDirection.Right, ModifierKeys.Shift, 10, 4)]
    [InlineData(FreeXEnterDirection.Up, ModifierKeys.None, 9, 5)]
    [InlineData(FreeXEnterDirection.Left, ModifierKeys.None, 10, 4)]
    public void GetIntent_UsesConfiguredEnterDirection(
        FreeXEnterDirection direction,
        ModifierKeys modifiers,
        uint expectedRow,
        uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            Key.Enter,
            modifiers,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            enterDirection: direction);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Fact]
    public void GetIntent_CanCommitEnterWithoutMovingSelection()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            Key.Enter,
            ModifierKeys.None,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            moveSelectionAfterEnter: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(Current);
    }

    [Theory]
    [InlineData(Key.Enter, ModifierKeys.Shift, 1, 1)]
    [InlineData(Key.Tab, ModifierKeys.Shift, 1, 1)]
    [InlineData(Key.Enter, ModifierKeys.None, CellAddress.MaxRow, CellAddress.MaxCol)]
    [InlineData(Key.Tab, ModifierKeys.None, CellAddress.MaxRow, CellAddress.MaxCol)]
    public void GetIntent_CommitMovementClampsAtWorksheetEdges(
        Key key,
        ModifierKeys modifiers,
        uint currentRow,
        uint currentCol)
    {
        var edgeCell = new CellAddress(SheetId, currentRow, currentCol);

        var intent = ExcelEditKeyPlanner.GetIntent(key, modifiers, edgeCell, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(edgeCell);
    }

    [Fact]
    public void GetIntent_DoesNotCommitInlineEditorOnPlainArrowKeys()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(Key.Left, ModifierKeys.None, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }

    [Theory]
    [InlineData(Key.Up, 9, 5)]
    [InlineData(Key.Down, 11, 5)]
    [InlineData(Key.Left, 10, 4)]
    [InlineData(Key.Right, 10, 6)]
    public void GetIntent_CommitsEmptyInlineEditorAndMovesOnPlainArrowKeys(Key key, uint expectedRow, uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            ModifierKeys.None,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            inlineEditorCommitsOnArrow: true);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(Key.Up, 9, 5)]
    [InlineData(Key.Down, 11, 5)]
    [InlineData(Key.Left, 10, 4)]
    [InlineData(Key.Right, 10, 6)]
    public void GetIntent_CommitsNonFormulaInlineEditorAndMovesOnPlainArrowKeys(Key key, uint expectedRow, uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            ModifierKeys.None,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            inlineEditorCommitsOnArrow: true);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Fact]
    public void GetIntent_LetsNonEmptyInlineEditorHandlePlainArrowKeys()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            Key.Right,
            ModifierKeys.None,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            inlineEditorCommitsOnArrow: false);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_LetsInlineEditorHandleShiftArrowTextSelection()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            Key.Right,
            ModifierKeys.Shift,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            inlineEditorCommitsOnArrow: true);

        intent.Action.Should().Be(ExcelEditKeyAction.None);
        intent.Target.Should().BeNull();
    }

    [Theory]
    [InlineData(Key.Up, 9, 5)]
    [InlineData(Key.Down, 11, 5)]
    [InlineData(Key.PageUp, 1, 5)]
    [InlineData(Key.PageDown, 19, 5)]
    public void GetIntent_AllowsFormulaBarNavigationKeys(Key key, uint expectedRow, uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(key, ModifierKeys.None, Current, pageSize: 9, allowFormulaBarNavigationKeys: true);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(Key.Up, 9, 5)]
    [InlineData(Key.Down, 11, 5)]
    [InlineData(Key.Left, 10, 4)]
    [InlineData(Key.Right, 10, 6)]
    [InlineData(Key.PageUp, 1, 5)]
    [InlineData(Key.PageDown, 19, 5)]
    public void GetIntent_SelectsFormulaReferenceRangeInsteadOfCommittingWhenFormulaRangeEntryIsActive(
        Key key,
        uint expectedRow,
        uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            ModifierKeys.None,
            Current,
            pageSize: 9,
            allowFormulaBarNavigationKeys: false,
            formulaRangeEntryActive: true);

        intent.Action.Should().Be(ExcelEditKeyAction.SelectFormulaReference);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(Key.Up, 1, 1)]
    [InlineData(Key.Left, 1, 1)]
    [InlineData(Key.PageUp, 1, 1)]
    [InlineData(Key.Down, CellAddress.MaxRow, CellAddress.MaxCol)]
    [InlineData(Key.Right, CellAddress.MaxRow, CellAddress.MaxCol)]
    [InlineData(Key.PageDown, CellAddress.MaxRow, CellAddress.MaxCol)]
    public void GetIntent_FormulaReferenceMovementClampsAtWorksheetEdges(
        Key key,
        uint currentRow,
        uint currentCol)
    {
        var edgeCell = new CellAddress(SheetId, currentRow, currentCol);

        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            ModifierKeys.None,
            edgeCell,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            formulaRangeEntryActive: true);

        intent.Action.Should().Be(ExcelEditKeyAction.SelectFormulaReference);
        intent.Target.Should().Be(edgeCell);
    }

    [Theory]
    [InlineData(Key.Enter, ModifierKeys.None, 11, 5)]
    [InlineData(Key.Tab, ModifierKeys.None, 10, 6)]
    public void GetIntent_StillCommitsEnterAndTabWhenFormulaRangeEntryIsActive(
        Key key,
        ModifierKeys modifiers,
        uint expectedRow,
        uint expectedCol)
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            key,
            modifiers,
            Current,
            pageSize: 9,
            allowFormulaBarNavigationKeys: false,
            formulaRangeEntryActive: true);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitAndMove);
        intent.Target.Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Fact]
    public void GetIntent_MapsAltEnterToLineBreakInsertion()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(Key.Enter, ModifierKeys.Alt, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.InsertLineBreak);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_MapsSystemAltEnterToLineBreakInsertion()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            Key.System,
            ModifierKeys.Alt,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            systemKey: Key.Enter);

        intent.Action.Should().Be(ExcelEditKeyAction.InsertLineBreak);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_MapsCtrlEnterToCommitSelection()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(Key.Enter, ModifierKeys.Control, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitSelection);
        intent.Target.Should().BeNull();
    }

    [Fact]
    public void GetIntent_MapsSystemCtrlEnterToCommitSelection()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(
            Key.System,
            ModifierKeys.Control,
            Current,
            pageSize: 20,
            allowFormulaBarNavigationKeys: false,
            systemKey: Key.Enter);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitSelection);
        intent.Target.Should().BeNull();
    }
}
