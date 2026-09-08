using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// The host suite is split across seven batch projects so it can run as seven parallel CI jobs. The
/// split used to be contiguous letter ranges, which made each batch cost whatever its letters
/// happened to cost -- M alone was 8.9 of the 21.9 total minutes -- so the slowest batch set the
/// release critical path. The batches are now balanced by measured duration, which means the
/// prefixes no longer tile the alphabet and the partition has to be checked rather than assumed.
/// </summary>
public sealed class HostTestBatchPartitionTests
{
    private const string Namespace = "FreeX.App.Host.Tests.";
    private const int BatchCount = 7;
    private const int CatchAllBatch = 7;

    [Fact]
    public void EveryTestClass_RunsInExactlyOneBatch()
    {
        var included = IncludedPrefixesByBatch();
        var excluded = ExcludedPrefixes();

        var unassigned = new List<string>();
        var duplicated = new List<string>();

        foreach (var testClass in TestClassNames())
        {
            var matches = included
                .Where(batch => batch.Value.Any(prefix => testClass.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)))
                .Select(batch => batch.Key)
                .ToList();

            // The catch-all batch runs whatever the explicit ones do not, which is what stops a newly
            // added class from silently running nowhere.
            if (!excluded.Any(prefix => testClass.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(CatchAllBatch);
            }

            if (matches.Count == 0)
            {
                unassigned.Add(testClass);
            }
            else if (matches.Count > 1)
            {
                duplicated.Add($"{testClass} -> batches {string.Join(", ", matches)}");
            }
        }

        unassigned.Should().BeEmpty("every test class must run in some batch, or it is silently skipped");
        duplicated.Should().BeEmpty("a class matching two batches is run twice, wasting a CI job");
    }

    [Fact]
    public void NoBatchPrefix_IsAPrefixOfAnother()
    {
        // This is what makes the batches mutually exclusive. Adding "R1" beside an existing "R19"
        // would silently double-run every R19 class.
        var all = IncludedPrefixesByBatch()
            .SelectMany(batch => batch.Value.Select(prefix => (Batch: batch.Key, Prefix: prefix)))
            .ToList();

        var overlaps =
            from a in all
            from b in all
            where a.Prefix != b.Prefix
                  && b.Prefix.StartsWith(a.Prefix, System.StringComparison.OrdinalIgnoreCase)
            select $"'{a.Prefix}' (batch {a.Batch}) is a prefix of '{b.Prefix}' (batch {b.Batch})";

        overlaps.Should().BeEmpty();
    }

    [Fact]
    public void CatchAllBatch_ExcludesExactlyWhatTheOtherBatchesInclude()
    {
        // If these drift apart the partition silently gains a hole (a class in neither) or an overlap
        // (a class in two), which is exactly what the other two tests here would then report.
        var included = IncludedPrefixesByBatch().SelectMany(batch => batch.Value).OrderBy(p => p).ToList();
        var excluded = ExcludedPrefixes().OrderBy(p => p).ToList();

        excluded.Should().Equal(included);
    }

    private static IEnumerable<string> TestClassNames()
    {
        return typeof(HostTestBatchPartitionTests).Assembly
            .GetTypes()
            .Where(type => type.IsClass && type.FullName is not null && type.FullName.StartsWith(Namespace, System.StringComparison.Ordinal))
            .Where(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(method => method
                    .GetCustomAttributes(inherit: true)
                    .Any(attribute =>
                    {
                        // Custom attributes derive from these, so match by name rather than by type.
                        var name = attribute.GetType().Name;
                        return name.EndsWith("FactAttribute", System.StringComparison.Ordinal)
                            || name.EndsWith("TheoryAttribute", System.StringComparison.Ordinal);
                    })))
            .Select(type => type.FullName![Namespace.Length..])
            .Distinct()
            .ToList();
    }

    private static Dictionary<int, List<string>> IncludedPrefixesByBatch()
    {
        var byBatch = new Dictionary<int, List<string>>();
        for (var batch = 1; batch < CatchAllBatch; batch++)
        {
            byBatch[batch] = MatchPrefixes(BatchFilter(batch), "~");
        }

        return byBatch;
    }

    private static List<string> ExcludedPrefixes()
    {
        return MatchPrefixes(BatchFilter(CatchAllBatch), "!~");
    }

    private static List<string> MatchPrefixes(string filter, string op)
    {
        var pattern = $"FullyQualifiedName{Regex.Escape(op)}{Regex.Escape(Namespace)}(?<prefix>[A-Za-z0-9_]+)";
        return Regex.Matches(filter, pattern)
            .Select(match => match.Groups["prefix"].Value)
            .Distinct()
            .ToList();
    }

    private static string BatchFilter(int batch)
    {
        var project = WorkspaceFileLocator.ReadAllText(
            "tests",
            "FreeX.App.Host.Tests",
            $"FreeX.App.Host.Tests.Batch{batch}.csproj");

        var match = Regex.Match(project, "<VSTestTestCaseFilter>(?<filter>.*?)</VSTestTestCaseFilter>", RegexOptions.Singleline);
        match.Success.Should().BeTrue($"batch {batch} must declare a test-case filter");
        return match.Groups["filter"].Value;
    }
}
