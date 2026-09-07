using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// r523: guards the 32 fixes from r520-r522 against regression, and encodes what the sweep behind
/// them actually established.
///
/// <para>An unchecked cast of a collection element is not dangerous in itself. A sweep of every such
/// cast in production - 42 of them - found all but the command layer safe BY CONSTRUCTION, because
/// they resolve an index and use it immediately: ResolveTargets and ResolveParagraphIndices filter on
/// <c>is Paragraph</c>, ResolveBodyTextRanges skips non-paragraph blocks, CaptionRangeFor returns null
/// unless the block is a paragraph, and the formula aggregation casts only indices already tested
/// with <c>is RangeValue</c>. Validation sits beside use.</para>
///
/// <para>Commands are the exception, and the reason is temporal: a command CAPTURES an index when it
/// is constructed and dereferences it later, at Apply or Revert or from HasEffect. The document can
/// change in between. So the rule this test enforces is deliberately narrow -- it applies to the
/// command layer, where the gap exists, and not to code where the check and the use are adjacent.</para>
/// </summary>
public class R523_CommandsDoNotCastBlocksUncheckedTests
{
    private static readonly Regex UncheckedElementCast = new(
        @"\((Paragraph|Table|Block|Run|TableRow|TableCell)\)[A-Za-z_][A-Za-z_.]*\[",
        RegexOptions.Compiled);

    [Fact]
    public void CommandFilesResolveBlocksThroughAValidatingAccessor()
    {
        var root = RepoRoot();
        var offenders = new List<string>();

        foreach (var file in CommandSources(root))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Skip comments: r520's own explanation quotes the old cast verbatim.
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
                    continue;

                if (UncheckedElementCast.IsMatch(line))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {trimmed}");
            }
        }

        offenders.Should().BeEmpty(
            "a command dereferences an index it captured earlier, so the document may have changed "
            + "underneath it; use a validating TryGet accessor as TryGetCell/TryGetTable/"
            + "TryGetParagraph do (see r520-r522)");
    }

    private static IEnumerable<string> CommandSources(string root) =>
        new[]
        {
            Path.Combine(root, "freew", "FreeW.Core.Model"),
            Path.Combine(root, "freep", "FreeP.Core.Model"),
            Path.Combine(root, "src", "FreeX.Core.Commands"),
        }
        .Where(Directory.Exists)
        .SelectMany(dir => Directory.EnumerateFiles(dir, "*Command*.cs", SearchOption.AllDirectories))
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static string RepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null && !File.Exists(Path.Combine(dir, "FreeX.slnx")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir!;
    }
}
