using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R101-io-source-package-snapshot-hyperlink-guard: <see cref="XlsxFileAdapter"/>'s patch-safety
/// fingerprint (<c>WriteDrawingChartFingerprint</c>/<c>WriteDrawingPictureFingerprint</c>/
/// <c>WriteDrawingTextBoxFingerprint</c>/<c>WriteDrawingShapeFingerprint</c> in
/// <c>XlsxFileAdapter.SourcePackageSnapshot.cs</c>) does not compare <c>DrawingObjectHyperlink</c>
/// fields (r97 added the field to <see cref="Model.DrawingShapeModel"/>/<see cref="Model.TextBoxModel"/>/
/// <see cref="Model.PictureModel"/>; r98 added <see cref="Model.ChartModel"/>'s copy). Both rounds
/// judged the omission harmless because NO <c>IWorkbookCommand</c> in <c>FreeX.Core.Commands</c> can
/// currently SET a new hyperlink value onto one of these fields -- every occurrence is either the LOAD
/// path populating the field from the source package, or a clone/paste/duplicate path copying an
/// EXISTING object's own <c>Hyperlink</c> value onto its copy (no new data is introduced, so a
/// cell-patch save that skips the fingerprint-guarded full rebuild can never disagree with what's
/// already on disk).
/// <para>
/// AUDIT (this round): re-verified -- the premise still holds today (see the enumeration this test
/// performs below). But leaving that premise unchecked is a latent trap: the day a real "Edit Object
/// Hyperlink" command is added, a plain cell edit elsewhere in the same save would still take the cheap
/// cell-patch path (which copies the drawing/chart parts byte-for-byte) and silently discard the new
/// hyperlink, because nothing in the patch-safety fingerprint would detect the change. This test is that
/// guard: it fails the moment any file under <c>src/FreeX.Core.Commands</c> assigns something OTHER than
/// a plain copy-forward of an existing object's own <c>Hyperlink</c> property (e.g.
/// <c>Hyperlink = shape.Hyperlink</c>) to a <c>Hyperlink</c> property -- signaling that
/// <c>WriteDrawingChartFingerprint</c>/<c>WriteDrawingPictureFingerprint</c>/
/// <c>WriteDrawingTextBoxFingerprint</c>/<c>WriteDrawingShapeFingerprint</c> must be updated to include
/// the new field BEFORE that command can safely ship.
/// </para>
/// <para>
/// AUDIT (R106, Duplicate Sheet): fired for <c>DuplicateSheetDrawingCloner</c>'s rebase of a
/// duplicated sheet's own drawing hyperlinks. Not a hole -- a just-duplicated sheet has no
/// worksheet path in the source package, so <c>PackageAllowsCellPatchSave</c> always forces the
/// full rebuild for that save. Allowlisted via <c>AllowedRewriteSameSheetHyperlinkTargetRhs</c>.
/// </para>
/// <para>
/// AUDIT (R108, Rename/Delete Sheet): fired again for <c>RenameSheetCommand</c>/
/// <c>RemoveSheetCommand</c>'s R107 drawing-object hyperlink rewrite in <c>SheetCommands.cs</c>.
/// Also not a hole, but via a DIFFERENT mechanism than R106 (traced concretely rather than assumed):
/// a rename changes the live <c>Sheet.Name</c> away from the key <c>PackageAllowsCellPatchSave</c>'s
/// <c>worksheetPathMap.SheetPathsByName</c> lookup uses (the PRISTINE baseline's name), so that
/// lookup fails with <c>"package_guard_sheet_path_missing"</c>; a delete leaves an unmatched
/// worksheet part in the archive, failing with <c>"package_guard_unmatched_worksheet_part"</c>.
/// Either way the full rebuild is forced for the whole workbook, independent of the hyperlink
/// rewrite -- confirmed both by existing tests (<c>R102_RenameSheetPreservedPartsTests</c> asserts
/// <c>LastSaveDiagnostics.Path == XlsxSavePath.FullSave</c> for a plain rename) and by a direct
/// real-Load/real-Command/real-Save probe against a shape hyperlink during this audit. Allowlisted
/// via <c>AllowedDrawingObjectHyperlinkRewriterRhs</c>/<c>AllowedDrawingObjectHyperlinkUndoRestoreRhs</c>.
/// </para>
/// </summary>
public sealed class R101_DrawingChartHyperlinkPatchSafetyGuardTests
{
    // Matches a `Hyperlink = <rhs>` assignment (object-initializer style `Hyperlink = expr,`, a plain
    // statement `x.Hyperlink = expr;`, or a LAST member of an object/`with`-initializer
    // `Hyperlink = expr }` with no trailing comma), capturing the right-hand side. Word-bounded so it
    // never matches `HyperlinkMetadata = ...` or `Hyperlinks[...] = ...` (unrelated cell-hyperlink
    // fields). The negative lookahead excludes `Hyperlink => ...` (a switch/pattern arm, e.g.
    // `ConditionalFormulaScalarFunctionKind.Hyperlink => ...`) and `Hyperlink == ...` (an equality
    // comparison), neither of which is an assignment.
    // The terminator class includes `}` (R102-multiline-hyperlink-guard-scan) so a last-member
    // initializer entry with no trailing comma is still recognized as terminated, and the pattern is
    // matched against the WHOLE FILE TEXT (not line-by-line) so a multi-line RHS -- e.g. a wrapped
    // ternary `Hyperlink = flag\n    ? new DrawingObjectHyperlink(a, b, c)\n    : null;` -- is not
    // silently skipped just because its terminator lands on a different line than `Hyperlink =`.
    private static readonly Regex HyperlinkAssignmentPattern = new(
        @"\bHyperlink\s*=(?![=>])\s*([^,;}]+)[,;}]",
        RegexOptions.Compiled);

