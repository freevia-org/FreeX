using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r517: a static collection that is MUTATED at runtime and reachable from more than one thread is a
/// corruption hazard -- concurrent writes to a Dictionary can tear its buckets or spin forever, and
/// the symptom appears far from the cause. The sweep that produced this test found every existing
/// site already guarded, by one of three deliberate idioms: a dedicated gate object, marking the
/// field [ThreadStatic] so each thread owns its own instance, or locking the collection itself.
///
/// <para>This is a TRIPWIRE, not a proof. It cannot tell which lock protects which field, so it asks
/// the weaker question that still catches the regression worth catching: a production file that
/// declares a mutated static collection must contain some guarding construct. A new cache added with
/// no synchronisation anywhere in its file fails here. A file that locks for an unrelated reason
/// would pass, and that limit is deliberate -- a check this cheap earns its place by having no false
/// positives, not by being complete.</para>
/// </summary>
public sealed class R517_SharedMutableStaticCollectionsAreGuardedTests
{
    /// <summary>
    /// Exemptions, each with the mechanism that makes it safe. WPF attached behaviours are guarded by
    /// the framework rather than by a lock: the collection holds DispatcherObject-derived controls,
    /// which throw on cross-thread access, and the events that mutate it are raised on the UI thread.
    /// That is a real guarantee, just not one this text scan can see.
    /// </summary>
    private static readonly HashSet<string> ThreadAffineByFramework = new(System.StringComparer.Ordinal)
    {
        "ComboBoxDropDownWheelBehavior.cs :: OpenComboBoxes",
    };

    private static readonly Regex FieldPattern = new(
        @"static\s+(?:readonly\s+)?(?:Dictionary|HashSet|SortedDictionary|SortedSet|List|Queue|Stack)<.*?>\??\s+(\w+)",
        RegexOptions.Compiled);

    [Fact]
    public void EveryMutatedStaticCollectionSitsInAFileThatSynchronisesSomehow()
    {
        var root = RepoRoot();
        var offenders = new List<string>();

        foreach (var file in ProductionSources(root))
        {
            var text = File.ReadAllText(file);
            var declared = FieldPattern.Matches(text)
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .ToList();
            if (declared.Count == 0)
                continue;

            var guarded = text.Contains("lock (") || text.Contains("[ThreadStatic]");
            if (guarded)
                continue;

            foreach (var name in declared)
            {
                // The name must NOT be preceded by a dot: `slide.Shapes.Remove(...)` is an instance
                // member that merely shares a name with a static field, and treating it as a
                // mutation is exactly the false positive that made the ad-hoc sweep untrustworthy.
                var mutation = new Regex(
                    @"(?<![.\w])" + Regex.Escape(name) + @"\s*(?:\.\s*(?:Add|Remove|Clear|Enqueue|Push)\s*\(|\[[^\]]+\]\s*=[^=])");

                if (!mutation.IsMatch(text))
                    continue;

                var label = Path.GetFileName(file) + " :: " + name;
                if (ThreadAffineByFramework.Contains(label))
                    continue;

                offenders.Add(Path.GetRelativePath(root, file) + " :: " + name);
            }
        }

        offenders.Should().BeEmpty(
            "a static collection mutated at runtime needs a gate, [ThreadStatic] isolation, or a "
            + "concurrent collection type; see r517");
    }

    private static IEnumerable<string> ProductionSources(string root) =>
        new[] { "src", "freew", "freep", "shared" }
            .Select(area => Path.Combine(root, area))
            .Where(Directory.Exists)
            .SelectMany(area => Directory.EnumerateFiles(area, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !path.Contains("Tests")
                        && !path.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}"));

    private static string RepoRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
