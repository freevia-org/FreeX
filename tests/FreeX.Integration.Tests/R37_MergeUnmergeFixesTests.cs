using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Round-37 merge/unmerge fixes:
///
/// R37-commands-merge-unmerge-2-1 (HIGH): the Avalonia shell's "Merge Across" built its per-row
/// <see cref="MergeCellsCommand"/>s directly with <see cref="MergeCellContentResolution.KeepFirstCell"/>
/// and never consulted <see cref="CellMergePlanner.AnalyzeContent"/>, so multi-cell rows lost every
/// non-left-most value with zero confirmation -- unlike "Merge Cells" in the very same file, and unlike
/// the WPF host's own "Merge Across". Fixed by analyzing the whole selection once (matching the WPF
/// host's TryResolveMergeContentResolution) before the per-row split, and by having
/// BuildMergeWithoutCenterCommand delegate to CellMergePlanner.CreateFormatCellsMergeCommands instead of
/// duplicating merge/concatenate logic inline. Verified here as a source-contract test (MainWindow is an
/// Avalonia UI class with no headless-window harness in this test project).
///
/// R37-commands-merge-unmerge-2-2 (MED): merging a range that fully CONTAINS a smaller existing merged
/// region was rejected outright ("Range overlaps an existing merged region.") instead of absorbing it,
/// as real Excel does. Fixed in <see cref="MergeCellsCommand"/> to distinguish full containment (absorb)
/// from a genuine partial overlap (still rejected).
///
/// R37-commands-merge-unmerge-2-3 (MED): "Merge Cells" (and, via the per-row loop, "Merge Across") did
/// not get the toggle-to-unmerge gesture that "Merge & Center" already has, so re-clicking them on an
/// already-merged selection errored instead of unmerging. Fixed in
/// <see cref="CellMergePlanner.CreateMergeCommands"/> (shared by CreateFormatCellsMergeCommands, and
/// therefore by both the WPF host and, after the BuildMergeWithoutCenterCommand delegation fix above,
/// the Avalonia shell).
/// </summary>
public sealed class R37_MergeUnmergeFixesTests
{
    // ---- R37-commands-merge-unmerge-2-1: Avalonia Merge Across content-loss warning ----

    [Fact]
    public void AvaloniaMergeAcross_AnalyzesContentAndWarnsBeforeDiscarding_LikeMergeCells()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.MergePaste.cs");

        var mergeAcrossBody = ExtractMethodBody(source, "private async Task MergeAcrossSelectedRangeAsync()");

        mergeAcrossBody.Should().Contain(
            "CellMergePlanner.AnalyzeContent(",
            "Merge Across must analyze the selection's content before its per-row merges discard it, " +
            "like Merge Cells already does");
        mergeAcrossBody.Should().Contain(
            "contentPlan.WouldLoseContent",
            "the analysis result must actually gate whether the warning dialog is shown");
        mergeAcrossBody.Should().Contain(
            "ShowMergeCellsContentWarningDialogAsync(",
            "Merge Across must show the same content-loss confirmation dialog as Merge Cells");
        mergeAcrossBody.Should().Contain(
            "MergeCellsWarningChoice.Cancel",
            "cancelling the warning dialog must abort the Merge Across operation entirely");

        // R127-avalonia-mergepaste-multiarea-2: the analysis must also cover every disjoint Ctrl+click
        // area the per-area/per-row loop below is about to merge, not just the single active range --
        // otherwise a non-active area's content is discarded with zero warning even though the merge
        // itself already touches that area (the multi-area EXECUTION fix from the same round).
        mergeAcrossBody.Should().Contain(
            "CellMergePlanner.AnalyzeContent(sheet, areas, perRow: true)",
            "the analysis must run over every disjoint area (`areas`), not just the single active " +
            "`range`, or a non-active area's content is silently discarded with no warning");

