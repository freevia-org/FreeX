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
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

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
    public void GeneratedBibliography_RoundTripsNativeSpanningFieldInsideBibliographyControl()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Sources.Add(new Source { Tag = "Ad79", Author = "Adams", Title = "Guide", Year = "1979" });
        doc.Sources.Add(new Source { Tag = "Kn97", Author = "Knuth", Title = "TAOCP", Year = "1997" });
        var control = BlockContentControl.BibliographyRegion();
        var generated = Citations.BuildBibliography(doc, CitationStyle.Apa);
        foreach (var paragraph in generated)
        {
            paragraph.BlockContentControl = control;
            doc.Blocks.Add(paragraph);
        }

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using (var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read))
        using (var entry = zip.GetEntry("word/document.xml")!.Open())
        {
            var xml = XDocument.Load(entry);
            var sdt = xml.Descendants(W + "sdt").Should().ContainSingle().Subject;
            sdt.Descendants(W + "docPartGallery").Should().ContainSingle()
                .Which.Attribute(W + "val")!.Value.Should().Be(BlockContentControl.BibliographyGallery);
            sdt.Descendants(W + "docPartUnique").Should().ContainSingle();

            var paragraphs = sdt.Descendants(W + "p").ToArray();
            paragraphs.Should().HaveCount(3);
            paragraphs[0].Descendants(W + "fldChar").Should().BeEmpty();
            paragraphs[1].Descendants(W + "instrText").Should().ContainSingle()
                .Which.Value.Should().Be(Citations.NativeFieldInstruction);
            paragraphs[1].Descendants(W + "fldChar")
                .Select(field => field.Attribute(W + "fldCharType")!.Value)
                .Should().Equal("begin", "separate");
            paragraphs[2].Descendants(W + "fldChar")
                .Select(field => field.Attribute(W + "fldCharType")!.Value)
                .Should().Equal("end");
        }

        stream.Position = 0;
        var reopened = DocxReader.Read(stream);
        var result = reopened.Blocks.OfType<Paragraph>().ToArray();
        result.Select(paragraph => paragraph.PlainText).Should().Equal(
            "References",
            "Adams. (1979). Guide.",
            "Knuth. (1997). TAOCP.");
        result[0].SpanningFieldOwner.Should().BeNull();
        result.Skip(1).Should().OnlyContain(paragraph =>
            paragraph.SpanningFieldOwner != null
            && paragraph.SpanningFieldOwner.Instruction == Citations.NativeFieldInstruction);
        result[1].SpanningFieldStart!.Instruction.Should().Be(Citations.NativeFieldInstruction);
        result[2].EndsSpanningField.Should().BeTrue();
        result.Should().OnlyContain(paragraph =>
            paragraph.BlockContentControl != null
            && paragraph.BlockContentControl.Kind == BlockContentControlKind.Bibliography);
    }

    [Fact]
    public void EmptyGeneratedBibliography_RoundTripsWordEmptyFieldResult()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.AddRange(Citations.BuildBibliography(doc));

        var reopened = RoundTrip(doc);
        var paragraphs = reopened.Blocks.OfType<Paragraph>().ToArray();

        paragraphs.Select(paragraph => paragraph.PlainText).Should().Equal(
            Citations.HeadingText,
            Citations.EmptyResultText);
        paragraphs[1].Runs.Should().ContainSingle();
        paragraphs[1].Runs[0].ComplexField!.Instruction.Should().Be(Citations.NativeFieldInstruction);
        paragraphs[1].EndsSpanningField.Should().BeFalse();
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
            Publisher = "Addison-Wesley",
            City = "Reading",
            Edition = "3",
            StandardNumber = "978-0201896831",
            ShortTitle = "TAOCP",
            Comments = "Classic reference"
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
        source.City.Should().Be("Reading");
        source.Edition.Should().Be("3");
        source.StandardNumber.Should().Be("978-0201896831");
        source.ShortTitle.Should().Be("TAOCP");
        source.Comments.Should().Be("Classic reference");
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
            Pages = "379-423",
            StandardNumber = "ISSN 0005-8580",
            ShortTitle = "Communication theory",
            Comments = "Journal note"
        });

        var source = RoundTrip(doc).Sources.Should().ContainSingle().Subject;
        source.Type.Should().Be(SourceType.JournalArticle);
        source.Journal.Should().Be("Bell System Technical Journal");
        source.Volume.Should().Be("27");
        source.Issue.Should().Be("3");
        source.Pages.Should().Be("379-423");
        source.StandardNumber.Should().Be("ISSN 0005-8580");
        source.ShortTitle.Should().Be("Communication theory");
        source.Comments.Should().Be("Journal note");
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
            Accessed = "3 May 2024",
            ShortTitle = "CSS Grid",
            Comments = "Web note"
        });

        var source = RoundTrip(doc).Sources.Should().ContainSingle().Subject;
        source.Type.Should().Be(SourceType.WebSite);
        source.Url.Should().Be("https://developer.mozilla.org/grid");
        source.Accessed.Should().Be("3 May 2024");
        source.Publisher.Should().Be("MDN Web Docs");
        source.ShortTitle.Should().Be("CSS Grid");
        source.Comments.Should().Be("Web note");
    }

    [Fact]
    public void WebSiteSource_StructuredAccessedDate_SurvivesRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "MDNGrid",
            Type = SourceType.WebSite,
            Title = "CSS Grid Layout",
            Url = "https://developer.mozilla.org/grid",
            AccessedDay = "3",
            AccessedMonth = "May",
            AccessedYear = "2024"
        });

        var source = RoundTrip(doc).Sources.Should().ContainSingle().Subject;

        source.Accessed.Should().BeNull();
        source.AccessedDay.Should().Be("3");
        source.AccessedMonth.Should().Be("May");
        source.AccessedYear.Should().Be("2024");
    }

    [Fact]
    public void BibliographyPart_WritesStructuredAccessedDateFields()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "MDNGrid",
            Type = SourceType.WebSite,
            Title = "CSS Grid Layout",
            Accessed = "legacy fallback",
            AccessedDay = "3",
            AccessedMonth = "May",
            AccessedYear = "2024"
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/bibliography/sources.xml")!.Open();
        var source = XDocument.Load(entry).Root!.Element(B + "Source")!;

        source.Element(B + "DayAccessed")!.Value.Should().Be("3");
        source.Element(B + "MonthAccessed")!.Value.Should().Be("May");
        source.Element(B + "YearAccessed")!.Value.Should().Be("2024");
        source.Element(B + "YearAccessed")!.Value.Should().NotBe("legacy fallback");
    }

    [Fact]
    public void WordStyleStructuredAccessedDate_ReadsDateParts()
    {
        var result = ReadDocxWithSourcesXml(
            """
            <b:Sources xmlns:b="http://schemas.openxmlformats.org/officeDocument/2006/bibliography">
              <b:Source>
                <b:Tag>Web2024</b:Tag>
                <b:SourceType>DocumentFromInternetSite</b:SourceType>
                <b:Title>Word Web Source</b:Title>
                <b:DayAccessed>3</b:DayAccessed>
                <b:MonthAccessed>May</b:MonthAccessed>
                <b:YearAccessed>2024</b:YearAccessed>
              </b:Source>
            </b:Sources>
            """);

        var source = result.Sources.Should().ContainSingle().Subject;
        source.Type.Should().Be(SourceType.WebSite);
        source.Accessed.Should().BeNull();
        source.AccessedDay.Should().Be("3");
        source.AccessedMonth.Should().Be("May");
        source.AccessedYear.Should().Be("2024");
    }

    [Fact]
    public void ReportSource_AllFields_SurviveRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "NBS2026",
            Type = SourceType.Report,
            Author = "Doe, J.",
            Title = "Measurements Report",
            Year = "2026",
            Institution = "National Bureau of Standards",
            City = "Washington",
            Publisher = "Government Printing Office",
            StandardNumber = "NBS-2026-01",
            ShortTitle = "Measurements",
            Comments = "Report note"
        });

        var source = RoundTrip(doc).Sources.Should().ContainSingle().Subject;
        source.Type.Should().Be(SourceType.Report);
        source.Author.Should().Be("Doe, J.");
        source.Title.Should().Be("Measurements Report");
        source.Year.Should().Be("2026");
        source.Institution.Should().Be("National Bureau of Standards");
        source.City.Should().Be("Washington");
        source.Publisher.Should().Be("Government Printing Office");
        source.StandardNumber.Should().Be("NBS-2026-01");
        source.ShortTitle.Should().Be("Measurements");
        source.Comments.Should().Be("Report note");
    }

    [Fact]
    public void BookSectionSource_AllFields_SurviveRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Chapter2026",
            Type = SourceType.BookSection,
            Author = "Doe, J.",
            Title = "Chapter Title",
            BookTitle = "Containing Book",
            Year = "2026",
            ChapterNumber = "3",
            Pages = "12-20",
            City = "London",
            Publisher = "Test Press",
            Edition = "2",
            StandardNumber = "ISBN-1",
            ShortTitle = "Chapter",
            Comments = "Book section note"
        });

        var source = RoundTrip(doc).Sources.Should().ContainSingle().Subject;
        source.Type.Should().Be(SourceType.BookSection);
        source.Author.Should().Be("Doe, J.");
        source.Title.Should().Be("Chapter Title");
        source.BookTitle.Should().Be("Containing Book");
        source.Year.Should().Be("2026");
        source.ChapterNumber.Should().Be("3");
        source.Pages.Should().Be("12-20");
        source.City.Should().Be("London");
        source.Publisher.Should().Be("Test Press");
        source.Edition.Should().Be("2");
        source.StandardNumber.Should().Be("ISBN-1");
        source.ShortTitle.Should().Be("Chapter");
        source.Comments.Should().Be("Book section note");
    }

    [Fact]
    public void ConferenceProceedingsSource_AllFields_SurviveRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Conf2026",
            Type = SourceType.ConferenceProceedings,
            Author = "Doe, J.",
            Title = "Proceedings Paper",
            ConferenceName = "Proceedings of the Example Conference",
            Year = "2026",
            Pages = "101-109",
            City = "Berlin",
            Publisher = "ACM",
            StandardNumber = "ISBN-CP-1",
            ShortTitle = "Proceedings Paper",
            Comments = "Conference note"
        });

        var source = RoundTrip(doc).Sources.Should().ContainSingle().Subject;
        source.Type.Should().Be(SourceType.ConferenceProceedings);
        source.Author.Should().Be("Doe, J.");
        source.Title.Should().Be("Proceedings Paper");
        source.ConferenceName.Should().Be("Proceedings of the Example Conference");
        source.Year.Should().Be("2026");
        source.Pages.Should().Be("101-109");
        source.City.Should().Be("Berlin");
        source.Publisher.Should().Be("ACM");
        source.StandardNumber.Should().Be("ISBN-CP-1");
        source.ShortTitle.Should().Be("Proceedings Paper");
        source.Comments.Should().Be("Conference note");
    }

    [Fact]
    public void ArticleInPeriodicalSource_AllFields_SurviveRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Periodical2026",
            Type = SourceType.ArticleInPeriodical,
            Author = "Roe",
            Title = "City Desk",
            Year = "2026",
            Journal = "Daily Planet",
            Volume = "12",
            Issue = "4",
            Pages = "5-7",
            StandardNumber = "ISSN 1234-5678",
            ShortTitle = "City Desk",
            Comments = "Periodical note"
        });

        var source = RoundTrip(doc).Sources.Should().ContainSingle().Subject;
        source.Type.Should().Be(SourceType.ArticleInPeriodical);
        source.Author.Should().Be("Roe");
        source.Title.Should().Be("City Desk");
        source.Year.Should().Be("2026");
        source.Journal.Should().Be("Daily Planet");
        source.Volume.Should().Be("12");
        source.Issue.Should().Be("4");
        source.Pages.Should().Be("5-7");
        source.StandardNumber.Should().Be("ISSN 1234-5678");
        source.ShortTitle.Should().Be("City Desk");
        source.Comments.Should().Be("Periodical note");
    }

    [Fact]
    public void ElectronicSource_AllFields_SurviveRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Electronic2026",
            Type = SourceType.ElectronicSource,
            Author = "Ada",
            Title = "Online Notes",
            Year = "2026",
            Publisher = "Example Archive",
            Url = "https://example.test/notes",
            AccessedDay = "4",
            AccessedMonth = "July",
            AccessedYear = "2026",
            ShortTitle = "Notes",
            Comments = "Electronic note"
        });

        var source = RoundTrip(doc).Sources.Should().ContainSingle().Subject;
        source.Type.Should().Be(SourceType.ElectronicSource);
        source.Author.Should().Be("Ada");
        source.Title.Should().Be("Online Notes");
        source.Year.Should().Be("2026");
        source.Publisher.Should().Be("Example Archive");
        source.Url.Should().Be("https://example.test/notes");
        source.Accessed.Should().BeNull();
        source.AccessedDay.Should().Be("4");
        source.AccessedMonth.Should().Be("July");
        source.AccessedYear.Should().Be("2026");
        source.ShortTitle.Should().Be("Notes");
        source.Comments.Should().Be("Electronic note");
    }

    [Fact]
    public void SourceManagerBreadthTypes_AllFields_SurviveRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Patent2026",
            Type = SourceType.Patent,
            Inventor = "Lovelace, Ada",
            Title = "Analytical Engine Control",
            Year = "1843",
            Month = "July",
            Day = "4",
            PatentNumber = "GB-1843-1",
            CountryRegion = "United Kingdom",
            StateProvince = "London",
            ShortTitle = "Engine Control",
            Comments = "Patent note"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Interview2026",
            Type = SourceType.Interview,
            Interviewee = "Hopper, Grace",
            Interviewer = "Mauchly, Jean",
            Title = "Compiler Notes",
            Year = "1968",
            Month = "April",
            Day = "9",
            Medium = "Recorded interview",
            ShortTitle = "Compiler interview",
            Comments = "Interview note"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Misc2026",
            Type = SourceType.Misc,
            Author = "Example Archive",
            Title = "Loose note",
            Year = "2026",
            Month = "June",
            Day = "2",
            SourceKind = "Manuscript",
            Medium = "Scan",
            ShortTitle = "Loose note",
            Comments = "Misc note"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Case2026",
            Type = SourceType.Case,
            Author = "Brown",
            Title = "Brown v. Board of Education",
            Year = "1954",
            Month = "May",
            Day = "17",
            CaseNumber = "1",
            Court = "U.S. Supreme Court",
            Reporter = "347 U.S. 483",
            CountryRegion = "United States",
            StateProvince = "District of Columbia",
            City = "Washington",
            ShortTitle = "Brown",
            Comments = "Case note"
        });

        var result = RoundTrip(doc);

        result.Sources.Should().HaveCount(4);
        result.Sources[0].Type.Should().Be(SourceType.Patent);
        result.Sources[0].Inventor.Should().Be("Lovelace, Ada");
        result.Sources[0].PatentNumber.Should().Be("GB-1843-1");
        result.Sources[0].CountryRegion.Should().Be("United Kingdom");
        result.Sources[0].StateProvince.Should().Be("London");
        result.Sources[1].Type.Should().Be(SourceType.Interview);
        result.Sources[1].Interviewee.Should().Be("Hopper, Grace");
        result.Sources[1].Interviewer.Should().Be("Mauchly, Jean");
        result.Sources[1].Medium.Should().Be("Recorded interview");
        result.Sources[2].Type.Should().Be(SourceType.Misc);
        result.Sources[2].SourceKind.Should().Be("Manuscript");
        result.Sources[2].Medium.Should().Be("Scan");
        result.Sources[3].Type.Should().Be(SourceType.Case);
        result.Sources[3].CaseNumber.Should().Be("1");
        result.Sources[3].Court.Should().Be("U.S. Supreme Court");
        result.Sources[3].Reporter.Should().Be("347 U.S. 483");
        result.Sources[3].CountryRegion.Should().Be("United States");
        result.Sources[3].StateProvince.Should().Be("District of Columbia");
        result.Sources[3].City.Should().Be("Washington");
        result.Sources.Select(source => source.Day).Should().Equal("4", "9", "2", "17");
        result.Sources.Select(source => source.Month).Should().Equal("July", "April", "June", "May");
    }

    [Fact]
    public void MediaSourceManagerBreadthTypes_AllFields_SurviveRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Film2026",
            Type = SourceType.Film,
            Director = "Kubrick, Stanley",
            ProducerName = "MGM",
            Writer = "Clarke, Arthur C.",
            Performer = "Dullea, Keir",
            Title = "2001: A Space Odyssey",
            Year = "1968",
            ProductionCompany = "Metro-Goldwyn-Mayer",
            Medium = "Film",
            ShortTitle = "2001",
            Comments = "Film note"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Recording2026",
            Type = SourceType.SoundRecording,
            Artist = "Holiday, Billie",
            Composer = "Strange, Lewis Allan",
            Conductor = "Jones, Quincy",
            Performer = "Holiday, Billie",
            ProducerName = "Norman Granz",
            Title = "Strange Fruit",
            AlbumTitle = "Lady Sings",
            Year = "1956",
            Medium = "LP",
            RecordingNumber = "RS-1",
            ShortTitle = "Strange Fruit",
            Comments = "Recording note"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Art2026",
            Type = SourceType.Art,
            Artist = "Kahlo, Frida",
            Title = "The Broken Column",
            Year = "1944",
            Medium = "Oil on masonite",
            Institution = "Museo Dolores Olmedo",
            City = "Mexico City",
            ShortTitle = "Broken Column",
            Comments = "Art note"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Site2026",
            Type = SourceType.InternetSite,
            Author = "Example Archive",
            Title = "Example Home",
            Year = "2026",
            Publisher = "Example Site",
            Url = "https://example.test",
            AccessedDay = "7",
            AccessedMonth = "July",
            AccessedYear = "2026",
            ShortTitle = "Example",
            Comments = "Site note"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Performance2026",
            Type = SourceType.Performance,
            Performer = "Royal Shakespeare Company",
            Conductor = "Doe, Jane",
            Title = "Hamlet",
            Year = "2026",
            Month = "May",
            Day = "8",
            Theater = "Globe Theatre",
            City = "London",
            Medium = "Stage performance",
            ShortTitle = "Hamlet",
            Comments = "Performance note"
        });

        var result = RoundTrip(doc);

        result.Sources.Should().HaveCount(5);
        result.Sources[0].Type.Should().Be(SourceType.Film);
        result.Sources[0].Director.Should().Be("Kubrick, Stanley");
        result.Sources[0].ProducerName.Should().Be("MGM");
        result.Sources[0].Writer.Should().Be("Clarke, Arthur C.");
        result.Sources[0].Performer.Should().Be("Dullea, Keir");
        result.Sources[0].ProductionCompany.Should().Be("Metro-Goldwyn-Mayer");
        result.Sources[1].Type.Should().Be(SourceType.SoundRecording);
        result.Sources[1].Artist.Should().Be("Holiday, Billie");
        result.Sources[1].Composer.Should().Be("Strange, Lewis Allan");
        result.Sources[1].Conductor.Should().Be("Jones, Quincy");
        result.Sources[1].AlbumTitle.Should().Be("Lady Sings");
        result.Sources[1].RecordingNumber.Should().Be("RS-1");
        result.Sources[2].Type.Should().Be(SourceType.Art);
        result.Sources[2].Artist.Should().Be("Kahlo, Frida");
        result.Sources[2].Institution.Should().Be("Museo Dolores Olmedo");
        result.Sources[2].City.Should().Be("Mexico City");
        result.Sources[3].Type.Should().Be(SourceType.InternetSite);
        result.Sources[3].Url.Should().Be("https://example.test");
        result.Sources[3].AccessedYear.Should().Be("2026");
        result.Sources[4].Type.Should().Be(SourceType.Performance);
        result.Sources[4].Performer.Should().Be("Royal Shakespeare Company");
        result.Sources[4].Conductor.Should().Be("Doe, Jane");
        result.Sources[4].Theater.Should().Be("Globe Theatre");
        result.Sources[4].Month.Should().Be("May");
        result.Sources[4].Day.Should().Be("8");
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
    public void BibliographyPart_WritesWordFieldDepthElements()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Depth",
            Type = SourceType.Book,
            Title = "Field Depth",
            Publisher = "Test Press",
            City = "London",
            Edition = "2",
            StandardNumber = "ISBN-1",
            ShortTitle = "Depth",
            Comments = "Source note"
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/bibliography/sources.xml")!.Open();
        var source = XDocument.Load(entry).Root!.Element(B + "Source")!;

        source.Element(B + "City")!.Value.Should().Be("London");
        source.Element(B + "Edition")!.Value.Should().Be("2");
        source.Element(B + "StandardNumber")!.Value.Should().Be("ISBN-1");
        source.Element(B + "ShortTitle")!.Value.Should().Be("Depth");
        source.Element(B + "Comments")!.Value.Should().Be("Source note");
    }

    [Fact]
    public void BibliographyPart_WritesReportSourceTypeAndInstitution()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Report1",
            Type = SourceType.Report,
            Title = "Report Title",
            Institution = "Research Institute"
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/bibliography/sources.xml")!.Open();
        var source = XDocument.Load(entry).Root!.Element(B + "Source")!;

        source.Element(B + "SourceType")!.Value.Should().Be("Report");
        source.Element(B + "Institution")!.Value.Should().Be("Research Institute");
    }

    [Fact]
    public void BibliographyPart_WritesBookSectionSourceTypeAndFields()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Chapter2026",
            Type = SourceType.BookSection,
            Title = "Chapter Title",
            BookTitle = "Containing Book",
            ChapterNumber = "3",
            Pages = "12-20"
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/bibliography/sources.xml")!.Open();
        var source = XDocument.Load(entry).Root!.Element(B + "Source")!;

        source.Element(B + "SourceType")!.Value.Should().Be("BookSection");
        source.Element(B + "BookTitle")!.Value.Should().Be("Containing Book");
        source.Element(B + "ChapterNumber")!.Value.Should().Be("3");
        source.Element(B + "Pages")!.Value.Should().Be("12-20");
    }

    [Fact]
    public void BibliographyPart_WritesConferenceProceedingsSourceTypeAndFields()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Conf2026",
            Type = SourceType.ConferenceProceedings,
            Title = "Proceedings Paper",
            ConferenceName = "Proceedings of the Example Conference",
            Pages = "101-109"
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/bibliography/sources.xml")!.Open();
        var source = XDocument.Load(entry).Root!.Element(B + "Source")!;

        source.Element(B + "SourceType")!.Value.Should().Be("ConferenceProceedings");
        source.Element(B + "ConferenceName")!.Value.Should().Be("Proceedings of the Example Conference");
        source.Element(B + "Pages")!.Value.Should().Be("101-109");
    }

    [Fact]
    public void BibliographyPart_WritesNewWordSourceTypeTokens()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Periodical2026",
            Type = SourceType.ArticleInPeriodical,
            Title = "City Desk",
            Journal = "Daily Planet",
            Pages = "5-7"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Electronic2026",
            Type = SourceType.ElectronicSource,
            Title = "Online Notes",
            Url = "https://example.test/notes"
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/bibliography/sources.xml")!.Open();
        var sources = XDocument.Load(entry).Root!.Elements(B + "Source").ToList();

        sources[0].Element(B + "SourceType")!.Value.Should().Be("ArticleInAPeriodical");
        sources[0].Element(B + "JournalName")!.Value.Should().Be("Daily Planet");
        sources[0].Element(B + "Pages")!.Value.Should().Be("5-7");
        sources[1].Element(B + "SourceType")!.Value.Should().Be("ElectronicSource");
        sources[1].Element(B + "URL")!.Value.Should().Be("https://example.test/notes");
    }

    [Fact]
    public void BibliographyPart_WritesSourceManagerBreadthTokensFieldsAndRoles()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Patent2026",
            Type = SourceType.Patent,
            Inventor = "Lovelace, Ada",
            Title = "Analytical Engine Control",
            PatentNumber = "GB-1843-1",
            CountryRegion = "United Kingdom"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Interview2026",
            Type = SourceType.Interview,
            Interviewee = "Hopper, Grace",
            Interviewer = "Mauchly, Jean",
            Medium = "Recorded interview"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Misc2026",
            Type = SourceType.Misc,
            SourceKind = "Manuscript",
            Medium = "Scan"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Case2026",
            Type = SourceType.Case,
            Title = "Brown v. Board of Education",
            CaseNumber = "1",
            Court = "U.S. Supreme Court",
            Reporter = "347 U.S. 483",
            CountryRegion = "United States",
            StateProvince = "District of Columbia"
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/bibliography/sources.xml")!.Open();
        var sources = XDocument.Load(entry).Root!.Elements(B + "Source").ToList();

        sources[0].Element(B + "SourceType")!.Value.Should().Be("Patent");
        sources[0].Element(B + "PatentNumber")!.Value.Should().Be("GB-1843-1");
        sources[0].Element(B + "CountryRegion")!.Value.Should().Be("United Kingdom");
        sources[0].Element(B + "Author")!.Element(B + "Inventor")!
            .Element(B + "Corporate")!.Value.Should().Be("Lovelace, Ada");
        sources[1].Element(B + "SourceType")!.Value.Should().Be("Interview");
        sources[1].Element(B + "Author")!.Element(B + "Interviewee")!
            .Element(B + "Corporate")!.Value.Should().Be("Hopper, Grace");
        sources[1].Element(B + "Author")!.Element(B + "Interviewer")!
            .Element(B + "Corporate")!.Value.Should().Be("Mauchly, Jean");
        sources[1].Element(B + "Medium")!.Value.Should().Be("Recorded interview");
        sources[2].Element(B + "SourceType")!.Value.Should().Be("Misc");
        sources[2].Element(B + "Type")!.Value.Should().Be("Manuscript");
        sources[2].Element(B + "Medium")!.Value.Should().Be("Scan");
        sources[3].Element(B + "SourceType")!.Value.Should().Be("Case");
        sources[3].Element(B + "CaseNumber")!.Value.Should().Be("1");
        sources[3].Element(B + "Court")!.Value.Should().Be("U.S. Supreme Court");
        sources[3].Element(B + "Reporter")!.Value.Should().Be("347 U.S. 483");
        sources[3].Element(B + "CountryRegion")!.Value.Should().Be("United States");
        sources[3].Element(B + "StateProvince")!.Value.Should().Be("District of Columbia");
    }

    [Fact]
    public void BibliographyPart_WritesMediaBreadthTokensFieldsAndRoles()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Film2026",
            Type = SourceType.Film,
            Director = "Kubrick, Stanley",
            ProducerName = "MGM",
            Writer = "Clarke, Arthur C.",
            Performer = "Dullea, Keir",
            ProductionCompany = "Metro-Goldwyn-Mayer"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Recording2026",
            Type = SourceType.SoundRecording,
            Artist = "Holiday, Billie",
            Composer = "Strange, Lewis Allan",
            Conductor = "Jones, Quincy",
            AlbumTitle = "Lady Sings",
            RecordingNumber = "RS-1"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Art2026",
            Type = SourceType.Art,
            Artist = "Kahlo, Frida",
            Medium = "Oil on masonite"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Site2026",
            Type = SourceType.InternetSite,
            Url = "https://example.test",
            AccessedYear = "2026"
        });
        doc.Sources.Add(new Source
        {
            Tag = "Performance2026",
            Type = SourceType.Performance,
            Performer = "Royal Shakespeare Company",
            Conductor = "Doe, Jane",
            Theater = "Globe Theatre"
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/bibliography/sources.xml")!.Open();
        var sources = XDocument.Load(entry).Root!.Elements(B + "Source").ToList();

        sources[0].Element(B + "SourceType")!.Value.Should().Be("Film");
        sources[0].Element(B + "ProductionCompany")!.Value.Should().Be("Metro-Goldwyn-Mayer");
        sources[0].Element(B + "Author")!.Element(B + "Director")!
            .Element(B + "Corporate")!.Value.Should().Be("Kubrick, Stanley");
        sources[0].Element(B + "Author")!.Element(B + "ProducerName")!
            .Element(B + "Corporate")!.Value.Should().Be("MGM");
        sources[0].Element(B + "Author")!.Element(B + "Writer")!
            .Element(B + "Corporate")!.Value.Should().Be("Clarke, Arthur C.");
        sources[0].Element(B + "Author")!.Element(B + "Performer")!
            .Element(B + "Corporate")!.Value.Should().Be("Dullea, Keir");
        sources[1].Element(B + "SourceType")!.Value.Should().Be("SoundRecording");
        sources[1].Element(B + "AlbumTitle")!.Value.Should().Be("Lady Sings");
        sources[1].Element(B + "RecordingNumber")!.Value.Should().Be("RS-1");
        sources[1].Element(B + "Author")!.Element(B + "Artist")!
            .Element(B + "Corporate")!.Value.Should().Be("Holiday, Billie");
        sources[1].Element(B + "Author")!.Element(B + "Composer")!
            .Element(B + "Corporate")!.Value.Should().Be("Strange, Lewis Allan");
        sources[1].Element(B + "Author")!.Element(B + "Conductor")!
            .Element(B + "Corporate")!.Value.Should().Be("Jones, Quincy");
        sources[2].Element(B + "SourceType")!.Value.Should().Be("Art");
        sources[2].Element(B + "Author")!.Element(B + "Artist")!
            .Element(B + "Corporate")!.Value.Should().Be("Kahlo, Frida");
        sources[2].Element(B + "Medium")!.Value.Should().Be("Oil on masonite");
        sources[3].Element(B + "SourceType")!.Value.Should().Be("InternetSite");
        sources[3].Element(B + "URL")!.Value.Should().Be("https://example.test");
        sources[3].Element(B + "YearAccessed")!.Value.Should().Be("2026");
        sources[4].Element(B + "SourceType")!.Value.Should().Be("Performance");
        sources[4].Element(B + "Theater")!.Value.Should().Be("Globe Theatre");
        sources[4].Element(B + "Author")!.Element(B + "Performer")!
            .Element(B + "Corporate")!.Value.Should().Be("Royal Shakespeare Company");
    }

    [Fact]
    public void WordStyleNewSourceTypeTokens_ReadBackToModeledTypes()
    {
        var result = ReadDocxWithSourcesXml(
            """
            <b:Sources xmlns:b="http://schemas.openxmlformats.org/officeDocument/2006/bibliography">
              <b:Source>
                <b:Tag>Periodical2026</b:Tag>
                <b:SourceType>ArticleInAPeriodical</b:SourceType>
                <b:Title>City Desk</b:Title>
                <b:JournalName>Daily Planet</b:JournalName>
                <b:Pages>5-7</b:Pages>
              </b:Source>
              <b:Source>
                <b:Tag>Electronic2026</b:Tag>
                <b:SourceType>ElectronicSource</b:SourceType>
                <b:Title>Online Notes</b:Title>
                <b:URL>https://example.test/notes</b:URL>
                <b:DayAccessed>4</b:DayAccessed>
                <b:MonthAccessed>July</b:MonthAccessed>
                <b:YearAccessed>2026</b:YearAccessed>
              </b:Source>
            </b:Sources>
            """);

        result.Sources.Should().HaveCount(2);
        result.Sources[0].Type.Should().Be(SourceType.ArticleInPeriodical);
        result.Sources[0].Journal.Should().Be("Daily Planet");
        result.Sources[0].Pages.Should().Be("5-7");
        result.Sources[1].Type.Should().Be(SourceType.ElectronicSource);
        result.Sources[1].Url.Should().Be("https://example.test/notes");
        result.Sources[1].AccessedDay.Should().Be("4");
        result.Sources[1].AccessedMonth.Should().Be("July");
        result.Sources[1].AccessedYear.Should().Be("2026");
    }

    [Fact]
    public void WordStyleSourceManagerBreadthTokens_ReadBackToModeledTypes()
    {
        var result = ReadDocxWithSourcesXml(
            """
            <b:Sources xmlns:b="http://schemas.openxmlformats.org/officeDocument/2006/bibliography">
              <b:Source>
                <b:Tag>Patent2026</b:Tag>
                <b:SourceType>Patent</b:SourceType>
                <b:Author>
                  <b:Inventor><b:Corporate>Lovelace, Ada</b:Corporate></b:Inventor>
                </b:Author>
                <b:Title>Analytical Engine Control</b:Title>
                <b:PatentNumber>GB-1843-1</b:PatentNumber>
                <b:CountryRegion>United Kingdom</b:CountryRegion>
                <b:StateProvince>London</b:StateProvince>
                <b:Month>July</b:Month>
                <b:Day>4</b:Day>
                <b:Year>1843</b:Year>
              </b:Source>
              <b:Source>
                <b:Tag>Interview2026</b:Tag>
                <b:SourceType>Interview</b:SourceType>
                <b:Author>
                  <b:Interviewee><b:Corporate>Hopper, Grace</b:Corporate></b:Interviewee>
                  <b:Interviewer><b:Corporate>Mauchly, Jean</b:Corporate></b:Interviewer>
                </b:Author>
                <b:Title>Compiler Notes</b:Title>
                <b:Medium>Recorded interview</b:Medium>
              </b:Source>
              <b:Source>
                <b:Tag>Misc2026</b:Tag>
                <b:SourceType>Misc</b:SourceType>
                <b:Author><b:Author><b:Corporate>Example Archive</b:Corporate></b:Author></b:Author>
                <b:Title>Loose note</b:Title>
                <b:Type>Manuscript</b:Type>
                <b:Medium>Scan</b:Medium>
              </b:Source>
              <b:Source>
                <b:Tag>Case2026</b:Tag>
                <b:SourceType>Case</b:SourceType>
                <b:Author><b:Author><b:Corporate>Brown</b:Corporate></b:Author></b:Author>
                <b:Title>Brown v. Board of Education</b:Title>
                <b:CaseNumber>1</b:CaseNumber>
                <b:Court>U.S. Supreme Court</b:Court>
                <b:Reporter>347 U.S. 483</b:Reporter>
                <b:CountryRegion>United States</b:CountryRegion>
                <b:StateProvince>District of Columbia</b:StateProvince>
                <b:City>Washington</b:City>
                <b:Month>May</b:Month>
                <b:Day>17</b:Day>
                <b:Year>1954</b:Year>
              </b:Source>
            </b:Sources>
            """);

        result.Sources.Should().HaveCount(4);
        result.Sources[0].Type.Should().Be(SourceType.Patent);
        result.Sources[0].Inventor.Should().Be("Lovelace, Ada");
        result.Sources[0].PatentNumber.Should().Be("GB-1843-1");
        result.Sources[0].Month.Should().Be("July");
        result.Sources[0].Day.Should().Be("4");
        result.Sources[1].Type.Should().Be(SourceType.Interview);
        result.Sources[1].Interviewee.Should().Be("Hopper, Grace");
        result.Sources[1].Interviewer.Should().Be("Mauchly, Jean");
        result.Sources[1].Medium.Should().Be("Recorded interview");
        result.Sources[2].Type.Should().Be(SourceType.Misc);
        result.Sources[2].Author.Should().Be("Example Archive");
        result.Sources[2].SourceKind.Should().Be("Manuscript");
        result.Sources[2].Medium.Should().Be("Scan");
        result.Sources[3].Type.Should().Be(SourceType.Case);
        result.Sources[3].Author.Should().Be("Brown");
        result.Sources[3].CaseNumber.Should().Be("1");
        result.Sources[3].Court.Should().Be("U.S. Supreme Court");
        result.Sources[3].Reporter.Should().Be("347 U.S. 483");
        result.Sources[3].CountryRegion.Should().Be("United States");
        result.Sources[3].StateProvince.Should().Be("District of Columbia");
        result.Sources[3].City.Should().Be("Washington");
        result.Sources[3].Month.Should().Be("May");
        result.Sources[3].Day.Should().Be("17");
        result.Sources[3].Year.Should().Be("1954");
    }

    [Fact]
    public void WordStyleMediaBreadthTokens_ReadBackToModeledTypes()
    {
        var result = ReadDocxWithSourcesXml(
            """
            <b:Sources xmlns:b="http://schemas.openxmlformats.org/officeDocument/2006/bibliography">
              <b:Source>
                <b:Tag>Film2026</b:Tag>
                <b:SourceType>Film</b:SourceType>
                <b:Author>
                  <b:Director><b:Corporate>Kubrick, Stanley</b:Corporate></b:Director>
                  <b:ProducerName><b:Corporate>MGM</b:Corporate></b:ProducerName>
                  <b:Writer><b:Corporate>Clarke, Arthur C.</b:Corporate></b:Writer>
                  <b:Performer><b:Corporate>Dullea, Keir</b:Corporate></b:Performer>
                </b:Author>
                <b:Title>2001: A Space Odyssey</b:Title>
                <b:ProductionCompany>Metro-Goldwyn-Mayer</b:ProductionCompany>
              </b:Source>
              <b:Source>
                <b:Tag>Recording2026</b:Tag>
                <b:SourceType>SoundRecording</b:SourceType>
                <b:Author>
                  <b:Artist><b:Corporate>Holiday, Billie</b:Corporate></b:Artist>
                  <b:Composer><b:Corporate>Strange, Lewis Allan</b:Corporate></b:Composer>
                  <b:Conductor><b:Corporate>Jones, Quincy</b:Corporate></b:Conductor>
                </b:Author>
                <b:Title>Strange Fruit</b:Title>
                <b:AlbumTitle>Lady Sings</b:AlbumTitle>
                <b:RecordingNumber>RS-1</b:RecordingNumber>
              </b:Source>
              <b:Source>
                <b:Tag>Art2026</b:Tag>
                <b:SourceType>Art</b:SourceType>
                <b:Author><b:Artist><b:Corporate>Kahlo, Frida</b:Corporate></b:Artist></b:Author>
                <b:Title>The Broken Column</b:Title>
                <b:Institution>Museo Dolores Olmedo</b:Institution>
                <b:City>Mexico City</b:City>
              </b:Source>
              <b:Source>
                <b:Tag>Site2026</b:Tag>
                <b:SourceType>InternetSite</b:SourceType>
                <b:Title>Example Home</b:Title>
                <b:URL>https://example.test</b:URL>
                <b:YearAccessed>2026</b:YearAccessed>
              </b:Source>
              <b:Source>
                <b:Tag>Performance2026</b:Tag>
                <b:SourceType>Performance</b:SourceType>
                <b:Author>
                  <b:Performer><b:Corporate>Royal Shakespeare Company</b:Corporate></b:Performer>
                  <b:Conductor><b:Corporate>Doe, Jane</b:Corporate></b:Conductor>
                </b:Author>
                <b:Title>Hamlet</b:Title>
                <b:Theater>Globe Theatre</b:Theater>
                <b:Month>May</b:Month>
                <b:Day>8</b:Day>
              </b:Source>
            </b:Sources>
            """);

        result.Sources.Should().HaveCount(5);
        result.Sources[0].Type.Should().Be(SourceType.Film);
        result.Sources[0].Director.Should().Be("Kubrick, Stanley");
        result.Sources[0].ProducerName.Should().Be("MGM");
        result.Sources[0].Writer.Should().Be("Clarke, Arthur C.");
        result.Sources[0].Performer.Should().Be("Dullea, Keir");
        result.Sources[0].ProductionCompany.Should().Be("Metro-Goldwyn-Mayer");
        result.Sources[1].Type.Should().Be(SourceType.SoundRecording);
        result.Sources[1].Artist.Should().Be("Holiday, Billie");
        result.Sources[1].Composer.Should().Be("Strange, Lewis Allan");
        result.Sources[1].Conductor.Should().Be("Jones, Quincy");
        result.Sources[1].AlbumTitle.Should().Be("Lady Sings");
        result.Sources[1].RecordingNumber.Should().Be("RS-1");
        result.Sources[2].Type.Should().Be(SourceType.Art);
        result.Sources[2].Artist.Should().Be("Kahlo, Frida");
        result.Sources[2].Institution.Should().Be("Museo Dolores Olmedo");
        result.Sources[2].City.Should().Be("Mexico City");
        result.Sources[3].Type.Should().Be(SourceType.InternetSite);
        result.Sources[3].Url.Should().Be("https://example.test");
        result.Sources[3].AccessedYear.Should().Be("2026");
        result.Sources[4].Type.Should().Be(SourceType.Performance);
        result.Sources[4].Performer.Should().Be("Royal Shakespeare Company");
        result.Sources[4].Conductor.Should().Be("Doe, Jane");
        result.Sources[4].Theater.Should().Be("Globe Theatre");
        result.Sources[4].Month.Should().Be("May");
        result.Sources[4].Day.Should().Be("8");
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
                <b:Institution>Institute for Word Tests</b:Institution>
                <b:City>London</b:City>
                <b:Edition>2</b:Edition>
                <b:StandardNumber>ISBN-2</b:StandardNumber>
                <b:ShortTitle>Word Source</b:ShortTitle>
                <b:Comments>Imported note</b:Comments>
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
        source.Institution.Should().Be("Institute for Word Tests");
        source.City.Should().Be("London");
        source.Edition.Should().Be("2");
        source.StandardNumber.Should().Be("ISBN-2");
        source.ShortTitle.Should().Be("Word Source");
        source.Comments.Should().Be("Imported note");
    }

    [Fact]
    public void WordStyleContributorRoles_ReadsEditorAndTranslatorNameLists()
    {
        var result = ReadDocxWithSourcesXml(
            """
            <b:Sources xmlns:b="http://schemas.openxmlformats.org/officeDocument/2006/bibliography">
              <b:Source>
                <b:Tag>Edited2026</b:Tag>
                <b:SourceType>BookSection</b:SourceType>
                <b:Author>
                  <b:Author>
                    <b:NameList>
                      <b:Person>
                        <b:Last>Author</b:Last>
                        <b:First>Ada</b:First>
                      </b:Person>
                    </b:NameList>
                  </b:Author>
                  <b:Editor>
                    <b:NameList>
                      <b:Person>
                        <b:Last>Editor</b:Last>
                        <b:First>Edna</b:First>
                        <b:Middle>Q.</b:Middle>
                      </b:Person>
                    </b:NameList>
                  </b:Editor>
                  <b:Translator>
                    <b:NameList>
                      <b:Person>
                        <b:Last>Translator</b:Last>
                        <b:First>Tara</b:First>
                      </b:Person>
                    </b:NameList>
                  </b:Translator>
                </b:Author>
                <b:Title>Chapter with Roles</b:Title>
              </b:Source>
            </b:Sources>
            """);

        var source = result.Sources.Should().ContainSingle().Subject;
        source.Author.Should().Be("Ada Author");
        source.PersonalAuthors.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Ada", string.Empty, "Author"));
        source.Editors.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Edna", "Q.", "Editor"));
        source.Translators.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Tara", string.Empty, "Translator"));
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

    [Fact]
    public void ContributorRoleNameLists_WriteEditorAndTranslatorBlocks()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Edited2026",
            Type = SourceType.Book,
            Author = "Ada Author",
            PersonalAuthors = [SourceAuthorPerson.Create("Ada", string.Empty, "Author")],
            Editors = [SourceAuthorPerson.Create("Edna", "Q.", "Editor")],
            Translators = [SourceAuthorPerson.Create("Tara", string.Empty, "Translator")],
            Title = "Book with Roles"
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/bibliography/sources.xml")!.Open();
        var contributors = XDocument.Load(entry).Root!.Element(B + "Source")!.Element(B + "Author")!;

        contributors.Element(B + "Author")!.Element(B + "NameList")!
            .Elements(B + "Person").Should().ContainSingle();
        var editor = contributors.Element(B + "Editor")!.Element(B + "NameList")!
            .Elements(B + "Person").Should().ContainSingle().Subject;
        editor.Element(B + "First")!.Value.Should().Be("Edna");
        editor.Element(B + "Middle")!.Value.Should().Be("Q.");
        editor.Element(B + "Last")!.Value.Should().Be("Editor");
        var translator = contributors.Element(B + "Translator")!.Element(B + "NameList")!
            .Elements(B + "Person").Should().ContainSingle().Subject;
        translator.Element(B + "First")!.Value.Should().Be("Tara");
        translator.Element(B + "Last")!.Value.Should().Be("Translator");
    }

    [Fact]
    public void ContributorRoleNameLists_RoundTripThroughDocx()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Sources.Add(new Source
        {
            Tag = "Chapter2026",
            Type = SourceType.BookSection,
            Author = "Ada Author",
            PersonalAuthors = [SourceAuthorPerson.Create("Ada", string.Empty, "Author")],
            Editors = [SourceAuthorPerson.Create("Edna", "Q.", "Editor")],
            Translators = [SourceAuthorPerson.Create("Tara", string.Empty, "Translator")],
            Title = "Chapter with Roles",
            BookTitle = "Edited Book"
        });

        var source = RoundTrip(doc).Sources.Should().ContainSingle().Subject;

        source.PersonalAuthors.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Ada", string.Empty, "Author"));
        source.Editors.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Edna", "Q.", "Editor"));
        source.Translators.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Tara", string.Empty, "Translator"));
    }
}
