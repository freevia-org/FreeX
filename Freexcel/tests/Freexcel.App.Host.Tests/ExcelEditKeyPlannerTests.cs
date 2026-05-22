using System.Windows.Input;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ExcelEditKeyPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();
    private static readonly CellAddress Current = new(SheetId, 10, 5);

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
    [InlineData(FreexcelEnterDirection.Right, ModifierKeys.None, 10, 6)]
    [InlineData(FreexcelEnterDirection.Right, ModifierKeys.Shift, 10, 4)]
    [InlineData(FreexcelEnterDirection.Up, ModifierKeys.None, 9, 5)]
    [InlineData(FreexcelEnterDirection.Left, ModifierKeys.None, 10, 4)]
    public void GetIntent_UsesConfiguredEnterDirection(
        FreexcelEnterDirection direction,
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
    public void GetIntent_MapsCtrlEnterToCommitSelection()
    {
        var intent = ExcelEditKeyPlanner.GetIntent(Key.Enter, ModifierKeys.Control, Current, pageSize: 20, allowFormulaBarNavigationKeys: false);

        intent.Action.Should().Be(ExcelEditKeyAction.CommitSelection);
        intent.Target.Should().BeNull();
    }
}
