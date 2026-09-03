using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r250: a coverage contract that uses the type's OWN definition of itself as the field list.
/// <para>
/// <c>DataValidation.Clone</c> is the maintained enumeration of what a conditional-format rule
/// consists of -- it has to be, or cloning would lose data. So rather than reflect over the type and
/// maintain an exemption list, this contract compares <c>SameAs</c> against <c>Clone</c>: every
/// member Clone assigns must appear in the comparison. A member added to the type reaches Clone
/// because cloning would otherwise be wrong, and from there this contract carries it into SameAs.
/// </para>
/// <para>
/// That is cheaper and stronger than r234's and r248's reflection contracts for a type this size:
/// sixty members, no exemption list to keep honest, and the source of truth is code that already
/// has to be right for an unrelated reason.
/// </para>
/// </summary>
public sealed class R250_DataValidationComparisonCoverageContractTests
{
    [Fact]
    public void SameAsComparesEveryMemberCloneCopies()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Model"),
            "DataValidation.cs"));

        var cloneBody = MemberBody(source, source.IndexOf("public DataValidation CloneForRanges(", StringComparison.Ordinal));
        var sameBody = MemberBody(source, source.IndexOf("public bool SameAs(", StringComparison.Ordinal));

        cloneBody.Should().NotBeNullOrEmpty("Clone must exist for this contract to have a field list");
        sameBody.Should().NotBeNullOrEmpty("SameAs must exist for this contract to check anything");

        var cloned = new Regex(@"(?:^\s+|clone\.)([A-Za-z]\w*)(?: = |\.AddRange)", RegexOptions.Multiline)
            .Matches(cloneBody!)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        cloned.Should().HaveCountGreaterThan(15,
            "a tiny field list would make this pass while guarding nothing");

        var missing = cloned
            .Where(name => !Regex.IsMatch(sameBody!, @"\b" + Regex.Escape(name) + @"\b"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "Clone copies these members because losing them would corrupt a cloned rule, so SameAs "
            + "ignoring them means a no-op decision that calls a changed rule unchanged. Missing:\n"
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
