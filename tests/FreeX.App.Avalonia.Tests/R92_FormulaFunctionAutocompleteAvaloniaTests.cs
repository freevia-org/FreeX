using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R92-meta-2 (MED): FormulaFunctionAutocompletePlanner (FreeX.App.Presentation.FormulaBar) was wired
/// into the WPF host's formula-editing key/text handlers in round 91, but its own doc comment claims
/// it "stays reusable from either shell's formula editor" -- untrue, since the Avalonia shell never
/// called it at all. Before this fix, typing "=XNP" into the Formula Bar or the in-cell inline editor
/// never showed the SUM/SUMIF/...-style AutoComplete dropdown, and pressing Enter mid-formula
/// committed the half-typed text into the cell and moved the active cell down (the ordinary
/// commit-and-move path), instead of the popup intercepting Enter to complete the function name --
/// exactly like the WPF host already behaves.
///
/// These tests drive the real per-keystroke TextInput pipeline (mirroring the existing
/// AvaloniaWorksheetPhysicalEditingTests' RaiseRawTextInput pattern) via the
/// RaiseFormulaBoxTextInputForTest/RaiseInlineCellEditorTextInputForTest seams, then the real
/// FormulaBox_KeyDown/InlineCellEditor_KeyDown handlers via the existing RaiseFormulaBoxKeyDownForTest/
/// RaiseInlineCellEditorKeyDownForTest seams, asserting on <see cref="MainWindow.Session"/> state
/// rather than a source-string proxy.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R92_FormulaFunctionAutocompleteAvaloniaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FormulaBar_TypingUniqueFunctionPrefix_OpensAutocompleteWithMatchingCandidate()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var start = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(start);
            window.BeginFormulaEditForTest(start);

            TypeIntoFormulaBar(window, "=XNP");

            // Before the fix, the Avalonia shell had no autocomplete popup at all -- this would be
            // permanently false and the candidate list permanently empty.
            window.FunctionAutocompleteOpenForTest.Should().BeTrue(
                "typing a formula prefix that matches a built-in function name must open the same " +
                "AutoComplete dropdown the WPF host already shows");
            window.FunctionAutocompleteCandidatesForTest.Should().ContainSingle().Which.Should().Be("XNPV");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaBar_EnterWhileAutocompleteOpen_CommitsFunctionNameInsteadOfCommittingTheCell()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var start = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(start);
            window.BeginFormulaEditForTest(start);
            TypeIntoFormulaBar(window, "=XNP");
            window.FunctionAutocompleteOpenForTest.Should().BeTrue();

            // This is the exact regression: before the fix, Enter fell straight through to the
            // ordinary formula-editing key handling, committing "=XNP" into the cell (a #NAME? error)
            // and moving the active cell down -- the popup never got a chance to intercept the key.
            window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.None });

            window.FormulaBoxTextForTest.Should().Be("=XNPV(",
                "Enter while the popup is open must complete the function name plus '(' , matching Excel");
            window.FunctionAutocompleteOpenForTest.Should().BeFalse("committing a candidate must close the popup");
            window.Session.ActiveCell.Should().Be(start,
                "the Enter keystroke must be consumed by the AutoComplete popup, not fall through to " +
                "the normal commit-and-move handling that would have moved the active cell");
            sheet.GetCell(start).Should().BeNull("the half-typed formula must not have been committed into the cell");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineCellEditor_TypingUniqueFunctionPrefix_OpensAutocompleteAndEnterCommitsIt()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var address = new CellAddress(sheet.Id, 5, 5);
            window.Session.SelectCell(address);
            window.BeginInlineCellEditForTest(address, "", 0);

            // Real per-keystroke typing goes through TryApplyInlineCellTextInput (ApplyTextBoxEdit
            // sets Text before CaretIndex), so this also exercises the R92-meta-2 fix in that method
            // that re-runs the AutoComplete refresh once the caret reflects the keystroke just typed.
            foreach (var ch in "=XNP")
                window.RaiseInlineCellEditorTextInputForTest(ch.ToString());

            window.InlineCellEditorTextForTest.Should().Be("=XNP");
            window.FunctionAutocompleteOpenForTest.Should().BeTrue(
                "the in-cell inline editor must get the same AutoComplete popup as the Formula Bar");
            window.FunctionAutocompleteCandidatesForTest.Should().ContainSingle().Which.Should().Be("XNPV");

            window.RaiseInlineCellEditorKeyDownForTest(new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.None });

            window.InlineCellEditorTextForTest.Should().Be("=XNPV(");
            window.FunctionAutocompleteOpenForTest.Should().BeFalse();

            window.RaiseInlineCellEditorKeyDownForTest(new KeyEventArgs { Key = Key.Escape, KeyModifiers = KeyModifiers.None });
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    // ── No-regression siblings: ordinary formula editing (no autocomplete match) is untouched ───

    [Fact]
    public async Task FormulaBar_EnterWithNoAutocompleteMatch_StillCommitsAndMovesActiveCell_NoRegression()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var start = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(start);
            window.BeginFormulaEditForTest(start);

            // "=1+1" never matches an identifier-shaped AutoComplete token, so the popup must stay
            // closed and Enter must behave exactly as it did before this fix.
            TypeIntoFormulaBar(window, "=1+1");
            window.FunctionAutocompleteOpenForTest.Should().BeFalse();

            window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.None });

            sheet.GetValue(start).Should().Be(new NumberValue(2));
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 3, 2),
                "Enter must still commit-and-move down when the AutoComplete popup was never open");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineCellEditor_TypingPlainTextWithNoMatch_PopupStaysClosed_NoRegression()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var address = new CellAddress(sheet.Id, 5, 5);
            window.Session.SelectCell(address);
            window.BeginInlineCellEditForTest(address, "", 0);

            foreach (var ch in "hello")
                window.RaiseInlineCellEditorTextInputForTest(ch.ToString());

            window.InlineCellEditorTextForTest.Should().Be("hello");
            window.FunctionAutocompleteOpenForTest.Should().BeFalse(
                "plain text that never starts with '=' must never open the function AutoComplete popup");

            window.RaiseInlineCellEditorKeyDownForTest(new KeyEventArgs { Key = Key.Escape, KeyModifiers = KeyModifiers.None });
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    private static void TypeIntoFormulaBar(MainWindow window, string text) =>
        window.SimulateFormulaBoxTypedTextForTest(text, text.Length);

    private static MainWindow CreateWindowWithCleanSheet(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("CleanFixture");
        window.Session.SelectSheet(sheet.Id);
        return window;
    }
}
