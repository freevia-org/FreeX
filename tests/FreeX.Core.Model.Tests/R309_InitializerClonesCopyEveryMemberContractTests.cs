using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r309: closes r307's hand-written-copy class at the hazard rather than one caller at a time.
///
/// <para>r308 and the first half of r309 guarded the copies reachable by reflection: a public type
/// with a parameterless <c>Clone()</c> can be built, populated and compared. That left three the
/// helper cannot touch -- a private nested reader state, and copies that pass members as constructor
/// arguments -- and, worse, it left the hazard live for the next author. r277's lesson was exactly
/// this: a guard that names N call sites does nothing about caller N+1.</para>
///
/// <para>So this checks the shape instead. A <c>Clone()</c> written as <c>=&gt; new(...) { ... }</c>
/// enumerates what it copies in one place, which makes the omission mechanically visible: every
/// settable instance member the type declares must appear either as a constructor argument or as an
/// initializer assignment. A member added to the type and forgotten in the copy is not a compile
/// error and not a wrong value -- the clone simply, silently, lacks it.</para>
/// </summary>
public sealed class R309_InitializerClonesCopyEveryMemberContractTests
{
    /// <summary>The shape this contract understands: an expression-bodied Clone building its own type.</summary>
    private static readonly Regex CloneDeclaration = new(
        @"^(?<indent>\s*)(?:public|internal|protected|private)\s+(?:\w+\s+)*?\w[\w<>,\.\[\]\? ]*\s+Clone\(\)\s*=>\s*new",
        RegexOptions.Compiled);

    private static readonly Regex TypeDeclaration = new(
        @"^(?<indent>\s*)(?:\[.*\]\s*)?(?:public|internal|protected|private|sealed|partial|abstract|static|readonly|record|ref|\s)*\b(?:class|struct|record)\s+(?<name>\w+)",
        RegexOptions.Compiled);

    private static readonly Regex AutoProperty = new(
        @"^\s*(?:public|internal|protected)\s+(?!.*\b(?:static|const)\b)[\w<>,\.\[\]\? ]+?\s+(?<name>\w+)\s*\{\s*get;\s*(?:set|init);",
        RegexOptions.Compiled);

    /// <summary>
    /// A stored field. The <c>=&gt;</c> exclusion is the point of this line: an expression-bodied
    /// read-only property (<c>public bool IsFloating =&gt; Wrapping != ImageWrapping.Inline;</c>) is
    /// shaped exactly like a field with an initializer, and reading it as one made this contract
    /// report four copies as incomplete when all four were correct -- a derived value follows from
    /// members the clone already carries, so there is nothing for the copy to assign.
    /// </summary>
    private static readonly Regex Field = new(
        @"^\s*(?:public|internal|protected)\s+(?!.*\b(?:static|const|event)\b)(?!.*=>)(?!.*[\(\)])[\w<>,\.\[\]\?]+\s+(?<name>\w+)\s*(?:=[^;]*)?;\s*$",
        RegexOptions.Compiled);

    [Fact]
    public void EveryInitializerCloneCarriesEverySettableMember()
    {
        var root = RepositoryRoot();
        var examined = 0;
        var gaps = new List<string>();

        foreach (var file in SourceFiles(root))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!CloneDeclaration.IsMatch(lines[i]))
                    continue;

                var body = EnclosingTypeBody(lines, i);
                if (body is not { } scope)
                    continue;

                examined++;
                var carried = CarriedText(lines, i);

                foreach (var member in SettableMembers(lines, scope.Start, scope.End))
                {
                    if (!Regex.IsMatch(carried, $@"\b{Regex.Escape(member)}\b"))
                    {
                        gaps.Add(
                            $"{Path.GetRelativePath(root, file)}:{i + 1} ({scope.Name}.Clone) never copies {member}");
                    }
                }
            }
        }

        examined.Should().BeGreaterThanOrEqualTo(10,
            "r309 measured thirteen initializer-shaped Clone methods across the three apps; if this "
            + "collapses the pattern stopped matching and the contract is passing vacuously");

        gaps.Should().BeEmpty(
            "a Clone written as an object initializer lists what it copies, so a member missing from "
            + "that list is one the copy silently lacks:\n" + string.Join("\n", gaps));
    }

    /// <summary>The constructor arguments and initializer assignments, i.e. everything the copy carries.</summary>
    private static string CarriedText(string[] lines, int cloneLine)
    {
        var indent = CloneDeclaration.Match(lines[cloneLine]).Groups["indent"].Value.Length;
        var text = new System.Text.StringBuilder(lines[cloneLine]);

        for (var i = cloneLine + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            text.Append('\n').Append(line);

            // The statement ends at the "};" closing the initializer, at the Clone's own indent.
            var trimmed = line.TrimEnd();
            if (trimmed.EndsWith(';') && trimmed.Length - trimmed.TrimStart().Length <= indent)
                break;
        }

        return text.ToString();
    }

    private static (string Name, int Start, int End)? EnclosingTypeBody(string[] lines, int cloneLine)
    {
        var cloneIndent = CloneDeclaration.Match(lines[cloneLine]).Groups["indent"].Value.Length;

        for (var i = cloneLine - 1; i >= 0; i--)
        {
            var match = TypeDeclaration.Match(lines[i]);
            if (!match.Success || match.Groups["indent"].Value.Length >= cloneIndent)
                continue;

            var depth = 0;
            var opened = false;
            for (var j = i; j < lines.Length; j++)
            {
                foreach (var c in lines[j])
                {
                    if (c == '{') { depth++; opened = true; }
                    else if (c == '}') depth--;
                }

                if (opened && depth == 0)
                    return (match.Groups["name"].Value, i, j);
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// Members declared directly by the type -- one brace level inside its body, so a nested type's
    /// members are not attributed to its parent.
    /// </summary>
    private static IEnumerable<string> SettableMembers(string[] lines, int start, int end)
    {
        var depth = 0;
        for (var i = start; i <= end; i++)
        {
            var line = lines[i];
            var depthAtLineStart = depth;
            foreach (var c in line)
            {
                if (c == '{') depth++;
                else if (c == '}') depth--;
            }

            if (depthAtLineStart != 1)
                continue;

            if (AutoProperty.Match(line) is { Success: true } property)
                yield return property.Groups["name"].Value;
            else if (Field.Match(line) is { Success: true } field)
                yield return field.Groups["name"].Value;
        }
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
