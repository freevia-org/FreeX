using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class IconPickerDialogSessionTests
{
    [Fact]
    public void CatalogEnumeratesResourcesDeterministicallyAndOwnsDisplayText()
    {
        using var catalog = new TemporaryCatalog();
        catalog.Add("technology", "zebra-icon.svg");
        catalog.Add("arrows", "arrow-right.svg");
        catalog.Add("business", "bank-account.svg");
        catalog.Add("arrows", "ignore.txt");

        var entries = IconPickerCatalog.Load(catalog.Root);

        entries.Select(entry => entry.Name).Should().Equal("Arrow Right", "Bank Account", "Zebra Icon");
        entries.Select(entry => entry.Category).Should().Equal("Arrows", "Business", "Technology");
        entries.Select(entry => entry.Keywords).Should().Equal(
            "arrow right arrows",
            "bank account business",
            "zebra icon technology");
        entries.Should().OnlyContain(entry => Path.IsPathFullyQualified(entry.Path));
    }

    [Fact]
    public void MissingCatalogProducesAnEmptyPortableProjection()
    {
        var entries = IconPickerCatalog.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var session = new IconPickerDialogSession(entries);

        entries.Should().BeEmpty();
        session.Categories.Should().BeEmpty();
        session.State.VisibleEntries.Should().BeEmpty();
        session.State.StatusText.Should().Be(IconPickerDialogPlanner.NoMatchesStatusText);
    }

    [Fact]
    public void SessionOwnsCategoriesFilterSearchSelectionAndAcceptPlanning()
    {
        var arrow = Entry("Arrow Right", "Arrows");
        var laptop = Entry("Laptop", "Technology");
        var phone = Entry("Phone", "Technology");
        var session = new IconPickerDialogSession([phone, arrow, laptop]);

        session.Categories.Should().Equal("Arrows", "Technology");
        session.State.StatusText.Should().Be("3 icons");

        var filtered = session.ApplyFilter("Technology", " lap ");
        filtered.VisibleEntries.Should().Equal(laptop);
        filtered.StatusText.Should().Be("1 icons");
        filtered.SelectedEntry.Should().BeNull();

        var selected = session.Select(laptop);
        selected.SelectedEntry.Should().Be(laptop);
        session.PlanAccept().Should().Be(new IconPickerAcceptPlan(
            new IconPickerSelection("Laptop", "Technology", laptop.Path),
            WarningMessage: null));
    }

    [Fact]
    public void FilterChangesResetSelectionAndEmptyAcceptPlansRequestAWarning()
    {
        var arrow = Entry("Arrow Right", "Arrows");
        var laptop = Entry("Laptop", "Technology");
        var session = new IconPickerDialogSession([arrow, laptop]);
        session.Select(arrow);

        var state = session.ApplyFilter(IconPickerDialogPlanner.AllCategoriesLabel, "missing");
        var plan = session.PlanAccept();

        state.VisibleEntries.Should().BeEmpty();
        state.StatusText.Should().Be(IconPickerDialogPlanner.NoMatchesStatusText);
        state.SelectedEntry.Should().BeNull();
        plan.ShouldAccept.Should().BeFalse();
        plan.Selection.Should().BeNull();
        plan.WarningMessage.Should().Be(IconPickerDialogPlanner.SelectionRequiredMessage);
    }

    [Fact]
    public void SessionRejectsSelectionsOutsideTheCurrentProjection()
    {
        var arrow = Entry("Arrow Right", "Arrows");
        var laptop = Entry("Laptop", "Technology");
        var session = new IconPickerDialogSession([arrow, laptop]);
        session.ApplyFilter("Arrows", null);

        session.Select(laptop).SelectedEntry.Should().BeNull();
        session.PlanAccept().ShouldAccept.Should().BeFalse();
    }

    private static IconPickerEntry Entry(string name, string category) =>
        new(name, category, $"{name} {category}".ToLowerInvariant(), $"{category}/{name}.svg");

    private sealed class TemporaryCatalog : IDisposable
    {
        public TemporaryCatalog()
        {
            Root = Path.Combine(Path.GetTempPath(), "freew-icon-picker-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Add(string category, string fileName)
        {
            var directory = Path.Combine(Root, category);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), "<svg />");
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
