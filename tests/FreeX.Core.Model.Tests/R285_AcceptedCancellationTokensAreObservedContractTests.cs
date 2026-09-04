using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r285: a method that accepts a <see cref="System.Threading.CancellationToken"/> must observe it.
///
/// <para>A token taken and ignored is worse than one never offered: the signature promises the work
/// can be cancelled, callers wire a Cancel button to it, and the button does nothing. The failure is
/// silent and it looks like a hang.</para>
///
/// <para>All 65 sites are correct today. This is the fence, not a fix -- the class was surveyed and
/// came back clean, and the point is that the next signature to grow a token cannot quietly drop
/// it.</para>
///
/// <para>Two shapes are excluded and both are real exclusions rather than convenience: a declaration
/// with no body (interface member, abstract, partial) has nothing to observe the token WITH, and a
/// positional record parameter is a property, not a method. Including either would report
/// well-formed code, which is how five earlier detectors in this program went wrong.</para>
/// </summary>
public sealed class R285_AcceptedCancellationTokensAreObservedContractTests
{
    private static readonly string[] Layers =
    [
        "src", "shared", "freew", "freep",
    ];

    [Fact]
    public void EveryMethodThatAcceptsACancellationTokenReferencesIt()
    {
        var root = RepositoryRoot();
        var ignored = new List<string>();
        var examined = 0;

        foreach (var file in SourceFiles(root))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var match = Regex.Match(lines[i], @"CancellationToken\s+(\w+)\s*[,)]");
                if (!match.Success)
                    continue;

                var parameter = match.Groups[1].Value;

                // `CancellationToken CancellationToken` is a positional record property.
                if (string.Equals(parameter, "CancellationToken", StringComparison.Ordinal))
                    continue;

                var body = MethodBody(lines, i);
                if (body is null)
                    continue;

                examined++;
                if (!Regex.IsMatch(body, @"\b" + Regex.Escape(parameter) + @"\b"))
                    ignored.Add($"{Relative(root, file)}:{i + 1} -- '{parameter}' is accepted and never used");
            }
        }

        examined.Should().BeGreaterThan(40,
            "the scan must find the token-taking methods; a collapsed count means the signature shape "
            + "stopped matching and this passed while checking nothing");

        ignored.Should().BeEmpty(
            "a signature that accepts a token promises the work can be cancelled. Ignoring it turns "
            + "the caller's Cancel into a button that does nothing, which presents as a hang.\n"
            + string.Join("\n", ignored));
    }

    /// <summary>
    /// The body between the signature's opening brace and its match, or null when there is no body --
    /// a declaration ending in ';' or an expression-bodied member.
    /// </summary>
    private static string? MethodBody(string[] lines, int signature)
    {
        var open = -1;
        for (var i = signature; i < Math.Min(signature + 8, lines.Length); i++)
        {
            if (lines[i].Contains("=>", StringComparison.Ordinal))
                return null;
            if (lines[i].TrimEnd().EndsWith(";", StringComparison.Ordinal))
                return null;
            if (lines[i].TrimEnd().EndsWith("{", StringComparison.Ordinal))
            {
                open = i;
                break;
            }
        }

        if (open < 0)
            return null;

        var text = string.Empty;
        var depth = 0;
        for (var i = open; i < lines.Length; i++)
        {
            depth += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');
            text += lines[i] + "\n";
            if (depth <= 0)
                break;
        }

        return text;
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
