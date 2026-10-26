using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r264: the coverage contract for <c>PivotSourceSnapshot.Matches</c>, on the r249 pattern -- the
/// record's own <c>Capture</c> is the field list, because Capture has to be complete or undo would
/// lose the pivot's source binding.
///
/// <para>This snapshot has the largest member count of any in the no-op program that is compared
/// member by member rather than by re-capture, and one of its members -- <c>OriginalCache</c> -- is
/// deliberately compared by IDENTITY rather than content, which is the kind of exception that rots
/// quietly. The contract does not care why a member is compared, only that it is.</para>
/// </summary>
public sealed class R264_PivotSourceSnapshotCoverageContractTests
{
    [Fact]
    public void MatchesComparesEveryMemberCaptureRecords()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands"),
            "PivotTableCalculatedAndSourceCommands.cs"));

        var start = source.IndexOf("private sealed record PivotSourceSnapshot(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "PivotSourceSnapshot must exist for this contract to check it");

        var declared = new Regex(@"^\s{8}(?:[A-Za-z][\w<>,.\?\s]*?)\s([A-Z]\w*)[,\)]", RegexOptions.Multiline)
            .Matches(source[start..(source.IndexOf(')', start) + 1)])
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        declared.Should().HaveCountGreaterThan(6,
            "a short member list would mean the parse broke and this contract passed while guarding "
            + "nothing -- the failure mode r263's own parser hit");

        var matchesStart = source.IndexOf("public bool Matches(PivotTableModel pivotTable, PivotCacheModel", start, StringComparison.Ordinal);
        matchesStart.Should().BeGreaterThan(0, "Matches must exist for this contract to check anything");
        var matchesBody = source[matchesStart..source.IndexOf(';', matchesStart)];

        var missing = declared
            .Where(name => !Regex.IsMatch(matchesBody, @"\b" + Regex.Escape(name) + @"\b"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "Capture records these because undo would otherwise lose the pivot's source binding, so "
            + "Matches ignoring one means a no-op decision that calls a re-pointed pivot unchanged. "
            + "Missing:\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// The identity exception, pinned. <c>OriginalCache</c> is the live cache object whenever Apply
    /// mutates in place, so comparing its content against itself is vacuous -- r231 was right about
    /// that, and it is why this command sat on the debt for thirty rounds. What is NOT vacuous is its
    /// identity: crossing the table/range boundary swaps in a replacement cache. If someone
    /// "improves" this to a content comparison the guard silently starts reporting no-ops for a real
    /// source change, so the reference comparison is asserted rather than left to a comment.
    /// </summary>
    [Fact]
    public void TheCacheObjectIsComparedByIdentityNotContent()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands"),
            "PivotTableCalculatedAndSourceCommands.cs"));

        var matchesStart = source.IndexOf("public bool Matches(PivotTableModel pivotTable, PivotCacheModel", StringComparison.Ordinal);
        var matchesBody = source[matchesStart..source.IndexOf(';', matchesStart)];

        matchesBody.Should().Contain("ReferenceEquals(OriginalCache, currentCache)",
            "content-comparing the cache object against itself is always true and would make this "
            + "clause vacuous; identity is what distinguishes an in-place mutation from a swap");
    }
}
