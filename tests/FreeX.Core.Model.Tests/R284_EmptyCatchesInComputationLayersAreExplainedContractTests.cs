using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r284: in the computation and persistence layers, an exception caught into an empty body must say
/// why in a comment.
///
/// <para>Every such site in these layers today is legitimate -- an <c>OverflowException</c> falling
/// through from a decimal precision correction to the double path, a culture whose calendar will not
/// accept Gregorian, an unavailable region contributing no currency label. All are narrow exception
/// types with an obvious fallback. The rule is not that they are wrong; it is that the NEXT one
/// should have to be explained, because a silent catch here changes a number rather than a
/// pixel.</para>
///
/// <para>Scoped to these layers deliberately. The UI layers are full of legitimate best-effort
/// teardown -- media sessions, capture devices, process shutdown -- where demanding a comment on
/// each would be noise, and r270/r282 already fence the part of the UI that matters.</para>
/// </summary>
public sealed class R284_EmptyCatchesInComputationLayersAreExplainedContractTests
{
    private static readonly string[] Layers =
    [
        "src/FreeX.Core.Formula",
        "src/FreeX.Core.IO",
        "src/FreeX.Core.Model",
        "src/FreeX.Core.Commands",
        "src/FreeX.App.Services",
        "shared/Free.Shared.IO",
    ];

    [Fact]
    public void EveryEmptyCatchInAComputationLayerCarriesAComment()
    {
        var root = RepositoryRoot();
        var undocumented = new List<string>();
        var examined = 0;

        foreach (var file in SourceFiles(root))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var indent = Indent(lines[i]);
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;
                if (!lines[i].TrimStart().StartsWith("catch", StringComparison.Ordinal))
                    continue;

                // The opening brace is on this line or the next; anything else is not a form this
                // contract recognises and is left alone rather than guessed at.
                var brace = lines[i].TrimEnd().EndsWith("{", StringComparison.Ordinal)
                    ? i
                    : i + 1 < lines.Length && lines[i + 1].Trim() == "{" ? i + 1 : -1;
                if (brace < 0)
                    continue;

                var (isEmpty, hasComment) = Body(lines, brace, indent);
                if (!isEmpty)
                    continue;

                examined++;
                if (!hasComment)
                    undocumented.Add($"{Relative(root, file)}:{i + 1} -- {lines[i].Trim()}");
            }
        }

        examined.Should().BeGreaterThan(3,
            "the scan must find the empty catches in these layers; a collapsed count means the shape "
            + "stopped matching and this passed while checking nothing");

        undocumented.Should().BeEmpty(
            "an empty catch in a computation or persistence layer silently changes a result -- a "
            + "number, or a saved byte -- so the reason it is safe belongs next to it.\n"
            + string.Join("\n", undocumented));
    }

    /// <summary>
    /// Walks from the opening brace to the first line that is neither blank nor a comment. The catch
    /// is empty when that line is the closing brace at the catch's own indent.
    /// </summary>
    private static (bool IsEmpty, bool HasComment) Body(string[] lines, int brace, string indent)
    {
        var hasComment = false;
        for (var i = brace + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
                continue;

            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal)
                || trimmed.StartsWith('*'))
            {
                hasComment = true;
                continue;
            }

            return (lines[i] == indent + "}", hasComment);
        }

        return (false, hasComment);
    }

    private static string Indent(string line) =>
        line[..(line.Length - line.TrimStart().Length)];

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
        Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
