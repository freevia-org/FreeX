using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r252: the coverage contract behind FilterUndoSnapshot.Matches, on the r249 shape.
/// <para>
/// Capture is the maintained list of what filter state consists of -- it has to be, or undo would
/// lose some of it -- so it is the field list, and this contract fails if Capture reads a sheet
/// member that Matches does not. Eight commands are meant to depend on Matches, so a member added to
/// Capture and forgotten there would be a partial mirror eight times over.
/// </para>
/// </summary>
public sealed class R252_FilterSnapshotComparisonCoverageContractTests
{
    [Fact]
    public void MatchesComparesEverySheetMemberCaptureReads()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands"),
            "FilterCommand.cs"));

        var captureBody = MemberBody(source, source.IndexOf("public void Capture(Sheet sheet)", StringComparison.Ordinal));
        var matchesBody = MemberBody(source, source.IndexOf("public readonly bool Matches(Sheet sheet)", StringComparison.Ordinal));

        captureBody.Should().NotBeNullOrEmpty("Capture must exist for this contract to have a field list");
        matchesBody.Should().NotBeNullOrEmpty("Matches must exist for this contract to check anything");

        var read = new Regex(@"sheet\.([A-Za-z]\w*)", RegexOptions.Compiled)
            .Matches(captureBody!)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        read.Should().HaveCountGreaterThan(3,
            "a tiny field list would make this pass while guarding nothing");

        var missing = read
            .Where(name => !matchesBody!.Contains("sheet." + name, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "Capture snapshots these members because undo needs them, so Matches ignoring one means "
            + "a filter command reporting \"nothing changed\" for state that did change. Missing:\n"
            + string.Join("\n", missing));
    }

    private static string? MemberBody(string source, int start)
    {
        if (start < 0)
            return null;

        var depth = 0;
        var opened = false;
        for (var index = start; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
                opened = true;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (opened && depth == 0)
                    return source[start..(index + 1)];
            }
        }

        return null;
    }
}
