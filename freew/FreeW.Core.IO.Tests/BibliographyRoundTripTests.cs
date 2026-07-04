using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for the citation/bibliography store — word/bibliography/sources.xml. The writer
/// persists the document's selected <see cref="TextDocument.BibliographyStyle"/> (b:Sources/@SelectedStyle)
/// and every <see cref="Source"/> (b:Source with its tag/type/fields); the reader recovers both, so the
/// chosen style and the source data survive a save/load.
/// </summary>
public class BibliographyRoundTripTests
{
    private static readonly XNamespace B = "http://schemas.openxmlformats.org/officeDocument/2006/bibliography";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument ReadDocxWithSourcesXml(string sourcesXml)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                var entry = zip.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(xml);
            }

            Add("word/document.xml",
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body><w:p><w:r><w:t>Body</w:t></w:r></w:p></w:body>
                </w:document>
                """);
            Add("word/bibliography/sources.xml", sourcesXml);
        }
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    [Fact]
    public void SelectedStyle_SurvivesRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.BibliographyStyle = CitationStyle.Ieee;

        RoundTrip(doc).BibliographyStyle.Should().Be(CitationStyle.Ieee);
    }

    [Fact]
    public void DefaultStyle_NoSources_EmitsNoBibliographyPart()
    {
        // A pristine document (APA default, no sources) must not gain a bibliography part — byte-stable.
        using var stream = new MemoryStream();
        DocxWriter.Write(TextDocument.CreateEmpty(), stream);

        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        zip.GetEntry("word/bibliography/sources.xml").Should().BeNull();
    }

    [Fact]
    public void BookSource_AllFields_SurviveRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.BibliographyStyle = CitationStyle.Chicago;
        doc.Sources.Add(new Source
        {
            Tag = "Knuth1997",
            Type = SourceType.Book,
            Author = "Knuth, D.",
            Title = "The Art of Computer Programming",
            Year = "1997",
            Publisher = "Addison-Wesley"
        });

        var result = RoundTrip(doc);

        result.BibliographyStyle.Should().Be(CitationStyle.Chicago);
        var source = result.Sources.Should().ContainSingle().Subject;
        source.Tag.Should().Be("Knuth1997");
        source.Type.Should().Be(SourceType.Book);
        source.Author.Should().Be("Knuth, D.");
        source.Title.Should().Be("The Art of Computer Programming");
        source.Year.Should().Be("1997");
        source.Publisher.Should().Be("Addison-Wesley");
    }

    [Fact]
    public void JournalArticleSource_AllFields_SurviveRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Shannon1948",
            Type = SourceType.JournalArticle,
            Author = "Shannon, C.",
            Title = "A Mathematical Theory of Communication",
            Year = "1948",
            Journal = "Bell System Technical Journal",
            Volume = "27",
            Issue = "3",
            Pages = "379-423"
        });

        var source = RoundTrip(doc).Sources.Should().ContainSingle().Subject;
        source.Type.Should().Be(SourceType.JournalArticle);
        source.Journal.Should().Be("Bell System Technical Journal");
        source.Volume.Should().Be("27");
        source.Issue.Should().Be("3");
        source.Pages.Should().Be("379-423");
    }

    [Fact]
    public void WebSiteSource_AllFields_SurviveRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "MDNGrid",
            Type = SourceType.WebSite,
            Author = "Mozilla",
            Title = "CSS Grid Layout",
            Year = "2023",
            Publisher = "MDN Web Docs",
            Url = "https://developer.mozilla.org/grid",
            Accessed = "3 May 2024"
        });

        var source = RoundTrip(doc).Sources.Should().ContainSingle().Subject;
        source.Type.Should().Be(SourceType.WebSite);
        source.Url.Should().Be("https://developer.mozilla.org/grid");
        source.Accessed.Should().Be("3 May 2024");
        source.Publisher.Should().Be("MDN Web Docs");
    }

    [Fact]
    public void MultipleSources_PreserveOrderAndCount()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source { Tag = "A", Author = "Adams", Title = "Guide", Year = "1979" });
        doc.Sources.Add(new Source { Tag = "Z", Author = "Zimmerman", Title = "Zed", Year = "2001" });

        var result = RoundTrip(doc);

        result.Sources.Select(s => s.Tag).Should().Equal("A", "Z");
    }

    [Fact]
    public void BibliographyPart_DeclaresSelectedStyleAndSourceType()
    {
        var doc = TextDocument.CreateEmpty();
        doc.BibliographyStyle = CitationStyle.Mla;
        doc.Sources.Add(new Source { Tag = "X", Type = SourceType.JournalArticle, Author = "Doe", Title = "T" });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/bibliography/sources.xml")!.Open();
        var root = XDocument.Load(entry).Root!;

        root.Name.Should().Be(B + "Sources");
        root.Attribute("SelectedStyle")!.Value.Should().Be("MLA");
        root.Element(B + "Source")!.Element(B + "SourceType")!.Value.Should().Be("JournalArticle");
        // The author is stored as a single corporate author.
        root.Element(B + "Source")!.Element(B + "Author")!.Element(B + "Author")!
            .Element(B + "Corporate")!.Value.Should().Be("Doe");
    }

    [Fact]
    public void WordStylePersonAuthor_ReadsStructuredNameList()
    {
        var result = ReadDocxWithSourcesXml(
            """
            <b:Sources xmlns:b="http://schemas.openxmlformats.org/officeDocument/2006/bibliography">
              <b:Source>
                <b:Tag>Doe2024</b:Tag>
                <b:SourceType>Book</b:SourceType>
                <b:Author>
                  <b:Author>
                    <b:NameList>
                      <b:Person>
                        <b:Last>Doe</b:Last>
                        <b:First>Jane</b:First>
                        <b:Middle>Q.</b:Middle>
                      </b:Person>
                      <b:Person>
                        <b:Last>Smith</b:Last>
                        <b:First>Alex</b:First>
                      </b:Person>
                    </b:NameList>
                  </b:Author>
                </b:Author>
                <b:Title>Word Authored Source</b:Title>
                <b:Year>2024</b:Year>
              </b:Source>
            </b:Sources>
            """);

        var source = result.Sources.Should().ContainSingle().Subject;
        source.Tag.Should().Be("Doe2024");
        source.Author.Should().Be("Jane Q. Doe; Alex Smith");
        source.PersonalAuthors.Should().Equal(
            SourceAuthorPerson.Create("Jane", "Q.", "Doe"),
            SourceAuthorPerson.Create("Alex", string.Empty, "Smith"));
        source.CorporateAuthor.Should().BeNull();
        source.Title.Should().Be("Word Authored Source");
        source.Year.Should().Be("2024");
    }

    [Fact]
    public void StructuredPersonAuthors_WriteNameListInsteadOfCorporate()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Doe2024",
            Type = SourceType.Book,
            Author = "Jane Q. Doe; Alex Smith",
            PersonalAuthors =
            [
                SourceAuthorPerson.Create("Jane", "Q.", "Doe"),
                SourceAuthorPerson.Create("Alex", string.Empty, "Smith")
            ],
            Title = "Word Authored Source",
            Year = "2024"
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/bibliography/sources.xml")!.Open();
        var root = XDocument.Load(entry).Root!;
        var author = root.Element(B + "Source")!.Element(B + "Author")!.Element(B + "Author")!;

        author.Element(B + "Corporate").Should().BeNull();
        var people = author.Element(B + "NameList")!.Elements(B + "Person").ToList();
        people.Should().HaveCount(2);
        people[0].Element(B + "First")!.Value.Should().Be("Jane");
        people[0].Element(B + "Middle")!.Value.Should().Be("Q.");
        people[0].Element(B + "Last")!.Value.Should().Be("Doe");
        people[1].Element(B + "First")!.Value.Should().Be("Alex");
        people[1].Element(B + "Last")!.Value.Should().Be("Smith");
    }

    [Fact]
    public void StructuredPersonAuthors_RoundTripThroughDocx()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Ada1843",
            Author = "Ada Lovelace",
            PersonalAuthors = [SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace")],
            Title = "Notes",
            Year = "1843"
        });

        var source = RoundTrip(doc).Sources.Should().ContainSingle().Subject;

        source.Author.Should().Be("Ada Lovelace");
        source.PersonalAuthors.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace"));
        source.CorporateAuthor.Should().BeNull();
    }
}
