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
/// </summary>
public sealed class R101_DrawingChartHyperlinkPatchSafetyGuardTests
{
    // Matches a `Hyperlink = <rhs>` assignment (object-initializer style `Hyperlink = expr,` or a plain
    // statement `x.Hyperlink = expr;`), capturing the right-hand side. Word-bounded so it never matches
    // `HyperlinkMetadata = ...` or `Hyperlinks[...] = ...` (unrelated cell-hyperlink fields). The
    // negative lookahead excludes `Hyperlink => ...` (a switch/pattern arm, e.g.
    // `ConditionalFormulaScalarFunctionKind.Hyperlink => ...`) and `Hyperlink == ...` (an equality
    // comparison), neither of which is an assignment.
    private static readonly Regex HyperlinkAssignmentPattern = new(
        @"\bHyperlink\s*=(?![=>])\s*([^,;]+)[,;]",
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

    [Fact]
    public void NoCommandAssignsANewDrawingOrChartHyperlinkValue_WithoutUpdatingThePatchSafetyFingerprint()
    {
        var commandsDirectory = Path.Combine(FindRepositoryRoot(), "src", "FreeX.Core.Commands");
        Directory.Exists(commandsDirectory).Should().BeTrue($"expected {commandsDirectory} to exist");

        var violations = new System.Collections.Generic.List<string>();

        foreach (var filePath in Directory.EnumerateFiles(commandsDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (filePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                filePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            if (UnrelatedHyperlinkIdentifierFiles.Contains(Path.GetFileName(filePath)))
                continue;

            var lines = File.ReadAllLines(filePath);
            for (var lineNumber = 0; lineNumber < lines.Length; lineNumber++)
            {
                var line = lines[lineNumber];
                foreach (Match match in HyperlinkAssignmentPattern.Matches(line))
                {
                    var rhs = match.Groups[1].Value.Trim();
                    if (AllowedCopyForwardRhs.IsMatch(rhs))
                        continue;

                    violations.Add(
                        $"{Path.GetFileName(filePath)}:{lineNumber + 1}: `{line.Trim()}` -- assigns a " +
                        "non-copy-forward value to a Hyperlink property. If this is a NEW " +
                        "DrawingObjectHyperlink/ChartModel.Hyperlink mutation capability, " +
                        "WriteDrawingChartFingerprint/WriteDrawingPictureFingerprint/" +
                        "WriteDrawingTextBoxFingerprint/WriteDrawingShapeFingerprint in " +
                        "XlsxFileAdapter.SourcePackageSnapshot.cs must be updated to compare the " +
                        "Hyperlink field BEFORE this command ships, or a cell-patch save elsewhere in " +
                        "the same file will silently discard the hyperlink change.");
                }
            }
        }

        violations.Should().BeEmpty(
            "no FreeX.Core.Commands command may set a new drawing/chart Hyperlink value until the " +
            "patch-safety fingerprint covers it (see this test's class-level doc comment):\n" +
            string.Join("\n", violations));
    }

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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root (FreeX.slnx) above " + AppContext.BaseDirectory);
    }
}
