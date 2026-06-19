using FluentAssertions;
using System.Globalization;

namespace FreeX.Core.IO.Tests;

public sealed class FidelityCorpusManifestTests
{
    private static readonly string[] ExpectedHeader =
    [
        "id",
        "file",
        "source",
        "license",
        "retrieved_on",
        "url",
        "feature_tags",
        "notes"
    ];

    [Fact]
    public void FidelityManifestRows_AreCompleteUniqueAndDownloadableMetadata()
    {
        var rows = ReadManifestRows();

        rows.Should().HaveCountGreaterThanOrEqualTo(40);
        rows.Select(row => row.Id).Should().OnlyHaveUniqueItems();
        rows.Select(row => row.File).Should().OnlyHaveUniqueItems();

        foreach (var row in rows)
        {
            row.Id.Should().MatchRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$");
            Path.GetExtension(row.File).Should().BeOneOf(".xlsx", ".xls");
            row.File.Should().NotContain("\\");
            row.File.Split('/').Should().OnlyContain(part =>
                !string.IsNullOrWhiteSpace(part) && part != "." && part != "..");
            row.Source.Should().NotBeNullOrWhiteSpace();
            row.License.Should().NotBeNullOrWhiteSpace();
            row.FeatureTags.Should().NotBeEmpty();
            row.FeatureTags.Should().OnlyContain(tag => !tag.Contains(','));
            row.Notes.Should().NotBeNullOrWhiteSpace();

            DateOnly.TryParseExact(
                row.RetrievedOn,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _).Should().BeTrue($"{row.Id} should record a stable retrieval date");

            if (row.Source.Equals("local", StringComparison.OrdinalIgnoreCase))
            {
                row.Url.Should().StartWith("local://");
            }
            else
            {
                row.License.Should().BeOneOf(
                    "Apache-2.0",
                    "BSD",
                    "MIT",
                    "CC0-1.0",
                    "Public-Domain",
                    "free-download-redistribution-unconfirmed");
                Uri.TryCreate(row.Url, UriKind.Absolute, out var uri).Should().BeTrue(row.Id);
                uri!.Scheme.Should().Be(Uri.UriSchemeHttps);
            }
        }
    }

    [Fact]
    public void FidelityManifestCoversImplementedFreeXFeatureFamilies()
    {
        var tags = ReadManifestRows()
            .SelectMany(row => row.FeatureTags)
            .ToHashSet(StringComparer.Ordinal);

        tags.Should().Contain([
            "3d-charts",
            "activex-controls",
            "allow-edit-ranges",
            "area-charts",
            "autofilter",
            "bar-charts",
            "budget-actual",
            "cached-results",
            "chartex",
            "charts",
            "chartsheets",
            "color-scales",
            "combo-charts",
            "comments",
            "conditional-formatting",
            "ctrlprops",
            "cx-charts",
            "data-bars",
            "data-labels",
            "data-validation",
            "drawings",
            "dropdowns",
            "embedded-objects",
            "emoji-labels",
            "form-controls",
            "formulas",
            "freeze-panes",
            "full-column-references",
            "funnel-charts",
            "headers-footers",
            "hidden-columns",
            "hidden-rows",
            "histogram-charts",
            "hyperlinks",
            "icon-sets",
            "images",
            "interactivity",
            "line-charts",
            "list-controls",
            "lookup-reference",
            "merged-cells",
            "outline-groups",
            "page-setup",
            "pareto-charts",
            "pie-charts",
            "pivot-caches",
            "pivot-filters",
            "pivottables",
            "print-titles",
            "protection",
            "shared-formulas",
            "sparklines",
            "structured-references",
            "sunburst-charts",
            "tables",
            "text-boxes",
            "themes",
            "treemap-charts",
            "vml"
        ]);
    }

    [Fact]
    public void FidelityManifestIncludesRichChartExAndPivotHeavyRows()
    {
        var rows = ReadManifestRows();

        rows
            .Where(row => row.FeatureTags.Contains("chartex"))
            .Should().HaveCountGreaterThanOrEqualTo(3);

        rows
            .Where(row => row.FeatureTags.Contains("pivottables") &&
                          row.FeatureTags.Contains("pivot-caches"))
            .Should().HaveCountGreaterThanOrEqualTo(6);
    }

    private static IReadOnlyList<FidelityRow> ReadManifestRows()
    {
        var lines = TestWorkspaceFiles.ReadRepoText("fidelity-corpus", "manifest.csv")
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

        lines[0].Split(',').Should().Equal(ExpectedHeader);

        return lines
            .Skip(1)
            .Select(ParseRow)
            .ToArray();
    }

    private static FidelityRow ParseRow(string line)
    {
        var fields = line.Split(',');
        fields.Should().HaveCount(ExpectedHeader.Length);

        return new FidelityRow(
            fields[0],
            fields[1],
            fields[2],
            fields[3],
            fields[4],
            fields[5],
            fields[6].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            fields[7]);
    }

    private sealed record FidelityRow(
        string Id,
        string File,
        string Source,
        string License,
        string RetrievedOn,
        string Url,
        IReadOnlyCollection<string> FeatureTags,
        string Notes);
}
