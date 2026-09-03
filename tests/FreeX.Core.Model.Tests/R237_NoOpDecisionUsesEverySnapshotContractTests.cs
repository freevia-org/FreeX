using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r237: the invariant this round found, stated as a contract.
/// <para>
/// A command's UNDO snapshots are, by construction, the complete record of everything it writes --
/// that is what makes undo correct. So a command's no-op decision is complete exactly when it
/// consults every one of them, and incomplete the moment it skips one. Undo-completeness and
/// no-op-completeness are the same list.
/// </para>
/// <para>
/// That gives a mechanical check where there was only care before: for a command whose no-op
/// decision is expected to be complete, every <c>_*Snapshot</c>-style field it declares must be
/// referenced by the method that makes the decision. Adding a sixth snapshot without extending the
/// comparison compiles cleanly and silently narrows the guard -- this fails instead.
/// </para>
/// <para>
/// The list is opt-in rather than universal because most commands do not have a decision method to
/// point at yet; entries are added as commands adopt one. That makes this a ratchet on the commands
/// that HAVE been done, not a claim about the ones that have not.
/// </para>
/// </summary>
public sealed class R237_NoOpDecisionUsesEverySnapshotContractTests
{
    /// <summary>Command file -> the method whose body must consult every snapshot field.</summary>
    private static readonly Dictionary<string, string> DecisionMethods = new()
    {
        ["FillCellsCommand.cs"] = "NothingChanged",
        ["AutofillCommand.cs"] = "NothingChanged",
        ["GroupedApplyStyleCommand.cs"] = "NothingChanged",
    };

    [Fact]
    public void EverySnapshotFieldParticipatesInTheNoOpDecision()
    {
        var directory = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands");

        foreach (var (fileName, methodName) in DecisionMethods)
        {
            var source = File.ReadAllText(Path.Combine(directory, fileName));

            var fields = new Regex(@"private\s+[^;=]*?\b(_\w*[Ss]napshot)\s*;", RegexOptions.Compiled)
                .Matches(source)
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            fields.Should().NotBeEmpty(
                $"{fileName} must declare snapshot fields for this contract to check anything");

            var start = source.IndexOf($"private bool {methodName}(", StringComparison.Ordinal);
            start.Should().BeGreaterThan(0, $"{fileName} must contain {methodName}");
            var end = source.IndexOf("\r\n    private ", start + 1, StringComparison.Ordinal);
            var body = end > start ? source[start..end] : source[start..];

            var unconsulted = fields
                .Where(field => !body.Contains(field, StringComparison.Ordinal))
                .OrderBy(field => field, StringComparer.Ordinal)
                .ToList();

            unconsulted.Should().BeEmpty(
                $"{methodName} decides whether {fileName} changed anything, and a snapshot field it "
                + "does not look at is a thing the command writes and the decision ignores -- which "
                + "reports \"nothing changed\" for an edit that happened. Unconsulted:\n"
                + string.Join("\n", unconsulted));
        }
    }
}
