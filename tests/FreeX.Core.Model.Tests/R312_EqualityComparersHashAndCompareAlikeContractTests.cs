using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r312: an <c>IEqualityComparer</c> must hash a string by the same rule it compares it by.
///
/// <para>r311 fixed a comparer that answered the wrong question. This guards the failure one step
/// worse: a comparer whose <c>Equals</c> and <c>GetHashCode</c> answer DIFFERENT questions. Two keys
/// that compare equal but hash differently land in different buckets, so a dictionary silently
/// stores duplicates and a lookup misses an entry that is provably there -- no exception, no wrong
/// value, just an item that cannot be found.</para>
///
/// <para>All eleven explicit string-hashing sites across the three apps currently pair correctly.
/// This exists so that stays true: the pairing is invisible at the call site, and the two halves are
/// written far enough apart to drift.</para>
/// </summary>
public sealed class R312_EqualityComparersHashAndCompareAlikeContractTests
{
    private static readonly Regex ComparerToken =
        new(@"String(?:Comparer|Comparison)\.(?<rule>[A-Za-z]+)", RegexOptions.Compiled);

    [Fact]
    public void EveryComparerHashesByTheRuleItComparesBy()
    {
        var root = RepositoryRoot();
        var examined = 0;
        var mismatches = new List<string>();

        foreach (var file in SourceFiles(root))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("IEqualityComparer", StringComparison.Ordinal)
                || !text.Contains("GetHashCode", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            var equalsRules = MemberRules(lines, "Equals");
            var hashRules = MemberRules(lines, "GetHashCode");
            if (equalsRules.Count == 0 && hashRules.Count == 0)
                continue;

            examined++;

            // Only a rule NAMED on both sides can be compared; a side that names none is using the
            // default rule for its member types, which this cannot see and must not guess about.
            if (equalsRules.Count == 0 || hashRules.Count == 0)
                continue;

            if (!equalsRules.SetEquals(hashRules))
            {
                mismatches.Add(
                    $"{Path.GetRelativePath(root, file)}: Equals uses [{string.Join(", ", equalsRules.Order())}] "
                    + $"but GetHashCode uses [{string.Join(", ", hashRules.Order())}]");
            }
        }

        examined.Should().BeGreaterThanOrEqualTo(5,
            "r312 measured eleven explicit string-hashing sites; if this stops finding comparers the "
            + "contract is passing vacuously");

        mismatches.Should().BeEmpty(
            "keys that compare equal must hash equal, or a dictionary stores duplicates and a lookup "
            + "misses an entry that is present:\n" + string.Join("\n", mismatches));
    }

    /// <summary>
    /// The comparison rules named inside every method with this name, from its signature to the line
    /// where its brace depth returns to zero.
    /// </summary>
    private static HashSet<string> MemberRules(string[] lines, string memberName)
    {
        var rules = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < lines.Length; i++)
        {
            if (!Regex.IsMatch(lines[i], $@"\b(?:int|bool)\s+{memberName}\s*[\(<]"))
                continue;

            var depth = 0;
            var opened = false;
            for (var j = i; j < lines.Length; j++)
            {
                foreach (Match match in ComparerToken.Matches(lines[j]))
                    rules.Add(match.Groups["rule"].Value);

                foreach (var c in lines[j])
                {
                    if (c == '{') { depth++; opened = true; }
                    else if (c == '}') depth--;
                }

                // An expression-bodied member ends at its semicolon, never opening a brace.
                if ((opened && depth == 0) || (!opened && lines[j].TrimEnd().EndsWith(';')))
                    break;
            }
        }

        return rules;
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        new[] { "src", "shared", "freew", "freep" }
            .Select(area => Path.Combine(root, area))
            .Where(Directory.Exists)
            .SelectMany(area => Directory.EnumerateFiles(area, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !file.Contains($"Tests{Path.DirectorySeparatorChar}"));

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
