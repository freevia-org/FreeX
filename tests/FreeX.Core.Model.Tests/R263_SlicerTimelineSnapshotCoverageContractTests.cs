using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r263: the coverage contract for the slicer and timeline snapshots' <c>Matches</c> -- written
/// BEFORE the comparisons this round adds, which is the order r262 established the hard way. The one
/// comparison in this program that shipped without a contract is the one that was wrong, twice.
///
/// <para>The field list comes from each snapshot's own <c>Capture</c>, on the r249 pattern: Capture
/// has to be complete or undo would lose slicer state, so it is the maintained enumeration of what
/// the snapshot consists of. A member Capture records but Matches ignores is a thing the command
/// writes and the no-op decision cannot see.</para>
/// </summary>
public sealed class R263_SlicerTimelineSnapshotCoverageContractTests
{
    public static TheoryData<string, string> Snapshots() => new()
    {
        { "PivotTableSlicerCommands.cs", "SlicerSelectionSnapshot" },
        { "PivotTableSlicerCommands.cs", "TableSlicerSelectionSnapshot" },
        { "PivotTableSlicerTimelineCommands.cs", "TimelineRangeSnapshot" },
    };

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void MatchesComparesEveryMemberCaptureRecords(string fileName, string recordName)
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands"),
            fileName));

        var recordBody = RecordBody(source, recordName);
        recordBody.Should().NotBeNullOrEmpty($"{recordName} must exist for this contract to check it");

        // The record's positional parameters ARE its members, and Capture supplies them in order.
        var declared = new Regex(@"^\s{8}(?:[A-Za-z][\w<>,.\?\s]*?)\s([A-Z]\w*)[,\)]", RegexOptions.Multiline)
            .Matches(recordBody![..(recordBody.IndexOf(')') + 1)])
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        declared.Should().HaveCountGreaterThan(2,
            "a short member list would mean the parse broke and this contract passed while guarding nothing");

        var matchesBody = MemberBody(recordBody, recordBody.IndexOf("public bool Matches(", StringComparison.Ordinal));
        matchesBody.Should().NotBeNullOrEmpty($"{recordName}.Matches must exist for this contract to check anything");

        var missing = declared
            .Where(name => !Regex.IsMatch(matchesBody!, @"\b" + Regex.Escape(name) + @"\b"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            $"{recordName} records these because undo would otherwise lose them, so Matches ignoring "
            + "one means a no-op decision that calls a changed slicer unchanged. Missing:\n"
            + string.Join("\n", missing));
    }

    private static string? RecordBody(string source, string recordName)
    {
        var start = source.IndexOf($"private sealed record {recordName}(", StringComparison.Ordinal);
        if (start < 0)
            return null;

        var next = source.IndexOf("\n    private sealed record ", start + 1, StringComparison.Ordinal);
        var end = source.IndexOf("\n}", start + 1, StringComparison.Ordinal);
        if (next >= 0 && (end < 0 || next < end))
            return source[start..next];
        return end < 0 ? source[start..] : source[start..end];
    }

    private static string? MemberBody(string source, int start)
    {
        if (start < 0)
            return null;

        var open = source.IndexOf('{', start);
        var semicolon = source.IndexOf(';', start);
        if (semicolon >= 0 && (open < 0 || semicolon < open))
            return source[start..semicolon];
        if (open < 0)
            return null;

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(i + 1)];
            }
        }

        return null;
    }
}