        // The analysis/warning must happen on the WHOLE selection BEFORE the per-area/per-row split, not
        // once per area or per row (which would pop one dialog per area/row for a multi-area or
        // multi-row selection). r127 rewrote the single-range `for (var row = range.Start.Row; ...)` loop
        // into a per-area `foreach (var area in areas) { for (var row = area.Start.Row; ...) }` nest, so
        // pin the outer per-area loop instead of the old per-row literal, which no longer exists.
        var analyzeIndex = mergeAcrossBody.IndexOf("CellMergePlanner.AnalyzeContent(", StringComparison.Ordinal);
        var areaLoopIndex = mergeAcrossBody.IndexOf("foreach (var area in areas)", StringComparison.Ordinal);
        var rowLoopIndex = mergeAcrossBody.IndexOf("for (var row = area.Start.Row;", StringComparison.Ordinal);
        analyzeIndex.Should().BeGreaterThanOrEqualTo(0);
        areaLoopIndex.Should().BeGreaterThanOrEqualTo(0);
        rowLoopIndex.Should().BeGreaterThanOrEqualTo(0);
        analyzeIndex.Should().BeLessThan(areaLoopIndex,
            "content analysis (and any resulting warning) must run once, before the per-area merge loop");
        areaLoopIndex.Should().BeLessThan(rowLoopIndex,
            "sanity check: the per-row loop must be nested inside the per-area loop, not the reverse");
    }

    [Fact]
    public void AvaloniaMergeCells_StillAnalyzesContentAndWarnsBeforeDiscarding_NoRegression()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.MergePaste.cs");

        var mergeCellsBody = ExtractMethodBody(source, "private async Task MergeSelectedRangeAsync()");

        mergeCellsBody.Should().Contain("CellMergePlanner.AnalyzeContent(");
        mergeCellsBody.Should().Contain("contentPlan.WouldLoseContent");
        mergeCellsBody.Should().Contain("ShowMergeCellsContentWarningDialogAsync(");
        mergeCellsBody.Should().Contain("MergeCellsWarningChoice.Cancel");
    }

    // ---- R127 follow-up (HIGH, data-loss): r127 made Merge & Center / Merge Cells / Merge Across
    // EXECUTE across every disjoint Ctrl+click area, but left the pre-merge content-loss ANALYSIS on the
    // single active range in both shells -- so an area other than the active one could lose content with
    // zero warning. Verified below as source-contract tests (both MainWindow classes are UI classes with
    // no headless-window harness in these test projects); the actual analysis logic itself is covered
    // behaviourally by CellMergePlannerTests' AnalyzeContent_MultiArea_* tests in
    // FreeX.App.Services.Tests, which both shells now route through.

    [Fact]
    public void AvaloniaMergeCells_AnalyzesEveryDisjointArea_NotJustActiveRange()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.MergePaste.cs");

        var mergeCellsBody = ExtractMethodBody(source, "private async Task MergeSelectedRangeAsync()");

        mergeCellsBody.Should().Contain(
            "CellMergePlanner.AnalyzeContent(_session.ActiveSheet, areas)",
            "the content-loss analysis must cover every disjoint Ctrl+click area ('areas'), not just " +
            "the single active 'range' -- otherwise a non-active area's content is merged away with no " +
            "warning even though the merge itself already touches that area");
    }

    [Fact]
    public void AvaloniaMergeAcross_AnalyzesEveryDisjointArea_NotJustActiveRange()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.MergePaste.cs");

        var mergeAcrossBody = ExtractMethodBody(source, "private async Task MergeAcrossSelectedRangeAsync()");

        mergeAcrossBody.Should().Contain(
            "CellMergePlanner.AnalyzeContent(sheet, areas, perRow: true)",
            "the content-loss analysis must cover every disjoint Ctrl+click area ('areas'), not just " +
            "the single active 'range'");
    }

    [Fact]
    public void AvaloniaMergeAndCenter_AnalyzesEveryDisjointArea_NotJustActiveRange()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.cs");

        var mergeAndCenterBody = ExtractMethodBody(source, "private async Task MergeAndCenterSelectedRangeAsync()");

        mergeAndCenterBody.Should().Contain(
            "var areas = SelectionStyleCommandPlanner.ResolveRanges(range, _session.SelectedRanges);",
            "Merge & Center must resolve every disjoint Ctrl+click area up front, matching " +
            "WorkbookSession.MergeAndCenterSelectedRange's own EXECUTION-side resolution");
        // R128: the analysis must be widened on BOTH axes the execution was widened on -- every
        // disjoint Ctrl+click area AND every grouped-edit sheet the merge fans out to.
        // AnalyzeGroupedSheetMergeContent remaps `areas` onto each grouped sheet and unions the
        // result; the earlier single-sheet CellMergePlanner.AnalyzeContent(_session.ActiveSheet,
        // areas) covered the areas but only on the ACTIVE sheet, so a grouped sheet's content was
        // merged away with no warning.
        mergeAndCenterBody.Should().Contain(
            "AnalyzeGroupedSheetMergeContent(areas)",
            "the content-loss analysis must cover every disjoint area AND every grouped-edit sheet, " +
            "not just the active sheet -- otherwise content on a non-active grouped sheet is merged " +
            "away with no warning");
        mergeAndCenterBody.Should().NotContain(
            "CellMergePlanner.AnalyzeContent(_session.ActiveSheet",
            "the single-active-sheet analysis is narrower than the merge it gates and must not return");

        // R128: the analysis must be REACHABLE, not merely present. It previously sat behind an
        // `if (!isUnmergeToggle)` whose condition was computed from the single active `range`, so
        // whenever the active area happened to be already merged the whole warning was skipped
        // while sibling areas were still merged and lost content. A source-contract test proves a
        // call exists; it cannot prove the call runs -- so pin the absence of that narrow gate.
        // Pin the absence of the GATE, not of the variable. `isUnmergeToggle` is still computed from
        // the active range and that is fine -- it only selects the status-bar wording ("Unmerged
        // cells in ..." vs "Merged and centered ..."). The defect was gating the content analysis
        // behind it, which skipped the warning for the WHOLE operation whenever the active area
        // happened to be already merged, while sibling areas were still merged and lost content.
        mergeAndCenterBody.Should().NotContain(
            "if (!isUnmergeToggle)",
            "the content analysis must run unconditionally -- gating it on whether the ACTIVE area " +
            "is an unmerge-toggle skips the warning for every other Ctrl+click area, which are still " +
            "merged and still lose content");
    }

    [Fact]
    public void WpfMergeContentResolution_AnalyzesEveryDisjointArea_NotJustActiveRange()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs");

        var resolveBody = ExtractMethodBody(source, "private bool TryResolveMergeContentResolution(");

        // TryResolveMergeContentResolution backs Merge & Center / Merge Cells / Merge Across in the WPF
        // host (MergeCenterBtn_Click / MergeCellsMenuItem_Click / MergeAcrossMenuItem_Click). It must
        // resolve every disjoint Ctrl+click area (GetCurrentSelectionRanges) -- the same choke point the
        // merge EXECUTION path (TryExecuteRepeatableCurrentRangesCommand /
        // TryExecuteRepeatableCurrentSelectionRangesCommand) already uses -- instead of analyzing only
        // the single fallback 'range' passed in.
        resolveBody.Should().Contain(
            "var ranges = GetCurrentSelectionRanges(range);",
            "the analysis must resolve every disjoint area via the same GetCurrentSelectionRanges choke " +
            "point the merge execution path uses, not just the single fallback 'range'");
        // R128: widened again along the SHEET axis. The merge execution fans `ranges` out to every
        // sheet in CurrentGroupedEditSheetIds(), so the analysis is now built over `sheetRanges` --
        // each resolved area remapped onto each grouped sheet via
        // GroupedSheetRangePlanner.RemapRangeToSheet -- and analysed in one call. Analysing only the
        // active sheet (the pre-R128 form, AnalyzeContent(sheet, ranges, perRow)) was narrower than
        // the operation it gated and silently discarded grouped sheets' content.
        resolveBody.Should().Contain(
            "GroupedSheetRangePlanner.RemapRangeToSheet",
            "the analysis must remap every resolved area onto every grouped-edit sheet, since the " +
            "merge execution fans out to all of them");
        resolveBody.Should().Contain(
            "CellMergePlanner.AnalyzeContent(sheetRanges, perRow)",
            "the analysis must run over every area on every grouped sheet ('sheetRanges'), not just " +
            "the resolved areas on the single active sheet");

        // The analysis must run BEFORE any dialog is shown, over the full multi-area set, not once per
        // area (ShowMergeCellsContentWarningDialog must appear exactly once in this method).
        System.Text.RegularExpressions.Regex.Matches(resolveBody, "ShowMergeCellsContentWarningDialog\\(")
            .Count.Should().Be(1, "the warning dialog must be shown at most once per merge invocation, " +
                "never once per disjoint area");
    }

    /// <summary>
    /// Extracts a method's full body (from its opening to its matching closing brace) out of a C#
    /// source file's text, by brace-depth counting from the first '{' after <paramref name="signature"/>.
    /// </summary>
    private static string ExtractMethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"expected to find method signature '{signature}'");

        var braceStart = source.IndexOf('{', start);
        braceStart.Should().BeGreaterThanOrEqualTo(0, $"expected an opening brace after '{signature}'");

        var depth = 0;
        for (var i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[braceStart..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced braces while extracting body of '{signature}'.");
    }

    // ---- R37-commands-merge-unmerge-2-2: absorbing a fully-contained smaller merge ----

    [Fact]
    public void MergeCellsCommand_RangeFullyContainingSmallerMerge_AbsorbsItInsteadOfRejecting()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // A small header merge A1:B1 (e.g. labeled "Q1"), like the finding's failure scenario.
        var smallMerge = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2));
        sheet.SetCell(smallMerge.Start, new TextValue("Q1"));
        sheet.AddMergedRegion(smallMerge);

        // User selects the larger A1:D3, which fully contains A1:B1, and invokes Merge Cells.
        var bigRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 4));
        var outcome = new MergeCellsCommand(sheet.Id, bigRange).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "Excel absorbs a smaller merge that is fully contained by the new selection instead of " +
            "rejecting the whole merge");
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(bigRange);
        sheet.GetCell(smallMerge.Start)!.Value.Should().Be(new TextValue("Q1"),
            "the absorbed region's top-left content survives as the new merge's top-left content");
    }

    [Fact]
    public void MergeCellsCommand_RangeFullyContainingSmallerMerge_RevertRestoresOriginalSmallerMerge()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var smallMerge = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2));
        sheet.SetCell(smallMerge.Start, new TextValue("Q1"));
        sheet.AddMergedRegion(smallMerge);

        var bigRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 4));
        var command = new MergeCellsCommand(sheet.Id, bigRange);
        command.Apply(ctx).Success.Should().BeTrue();

        command.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(smallMerge);
        sheet.GetCell(smallMerge.Start)!.Value.Should().Be(new TextValue("Q1"));
    }

    [Fact]
    public void MergeCellsCommand_GenuinePartialOverlapWithExistingMerge_StillRejected_NoRegression()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // Existing merge B2:C3.
        var existing = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));
        sheet.AddMergedRegion(existing);

        // New range C3:D4 shares only the C3 corner with the existing merge -- neither range contains
        // the other, so this is a genuine conflict that must still be rejected exactly as before.
        var straddling = new GridRange(
            new CellAddress(sheet.Id, 3, 3),
            new CellAddress(sheet.Id, 4, 4));
        var outcome = new MergeCellsCommand(sheet.Id, straddling).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Range overlaps an existing merged region.");
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(existing,
            "a genuinely conflicting (non-containing) overlap must leave the original merge untouched");
    }

    // ---- R37-commands-merge-unmerge-2-3: Merge Cells toggle-to-unmerge ----

    [Fact]
    public void CreateMergeCommands_MergeCellsOnAlreadyMergedSelection_TogglesToUnmergeInsteadOfErroring()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 2),  // B2
            new CellAddress(sheet.Id, 2, 4)); // D2

        // First "Merge Cells" click merges B2:D2, exactly like the ribbon "Merge Cells" button.
        var firstClick = CellMergePlanner.CreateMergeCommands(sheet, sheet.Id, range, mergeCells: true);
        firstClick.Should().ContainSingle().Which.Should().BeOfType<MergeCellsCommand>();
        firstClick[0].Apply(ctx).Success.Should().BeTrue();
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(range);

        // Re-clicking "Merge Cells" on the same (now-merged) selection must toggle it off, like
        // Merge & Center already does, instead of failing with "Range overlaps an existing merged region."
        var secondClick = CellMergePlanner.CreateMergeCommands(sheet, sheet.Id, range, mergeCells: true);
        secondClick.Should().ContainSingle().Which.Should().BeOfType<UnmergeCellsCommand>();

        var outcome = secondClick[0].Apply(ctx);
        outcome.Success.Should().BeTrue();
        sheet.MergedRegions.Should().BeEmpty();
    }

    [Fact]
    public void CreateMergeCommands_MergeAcrossPerRowCommand_TogglesRowOffWhenAlreadyMerged()
    {
        // Mirrors how MainWindow.MergePaste.cs's Merge Across builds one per-row command via
        // CellMergePlanner.CreateFormatCellsMergeCommands (mergeCells: true) -> CreateMergeCommands.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var rowRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),  // A1
            new CellAddress(sheet.Id, 1, 3)); // C1

        var firstClick = CellMergePlanner.CreateFormatCellsMergeCommands(
            sheet, sheet.Id, rowRange, mergeCells: true);
        firstClick.Should().ContainSingle().Which.Should().BeOfType<MergeCellsCommand>();
        firstClick[0].Apply(ctx).Success.Should().BeTrue();
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(rowRange);

        var secondClick = CellMergePlanner.CreateFormatCellsMergeCommands(
            sheet, sheet.Id, rowRange, mergeCells: true);
        secondClick.Should().ContainSingle().Which.Should().BeOfType<UnmergeCellsCommand>();
        secondClick[0].Apply(ctx).Success.Should().BeTrue();
        sheet.MergedRegions.Should().BeEmpty();
    }

    [Fact]
    public void CreateMergeCommands_MergeCellsOnFreshUnmergedSelection_StillMergesNormally_NoRegression()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));

        var commands = CellMergePlanner.CreateMergeCommands(sheet, sheet.Id, range, mergeCells: true);

        commands.Should().ContainSingle().Which.Should().BeOfType<MergeCellsCommand>();
        commands[0].Apply(ctx).Success.Should().BeTrue();
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(range);
    }
}
