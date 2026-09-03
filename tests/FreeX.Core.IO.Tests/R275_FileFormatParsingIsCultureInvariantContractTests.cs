using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r275: the culture class has been fixed seven times (r98, r108, r110, r111, r145, r151, r152) and
/// never fenced. Each round found one bug and closed it; nothing stopped the eighth.
///
/// <para>No analyzer covers this either -- CA1305 is not enabled anywhere in the build -- so the only
/// thing standing between the file writers and a locale-dependent wire format is that nobody has
/// written the wrong line yet.</para>
///
/// <para>This scans the parse side, where the failure is worst: a provider-less
/// <c>double.TryParse("1.5")</c> under de-DE does not throw, it returns <c>15</c>. Integer parses are
/// excluded deliberately -- <c>NumberStyles.Integer</c> forbids group separators, so a digit string
/// parses identically in every culture, and including them would bury the real signal in noise.</para>
/// </summary>
public sealed class R275_FileFormatParsingIsCultureInvariantContractTests
{
    private static readonly string[] Layers =
    [
        "src/FreeX.Core.IO",
        "shared/Free.Shared.IO",
        "shared/Free.Shared.Pdf",
        "freew/FreeW.Core.IO",
        "freep/FreeP.Core.IO",
    ];

    [Fact]
    public void EveryFloatingPointParseInAFileFormatLayerNamesItsCulture()
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
                if (!Regex.IsMatch(lines[i], @"\b(double|float|decimal)\.(Try)?Parse\s*\("))
                    continue;

                // The call routinely spans lines and the provider is often the LAST argument, so the
                // whole statement has to be read -- a single-line check would report most of these.
                var statement = Statement(lines, i);
                examined++;

                if (Regex.IsMatch(statement, @"[Cc]ulture|Invariant|[Pp]rovider"))
                    continue;

                offenders.Add($"{Relative(root, file)}:{i + 1} -- {lines[i].Trim()}");
            }
        }

        examined.Should().BeGreaterThan(40,
            "the scan must actually find the parse sites; a collapsed count means the layer paths or "
            + "the pattern stopped matching and this passed while checking nothing");

        offenders.Should().BeEmpty(
            "a provider-less floating-point parse does not fail under a comma-decimal locale -- it "
            + "silently returns a different number, so the file opens and the data is wrong.\n"
            + string.Join("\n", offenders));
    }

    private static string Statement(string[] lines, int start)
    {
        var text = string.Empty;
        var depth = 0;
        var opened = false;
        for (var i = start; i < Math.Min(start + 8, lines.Length); i++)
        {
            text += lines[i];
            depth += lines[i].Count(c => c == '(') - lines[i].Count(c => c == ')');
            if (lines[i].Contains('(', StringComparison.Ordinal))
                opened = true;
            if (opened && depth <= 0)
                break;
        }

        return text;
    }

    private static IEnumerable<string> SourceFiles(string root)
    {
        foreach (var layer in Layers)
        {
            var directory = Path.Combine(root, layer.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static string Relative(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
