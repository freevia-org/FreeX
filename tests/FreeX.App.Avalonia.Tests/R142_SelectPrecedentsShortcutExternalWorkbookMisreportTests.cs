using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.App.Presentation;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R142 remediation: Trace Precedents (TraceFormulaPrecedents, MainWindow.FormulaAuditing.cs) was
/// fixed to stop reporting "no precedents to trace" for a formula whose only precedent is an
/// external-workbook reference (R142-core-commands-formula-auditing-trace-precedents-external
/// -workbook-misreport), but its keyboard-shortcut sibling -- Ctrl+[ (Select Direct Precedents,
/// MainWindow.KeyboardParity.cs's SelectFormulaAuditCells) -- called the same
/// FormulaAuditSelectionPlanner.Plan / FormulaAuditingService.GetDirectPrecedents API and still
/// unconditionally reported the generic "no direct precedents" status with no
/// HasExternalPrecedentReference check.
///
/// This test drives the REAL Avalonia entry point: MainWindow_KeyDownAsync (via the
/// RaiseKeyDownForTest test hook), the same async handler a real Ctrl+[ keypress reaches, which
/// resolves the chord through WorkbookKeyboardShortcutCatalog to
/// KeyboardCommandShortcut.SelectDirectPrecedents and dispatches to SelectFormulaAuditCells --
/// not the private method called directly.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R142_SelectPrecedentsShortcutExternalWorkbookMisreportTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task CtrlOpenBracketShortcut_OnCellWithOnlyExternalWorkbookPrecedent_DoesNotReportNoPrecedents()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("Sheet1");
            window.Session.SelectSheet(sheet.Id);
            try
            {
                var formulaCell = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(formulaCell, Cell.FromFormula("'[Budget.xlsx]Sheet1'!A1"));

                // Sanity: this is exactly the misreport condition -- the formula's only precedent
                // cannot be represented as a local CellAddress, so the planner (built on
                // GetDirectPrecedents) has nothing to select and returns null.
                FormulaAuditSelectionPlanner.Plan(
                        window.Session.Workbook, formulaCell, selectDependents: false, includeTransitive: false)
                    .Should().BeNull("GetDirectPrecedents cannot address a cell in another workbook");
                FormulaAuditingService.HasExternalPrecedentReference(window.Session.Workbook, formulaCell)
                    .Should().BeTrue("the formula's only precedent lives in another workbook");

                window.Session.SelectCell(formulaCell);

                var args = new KeyEventArgs { Key = Key.OemOpenBrackets, KeyModifiers = KeyModifiers.Control };
                await window.RaiseKeyDownForTest(args);
                args.Handled.Should().BeTrue("Ctrl+[ should be consumed by MainWindow");

                var statusText = window.StatusTextForTest.Text;
                statusText.Should().NotBe("No direct precedents",
                    "the formula DOES have a precedent -- it is just in another workbook, which FreeX cannot select");
                statusText.Should().Contain("another workbook",
                    "the status must tell the user the true reason nothing was selected");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlOpenBracketShortcut_OnOrdinaryCellWithNoPrecedents_StillReportsNoDirectPrecedents()
    {
        // No-regression sibling: an ordinary formula-less/precedent-less cell must keep the plain
        // localized "no direct precedents" status -- only the genuine external-reference case gets
        // the new message.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("Sheet1");
            window.Session.SelectSheet(sheet.Id);
            try
            {
                var plainCell = new CellAddress(sheet.Id, 3, 3);
                sheet.SetCell(plainCell, new NumberValue(5));
                window.Session.SelectCell(plainCell);

                var args = new KeyEventArgs { Key = Key.OemOpenBrackets, KeyModifiers = KeyModifiers.Control };
                await window.RaiseKeyDownForTest(args);
                args.Handled.Should().BeTrue("Ctrl+[ should be consumed by MainWindow");

                // No-regression check: an ordinary cell must still take the plain
                // KeyboardLoc_NoDirectPrecedents branch, not the new external-reference branch --
                // asserted by absence rather than the exact resx string, since the headless test app
                // has no localization catalog bootstrap (UiText.Get resolves to "" here) the way the
                // real running shell does.
                window.StatusTextForTest.Text.Should().NotContain("another workbook",
                    "an ordinary precedent-less cell has no external reference to report");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }
}
