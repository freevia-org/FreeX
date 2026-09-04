using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r286: <c>StartsWith(string)</c> without a <see cref="StringComparison"/> uses the CURRENT CULTURE.
/// In a parser that is unsound, because the slicing that follows a match is ordinal, and the two
/// disagree: ICU skips ignorable characters (zero-width joiner, ZWNJ, soft hyphen) and ordinal
/// indexing does not.
///
/// <para>The codebase had already moved off culture-sensitive <c>IndexOf</c> and <c>EndsWith</c>
/// entirely -- zero of each -- and left seven <c>StartsWith</c> calls behind, six of them splitting
/// SUMIF/COUNTIF criteria operators. This fences what those fixes established.</para>
///
/// <para><c>Contains(string)</c> is deliberately NOT included: unlike the others it is ordinal by
/// default, so requiring a comparison there would be noise on 458 correct call sites.</para>
/// </summary>
public sealed class R286_StringPrefixMatchingIsOrdinalContractTests
{
    private static readonly string[] Layers = ["src", "shared", "freew", "freep"];

    [Fact]
    public void NoProductionCodeMatchesAStringLiteralPrefixWithTheCurrentCulture()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();
        var examined = 0;

        foreach (var file in SourceFiles(root))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;

                foreach (Match match in Regex.Matches(lines[i], @"\.(StartsWith|EndsWith|IndexOf)\("))
                {
                    examined++;

                    // Only the single-argument string-literal overload is culture-sensitive; a call
                    // that already names a comparison, or passes a char, is fine.
                    if (!Regex.IsMatch(lines[i], Regex.Escape(match.Value) + @"""[^""]*""\s*\)"))
                        continue;

                    offenders.Add($"{Relative(root, file)}:{i + 1} -- {lines[i].Trim()}");
                }
            }
        }

        examined.Should().BeGreaterThan(100,
            "the scan must find the prefix and index calls; a collapsed count means the pattern "
            + "stopped matching and this passed while checking nothing");

        offenders.Should().BeEmpty(
            "these overloads compare with the current culture, which skips ignorable characters, "
            + "while the slicing that follows a match is ordinal. Matching with one and indexing for "
            + "the other reads an operator from one interpretation of the string and its operand "
            + "from another -- pass StringComparison.Ordinal.\n" + string.Join("\n", offenders));
    }

    private static IEnumerable<string> SourceFiles(string root)
    {
        foreach (var layer in Layers)
        {
            var directory = Path.Combine(root, layer);
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var separator = Path.DirectorySeparatorChar;
                if (file.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                    || file.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                    || file.Contains("Tests", StringComparison.Ordinal)
                    || file.Contains($"{separator}tools{separator}", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static string Relative(string root, string file) =>
        Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
