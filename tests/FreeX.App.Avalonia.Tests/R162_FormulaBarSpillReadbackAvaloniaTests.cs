using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R162-formulabar-spill-readback (U3): the Avalonia counterpart of the WPF host's
/// MainWindow.Editing.cs fix (see MainWindowFormulaBarSyncTests.SpillReadback.cs) -- selecting a
/// non-anchor dynamic-array spill member must show the spilled value in the formula bar, not a
/// blank string, because <see cref="Sheet.GetCell"/> returns null for those addresses (their value
/// lives only in the spill overlay -- see its remarks) while the grid itself paints the value via
/// <see cref="Sheet.GetValue(CellAddress)"/>, which does consult the overlay.
///
/// Covers both call sites the wave-B audit named as still broken in this file: the per-selection
/// shell refresh (<c>RefreshShell</c>, reached here through the real <see cref="MainWindow.
/// SelectClickedCell"/> click-to-select entry point) and an edit-start path (F2, via
/// <c>NavigateActiveCell</c>). Both route through the shared <c>FormatEditText</c> helper in
/// MainWindow.cs, which now resolves through <see cref="FreeX.App.Presentation.
/// SpreadsheetDisplayFormatter.ResolveFormulaBarDisplayCell"/> -- the same rule the WPF host uses,
/// so the resolution exists in exactly one place, shared by both shells.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R162_FormulaBarSpillReadbackAvaloniaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    /// <summary>
    /// Seeds a dynamic-array spill the way the recalc engine would after evaluating
    /// <c>=SEQUENCE(<paramref name="count"/>)</c> anchored at (<paramref name="anchorRow"/>,
    /// <paramref name="anchorCol"/>): the anchor cell keeps the formula and its own first value,
    /// while every other member row only exists in the sheet's spill overlay.
    /// </summary>
    private static void SeedSequenceSpill(Sheet sheet, uint anchorRow, uint anchorCol, int count)
    {
        var anchor = new CellAddress(sheet.Id, anchorRow, anchorCol);
        sheet.SetCell(anchor, Cell.FromFormula($"SEQUENCE({count})"));
        sheet.GetCell(anchor)!.Value = new NumberValue(1);

        var cells = new ScalarValue[count, 1];
        for (var r = 0; r < count; r++)
            cells[r, 0] = new NumberValue(r + 1);
        sheet.SetSpillRange(anchor, new RangeValue(cells));
    }

    [Fact]
    public async Task RealPointerPressed_OnNonAnchorSpillMember_ShowsSpilledValueInsteadOfBlank()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                SeedSequenceSpill(sheet, 1, 1, 5);

                // Row 3 col 1 is a non-anchor spill member: no entry in Sheet's cell storage, but
                // the grid paints "3" there (via Sheet.GetValue, which does see the spill overlay).
                var member = new CellAddress(sheet.Id, 3, 1);

                // Drives the real click-to-select entry point named by the audit, not a synthetic
                // shortcut: SelectClickedCell is exactly what the per-cell Border's PointerPressed
                // handler calls for a plain click (MainWindow.cs).
                window.SelectClickedCell(member, KeyModifiers.None);
                Refresh(window);

                window.FormulaBoxTextForTest.Should().Be("3");
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
    public async Task F2EditStart_OnNonAnchorSpillMember_ShowsSpilledValueInsteadOfBlank()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                SeedSequenceSpill(sheet, 1, 1, 5);
                var member = new CellAddress(sheet.Id, 3, 1);

                window.SelectClickedCell(member, KeyModifiers.None);
                Refresh(window);

                PressF2(window);

                window.FormulaBoxTextForTest.Should().Be("3");
                window.InlineCellEditorTextForTest.Should().Be("3");
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
    public async Task RealPointerPressed_OnSpillAnchor_StillShowsFormulaNotValue()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                SeedSequenceSpill(sheet, 1, 1, 5);

                // Sibling case: the anchor cell (row 1) DOES have a real Cell with a formula, so
                // the spill-member fix must not affect it -- it must keep showing the formula
                // text, not fall back to the synthesized value-only cell.
                var anchor = new CellAddress(sheet.Id, 1, 1);

                window.SelectClickedCell(anchor, KeyModifiers.None);
                Refresh(window);

                window.FormulaBoxTextForTest.Should().Be("=SEQUENCE(5)");
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
    public async Task RealPointerPressed_OnGenuinelyBlankCell_StillShowsEmptyFormulaBar()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                SeedSequenceSpill(sheet, 1, 1, 5);

                // Sibling case: an ordinary blank cell (no formula, no spill overlay entry
                // either) must keep showing an empty formula bar -- the synthesized fallback
                // cell wraps BlankValue for this address, which formats identically to the null
                // it replaces.
                var blank = new CellAddress(sheet.Id, 9, 9);

                window.SelectClickedCell(blank, KeyModifiers.None);
                Refresh(window);

                window.FormulaBoxTextForTest.Should().BeEmpty();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static MainWindow CreateShownWindow(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("R162SpillReadbackFixture");
        window.Session.SelectSheet(sheet.Id);
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
        Refresh(window);
        return window;
    }

    private static void Refresh(MainWindow window) =>
        typeof(MainWindow)
            .GetMethod("RefreshShell", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, ["Ready"]);

    private static void PressF2(MainWindow window)
    {
        window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        window.KeyRelease(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
    }
}
