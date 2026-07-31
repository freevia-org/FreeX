using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;
using FreeX.App.Presentation.InteractionValidation;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaWorksheetKeyboardEditingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void InteractiveValidationScenarioIds_AreStableInventoryEntries()
    {
        MainWindow.InteractiveValidationKeyboardShortcutScenarioIds.Should().BeEquivalentTo(
            InteractiveValidationInventory.KeyboardShortcuts.Select(scenario => scenario.Id));
        MainWindow.InteractiveValidationKeyboardShortcutScenarioIds.Should().OnlyContain(
            id => InteractiveValidationInventory.KeyboardShortcuts.Any(scenario => scenario.Id == id));
    }

    [Fact]
    public async Task FormulaBar_PointModeKeyboardNavigation_ReplacesAndExtendsReferences()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var formulaCell = new CellAddress(sheet.Id, 3, 3);
            window.Session.SelectCell(formulaCell);
            window.BeginFormulaEditForTest(formulaCell, "=");

            window.FormulaPointModeForTest.Should().BeTrue();

            var right = Press(Key.Right);
            window.RaiseFormulaBoxKeyDownForTest(right);
            right.Handled.Should().BeTrue();
            window.FormulaBoxTextForTest.Should().Be("=D3");

            var extendDown = Press(Key.Down, KeyModifiers.Shift);
            window.RaiseFormulaBoxKeyDownForTest(extendDown);
            extendDown.Handled.Should().BeTrue();
            window.FormulaBoxTextForTest.Should().Be("=D3:D4");

            var pageRows = Math.Max(1, window.Session.Viewport.RowMetrics.Count - 1);
            var replacePageDown = Press(Key.PageDown);
            window.RaiseFormulaBoxKeyDownForTest(replacePageDown);
            replacePageDown.Handled.Should().BeTrue();
            window.FormulaBoxTextForTest.Should().Be($"=D{4 + pageRows}");

            window.RaiseFormulaBoxKeyDownForTest(Press(Key.F2));
            window.FormulaPointModeForTest.Should().BeFalse();
            var textInEditMode = window.FormulaBoxTextForTest;
            var ignoredArrow = Press(Key.Left);
            window.RaiseFormulaBoxKeyDownForTest(ignoredArrow);
            ignoredArrow.Handled.Should().BeFalse();
            window.FormulaBoxTextForTest.Should().Be(textInEditMode);

            window.RaiseFormulaBoxKeyDownForTest(Press(Key.F2));
            window.FormulaPointModeForTest.Should().BeTrue();
            window.RaiseFormulaBoxKeyDownForTest(Press(Key.Escape));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaBar_PointModeReverseExtension_PreservesDirectionalAnchor()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var formulaCell = new CellAddress(sheet.Id, 1, 1);
            window.Session.SelectCell(formulaCell);
            window.BeginFormulaEditForTest(formulaCell, "=");

            window.RaiseFormulaBoxKeyDownForTest(Press(Key.Right));
            window.RaiseFormulaBoxKeyDownForTest(Press(Key.Down));
            var extendUp = Press(Key.Up, KeyModifiers.Shift);
            window.RaiseFormulaBoxKeyDownForTest(extendUp);

            extendUp.Handled.Should().BeTrue();
            window.FormulaBoxTextForTest.Should().Be("=B1:B2");
            window.Session.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 1, 2),
                new CellAddress(sheet.Id, 2, 2)));
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 2, 2),
                "the reverse Shift+Arrow extension must retain the formula-point anchor");

            window.RaiseFormulaBoxKeyDownForTest(Press(Key.Escape));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaBar_ShiftF8AddMode_AppendsKeyboardCreatedAreas()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var formulaCell = new CellAddress(sheet.Id, 3, 3);
            window.Session.SelectCell(formulaCell);
            window.BeginFormulaEditForTest(formulaCell, "=");

            var addMode = Press(Key.F8, KeyModifiers.Shift);
            window.RaiseFormulaBoxKeyDownForTest(addMode);
            addMode.Handled.Should().BeTrue();
            window.FormulaRangeEntrySelectionModeForTest.Should().Be(FreeX.App.Presentation.ExcelSelectionMode.Add);

            window.RaiseFormulaBoxKeyDownForTest(Press(Key.Right));
            window.FormulaBoxTextForTest.Should().Be("=D3");

            window.RaiseFormulaBoxKeyDownForTest(Press(Key.Down));
            window.FormulaBoxTextForTest.Should().Be("=D3,D4");

            window.RaiseFormulaBoxKeyDownForTest(Press(Key.Right, KeyModifiers.Shift));
            window.FormulaBoxTextForTest.Should().Be("=D3,D4,D4:E4");

            window.RaiseFormulaBoxKeyDownForTest(Press(Key.Escape));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaBar_DisjointPointFormula_CommitsAndCalculates()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            try
            {
                var firstArea = new CellAddress(sheet.Id, 5, 6);
                var secondArea = new CellAddress(sheet.Id, 7, 6);
                var formulaCell = new CellAddress(sheet.Id, 5, 5);
                sheet.SetCell(firstArea, new NumberValue(10));
                sheet.SetCell(secondArea, new NumberValue(20));
                window.Session.SelectCell(formulaCell);
                window.BeginFormulaEditForTest(formulaCell, "=");
                window.FormulaBoxTextForTest = "=SUM(";
                window.SetFormulaBoxSelectionForTest("=SUM(".Length, 0);

                window.RaiseFormulaBoxKeyDownForTest(Press(Key.Right));
                window.RaiseFormulaBoxKeyDownForTest(Press(Key.F8, KeyModifiers.Shift));
                window.RaiseFormulaBoxKeyDownForTest(Press(Key.Down, KeyModifiers.Control));
                window.FormulaBoxTextForTest.Should().Be("=SUM(F5,F7");

                window.FormulaBoxTextForTest += ")";
                window.RaiseFormulaBoxKeyDownForTest(Press(Key.Enter));

                sheet.GetCell(formulaCell)!.FormulaText.Should().Be("SUM(F5,F7)");
                sheet.GetValue(formulaCell).Should().Be(new NumberValue(30));
                window.Session.FormulaEditAddress.Should().BeNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineEditor_ShiftF8AddMode_AppendsKeyboardCreatedAreas()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var formulaCell = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(formulaCell);
            window.BeginInlineCellEditForTest(formulaCell, "=", 1);

            var addMode = Press(Key.F8, KeyModifiers.Shift);
            window.RaiseInlineCellEditorKeyDownForTest(addMode);
            addMode.Handled.Should().BeTrue();

            window.RaiseInlineCellEditorKeyDownForTest(Press(Key.Right));
            window.InlineCellEditorTextForTest.Should().Be("=C2");
            window.RaiseInlineCellEditorKeyDownForTest(Press(Key.Down));
            window.InlineCellEditorTextForTest.Should().Be("=C2,C3");

            window.RaiseInlineCellEditorKeyDownForTest(Press(Key.Escape));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaBar_F4_CyclesReferenceAbsoluteState()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var formulaCell = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(formulaCell);
            window.BeginFormulaEditForTest(formulaCell, "=A1");
            window.SetFormulaBoxSelectionForTest(3, 0);

            var f4 = Press(Key.F4);
            window.RaiseFormulaBoxKeyDownForTest(f4);

            f4.Handled.Should().BeTrue();
            window.FormulaBoxTextForTest.Should().Be("=$A$1");
            window.RaiseFormulaBoxKeyDownForTest(Press(Key.Escape));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineEditor_PointModeKeyboardNavigationAndF4_MatchFormulaBar()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var formulaCell = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(formulaCell);
            window.BeginInlineCellEditForTest(formulaCell, "=", 1);

            var right = Press(Key.Right);
            window.RaiseInlineCellEditorKeyDownForTest(right);
            right.Handled.Should().BeTrue();
            window.InlineCellEditorTextForTest.Should().Be("=C2");
            window.FormulaBoxTextForTest.Should().Be("=C2");

            var extendDown = Press(Key.Down, KeyModifiers.Shift);
            window.RaiseInlineCellEditorKeyDownForTest(extendDown);
            extendDown.Handled.Should().BeTrue();
            window.InlineCellEditorTextForTest.Should().Be("=C2:C3");
            window.FormulaBoxTextForTest.Should().Be("=C2:C3");

            window.RaiseInlineCellEditorKeyDownForTest(Press(Key.F2));
            window.FormulaPointModeForTest.Should().BeFalse();
            var ignoredArrow = Press(Key.Right);
            window.RaiseInlineCellEditorKeyDownForTest(ignoredArrow);
            ignoredArrow.Handled.Should().BeFalse();
            window.InlineCellEditorTextForTest.Should().Be("=C2:C3");

            window.RaiseInlineCellEditorKeyDownForTest(Press(Key.Escape));
            window.Close();
        }, CancellationToken.None);

        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var formulaCell = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(formulaCell);
            window.BeginInlineCellEditForTest(formulaCell, "=A1", 3);

            var f4 = Press(Key.F4);
            window.RaiseInlineCellEditorKeyDownForTest(f4);

            f4.Handled.Should().BeTrue();
            window.InlineCellEditorTextForTest.Should().Be("=$A$1");
            window.FormulaBoxTextForTest.Should().Be("=$A$1");
            window.RaiseInlineCellEditorKeyDownForTest(Press(Key.Escape));
            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(Key.Enter, KeyModifiers.None, 6, 5)]
    [InlineData(Key.Enter, KeyModifiers.Shift, 4, 5)]
    [InlineData(Key.Tab, KeyModifiers.None, 5, 6)]
    [InlineData(Key.Tab, KeyModifiers.Shift, 5, 4)]
    public async Task PlainGrid_EnterAndTab_MoveInForwardOrReverseDirection(
        Key key,
        KeyModifiers modifiers,
        uint expectedRow,
        uint expectedColumn)
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            window.Session.SelectCell(new CellAddress(sheet.Id, 5, 5));

            var args = Press(key, modifiers);
            await window.RaiseKeyDownForTest(args);

            args.Handled.Should().BeTrue();
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, expectedRow, expectedColumn));
            window.Session.SelectedRange.Should().Be(new GridRange(window.Session.ActiveCell, window.Session.ActiveCell));
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static MainWindow CreateWindowWithCleanSheet(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("CleanFixture");
        window.Session.SelectSheet(sheet.Id);
        return window;
    }

    private static KeyEventArgs Press(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        new() { Key = key, KeyModifiers = modifiers };
}
