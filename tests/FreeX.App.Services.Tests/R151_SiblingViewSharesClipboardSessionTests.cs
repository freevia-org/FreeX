using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R151-clipboard-formats-F2: Avalonia's View &gt; New Window (<see cref="WorkbookSession.CreateSiblingView"/>)
/// opens a second window on the SAME workbook/document. Before this fix, <see cref="WorkbookSession"/>
/// gave every sibling its own brand-new <c>WorkbookClipboardSession</c> instance, so a Copy in one
/// sibling window was invisible to a Paste in the other -- the paste fell through to the plain
/// external-text path (<c>PasteExternalTextAtActiveCell</c>), silently dropping the copied cell's
/// formula (and, by the same code path, hyperlinks/comments/conditional formatting) and leaving only
/// the flattened computed value. The WPF host never has this gap because it shares one
/// <c>WorkbookClipboardSession</c> across every open window via DI (see MainWindow.xaml.cs's
/// "clip-2 (R143)" comment).
/// <para>
/// These tests drive the real <see cref="WorkbookSession"/> entry points
/// (<see cref="WorkbookSession.CreateSiblingView"/>, <see cref="WorkbookSession.CommitCellText"/>,
/// <see cref="WorkbookSession.TryCopySelectedRangeText"/>,
/// <see cref="WorkbookSession.PasteClipboardTextAtActiveCell"/>) exactly the way
/// MainWindow.WindowManagement.cs's <c>NewWindow()</c> and MainWindow.cs's clipboard handlers do,
/// and use the pasted cell's <see cref="Cell.FormulaText"/> as the observable signal for "did the
/// paste reuse the internal clipboard snapshot (formula survives) or fall back to a plain
/// external-text paste (formula is lost, only the flattened value remains)".
/// </para>
/// </summary>
public sealed class R151_SiblingViewSharesClipboardSessionTests
{
    [Fact]
    public void CopyInOneSiblingWindow_PasteInAnother_PreservesTheFormulaInsteadOfFlatteningToItsValue()
    {
        var (windowA, sheet) = CreateSession();
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 5, 5);

        // View > New Window on the SAME workbook, matching MainWindow.WindowManagement.cs's
        // NewWindow() -> _session.CreateSiblingView(...).
        var windowB = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        windowA.SelectCell(source);
        windowA.CommitCellText("=5*3").Success.Should().BeTrue();
        sheet.GetCell(source)!.FormulaText.Should().Be("5*3", "sanity: A1 really holds a formula, not a literal");
        var copy = windowA.TryCopySelectedRangeText();
        copy.Success.Should().BeTrue();

        // Ctrl+V in the SIBLING window: the OS clipboard text/marker windowB observes is exactly what
        // windowA's copy produced, mirroring the Avalonia shell reading the same real platform
        // clipboard from either window.
        windowB.SelectCell(destination);
        var paste = windowB.PasteClipboardTextAtActiveCell(copy.Text, clipboardMarker: copy.ClipboardMarker);

        paste.Success.Should().BeTrue();
        sheet.GetCell(destination)!.FormulaText.Should().Be(
            "5*3",
            "Copy in one sibling window followed by Paste in another sibling window on the SAME " +
            "workbook must preserve the formula (Excel parity, and what the WPF host already does), " +
            "not silently flatten it to its computed value of 15");
    }

    // No-regression sibling for the fix's own side effect: sharing one WorkbookClipboardSession across
    // siblings means an ordinary, UNRELATED committed edit in one window must not blow away another
    // window's still-live Copy/Cut -- exactly the "clip-2-regression" the WPF host's own
    // ClearIfOwnedBy/Owner mechanism (WorkbookClipboardSession.Owner) exists to prevent for its
    // process-wide singleton. Without WorkbookSession's own CancelPendingCutAfterMutatingEdit scoping
    // its Clear() to ClearIfOwnedBy(this), this test fails after making the session shared.
    [Fact]
    public void UnrelatedEditCommittedInOneSiblingWindow_DoesNotCancelAnotherSiblingWindowsPendingCopy()
    {
        var (windowA, sheet) = CreateSession();
        var source = new CellAddress(sheet.Id, 1, 1);
        var unrelatedCell = new CellAddress(sheet.Id, 9, 9);
        var destination = new CellAddress(sheet.Id, 5, 5);

        var windowB = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        windowA.SelectCell(source);
        windowA.CommitCellText("=7+1").Success.Should().BeTrue();
        var copy = windowA.TryCopySelectedRangeText();
        copy.Success.Should().BeTrue();
        windowA.HasPendingClipboardMarquee.Should().BeTrue("sanity: windowA's own copy is live");

        // A completely unrelated edit committed in the SIBLING window B -- Excel's real "cancel the
        // marquee" trigger is an edit that could invalidate the copy, not a mere Copy/Paste in some
        // other open window on the same document.
        windowB.SelectCell(unrelatedCell);
        windowB.CommitCellText("42").Success.Should().BeTrue();

        windowA.HasPendingClipboardMarquee.Should().BeTrue(
            "windowB's own unrelated edit carries no clipboard intent for windowA's still-live copy " +
            "and must not silently discard it");

        windowA.SelectCell(destination);
        var paste = windowA.PasteClipboardTextAtActiveCell(copy.Text, clipboardMarker: copy.ClipboardMarker);

        paste.Success.Should().BeTrue();
        sheet.GetCell(destination)!.FormulaText.Should().Be(
            "7+1",
            "windowA's own copy must still be pasteable (with its formula intact) after windowB's " +
            "unrelated edit, exactly as if windowB's edit had never happened");
    }

    private static (WorkbookSession Session, Sheet Sheet) CreateSession()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        return (session, sheet);
    }
}
