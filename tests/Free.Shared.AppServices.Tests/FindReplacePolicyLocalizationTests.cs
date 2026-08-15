namespace Free.Shared.AppServices.Tests;

public sealed class FindReplacePolicyLocalizationTests
{
    [Fact]
    public void Policy_UsesInjectedTextForValidationNavigationAndReplacementStatuses()
    {
        var text = new FindReplacePolicyTextSpec(
            "query-required",
            "nothing-found",
            "nothing-replaced",
            "missing:{0}",
            "position:{0}/{1}",
            "changed:{0}:{1}",
            "changes:{0}");

        FindReplaceDialogPolicy.ValidationMessageFor(
                FindReplaceValidationErrorKind.SearchTermRequired,
                text)
            .Should().Be("query-required");
        FindReplaceDialogPolicy.Navigate(0, 0, 1, text).StatusText
            .Should().Be("nothing-found");
        FindReplaceDialogPolicy.Navigate(0, 3, 1, text).StatusText
            .Should().Be("position:2/3");
        FindReplaceDialogPolicy.BuildNotFoundStatus("needle", text)
            .Should().Be("missing:needle");
        FindReplaceDialogPolicy.BuildReplaceAllOccurrenceStatus("needle", 2, text)
            .Should().Be("changed:2:s");
        FindReplaceDialogPolicy.BuildReplacementStatus(0, text).StatusText
            .Should().Be("nothing-replaced");
        FindReplaceDialogPolicy.BuildReplacementStatus(4, text).StatusText
            .Should().Be("changes:4");
    }

    [Fact]
    public void TextDescriptor_ResolvesCatalogValuesAndKeepsFallbacks()
    {
        var descriptor = new FindReplacePolicyTextDescriptor(
            Text("required", "required-fallback"),
            Text("none", "none-fallback"),
            Text("replacements.none", "replacements-fallback"),
            Text("not-found", "not-found:{0}"),
            Text("match", "match:{0}/{1}"),
            Text("occurrences", "occurrences:{0}:{1}"),
            Text("replacements", "replacements:{0}"));

        var text = FindReplacePolicyTextSpec.FromDescriptor(
            descriptor,
            key => key == "required" ? "localized-required" : null);

        text.SearchTermRequired.Should().Be("localized-required");
        text.NoMatches.Should().Be("none-fallback");
        FindReplaceDialogPolicy.BuildNotFoundStatus("x", text).Should().Be("not-found:x");
    }

    private static ResourceTextDescriptor Text(string key, string fallback) => new(key, fallback);
}
