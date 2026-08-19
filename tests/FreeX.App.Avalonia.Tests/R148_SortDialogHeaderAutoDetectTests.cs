using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R148-sort-semantics-F1: the Avalonia Custom Sort dialog (Data &gt; Sort...) always opened with "My
/// data has headers" pre-checked (a literal <c>IsChecked = true</c> at construction), regardless of
/// whether the selected range actually looked like it had a header row. If the user clicked OK
/// without noticing, <c>SortDialogPlanner.ExcludeHeaderRow</c> stripped the real first data row out
/// of the sort range, pinning it in place while the rest sorted around it -- a silently scrambled
/// result. The WPF host already auto-detects via <c>QuickSortRangePlanner.HasLikelyHeaderRow</c>
/// (see <c>MainWindow.DataFilterCommands.cs</c>'s <c>DetectSortDialogHasHeaders</c>).
///
/// The behavioral fix wires the identical call
/// (<c>QuickSortRangePlanner.HasLikelyHeaderRow(_session.ActiveSheet, range)</c>) into the Avalonia
/// dialog's <c>headersCheck.IsChecked</c> initializer, immediately before the dialog is shown. That
/// exact <c>ShowSortInputDialogAsync</c> method independently renders a real modal <c>Window</c> via
/// <c>ShowDialog</c> and hangs/balloons to multiple GB of memory when driven headless in this test
/// project regardless of this fix (reproduced against both the pre-fix and post-fix source, and
/// against the already-existing, unrelated <c>R118_PrintPreviewSettingsRailInteractiveTests</c>
/// dialog-driving pattern applied to THIS dialog specifically -- R118's own PrintPreview dialog runs
/// fine with that pattern, so the hazard is particular to the Sort dialog's construction, not the
/// harness). Driving it live is therefore unsafe on this shared machine; these tests instead prove
/// the fix at the two boundaries that together fully cover it without touching the live dialog:
/// (1) a source-contract test pinning the exact production wiring in MainWindow.cs, and (2) a
/// behavioral test of the shared detection function the wiring calls, covering both the fixed
/// (headerless) and adjacent (header-bearing) cases.
/// </summary>
public sealed class R148_SortDialogHeaderAutoDetectTests
{
    // ── (1) Source contract: MainWindow.cs must compute the checkbox default from the shared
    // detection heuristic, not from a literal, and must feed that computed value (not a constant)
    // into the checkbox it constructs. ──────────────────────────────────────────────────────────

    [Fact]
    public void SortDialogSource_ComputesHeadersCheckboxDefault_FromHasLikelyHeaderRow_NotALiteral()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        var dialogMethod = ExtractMethodSource(
            source,
            "private async Task<SortDialogResult?> ShowSortInputDialogAsync(GridRange range)",
            "private static void ApplySortDialogButtonChrome(Button button, bool isDefault = false)");

        dialogMethod.Should().Contain(
            "QuickSortRangePlanner.HasLikelyHeaderRow(_session.ActiveSheet, range)",
            "the dialog must auto-detect headers with the same heuristic as the shared quick-sort " +
            "planner and the WPF host's DetectSortDialogHasHeaders, instead of always defaulting to " +
            "checked");

        // The buggy literal must be gone from the CheckBox initializer itself. A plain
        // NotContain("IsChecked = true,") would be too broad (other checkboxes in this file
        // legitimately default to true), so anchor on the actual construction snippet instead.
        // MainWindow.cs is CRLF, so the multi-line needle below must use \r\n, not \n, or the
        // Contain/NotContain checks silently never match anything in either direction.
        dialogMethod.Should().NotContain(
            "Content = UiText.Get(\"RemoveDuplicates_MyDataHasHeadersAutomationName\"),\r\n            IsChecked = true,",
            "the headers checkbox must no longer hardcode IsChecked = true regardless of the range's content");
        dialogMethod.Should().Contain(
            "Content = UiText.Get(\"RemoveDuplicates_MyDataHasHeadersAutomationName\"),\r\n            IsChecked = likelyHasHeaders,",
            "the headers checkbox must be initialized from the computed likelyHasHeaders value");
    }

    // ── (2) Behavioral: the shared detection function the wiring above calls must actually say
    // "no header" for a headerless numeric range (this is the finding's failure scenario) and "has
    // header" for a classic labels-over-values range (the no-regression sibling: the fix must not
    // flip this adjacent, already-correct case off too). ────────────────────────────────────────

    [Fact]
    public void HasLikelyHeaderRow_OnHeaderlessNumericRange_ReturnsFalse()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("HeaderlessFixture");

        // Three plain rows of numbers -- no row here reads as a text label sitting over numeric
        // data, so the shared heuristic must say "no header". Matches the Avalonia dialog's own
        // ActiveSheet/range inputs to QuickSortRangePlanner.HasLikelyHeaderRow.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(150));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));

        QuickSortRangePlanner.HasLikelyHeaderRow(sheet, range).Should().BeFalse(
            "a range of plain headerless numeric data must not be reported as having a header row -- " +
            "the Avalonia dialog defaults 'My data has headers' from exactly this value");
    }

    // ── No-regression sibling: a range that DOES look like it has a header row must still report
    // true, exactly as before -- the fix must not flip this case off along with the buggy one.

    [Fact]
    public void HasLikelyHeaderRow_OnRangeWithLabelsOverValues_ReturnsTrue()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("HeaderedFixture");

        // Classic "labels over values" shape: a text header over numeric data below.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Widget"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Gadget"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));

        QuickSortRangePlanner.HasLikelyHeaderRow(sheet, range).Should().BeTrue(
            "a range whose first row is text labels over numeric data below is the classic header " +
            "shape and must still be detected as having a header row");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static string ExtractMethodSource(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"source should contain {startMarker}");

        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"source should contain {endMarker} after {startMarker}");

        return source[start..end];
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
