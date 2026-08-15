using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R93-formula-editing-assist-5-2: FormulaSignatureHelpPlanner (FreeX.App.Presentation.FormulaBar)
/// was built in round 91 and wired into the WPF host's formula-editing TextChanged handlers there
/// (MainWindow.Editing.cs / MainWindow.xaml.cs), but the Avalonia shell never called it at all --
/// exactly the same gap R92-meta-2 found and fixed for the sibling function-name AutoComplete
/// popup. Before this fix, typing "=VLOOKUP(" into either formula editor here never showed the
/// "VLOOKUP(lookup_value, table_array, ...)" live tooltip with the current argument bolded, even
/// though the WPF host already did.
///
/// These tests drive the real per-keystroke TextInput/TextChanged pipeline (the same
/// RaiseFormulaBoxTextInputForTest / SimulateFormulaBoxTypedTextForTest / RaiseInlineCellEditor*
/// seams R92's tests use), asserting on the new SignatureHelpOpenForTest/SignatureHelpTextForTest/
/// SignatureHelpBoldArgumentIndexForTest seams.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R93_FormulaSignatureHelpAvaloniaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FormulaBar_TypingOpenParenAfterFunctionName_ShowsSignatureTooltipWithFirstArgumentBold()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var start = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(start);
            window.BeginFormulaEditForTest(start);

            // Before the fix, the Avalonia shell had no signature-help consumer at all -- this would
            // be permanently false regardless of what was typed.
            TypeIntoFormulaBar(window, "=VLOOKUP(");

            window.SignatureHelpOpenForTest.Should().BeTrue(
                "typing past a function's opening parenthesis must show the same live argument " +
                "tooltip the WPF host already shows");
            window.SignatureHelpTextForTest.Should().Be("VLOOKUP(Lookup_value, Table_array, Col_index_num, [Range_lookup])");
            window.SignatureHelpBoldArgumentIndexForTest.Should().Be(0,
                "the caret sitting right after '(' is inside the first argument");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaBar_TypingPastComma_AdvancesTheBoldArgument()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var start = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(start);
            window.BeginFormulaEditForTest(start);

            TypeIntoFormulaBar(window, "=VLOOKUP(A1,B1:C10,");

            window.SignatureHelpOpenForTest.Should().BeTrue();
            window.SignatureHelpBoldArgumentIndexForTest.Should().Be(2,
                "two commas typed at the top level of the call means the caret is now in the third " +
                "argument (Col_index_num)");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaBar_PastingFullFormulaText_ShowsSignatureTooltip()
    {
        // Simulates a paste (the whole string lands in one Text assignment rather than one
        // TextInput event per character), which drives the same FormulaBox_TextChanged path a
        // genuine Ctrl+V paste reaches -- there is no separate paste-specific code path to wire.
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var start = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(start);
            window.BeginFormulaEditForTest(start);

            window.SimulateFormulaBoxTypedTextForTest("=SUM(1,2,", "=SUM(1,2,".Length);

            window.SignatureHelpOpenForTest.Should().BeTrue("a pasted formula must be resolved just like a typed one");
            window.SignatureHelpTextForTest.Should().Be("SUM(Number1, [Number2])");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineCellEditor_TypingOpenParen_ShowsSignatureTooltip()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var address = new CellAddress(sheet.Id, 5, 5);
            window.Session.SelectCell(address);
            window.BeginInlineCellEditForTest(address, "", 0);

            foreach (var ch in "=SUM(")
                window.RaiseInlineCellEditorTextInputForTest(ch.ToString());

            window.InlineCellEditorTextForTest.Should().Be("=SUM(");
            window.SignatureHelpOpenForTest.Should().BeTrue(
                "the in-cell inline editor must get the same signature tooltip as the Formula Bar");
            window.SignatureHelpBoldArgumentIndexForTest.Should().Be(0);

            window.RaiseInlineCellEditorKeyDownForTest(new KeyEventArgs { Key = Key.Escape, KeyModifiers = KeyModifiers.None });
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaBar_CancelingTheEdit_HidesSignatureTooltip()
    {
        // An unbalanced call like "=SUM(1,2" is rejected by CommitCellText (R91-formula-editing-
        // assist-5-4 -- matches Excel's own refusal to commit a genuinely malformed formula), so the
        // only real-product way to leave edit mode while the tooltip is still open is Escape/cancel,
        // which restores the original text unconditionally. This exercises the
        // ClearInlineCellEditorState hide-point (shared with the sibling AutoComplete popup).
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var start = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(start);
            window.BeginFormulaEditForTest(start);

            TypeIntoFormulaBar(window, "=SUM(1,2");
            window.SignatureHelpOpenForTest.Should().BeTrue("the call is still open (no closing paren yet)");

            window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Escape, KeyModifiers = KeyModifiers.None });

            window.SignatureHelpOpenForTest.Should().BeFalse("canceling the edit must close the live tooltip too");
            sheet.GetCell(start).Should().BeNull("Escape must restore the original (empty) cell, not commit the unclosed formula");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    // ── No-regression siblings ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task FormulaBar_TypingPlainNumber_NeverShowsSignatureTooltip_NoRegression()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var start = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(start);
            window.BeginFormulaEditForTest(start);

            TypeIntoFormulaBar(window, "1234");

            window.SignatureHelpOpenForTest.Should().BeFalse(
                "plain non-formula text must never show the function signature tooltip");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaBar_TypingFunctionPrefixBeforeOpenParen_ShowsAutocompleteNotSignatureHelp_NoRegression()
    {
        // The two popups are mutually exclusive by construction: FormulaSignatureHelpPlanner only
        // resolves once the caret is inside an *open* call, so "=XNP" (no parenthesis yet) must
        // keep showing only the sibling R92 AutoComplete popup, unaffected by this fix.
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var start = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(start);
            window.BeginFormulaEditForTest(start);

            TypeIntoFormulaBar(window, "=XNP");

            window.FunctionAutocompleteOpenForTest.Should().BeTrue();
            window.SignatureHelpOpenForTest.Should().BeFalse();

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
