using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r271: every target id handed to <c>AttachDialogRangePicker</c> must have exactly one registration.
///
/// <para>The Avalonia shell resolves it with
/// <c>DialogRangePickerRegistrations.Single(candidate =&gt; candidate.TargetId == targetId)</c>, so a
/// typo'd or removed id throws while BUILDING the dialog -- the range-picker button never appears,
/// the dialog fails to open, and the stack trace points at LINQ rather than at the id that is
/// missing. There were no tests referencing the registrations at all.</para>
///
/// <para>Source-based because the registrations are a private static of the Avalonia MainWindow and
/// the ids are string literals at the call sites; there is nothing to call. That is a weaker
/// instrument than the sibling behavioural test for the backstage plan, and it is the strongest one
/// available here -- worth saying plainly rather than implying the two are equivalent.</para>
/// </summary>
public sealed class R271_DialogRangePickerRegistrationsResolveContractTests
{
    [Fact]
    public void EveryAttachedTargetIdHasExactlyOneRegistration()
    {
        var shell = Path.Combine(RepositoryRoot(), "src", "FreeX.App.Avalonia");
        var registrationSource = File.ReadAllText(Path.Combine(shell, "MainWindow.DialogRangeSelection.cs"));

        // Target-typed `new("range.x", ...)` inside the array initializer -- NOT
        // `new DialogRangePickerRegistration(...)`, which the first draft assumed and which matched
        // nothing at all. The lower-bound assertion below is what caught that, for the fourth time
        // in this program.
        var arrayStart = registrationSource.IndexOf("DialogRangePickerRegistrations =", StringComparison.Ordinal);
        arrayStart.Should().BeGreaterThan(0, "the registration array must exist for this contract to read it");
        var arrayEnd = registrationSource.IndexOf("\n    ];", arrayStart, StringComparison.Ordinal);
        var arrayBody = arrayEnd < 0 ? registrationSource[arrayStart..] : registrationSource[arrayStart..arrayEnd];

        var registered = new Regex(@"new\(\s*""(range\.[^""]+)""", RegexOptions.Compiled)
            .Matches(arrayBody)
            .Select(match => match.Groups[1].Value)
            .ToList();

        registered.Should().HaveCountGreaterThan(5,
            "a collapsed registration list would mean the parse broke and this contract passed while "
            + "checking nothing -- the lower-bound guard that caught three parse bugs in r263-r266");

        var attached = new List<(string File, int Line, string TargetId)>();
        foreach (var file in Directory.EnumerateFiles(shell, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                // The literal may sit on the call line or on a following argument line for the
                // multi-line call shape; both are matched by scanning the call's argument region.
                if (!lines[i].Contains("AttachDialogRangePicker(", StringComparison.Ordinal)
                    || lines[i].Contains("private void AttachDialogRangePicker", StringComparison.Ordinal))
                {
                    continue;
                }

                var region = string.Join(" ", lines.Skip(i).Take(6));
                foreach (Match literal in Regex.Matches(region, @"""(range\.[^""]+)"""))
                    attached.Add((Path.GetFileName(file), i + 1, literal.Groups[1].Value));
            }
        }

        attached.Should().HaveCountGreaterThan(5,
            "the call-site scan must find the attachments, or this checks nothing");

        var unresolved = attached
            .Where(call => registered.Count(id => string.Equals(id, call.TargetId, StringComparison.Ordinal)) != 1)
            .Select(call => $"{call.File}:{call.Line} -- \"{call.TargetId}\" matches "
                + $"{registered.Count(id => string.Equals(id, call.TargetId, StringComparison.Ordinal))} registrations")
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        unresolved.Should().BeEmpty(
            "AttachDialogRangePicker resolves the id with .Single(), which throws on zero matches and "
            + "on two -- the dialog then fails to build rather than losing just its picker button.\n"
            + string.Join("\n", unresolved));
    }

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
