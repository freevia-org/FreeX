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
        var corpusRoot = FindRepoDirectory("freew-fidelity-corpus");

        rows.Should().HaveCountGreaterThanOrEqualTo(157);
        rows.Select(row => row.Id).Should().OnlyHaveUniqueItems();
        rows.Select(row => row.File).Should().OnlyHaveUniqueItems();

        foreach (var row in rows)
        {
            row.Id.Should().MatchRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$");
            row.File.Should().EndWith(".docx");
            Path.IsPathRooted(row.File).Should().BeFalse();
            row.File.Replace('\\', '/').Should().Be(row.File);
            row.File.Split('/').Should().NotContain("..");
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
                row.Url.Should().Be("local://" + row.File);
                File.Exists(Path.Combine(corpusRoot, row.File)).Should().BeTrue($"{row.Id} should point at a committed or local fixture");
            }
            else
            {
                Path.GetFileName(row.File).Should().Be(row.File, $"{row.Id} should keep downloaded rows flat under files/");
                Uri.TryCreate(row.Url, UriKind.Absolute, out var uri).Should().BeTrue();
                uri!.Scheme.Should().Be(Uri.UriSchemeHttps);
            }
        }
    }

    [Fact]
    public void Manifest_Covers_Docx_Files_Present_In_Corpus_Files_Folder()
    {
        var corpusRoot = FindRepoDirectory("freew-fidelity-corpus");
        var filesRoot = Path.Combine(corpusRoot, "files");
        if (!Directory.Exists(filesRoot))
            return;

        var manifestFiles = ReadManifest()
            .Select(row => row.File)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = Directory.GetFiles(filesRoot, "*.docx", System.IO.SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(corpusRoot, path).Replace('\\', '/'))
            .Where(relativePath =>
                !manifestFiles.Contains(relativePath) &&
                !manifestFiles.Contains(relativePath["files/".Length..]))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        missing.Should().BeEmpty("tracked or local FreeW corpus DOCX files must have manifest provenance rows");
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
            "advanced-typography",
            "bookmarks",
            "charts",
            "checkboxes",
            "comments",
            "content-controls",
            "custom-xml",
            "document-background",
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
            "orientation",
            "page-breaks",
            "page-color",
            "page-layout",
            "page-size",
            "proofing",
            "rtl",
            "settings",
            "shapes",
            "smartart",
            "styles",
            "tables",
            "text-boxes",
            "text-effects",
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
        var repoRoot = FindRepoDirectory(relativeParts[0]);
        var candidate = relativeParts.Skip(1).Aggregate(repoRoot, Path.Combine);
        if (File.Exists(candidate))
            return candidate;

        throw new FileNotFoundException($"Could not find {Path.Combine(relativeParts)} from {AppContext.BaseDirectory}.");
    }

    private static string FindRepoDirectory(params string[] relativeParts) =>
        TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory(FindRepoDirectory);

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
