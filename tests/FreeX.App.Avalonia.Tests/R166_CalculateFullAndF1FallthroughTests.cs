using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guards for round-166 findings F1/F2 (shared-keyboard-shortcuts):
///
///   F1 - Ctrl+Alt+F9 ("Calculate Full") silently downgraded to Calculate Now (dirty/volatile
///        cells only) instead of forcing WorkbookSession.RecalculateWorkbook to re-evaluate every
///        formula cell, matching the WPF host's CalcFullBtn_Click.
///   F2 - Alt+Shift+F1 (the legacy Excel "Insert Worksheet" alias WPF binds via
///        KeyboardShortcutMatcher.CommandRules.cs, alongside Shift+F11) was unimplemented on
///        Avalonia and fell through to the unmodified-F1 Help branch, launching the external help
///        browser instead of adding a sheet.
///
/// Each fixed case is paired with a sibling case proving the fix did not disturb the adjacent,
/// already-correct chord on the same key. Both fixed-case tests use a formula cell set directly via
/// Sheet.SetFormula (bypassing the command pipeline, so the recalculation engine's dependency graph
/// never learns the cell exists) as the discriminator between "dirty cells only" and "every formula
/// cell": WorkbookCellEditService.RecalculateDirty's Automatic-mode branch passes only the (empty)
/// data-table-refresh set to RecalcEngine.Recalculate, which short-circuits to an EmptyReport when
/// there are no changed/volatile/cyclic cells -- leaving such a cell permanently blank -- while
/// RecalculateAllFormulas (Calculate Full) walks every formula cell in every sheet regardless. This
/// is the same discriminator the existing Ctrl+Alt+Shift+F9 (RebuildDependenciesAndCalculate) case
/// in AvaloniaMainWindowKeyboardParityTests.CalculationFormulaBarAndZoomShortcuts_ExecuteRealHandlers
/// already relies on.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R166_CalculateFullAndF1FallthroughTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── F1: Ctrl+Alt+F9 must run Calculate Full, not Calculate Now ──────────────────────────────

    [Fact]
    public async Task CtrlAltF9_ForcesFullRecalculation_NotJustDirtyCells()
    {
        await Run(async (window, sheet) =>
        {
            var valueAddress = new CellAddress(sheet.Id, 1, 1);
            var formulaAddress = new CellAddress(sheet.Id, 1, 2);
            sheet.SetCell(valueAddress, new NumberValue(4));
            // Set directly on the model rather than through the command pipeline, so the
            // recalculation engine's dependency graph never registers this cell as dirty -- the
            // scenario Calculate Full exists to cover.
            sheet.SetFormula(formulaAddress, "A1*3");

            await Press(window, Key.F9, KeyModifiers.Control | KeyModifiers.Alt);

            sheet.GetValue(formulaAddress).Should().Be(new NumberValue(12),
                "Ctrl+Alt+F9 (Calculate Full) must rebuild and evaluate every formula cell in the " +
                "workbook, not just the ones the recalculation engine already tracks as dirty");
        });
    }

    [Fact]
    public async Task PlainF9_StillOnlyRecalculatesDirtyCells()
    {
        // Sibling/no-regression case: plain F9 (Calculate Now) must remain dirty-cells-only after
        // the Ctrl+Alt+F9 fix -- it must NOT have been widened into a full recalculation too.
        await Run(async (window, sheet) =>
        {
            var valueAddress = new CellAddress(sheet.Id, 1, 1);
            var formulaAddress = new CellAddress(sheet.Id, 1, 2);
            sheet.SetCell(valueAddress, new NumberValue(4));
            sheet.SetFormula(formulaAddress, "A1*3");

            await Press(window, Key.F9, KeyModifiers.None);

            sheet.GetValue(formulaAddress).Should().Be(BlankValue.Instance,
                "plain F9 (Calculate Now) only recalculates cells the engine already knows are " +
                "dirty/volatile, so a formula set directly on the model must stay unevaluated");
        });
    }

    // ── F2: Alt+Shift+F1 must insert a worksheet, not launch Help ───────────────────────────────

    [Fact]
    public async Task AltShiftF1_InsertsWorksheet_NotHelpBrowser()
    {
        await Run(async (window, _) =>
        {
            var sheetCountBefore = window.Session.Workbook.Sheets.Count;

            await Press(window, Key.F1, KeyModifiers.Alt | KeyModifiers.Shift);

            // Assert the COMMAND that ran (a sheet was actually inserted), not merely that the key
            // press was consumed -- before the fix this chord fell through to the unmodified-F1
            // Help branch, which sets e.Handled = true without adding any sheet.
            window.Session.Workbook.Sheets.Should().HaveCount(sheetCountBefore + 1,
                "Alt+Shift+F1 is the legacy Excel Insert Worksheet alias and must add a sheet, " +
                "matching WPF's KeyboardShortcutMatcher.CommandRules.cs InsertWorksheet rule");
        });
    }

    [Fact]
    public async Task AltF1_StillCreatesEmbeddedChart()
    {
        // Sibling/no-regression case: Alt+F1 (InsertEmbeddedChart, resolved earlier through the
        // shared WorkbookKeyboardShortcutCatalog) sits immediately next to Alt+Shift+F1 in the same
        // dispatch chain and must be unaffected by the new Alt+Shift+F1 branch.
        await Run(async (window, sheet) =>
        {
            var range = SeedChartData(sheet);
            window.Session.SelectRange(range);
            var sheetCountBefore = window.Session.Workbook.Sheets.Count;

            await Press(window, Key.F1, KeyModifiers.Alt);

            sheet.Charts.Should().ContainSingle(
                "Alt+F1 must still insert an embedded chart on the active sheet");
            window.Session.Workbook.Sheets.Should().HaveCount(sheetCountBefore,
                "Alt+F1 must not be confused with Alt+Shift+F1's Insert Worksheet");
        });
    }

    private static async Task Run(Func<MainWindow, Sheet, Task> test)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo -- run every scenario
            // on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("R166Fixture");
            window.Session.SelectSheet(sheet.Id);
            try
            {
                await test(window, sheet);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static async Task Press(MainWindow window, Key key, KeyModifiers modifiers)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers };
        await window.RaiseKeyDownForTest(args);
        args.Handled.Should().BeTrue($"{modifiers}+{key} should be consumed by MainWindow");
    }

    private static GridRange SeedChartData(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        return new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
    }
}
