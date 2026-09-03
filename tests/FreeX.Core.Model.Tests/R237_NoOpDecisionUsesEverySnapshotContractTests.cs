using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r237: the invariant, stated as a contract.
/// <para>
/// A command's UNDO snapshots are, by construction, the complete record of everything it writes --
/// that is what makes undo correct. So a command's no-op decision is complete exactly when it
/// consults every one of them, and incomplete the moment it skips one. Undo-completeness and
/// no-op-completeness are the same list, which turns "be careful" into a mechanical check.
/// </para>
/// <para>
/// r240 changed two things after both bit. The scan is now scoped to the CLASS rather than the file,
/// because several command classes share a file and a file-scoped scan would demand that one
/// command's decision mention another command's fields. And the field pattern now matches
/// <c>_previous*</c> as well as <c>_*Snapshot</c>: those are the same thing under a different name,
/// and matching only one spelling meant a command could pass by naming its snapshot the other way.
/// </para>
/// <para>
/// The list is opt-in, so it ratchets the commands that have adopted a decision method rather than
/// claiming anything about the ones that have not -- which also means a green result here says
/// nothing about a command until you have confirmed the command is IN the list (r238).
/// </para>
/// </summary>
public sealed class R237_NoOpDecisionUsesEverySnapshotContractTests
{
    /// <summary>Command class -> the method whose body must consult every snapshot field.</summary>
    private static readonly Dictionary<string, string> DecisionMethods = new()
    {
        ["FillCellsCommand"] = "NothingChanged",
        ["AutofillCommand"] = "NothingChanged",
        ["GroupedApplyStyleCommand"] = "NothingChanged",
        ["ApplyStyleCommand"] = "NothingChanged",
        ["ApplyCustomViewCommand"] = "NothingChanged",
        ["SetPageSetupCommand"] = "NothingChanged",
        ["SetHeaderFooterCommand"] = "NothingChanged",
        ["SetRowHeightCommand"] = "NothingChanged",
        ["SetColumnWidthCommand"] = "NothingChanged",
        ["RefreshStructuredTableTotalsCommand"] = "NothingChanged",
        ["AverageFilterCommand"] = "NothingChanged",
        ["TopBottomFilterCommand"] = "NothingChanged",
        ["FilterConditionCommand"] = "NothingChanged",
        ["CellFillColorFilterCommand"] = "NothingChanged",
        ["CellNoFillColorFilterCommand"] = "NothingChanged",
        ["CellFontColorFilterCommand"] = "NothingChanged",
        ["FilterCommand"] = "NothingChanged",
        ["AdvancedFilterCommand"] = "NothingChanged",
        ["ConfigurePivotTableFieldFiltersCommand"] = "NothingChanged",
        ["ConfigurePivotTableViewCommand"] = "NothingChanged",
        ["ConfigurePivotTableLayoutCommand"] = "NothingChanged",
        ["ConfigurePivotTableCalculatedItemsCommand"] = "NothingChanged",
        ["ClearPivotTableViewCommand"] = "NothingChanged",
        ["MovePivotTableCommand"] = "NothingChanged",
        ["DataTableBodyRefreshCommand"] = "NothingChanged",
        ["ExternalTextPasteSpecialCommand"] = "NothingChanged",
        ["SaveScenarioCommand"] = "NothingChanged",
        ["SaveCustomViewCommand"] = "NothingChanged",
        ["ConfigurePivotChartOptionsCommand"] = "NothingChanged",
        ["ConfigurePivotTableOptionsCommand"] = "NothingChanged",
        ["PasteColumnWidthsCommand"] = "NothingChanged",
        ["SetHyperlinkCommand"] = "NothingChanged",
        ["MergeCellsCommand"] = "NothingChanged",
    };

    [Fact]
    public void EverySnapshotFieldParticipatesInTheNoOpDecision()
    {
        var directory = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands");
        var sources = Directory.GetFiles(directory, "*.cs").Select(File.ReadAllText).ToList();

        foreach (var (className, methodName) in DecisionMethods)
        {
            var body = ClassBody(sources, className);
            body.Should().NotBeNullOrEmpty($"{className} must exist for this contract to check it");

            var fields = new Regex(
                    @"private\s+[^;=]*?\b(_(?:\w*[Ss]napshot|previous\w*|old\w*|hadOld\w*))\s*(?:=|;)",
                    RegexOptions.Compiled)
                .Matches(body!)
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            fields.Should().NotBeEmpty(
                $"{className} must declare snapshot fields for this contract to check anything");

            var start = body!.IndexOf($"private bool {methodName}(", StringComparison.Ordinal);
            start.Should().BeGreaterThan(0, $"{className} must contain {methodName}");
            var decision = MemberBody(body, start);

            var unconsulted = fields
                .Where(field => !decision.Contains(field, StringComparison.Ordinal))
                .OrderBy(field => field, StringComparer.Ordinal)
                .ToList();

            unconsulted.Should().BeEmpty(
                $"{methodName} decides whether {className} changed anything, and a snapshot field it "
                + "does not look at is a thing the command writes and the decision ignores -- which "
                + "reports \"nothing changed\" for an edit that happened. Unconsulted:\n"
                + string.Join("\n", unconsulted));
        }
    }

    /// <summary>The source text of one class, from its declaration to the next top-level one.</summary>
    private static string? ClassBody(IEnumerable<string> sources, string className)
    {
        foreach (var source in sources)
        {
            var match = new Regex(
                    @"\bclass\s+" + className + @"\b.*?(?=\n(?:public|internal)\s+(?:sealed\s+)?(?:partial\s+)?class\s|\z)",
                    RegexOptions.Singleline)
                .Match(source);

            if (match.Success)
                return match.Value;
        }

        return null;
    }

    /// <summary>
    /// r240: one member's text, from its signature to the end of its body -- by brace matching, not
    /// by looking for the next member.
    /// <para>
    /// The first version searched forward for the next <c>private</c> declaration, and when the
    /// decision method was the LAST private member of its class that search found nothing, so the
    /// slice ran to the end of the class and swept in <c>Revert</c>. Revert touches every snapshot by
    /// definition, so the contract passed for a decision method that consulted none of them. It was
    /// checking the wrong text and reporting success -- caught by deleting a clause and watching it
    /// stay green.
    /// </para>
    /// </summary>
    private static string MemberBody(string source, int start)
    {
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
            else if (source[index] == ';' && !opened)
            {
                // An expression-bodied member ends at its semicolon, with no braces at all.
                return source[start..(index + 1)];
            }
        }

        return source[start..];
    }
}
