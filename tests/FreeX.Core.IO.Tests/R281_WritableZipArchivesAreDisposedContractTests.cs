using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r281: a <c>ZipArchive</c> opened for <c>Create</c> or <c>Update</c> writes its central directory
/// on DISPOSE. Skip that and the bytes are not a leak -- they are an invalid package.
///
/// <para>The class is clean today, which is the finding: 74 such archives exist across the three
/// apps and 70 use the <c>using</c> form. The four that do not are deliberate -- each needs the
/// archive disposed EARLY, before its backing stream is read back, so it cannot be scoped by
/// <c>using</c> -- and each pairs that with a <c>finally</c> or an <c>IDisposable</c> owner.</para>
///
/// <para>That hand-written pairing is what this contract protects. It is the shape a later edit
/// breaks silently: the output still looks like a package, and the failure surfaces as a corrupt
/// file at the user rather than as an exception here.</para>
/// </summary>
public sealed class R281_WritableZipArchivesAreDisposedContractTests
{
    private static readonly string[] Layers =
    [
        "src/FreeX.Core.IO",
        "shared/Free.Shared.IO",
        "shared/Free.Shared.Opc",
        "freew/FreeW.Core.IO",
        "freep/FreeP.Core.IO",
    ];

    [Fact]
    public void EveryWritableZipArchiveIsScopedOrExplicitlyDisposed()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();
        var examined = 0;

        foreach (var file in SourceFiles(root))
        {
            var text = File.ReadAllText(file);
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;
                if (!Regex.IsMatch(line, @"ZipArchiveMode\.(Create|Update)"))
                    continue;

                examined++;

                // Scoped by using -- disposal is the compiler's problem.
                if (Regex.IsMatch(line, @"\busing\b"))
                    continue;

                // Otherwise the variable it is assigned to must be disposed somewhere in the file,
                // or held by a field whose owner implements IDisposable.
                var match = Regex.Match(line, @"(?:var\s+)?(\w+)\s*=\s*new\s+ZipArchive\s*\(");
                if (!match.Success)
                {
                    offenders.Add($"{Relative(root, file)}:{i + 1} -- writable archive in an unrecognised form");
                    continue;
                }

                var variable = match.Groups[1].Value;
                // `using (archive)` on an ALREADY-DECLARED variable is a disposal form too, and
                // missing it reported the sanitizer -- correct code -- as a bug. That two-step shape
                // exists so the constructor can carry its own try/catch for a better error message.
                var disposed =
                    Regex.IsMatch(text, Regex.Escape(variable) + @"\s*\??\s*\.Dispose\(\)")
                    || Regex.IsMatch(text, @"using\s*\(\s*" + Regex.Escape(variable) + @"\s*\)");
                var fieldOwnerDisposes = variable.StartsWith('_') && text.Contains("IDisposable", StringComparison.Ordinal);

                if (!disposed && !fieldOwnerDisposes)
                {
                    offenders.Add(
                        $"{Relative(root, file)}:{i + 1} -- '{variable}' is opened for writing and never disposed");
                }
            }
        }

        examined.Should().BeGreaterThan(30,
            "the scan must find the writable archives; a collapsed count means the layer paths or the "
            + "pattern stopped matching and this passed while checking nothing");

        offenders.Should().BeEmpty(
            "a Create/Update ZipArchive writes its central directory on Dispose, so an undisposed one "
            + "produces a file that is not a valid package -- a corrupt document at the user, not an "
            + "exception here.\n" + string.Join("\n", offenders));
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
        Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
