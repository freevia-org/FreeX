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
}
