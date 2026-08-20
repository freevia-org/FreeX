using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// A tripwire for the legacy-CSE-array-identity class, which took five rounds and nine call sites
/// to close.
///
/// <para>
/// The root is that <c>Cell.FormulaText</c>'s setter unconditionally clears
/// <c>ArrayMode</c>/<c>LegacyArrayRows</c>/<c>LegacyArrayCols</c>. That is correct for authoring a
/// NEW formula and wrong for every path that rewrites an EXISTING one -- a reference fixup after an
/// insert, a clone during copy/paste/fill/sort, a structured-reference lowering, a patch-save
/// revert. Each such path has to preserve the extent explicitly, and each time one was missed the
/// symptom was the same: a Ctrl+Shift+Enter array silently became a scalar, its extra cells went
/// blank on recalc, and <c>CommandGuards.RejectIfSplitsArray</c> stopped protecting it so a later
/// overwrite was allowed with no warning.
/// </para>
///
/// <para>
/// Every round that tried to close this class by fixing the sites it knew about was followed by a
/// round that found more, because a search for callers of the preserving helper cannot see a site
/// that assigns the property directly. So this test inverts it: assignments are enumerated from the
/// source, and any file not on the allowlist below fails. It does not assert that the listed files
/// are correct -- it asserts that no NEW file joins them without someone deciding it should.
/// </para>
///
/// <para>
/// If this test fails for a file you just wrote: use
/// <c>RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity</c> if you are rewriting an
/// existing formula, and only add yourself here if you are genuinely authoring a new one or
/// clearing to a literal.
/// </para>
/// </summary>
public sealed class R155_FormulaTextAssignmentAllowlistTests
{
    /// <summary>
    /// Files permitted to assign <c>Cell.FormulaText</c> directly, each with the reason it is not a
    /// rewrite that must preserve array identity. Keep the reasons: they are what makes a future
    /// addition to this list a decision rather than a formality.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sheet.cs"] = "authoring: SetFormula is where a new formula legitimately resets array identity",
        ["Sheet.Clone.cs"] = "clone: saves and restores the array fields around the assignment",
        ["CellStateSnapshot.cs"] = "restore: reassigns the array fields explicitly after the text",
        ["RowColumnShiftHelpers.Formulas.cs"] = "the preserving helper itself",
        ["XlsxFileAdapter.SourcePackageSnapshot.cs"] = "patch-save revert: restores the extent it captured",
        ["PasteCommandFactory.cs"] = "clear-to-literal: assigns null when a paste computes a value",
        ["PasteSpecialCommand.cs"] = "clear-to-literal: assigns null when an operation computes a value",
        ["ConsolidateCommand.cs"] = "authoring: writes fresh consolidation formulas into new cells",
        ["FindReplaceService.cs"] = "guarded upstream: EditCellsCommand rejects editing a legacy array anchor",
        ["InsertCopiedCellsPlanner.cs"] = "routes through the preserving helper",
    };

    // Deliberately not anchored to a receiver name: the point is to catch an assignment written in
    // a file nobody thought about, whatever the local variable happens to be called.
    // The negative lookahead matters: without it this also matches `=>` switch arms and `==`
    // comparisons, which is how the first version of this test flagged an accessibility checker
    // that only names the property in a pattern match.
    private static readonly Regex Assignment = new(@"\.FormulaText\s*=(?![=>])", RegexOptions.Compiled);

    /// <summary>
    /// <c>ConditionalFormat</c> and <c>DataValidation</c> have their own unrelated
    /// <c>FormulaText</c> properties. A source-text tripwire cannot resolve types, so those are
    /// filtered by receiver name and by file name -- an honest heuristic, not a precise one. The
    /// consequence to know: a cell assignment written through a receiver named <c>cf</c>,
    /// <c>rule</c> or <c>validation</c>, or living in a file whose name mentions conditional
    /// formatting, would slip past. That trade is deliberate -- the alternative is a compiler-level
    /// analyser, and the failure this guards is a NEW file quietly joining the set, which this
    /// catches.
    /// </summary>
    private static bool IsNotACellFormulaText(string fileName, string line) =>
        fileName.Contains("ConditionalFormat", StringComparison.OrdinalIgnoreCase)
        || fileName.Contains("DataValidation", StringComparison.OrdinalIgnoreCase)
        || line.Contains("cf.FormulaText", StringComparison.OrdinalIgnoreCase)
        || line.Contains("rule.FormulaText", StringComparison.OrdinalIgnoreCase)
        || line.Contains("validation.FormulaText", StringComparison.OrdinalIgnoreCase)
        || line.Contains("ConditionalFormat", StringComparison.OrdinalIgnoreCase)
        || line.Contains("DataValidation", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void NoUnreviewedFileAssignsCellFormulaTextDirectly()
    {
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var root in new[] { "src", "shared" })
        {
            var directory = TestWorkspaceFileLocator.TryFindDirectoryFromBaseDirectory(root);
            if (directory is null)
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                var name = Path.GetFileName(file);
                if (Allowed.ContainsKey(name))
                    continue;

                foreach (var line in File.ReadLines(file))
                {
                    if (!Assignment.IsMatch(line))
                        continue;
                    if (IsNotACellFormulaText(name, line))
                        continue;

                    offenders.Add(name + ": " + line.Trim());
                    break;
                }
            }
        }

        offenders.Should().BeEmpty(
            "a file outside the reviewed set assigns Cell.FormulaText directly, which silently "
            + "clears a legacy array's extent. Use "
            + "RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity when rewriting an "
            + "existing formula, or add the file to this test's allowlist with the reason it is "
            + "genuinely authoring or clearing one");
    }

    [Fact]
    public void TheAllowlistDoesNotNameFilesThatNoLongerAssignFormulaText()
    {
        // A stale allowlist entry is a hole: it would silently permit a future assignment in a file
        // that no longer needs the exemption.
        var stillAssigning = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var root in new[] { "src", "shared" })
        {
            var directory = TestWorkspaceFileLocator.TryFindDirectoryFromBaseDirectory(root);
            if (directory is null)
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                var name = Path.GetFileName(file);
                if (!Allowed.ContainsKey(name))
                    continue;

                if (File.ReadLines(file).Any(line => Assignment.IsMatch(line) && !IsNotACellFormulaText(name, line)))
                    stillAssigning.Add(name);
            }
        }

        var stale = Allowed.Keys.Where(name => !stillAssigning.Contains(name)).OrderBy(n => n, StringComparer.Ordinal);
        stale.Should().BeEmpty("these allowlist entries no longer assign FormulaText and should be removed");
    }
}
