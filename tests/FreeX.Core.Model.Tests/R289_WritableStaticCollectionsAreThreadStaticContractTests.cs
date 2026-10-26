using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r289: a writable static collection is shared by every thread in the process. FreeX recalculates
/// and saves off the UI thread, so an unsynchronised <see cref="Dictionary{TKey,TValue}"/> written
/// from two threads does not merely throw -- it can corrupt its bucket chain and spin forever, which
/// presents as a frozen application with no exception and no stack to read.
///
/// <para>Every writable static collection in production is already <c>[ThreadStatic]</c>: the
/// formula engine's named-formula recursion guard, the Avalonia ribbon icon caches, the grid's
/// text-measurement caches and FreeW's render diagnostics. One of them even carries the reasoning --
/// "no need for a lock because the dictionaries are never shared". This fences that decision.</para>
///
/// <para>Read-only lookup tables are excluded because they are initialised once and never mutated,
/// as are the <c>Concurrent</c>, <c>Immutable</c> and <c>Frozen</c> families, which are safe by
/// construction. Drawing those distinctions is the difference between a contract and a nuisance.</para>
/// </summary>
public sealed class R289_WritableStaticCollectionsAreThreadStaticContractTests
{
    private static readonly string[] Layers = ["src", "shared", "freew", "freep"];

    private const string CollectionTypes =
        @"\b(Dictionary|SortedDictionary|SortedSet|HashSet|List|Queue|Stack)\s*<";

    [Fact]
    public void EveryWritableStaticCollectionFieldIsThreadStatic()
    {
        var root = RepositoryRoot();
        var shared = new List<string>();
        var examined = 0;

        foreach (var file in SourceFiles(root))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!IsStaticCollectionField(line))
                    continue;

                examined++;

                // readonly lookup tables are initialised once and only read; the concurrent,
                // immutable and frozen families are safe to share by construction.
                if (Regex.IsMatch(line, @"\breadonly\b")
                    || Regex.IsMatch(line, @"Concurrent|Immutable|Frozen|IReadOnly|ReadOnlyDictionary|ReadOnlyCollection"))
                {
                    continue;
                }

                if (HasThreadStaticAttribute(lines, i))
                    continue;

                shared.Add($"{Relative(root, file)}:{i + 1} -- {line.Trim()}");
            }
        }

        examined.Should().BeGreaterThan(50,
            "the scan must find the static collection fields; a collapsed count means the field "
            + "shape stopped matching and this passed while checking nothing");

        shared.Should().BeEmpty(
            "a writable static collection is shared by every thread, and this codebase recalculates "
            + "and saves off the UI thread. Concurrent writes to a Dictionary can corrupt its bucket "
            + "chain and loop forever -- a freeze with no exception. Mark it [ThreadStatic], or use "
            + "a Concurrent/Immutable/Frozen collection.\n" + string.Join("\n", shared));
    }

    /// <summary>
    /// The attribute may sit on the field's own line or on any of the lines above it, past the
    /// doc-comment block that usually separates them.
    /// </summary>
    private static bool HasThreadStaticAttribute(string[] lines, int field)
    {
        if (lines[field].Contains("[ThreadStatic]", StringComparison.Ordinal))
            return true;

        for (var i = field - 1; i >= 0 && i >= field - 20; i--)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0
                || trimmed.StartsWith("///", StringComparison.Ordinal)
                || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            return trimmed.Contains("[ThreadStatic]", StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>
    /// A FIELD declaration, not a method returning a collection: the text before the first '=' or
    /// ';' must contain no '(' -- the distinction that a first draft of this scan missed, reporting
    /// 1,149 static methods as if they were caches.
    /// </summary>
    private static bool IsStaticCollectionField(string line)
    {
        if (!Regex.IsMatch(line, @"^\s{4}(private|internal|public|protected)"))
            return false;
        if (!Regex.IsMatch(line, @"\bstatic\b") || !Regex.IsMatch(line, CollectionTypes))
            return false;
        if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            return false;

        // An expression-bodied PROPERTY is not a field. Both icon caches and both measurement
        // caches are accessors over a [ThreadStatic] backing field declared just above, and
        // splitting on '=' treated their '=>' as an assignment -- reporting correct code, which is
        // how seven earlier detectors in this program went wrong.
        if (line.Contains("=>", StringComparison.Ordinal))
            return false;

        var head = line.Split('=', ';')[0];
        return !head.Contains('(', StringComparison.Ordinal);
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
