using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guards for round-13 review findings (bucket S2, Avalonia ribbon-command keyboard
/// wiring in the worksheet-grid key handler <c>MainWindow_KeyDownAsync</c>):
///
///   R13-ribbon-command-wiring-1 - Ctrl+Shift+V (Paste Values) and Ctrl+Alt+V (Paste Special) fell
///                                  through to the unguarded `else if (Key.V)` branch and performed
///                                  a full paste (formula + formatting) instead of a values-only
///                                  paste / opening the Paste Special dialog.
///   R13-ribbon-command-wiring-2 - Ctrl+Shift+digit number-format shortcuts (Percent/Number/Date/
///                                  Currency/Scientific/General), the Ctrl+Shift+7 outline-border
///                                  shortcut, and the Ctrl+2/Ctrl+3 Bold/Italic toggles were
///                                  entirely unwired.
///   R13-ribbon-command-wiring-3 - Ctrl+Shift+O ("Select Cells with Comments") fell through to the
///                                  unguarded `else if (Key.O)` branch and opened the file-Open
///                                  dialog instead.
///
/// These drive the real production key-handling code via the internal RaiseKeyDownForTest seam so
/// the resulting WorkbookSession/Workbook state reflects actual runtime behavior, not a
/// source-string proxy.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class FreeXR13S2Tests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── R13-ribbon-command-wiring-1: Ctrl+Shift+V pastes VALUES ONLY, not formula/formatting ──────

    [Fact]
    public async Task CtrlShiftV_PastesLiteralValueOnly_NotFormulaOrFormatting()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var sourceAddress = new CellAddress(sheet.Id, 1, 1); // A1: =B1*2, bold + red
            var targetAddress = new CellAddress(sheet.Id, 1, 3); // C1: paste destination (unstyled)

            sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5)); // B1
            sheet.SetCell(sourceAddress, new Cell { FormulaText = "B1*2", Value = new NumberValue(10) });
            sheet.GetCell(sourceAddress)!.StyleId = window.Session.Workbook.RegisterStyle(
                new CellStyle { Bold = true, FontColor = new CellColor(255, 0, 0) });

            window.Session.SelectCell(sourceAddress);
            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.C, KeyModifiers = KeyModifiers.Control });

            window.Session.SelectCell(targetAddress);
            await window.RaiseKeyDownForTest(new KeyEventArgs
            {
                Key = Key.V,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift
            });

            var pasted = sheet.GetCell(targetAddress);
            pasted.Should().NotBeNull("Ctrl+Shift+V must paste something into the target cell");
            pasted!.FormulaText.Should().BeNull(
                "Ctrl+Shift+V (Paste Values) must paste the literal computed value, not the source formula");
            pasted.Value.Should().Be(new NumberValue(10),
                "the pasted literal value must match the source cell's computed value");
            window.Session.Workbook.GetStyle(pasted.StyleId).Bold.Should().BeFalse(
                "Ctrl+Shift+V must not carry over the source cell's Bold formatting (values-only paste)");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── R13-ribbon-command-wiring-2: digit-key number-format / font-toggle / border shortcuts ──────

    [Fact]
    public async Task DigitKeyShortcuts_ApplyPercentFormat_BoldToggle_AndOutlineBorder()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new NumberValue(0.5));
            window.Session.SelectCell(address);

            // Ctrl+Shift+5 -> Percent number format ("0%"), matching Excel/WPF (WPF's
            // NumberFormatShortcutService.GetFormat(Percentage)).
            await window.RaiseKeyDownForTest(new KeyEventArgs
            {
                Key = Key.D5,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift
            });
            window.Session.Workbook.GetStyle(sheet.GetCell(address)!.StyleId).NumberFormat.Should().Be("0%",
                "Ctrl+Shift+5 must apply Excel's Percent number format");

            // Ctrl+2 -> Bold toggle (Excel's alternate to Ctrl+B).
            var boldBefore = window.Session.Workbook.GetStyle(sheet.GetCell(address)!.StyleId).Bold;
            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.D2, KeyModifiers = KeyModifiers.Control });
            window.Session.Workbook.GetStyle(sheet.GetCell(address)!.StyleId).Bold.Should().Be(!boldBefore,
                "Ctrl+2 must toggle Bold just like Ctrl+B does");

            // Ctrl+Shift+7 -> Outline border around the (single-cell) selection.
            await window.RaiseKeyDownForTest(new KeyEventArgs
            {
                Key = Key.D7,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift
            });
            var finalStyle = window.Session.Workbook.GetStyle(sheet.GetCell(address)!.StyleId);
            finalStyle.BorderTop.Style.Should().NotBe(BorderStyle.None,
                "Ctrl+Shift+7 must apply an outline border around the selection");
            finalStyle.BorderBottom.Style.Should().NotBe(BorderStyle.None,
                "Ctrl+Shift+7 must apply an outline border around the selection");
            finalStyle.BorderLeft.Style.Should().NotBe(BorderStyle.None,
                "Ctrl+Shift+7 must apply an outline border around the selection");
            finalStyle.BorderRight.Style.Should().NotBe(BorderStyle.None,
                "Ctrl+Shift+7 must apply an outline border around the selection");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── R13-ribbon-command-wiring-3: Ctrl+Shift+O selects commented cells, not file-Open ───────────

    [Fact]
    public async Task CtrlShiftO_SelectsCellsWithComments_NotFileOpenDialog()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var plainAddress = new CellAddress(sheet.Id, 1, 1);
            var commentedAddress = new CellAddress(sheet.Id, 5, 3);
            sheet.SetCell(plainAddress, new TextValue("plain"));
            sheet.SetCell(commentedAddress, new TextValue("has a note"));
            sheet.Comments[commentedAddress] = "Review this value";

            // Select a multi-cell range covering both cells (not just the single plain cell) so this
            // test isolates the KEY-ROUTING fix under test — Go To Special searches the current
            // selection, and a single-cell selection is a separate "expand to used range" concern
            // handled upstream of GoToSpecialService.Find.
            window.Session.SelectRange(new GridRange(plainAddress, commentedAddress));

            await window.RaiseKeyDownForTest(new KeyEventArgs
            {
                Key = Key.O,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift
            });

            window.Session.ActiveCell.Should().Be(commentedAddress,
                "Ctrl+Shift+O must select the cell with a comment (GoToSpecialKind.Comments), " +
                "not open the file-Open dialog (which would leave the selection unchanged)");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }
}
