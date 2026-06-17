using System.Globalization;
using Microsoft.VisualBasic.FileIO;

namespace FreeW.Core.IO.Tests;

public class FreeWFidelityCorpusManifestTests
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
    public void Manifest_Rows_Are_Complete_And_Unique()
    {
        var rows = ReadManifest();

        rows.Should().HaveCountGreaterThanOrEqualTo(45);
        rows.Select(row => row.Id).Should().OnlyHaveUniqueItems();
        rows.Select(row => row.File).Should().OnlyHaveUniqueItems();

        foreach (var row in rows)
        {
            row.Id.Should().MatchRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$");
            row.File.Should().EndWith(".docx");
            Path.GetFileName(row.File).Should().Be(row.File);
            row.Source.Should().NotBeNullOrWhiteSpace();
            row.License.Should().BeOneOf("Apache-2.0", "CC0-1.0", "MIT", "Public-Domain", "local-private");
            row.Notes.Should().NotBeNullOrWhiteSpace();
            row.FeatureTags.Should().NotBeEmpty();

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
                Uri.TryCreate(row.Url, UriKind.Absolute, out var uri).Should().BeTrue();
                uri!.Scheme.Should().Be(Uri.UriSchemeHttps);
            }
        }
    }

    [Fact]
    public void Manifest_Covers_Expected_Word_Feature_Families()
    {
        var tags = ReadManifest()
            .SelectMany(row => row.FeatureTags)
            .ToHashSet(StringComparer.Ordinal);

        tags.Should().Contain([
            "attachments",
            "altchunk",
            "bookmarks",
            "charts",
            "checkboxes",
            "comments",
            "content-controls",
            "custom-xml",
            "drawings",
            "embedded-objects",
            "endnotes",
            "equations",
            "external-relationships",
            "fields",
            "footnotes",
            "glossary",
            "headers-footers",
            "hyperlinks",
            "images",
            "legacy-forms",
            "mail-merge",
            "nested-tables",
            "numbering",
            "page-breaks",
            "settings",
            "shapes",
            "smartart",
            "styles",
            "tables",
            "text-boxes",
            "theme",
            "tracked-changes",
            "vml",
            "watermarks",
            "web-settings"
        ]);
    }

    private static IReadOnlyList<CorpusRow> ReadManifest()
    {
        var manifestPath = FindRepoFile("freew-fidelity-corpus", "manifest.csv");
        using var parser = new TextFieldParser(manifestPath);
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;

        var header = parser.ReadFields();
        header.Should().Equal(ExpectedHeader);

        var rows = new List<CorpusRow>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            fields.Should().HaveCount(ExpectedHeader.Length);

            rows.Add(new CorpusRow(
                fields![0],
                fields[1],
                fields[2],
                fields[3],
                fields[4],
                fields[5],
                fields[6].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                fields[7]));
        }

        return rows;
    }

    private static string FindRepoFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = relativeParts.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {Path.Combine(relativeParts)} from {AppContext.BaseDirectory}.");
    }

    private sealed record CorpusRow(
        string Id,
        string File,
        string Source,
        string License,
        string RetrievedOn,
        string Url,
        IReadOnlyCollection<string> FeatureTags,
        string Notes);
}
