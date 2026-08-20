namespace Free.Shared.AppServices.Tests;

public sealed class StartupFileOpenPlannerTests
{
    [Fact]
    public void Plan_applies_product_support_policy_and_routes_additional_documents()
    {
        var policy = new StartupFileOpenPolicy(
            path => string.Equals(Path.GetExtension(path), ".deck", StringComparison.OrdinalIgnoreCase));

        var plan = StartupFileOpenPlanner.Plan(
            ["ignored.txt", "first.deck", "second.DECK"],
            policy,
            fileExists: _ => true);

        plan.Entries.Should().Equal(
            new StartupFileOpenEntry(Path.GetFullPath("first.deck"), OpenInNewWindow: false),
            new StartupFileOpenEntry(Path.GetFullPath("second.DECK"), OpenInNewWindow: true));
        plan.FirstMissingPath.Should().Be("ignored.txt");
    }

    [Fact]
    public void Plan_can_preserve_first_supported_document_startup_policy()
    {
        var policy = new StartupFileOpenPolicy(
            path => string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase),
            MaximumOpenableFiles: 1);

        var plan = StartupFileOpenPlanner.Plan(
            ["missing.docx", "first.docx", "later.docx"],
            policy,
            fileExists: path => !path.EndsWith("missing.docx", StringComparison.OrdinalIgnoreCase));

        plan.Entries.Should().ContainSingle().Which.Path.Should().Be(Path.GetFullPath("first.docx"));
        plan.Entries[0].OpenInNewWindow.Should().BeFalse();
    }

    [Fact]
    public void Plan_routes_every_document_away_from_an_occupied_primary_window()
    {
        var policy = StartupFileOpenPolicy.AllLocalFiles(primaryWindowOccupied: true);

        var plan = StartupFileOpenPlanner.Plan(["one.xlsx", "two.xlsx"], policy, _ => true);

        plan.Entries.Should().OnlyContain(entry => entry.OpenInNewWindow);
        plan.ShouldPrewarm.Should().BeFalse();
    }

    [Fact]
    public void Plan_collapses_a_repeated_argument_into_a_single_entry()
    {
        // Windows delivers a multi-selected-and-dragged duplicate as one process launch with the
        // same path repeated in argv. Without dedup, the second occurrence used to spawn an
        // independent second window on the same file with its own, unsynchronized edit/undo
        // state -- whichever window saved last would silently clobber the other's edits.
        var plan = StartupFileOpenPlanner.Plan(
            ["one.xlsx", "one.xlsx"],
            StartupFileOpenPolicy.AllLocalFiles(),
            _ => true);

        plan.Entries.Should().ContainSingle().Which.Should().Be(
            new StartupFileOpenEntry(Path.GetFullPath("one.xlsx"), OpenInNewWindow: false));
    }

    [Fact]
    public void Plan_collapses_a_repeated_argument_regardless_of_case_or_separator_style()
    {
        // Same underlying file reached through a differently-cased, forward-slashed argument
        // must still be recognized as the same document identity on Windows.
        var plan = StartupFileOpenPlanner.Plan(
            [@"C:\work\one.xlsx", "C:/work/ONE.xlsx"],
            StartupFileOpenPolicy.AllLocalFiles(),
            _ => true);

        plan.Entries.Should().ContainSingle();
    }

    [Fact]
    public void Plan_still_routes_distinct_documents_to_their_own_windows_after_a_duplicate()
    {
        // Sibling case: a genuine duplicate must be dropped, but the routing (first entry keeps
        // the main window, later distinct entries open new windows) must still work for the
        // documents that are NOT duplicates.
        var plan = StartupFileOpenPlanner.Plan(
            ["one.xlsx", "one.xlsx", "two.xlsx"],
            StartupFileOpenPolicy.AllLocalFiles(),
            _ => true);

        plan.Entries.Should().Equal(
            new StartupFileOpenEntry(Path.GetFullPath("one.xlsx"), OpenInNewWindow: false),
            new StartupFileOpenEntry(Path.GetFullPath("two.xlsx"), OpenInNewWindow: true));
    }
}
