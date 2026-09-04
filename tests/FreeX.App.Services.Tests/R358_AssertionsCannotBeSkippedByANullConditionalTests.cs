using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r358: bans the one assertion shape in this repository that is guaranteed to be able to do
/// nothing -- <c>element.Attribute("x")?.Value.Should().Be("literal")</c>.
///
/// <para>The null-conditional short-circuits when the attribute is missing, so <c>.Should()</c> never
/// runs and the test passes on exactly the input it exists to reject. r353 found 623 assertions
/// written through a <c>?.</c> subject and proved the consequence on the product: deleting
/// <c>IsDefault="True"</c> from the Custom Views dialog -- so Enter no longer activates the default
/// button -- left its own pinning test green, 18 passed and 0 failed.</para>
///
/// <para>The rule is deliberately narrow, to the shape that can NEVER be intentional: a positive
/// comparison against a non-null string literal. A `?.` subject is legitimate when absence satisfies
/// the assertion (<c>.NotBe(...)</c>, <c>.BeNull()</c>) or when the expected value can itself be null
/// (a nullable parameter, a WML tri-state <c>w:val</c> where absent means on) -- r353 reverted 87 of
/// the first kind and several of the second. Those stay legal, so this guard has no false
/// positives.</para>
///
/// <para>Known limit, stated rather than hidden: an assertion split so the literal lands on the next
/// line is not matched. That form is rare and is not what the 623 looked like. This guard is the
/// cheap, exact half; it is not a claim that no vacuous assertion can exist.</para>
/// </summary>
public sealed class R358_AssertionsCannotBeSkippedByANullConditionalTests
{
    private static readonly Regex SkippableAssertion = new(
        "Attribute\\([^)]*\\)\\?\\.Value\\.Should\\(\\)\\s*\\.\\s*Be\\(\"",
        RegexOptions.Compiled);

    private static readonly string[] TestRoots = ["tests", "freep", "freew"];

    [Fact]
    public void NoTestAssertsAnAttributeLiteralThroughANullConditional()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        var offenders = new List<string>();
        foreach (var testRoot in TestRoots)
        {
            var directory = Path.Combine(root, testRoot);
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                var lines = File.ReadAllLines(file);
                for (var index = 0; index < lines.Length; index++)
                {
                    // A comment cannot execute, so it cannot be a vacuous assertion. This guard's own
                    // documentation quotes the banned shape, and so may a future explanation of it.
                    var trimmed = lines[index].TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                        trimmed.StartsWith("*", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (SkippableAssertion.IsMatch(lines[index]))
                        offenders.Add($"{Path.GetRelativePath(root, file)}({index + 1}): {lines[index].Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "an attribute assertion written as `Attribute(\"x\")?.Value.Should().Be(\"literal\")` is " +
            "skipped entirely when the attribute is missing, so it passes on the very input it exists " +
            "to catch. Use `!.Value` so a missing attribute fails loudly, or assert the absence " +
            "explicitly with `.Should().BeNull()`. Offenders:\n" + string.Join("\n", offenders));
    }
}