    // FreeX.Core.Commands files known to declare/assign an UNRELATED "Hyperlink" identifier that has
    // nothing to do with DrawingObjectHyperlink/ChartModel.Hyperlink -- SortCommand's SortCellPayload
    // carries a per-cell `string? Hyperlink` (the plain cell hyperlink TARGET string, mirroring
    // sheet.Hyperlinks) purely so a row reorder can move it along with the rest of the cell's data; it
    // is never a DrawingObjectHyperlink and is out of scope for this guard.
    private static readonly System.Collections.Generic.HashSet<string> UnrelatedHyperlinkIdentifierFiles = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "SortCommand.cs"
    };

    // A pure copy-forward of an existing object's own Hyperlink property, e.g. `shape.Hyperlink`,
    // `chartPart.Hyperlink`, `picturePart.Hyperlink` -- introduces no new data, so it can never desync
    // the patch-safety fingerprint (the source object's own Hyperlink already went through the exact
    // same fingerprint gap on ITS OWN save, which is a pre-existing, already-accepted no-op case: the
    // value being copied was never itself settable by a command either).
    private static readonly Regex AllowedCopyForwardRhs = new(
        @"^\w+(\.\w+)*\.Hyperlink$",
        RegexOptions.Compiled);

    // REVIEWED-AND-SAFE (R106-drawing-object-hyperlink-duplicate-rebase audit): this guard fired
    // exactly as designed when DuplicateSheetDrawingCloner.CopyDrawingCollections started assigning
    // `cloned.Hyperlink = RewriteSameSheetHyperlinkTarget(cloned.Hyperlink, source.Name, copy.Name)` --
    // a computed (non-copy-forward) value, breaking the "no command can SET a hyperlink" premise this
    // test's class doc describes. AUDITED: still NOT a patch-safety hole, for a reason independent of
    // the fingerprint gap this guard polices. DuplicateSheetCommand adds a brand-new sheet that has no
    // corresponding worksheet part in the ORIGINAL source package snapshot; XlsxFileAdapter's cell-patch
    // eligibility check (PackageAllowsCellPatchSave in XlsxFileAdapter.SourcePackageSnapshot.cs) walks
    // every live sheet and requires a source-package worksheet path for each one via
    // worksheetPathMap.SheetPathsByName -- the new sheet has none, so that walk fails with
    // "package_guard_sheet_path_missing" and blocks cell-patch save for the WHOLE workbook, forcing the
    // full ClosedXML rebuild (which recomputes the drawing/chart XML from the current model, including
    // the now-rebased Hyperlink) every single time a just-duplicated sheet is still present at save time.
    // The cheap cell-patch path (the one this guard's fingerprint actually gates) is therefore
    // categorically unreachable for this assignment -- the same conclusion R97_DrawingObjectHyperlinkCopyTests'
    // class doc already draws for the sibling verbatim-copy-forward Hyperlink assignments this cloner
    // makes. So the fingerprint methods do NOT need to start comparing Hyperlink for this case; only this
    // allowlist needed updating, so the guard keeps working for the NEXT command that sets a
    // drawing/chart Hyperlink outside of DuplicateSheetDrawingCloner's sheet-duplication path (where the
    // same argument would not apply).
    // Note: HyperlinkAssignmentPattern's RHS capture group ([^,;}]+) stops at the FIRST comma, so for a
    // multi-argument call like `RewriteSameSheetHyperlinkTarget(cloned.Hyperlink, source.Name, copy.Name)`
    // the captured `rhs` is only the truncated `RewriteSameSheetHyperlinkTarget(cloned.Hyperlink` prefix
    // (no closing paren, no trailing arguments) -- matched here accordingly.
    private static readonly Regex AllowedRewriteSameSheetHyperlinkTargetRhs = new(
        @"^RewriteSameSheetHyperlinkTarget\(\w+(\.\w+)*\.Hyperlink$",
        RegexOptions.Compiled);

    // REVIEWED-AND-SAFE (R108-rename-delete-sheet-drawing-hyperlink-rewrite audit): this guard
    // fired again when R107 made RenameSheetCommand/RemoveSheetCommand's Apply methods start
    // assigning `shape.Hyperlink = rewrittenDrawingObjectHyperlink` (and the TextBox/Picture/Chart
    // siblings) in SheetCommands.cs -- a computed (non-copy-forward) value produced by the
    // file-local DrawingObjectHyperlinkRewriter.Rewrite helper, breaking the "no command can SET a
    // hyperlink" premise again, the same way DuplicateSheetDrawingCloner's rebase did in R106.
    // AUDITED: still NOT a patch-safety hole, but for a DIFFERENT reason than R106's duplicate-sheet
    // case (a rename/delete is a different operation than a duplication and reaches the patch-path
    // guard through a different check -- this was traced concretely, not assumed from R106's
    // conclusion). PackageAllowsCellPatchSave (XlsxFileAdapter.SourcePackageSnapshot.cs) builds
    // `sheetsByWorksheetPath` by looking up each LIVE `sheet.Name` in the source package's
    // `worksheetPathMap.SheetPathsByName` (keyed by the sheet names as they were in the PRISTINE
    // baseline, i.e. before this session's edits). A rename changes the live Sheet.Name but the
    // baseline map still has the OLD name as its key, so that lookup unconditionally fails with
    // blockReason "package_guard_sheet_path_missing", forcing the full ClosedXML rebuild (which
    // recomputes drawing/chart XML from the current model, including the now-rewritten Hyperlink)
    // for the WHOLE workbook every time ANY sheet has been renamed -- independent of whether a
    // hyperlink was touched at all. A delete forces the same outcome even more directly: the
    // deleted sheet's own worksheetN.xml entry survives in the source archive with no matching live
    // sheet, so the unmatched-worksheet-part sweep over `archive.Entries` fails with
    // "package_guard_unmatched_worksheet_part" first. Both are proven by EXISTING, already-green
    // tests independent of this audit -- R102_RenameSheetPreservedPartsTests's four cases each
    // assert `adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, "renaming a sheet
    // is a structural edit that must not go through the cell-value patch shortcut")` -- so the cheap
    // cell-patch path this guard's fingerprint actually gates is categorically unreachable for
    // either command whenever the rewrite could have fired. This was additionally verified directly
    // against the real product entry point (real Load -> RenameSheetCommand/RemoveSheetCommand ->
    // real Save) during this audit: a workbook with a shape hyperlink, renamed/deleted then saved,
    // reports `LastSaveDiagnostics.Path == XlsxSavePath.FullSave` in both cases. So the fingerprint
    // methods do NOT need to start comparing Hyperlink for this case either; only this allowlist
    // needed updating, so the guard keeps working for the NEXT command that sets a drawing/chart
    // Hyperlink outside of a full-rebuild-forcing sheet-identity operation.
    // The two RHS shapes below are the local variable names SheetCommands.cs uses for this pass,
    // deliberately spelled out as compound, feature-specific identifiers (NOT the generic bare
    // "rewritten"/"oldValue" R107 originally used) specifically so this allowlist entry stays
    // narrow: a genuinely new, unrelated non-copy-forward Hyperlink assignment elsewhere that
    // happens to also use a short generic local name would NOT match either pattern below and would
    // still trip this guard as intended.
    //
    // R109 (anchor redesign): r107 flagged, and r108 repeated without fixing, that keying an
    // exemption on RHS text alone is fragile -- a generic-enough identifier reused in a DIFFERENT,
    // unreviewed file would silently pass too, since the regex is matched against every file's RHS
    // text with no notion of WHERE the reviewed site was. Compound identifiers like the ones below
    // make an accidental collision unlikely, but "unlikely" is a probability argument standing in
    // for what should be a structural guarantee: SheetCommands.cs was reviewed on this line-generating
    // pattern, no other file was. So every entry below is now (fileName, rhsPattern) pair, and a
    // match is only exempted when BOTH the file matches AND the RHS matches -- the same rhs text
    // appearing in a new, unreviewed file trips the guard exactly like a genuinely new pattern would.
    // (AllowedCopyForwardRhs above stays global/unanchored deliberately: it is not an audited
    // one-off exemption but a STRUCTURAL safety proof -- "the RHS is some object's own existing
    // Hyperlink property" is safe by construction everywhere, not because any particular file's use
    // of it was reviewed.)
    private static readonly (string FileName, Regex RhsPattern)[] AnchoredReviewedExemptions =
    [
        // R106-drawing-object-hyperlink-duplicate-rebase (see audit note above): DuplicateSheetDrawingCloner
        // is the only file that legitimately computes a rebased same-sheet hyperlink target this way.
        ("DuplicateSheetDrawingCloner.cs", AllowedRewriteSameSheetHyperlinkTargetRhs),
        // R108-rename-delete-sheet-drawing-hyperlink-rewrite (see audit note above): both the rewrite
        // and its Undo/Revert restore are SheetCommands.cs-only local-variable names.
        ("SheetCommands.cs", new Regex(@"^rewrittenDrawingObjectHyperlink$", RegexOptions.Compiled)),
        ("SheetCommands.cs", new Regex(@"^savedDrawingObjectHyperlink$", RegexOptions.Compiled)),
    ];

    [Fact]
    public void NoCommandAssignsANewDrawingOrChartHyperlinkValue_WithoutUpdatingThePatchSafetyFingerprint()
    {
        var commandsDirectory = Path.Combine(FindRepositoryRoot(), "src", "FreeX.Core.Commands");
        Directory.Exists(commandsDirectory).Should().BeTrue($"expected {commandsDirectory} to exist");

        var violations = ScanForViolations(commandsDirectory);

        violations.Should().BeEmpty(
            "no FreeX.Core.Commands command may set a new drawing/chart Hyperlink value until the " +
            "patch-safety fingerprint covers it (see this test's class-level doc comment):\n" +
            string.Join("\n", violations));
    }

    /// <summary>
    /// The actual production scan: enumerates every *.cs file under <paramref name="commandsDirectory"/>
    /// and reports every non-copy-forward <c>Hyperlink =</c> assignment. Extracted so the R102 regression
    /// tests below can drive this EXACT code path (not a hand-rolled duplicate that could silently drift
    /// out of sync with it) against synthetic fixture files.
    /// </summary>
    private static System.Collections.Generic.List<string> ScanForViolations(string commandsDirectory)
    {
        var violations = new System.Collections.Generic.List<string>();

        foreach (var filePath in Directory.EnumerateFiles(commandsDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (filePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                filePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            if (UnrelatedHyperlinkIdentifierFiles.Contains(Path.GetFileName(filePath)))
                continue;

            // Matched against the FULL FILE TEXT (not File.ReadAllLines + per-line matching) so a
            // multi-line RHS is still caught even when its terminator (`,`/`;`/`}`) lands on a
            // different line than the `Hyperlink =` token itself (R102-multiline-hyperlink-guard-scan).
            // Comments are stripped first: once matching spans the whole file, a `//` comment (or a
            // doc-comment example) that happens to mention "Hyperlink = ..." with no terminator on its
            // own line would otherwise bleed into the NEXT real code line's terminator and get
            // misreported as a violation (or, worse, swallow a real one). Line structure (and therefore
            // line-number bookkeeping below) is preserved by blanking rather than deleting text.
            var fileName = Path.GetFileName(filePath);
            var text = StripCommentsPreservingLineNumbers(File.ReadAllText(filePath));
            foreach (Match match in HyperlinkAssignmentPattern.Matches(text))
            {
                var rhs = match.Groups[1].Value.Trim();
                if (AllowedCopyForwardRhs.IsMatch(rhs))
                    continue;

                // R109 (anchor redesign): an entry only exempts a match when its file name ALSO
                // matches -- the same RHS text in a different, unreviewed file still trips the guard.
                var anchoredMatch = false;
                foreach (var (exemptFileName, rhsPattern) in AnchoredReviewedExemptions)
                {
                    if (!string.Equals(fileName, exemptFileName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!rhsPattern.IsMatch(rhs))
                        continue;
                    anchoredMatch = true;
                    break;
                }
                if (anchoredMatch)
                    continue;

                var lineNumber = text.AsSpan(0, match.Index).Count('\n') + 1;
                var snippet = match.Value.Trim().Replace('\n', ' ').Replace('\r', ' ');

                violations.Add(
                    $"{Path.GetFileName(filePath)}:{lineNumber}: `{snippet}` -- assigns a " +
                    "non-copy-forward value to a Hyperlink property. If this is a NEW " +
                    "DrawingObjectHyperlink/ChartModel.Hyperlink mutation capability, " +
                    "WriteDrawingChartFingerprint/WriteDrawingPictureFingerprint/" +
                    "WriteDrawingTextBoxFingerprint/WriteDrawingShapeFingerprint in " +
                    "XlsxFileAdapter.SourcePackageSnapshot.cs must be updated to compare the " +
                    "Hyperlink field BEFORE this command ships, or a cell-patch save elsewhere in " +
                    "the same file will silently discard the hyperlink change.");
            }
        }

        return violations;
    }

    // Matches a `/* ... */` block comment (including `/** ... */` doc-comment blocks), across lines.
    private static readonly Regex BlockCommentPattern = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches a `//` (or `///`) line comment to end-of-line. The negative lookbehind for `:` avoids
    // treating a `://` inside a string literal (e.g. a `"https://..."` URL) as a comment start -- good
    // enough for this heuristic scan since none of today's files pair a URL literal with an unterminated
    // `Hyperlink =` on the same line.
    private static readonly Regex LineCommentPattern = new(@"(?<!:)//.*", RegexOptions.Compiled);

    /// <summary>
    /// Blanks out comment text so it can never be mistaken for code, while preserving every original
    /// newline (and therefore line count) so line-number reporting on the caller's match indices stays
    /// accurate.
    /// </summary>
    private static string StripCommentsPreservingLineNumbers(string text)
    {
        text = BlockCommentPattern.Replace(text, m => new string('\n', m.Value.Count(c => c == '\n')));
        text = LineCommentPattern.Replace(text, string.Empty);
        return text;
    }

    /// <summary>
    /// Internal seam used by <see cref="R102_DrawingChartHyperlinkPatchSafetyGuardMultilineScanTests"/>
    /// to drive the real scan against synthetic fixture directories.
    /// </summary>
    internal static System.Collections.Generic.List<string> ScanForViolationsForTesting(string commandsDirectory)
        => ScanForViolations(commandsDirectory);

    /// <summary>
    /// No-regression sibling: proves the scan itself actually inspects real files and isn't vacuously
    /// passing over an empty/misconfigured directory enumeration.
    /// </summary>
    [Fact]
    public void Scan_ActuallyExaminesKnownCopyForwardSites()
    {
        var commandsDirectory = Path.Combine(FindRepositoryRoot(), "src", "FreeX.Core.Commands");
        var clonerPath = Path.Combine(commandsDirectory, "DuplicateSheetDrawingCloner.cs");
        File.Exists(clonerPath).Should().BeTrue();

        var content = File.ReadAllText(clonerPath);
        content.Should().Contain("Hyperlink = shape.Hyperlink");
        content.Should().Contain("Hyperlink = chart.Hyperlink");

        // Confirm the allowed-copy-forward regex actually matches these known-safe sites (otherwise
        // the main guard test above would be flagging them as false-positive violations).
        AllowedCopyForwardRhs.IsMatch("shape.Hyperlink").Should().BeTrue();
        AllowedCopyForwardRhs.IsMatch("chart.Hyperlink").Should().BeTrue();
        AllowedCopyForwardRhs.IsMatch("new DrawingObjectHyperlink(target, mode, tooltip)").Should().BeFalse();
        AllowedCopyForwardRhs.IsMatch("null").Should().BeFalse();
    }

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
