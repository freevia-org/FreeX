using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

public sealed class MasterSourceStoreTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.MasterSourceStoreTests-");

    public void Dispose() => _temporaryDirectory.Dispose();

    private string TemporaryPath(string fileName) => Path.Combine(_temporaryDirectory.Path, fileName);

    [Fact]
    public void MasterStore_AddSource_PersistsAndReloads()
    {
        var path = TemporaryPath("master-sources-test.json");
        try
        {
            var store1 = new MasterSourceStore();
            store1.AddOrUpdate(new Source
            {
                Tag       = "Smith2020",
                Author    = "Smith, John",
                Title     = "Test Book",
                Year      = "2020",
                Institution = "Test Institute",
                Publisher = "Test Press",
                City = "London",
                Edition = "2",
                StandardNumber = "ISBN-1",
                ShortTitle = "Test",
                Comments = "Master note"
            });
            var settingsStore = JsonSettingsStore<MasterSourceStore>.ForPath(path);
            settingsStore.Save(store1);

            // Reload from a new store instance.
            var store2 = JsonSettingsStore<MasterSourceStore>.ForPath(path).Load();
            store2.Sources.Should().HaveCount(1);
            store2.Sources[0].Tag.Should().Be("Smith2020");
            store2.Sources[0].Author.Should().Be("Smith, John");
            store2.Sources[0].Institution.Should().Be("Test Institute");
            store2.Sources[0].City.Should().Be("London");
            store2.Sources[0].Edition.Should().Be("2");
            store2.Sources[0].StandardNumber.Should().Be("ISBN-1");
            store2.Sources[0].ShortTitle.Should().Be("Test");
            store2.Sources[0].Comments.Should().Be("Master note");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MasterStore_ToSources_ReturnsModelObjects()
    {
        var store = new MasterSourceStore();
        store.AddOrUpdate(new Source
        {
            Tag = "A1",
            Author = "Alice",
            Title = "Alpha",
            Year = "2021",
            Institution = "Alpha Institute",
            City = "Paris",
            Edition = "1",
            StandardNumber = "ISBN-A",
            ShortTitle = "Alpha",
            Comments = "Preserved"
        });
        store.AddOrUpdate(new Source { Tag = "B2", Author = "Bob",   Title = "Beta",  Year = "2022" });

        var sources = store.ToSources();
        sources.Should().HaveCount(2);
        sources.Select(s => s.Tag).Should().Equal("A1", "B2");
        sources[0].Institution.Should().Be("Alpha Institute");
        sources[0].City.Should().Be("Paris");
        sources[0].Edition.Should().Be("1");
        sources[0].StandardNumber.Should().Be("ISBN-A");
        sources[0].ShortTitle.Should().Be("Alpha");
        sources[0].Comments.Should().Be("Preserved");
    }

    [Fact]
    public void MasterStore_ToSources_PreservesStructuredAuthors()
    {
        var store = new MasterSourceStore
        {
            Sources =
            [
                SourceRecord.FromSource(new Source
                {
                    Tag = "Ada1843",
                    Author = "Ada Lovelace",
                    PersonalAuthors = [SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace")],
                    Title = "Notes"
                }),
                SourceRecord.FromSource(new Source
                {
                    Tag = "Org2024",
                    Author = "World Health Organization",
                    CorporateAuthor = "World Health Organization"
                })
            ]
        };

        var sources = store.ToSources();

        sources[0].PersonalAuthors.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace"));
        sources[0].CorporateAuthor.Should().BeNull();
        sources[1].PersonalAuthors.Should().BeEmpty();
        sources[1].CorporateAuthor.Should().Be("World Health Organization");
    }

    [Fact]
    public void MasterStore_JsonRoundTrip_PreservesStructuredAuthors()
    {
        var path = TemporaryPath("master-sources-structured.json");
        try
        {
            var store = new MasterSourceStore
            {
                Sources =
                [
                    SourceRecord.FromSource(new Source
                    {
                        Tag = "Ada1843",
                        Author = "Ada Lovelace",
                        PersonalAuthors = [SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace")],
                        Editors = [SourceAuthorPerson.Create("Edna", string.Empty, "Editor")],
                        Translators = [SourceAuthorPerson.Create("Tara", string.Empty, "Translator")],
                        Title = "Notes"
                    }),
                    SourceRecord.FromSource(new Source
                    {
                        Tag = "Org2024",
                        Author = "World Health Organization",
                        CorporateAuthor = "World Health Organization"
                    })
                ]
            };
            var settingsStore = JsonSettingsStore<MasterSourceStore>.ForPath(path);

            settingsStore.Save(store);
            var reloaded = JsonSettingsStore<MasterSourceStore>.ForPath(path).Load().ToSources();

            reloaded[0].PersonalAuthors.Should().ContainSingle()
                .Which.Should().Be(SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace"));
            reloaded[0].Editors.Should().ContainSingle()
                .Which.Should().Be(SourceAuthorPerson.Create("Edna", string.Empty, "Editor"));
            reloaded[0].Translators.Should().ContainSingle()
                .Which.Should().Be(SourceAuthorPerson.Create("Tara", string.Empty, "Translator"));
            reloaded[0].CorporateAuthor.Should().BeNull();
            reloaded[1].PersonalAuthors.Should().BeEmpty();
            reloaded[1].CorporateAuthor.Should().Be("World Health Organization");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MasterStore_JsonRoundTrip_PreservesReportInstitution()
    {
        var path = TemporaryPath("master-sources-report.json");
        try
        {
            var store = new MasterSourceStore
            {
                Sources =
                [
                    SourceRecord.FromSource(new Source
                    {
                        Tag = "Report1",
                        Type = SourceType.Report,
                        Title = "Annual Report",
                        Year = "2026",
                        Institution = "National Bureau of Standards",
                        City = "Washington",
                        Publisher = "Government Printing Office",
                        StandardNumber = "NBS-2026-01"
                    })
                ]
            };
            var settingsStore = JsonSettingsStore<MasterSourceStore>.ForPath(path);

            settingsStore.Save(store);
            var source = JsonSettingsStore<MasterSourceStore>.ForPath(path).Load().ToSources()
                .Should().ContainSingle().Subject;

            source.Type.Should().Be(SourceType.Report);
            source.Institution.Should().Be("National Bureau of Standards");
            source.City.Should().Be("Washington");
            source.Publisher.Should().Be("Government Printing Office");
            source.StandardNumber.Should().Be("NBS-2026-01");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MasterStore_JsonRoundTrip_PreservesStructuredAccessedDate()
    {
        var path = TemporaryPath("master-sources-accessed.json");
        try
        {
            var store = new MasterSourceStore
            {
                Sources =
                [
                    SourceRecord.FromSource(new Source
                    {
                        Tag = "Web2024",
                        Type = SourceType.WebSite,
                        Title = "Web Source",
                        Url = "https://example.test",
                        AccessedDay = "3",
                        AccessedMonth = "May",
                        AccessedYear = "2024"
                    })
                ]
            };
            var settingsStore = JsonSettingsStore<MasterSourceStore>.ForPath(path);

            settingsStore.Save(store);
            var source = JsonSettingsStore<MasterSourceStore>.ForPath(path).Load().ToSources()
                .Should().ContainSingle().Subject;

            source.Type.Should().Be(SourceType.WebSite);
            source.AccessedDay.Should().Be("3");
            source.AccessedMonth.Should().Be("May");
            source.AccessedYear.Should().Be("2024");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MasterStore_JsonRoundTrip_PreservesBookSectionFields()
    {
        var path = TemporaryPath("master-sources-book-section.json");
        try
        {
            var store = new MasterSourceStore
            {
                Sources =
                [
                    SourceRecord.FromSource(new Source
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
                        StandardNumber = "ISBN-1"
                    })
                ]
            };
            var settingsStore = JsonSettingsStore<MasterSourceStore>.ForPath(path);

            settingsStore.Save(store);
            var source = JsonSettingsStore<MasterSourceStore>.ForPath(path).Load().ToSources()
                .Should().ContainSingle().Subject;

            source.Type.Should().Be(SourceType.BookSection);
            source.BookTitle.Should().Be("Containing Book");
            source.ChapterNumber.Should().Be("3");
            source.Pages.Should().Be("12-20");
            source.City.Should().Be("London");
            source.Publisher.Should().Be("Test Press");
            source.Edition.Should().Be("2");
            source.StandardNumber.Should().Be("ISBN-1");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MasterStore_JsonRoundTrip_PreservesConferenceProceedingsFields()
    {
        var path = TemporaryPath("master-sources-conference.json");
        try
        {
            var store = new MasterSourceStore
            {
                Sources =
                [
                    SourceRecord.FromSource(new Source
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
                        StandardNumber = "ISBN-CP-1"
                    })
                ]
            };
            var settingsStore = JsonSettingsStore<MasterSourceStore>.ForPath(path);

            settingsStore.Save(store);
            var source = JsonSettingsStore<MasterSourceStore>.ForPath(path).Load().ToSources()
                .Should().ContainSingle().Subject;

            source.Type.Should().Be(SourceType.ConferenceProceedings);
            source.ConferenceName.Should().Be("Proceedings of the Example Conference");
            source.Pages.Should().Be("101-109");
            source.City.Should().Be("Berlin");
            source.Publisher.Should().Be("ACM");
            source.StandardNumber.Should().Be("ISBN-CP-1");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MasterStore_JsonRoundTrip_PreservesSourceManagerBreadthFields()
    {
        var path = TemporaryPath("master-sources-breadth.json");
        try
        {
            var store = new MasterSourceStore
            {
                Sources =
                [
                    SourceRecord.FromSource(new Source
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
                        StateProvince = "London"
                    }),
                    SourceRecord.FromSource(new Source
                    {
                        Tag = "Interview2026",
                        Type = SourceType.Interview,
                        Interviewee = "Hopper, Grace",
                        Interviewer = "Mauchly, Jean",
                        Medium = "Recorded interview"
                    }),
                    SourceRecord.FromSource(new Source
                    {
                        Tag = "Misc2026",
                        Type = SourceType.Misc,
                        Author = "Example Archive",
                        SourceKind = "Manuscript",
                        Medium = "Scan"
                    }),
                    SourceRecord.FromSource(new Source
                    {
                        Tag = "Film2026",
                        Type = SourceType.Film,
                        Director = "Kubrick, Stanley",
                        ProducerName = "MGM",
                        Writer = "Clarke, Arthur C.",
                        Performer = "Dullea, Keir",
                        ProductionCompany = "Metro-Goldwyn-Mayer",
                        Medium = "Film"
                    }),
                    SourceRecord.FromSource(new Source
                    {
                        Tag = "Recording2026",
                        Type = SourceType.SoundRecording,
                        Artist = "Holiday, Billie",
                        Composer = "Strange, Lewis Allan",
                        Conductor = "Jones, Quincy",
                        Performer = "Holiday, Billie",
                        ProducerName = "Norman Granz",
                        AlbumTitle = "Lady Sings",
                        RecordingNumber = "RS-1",
                        Medium = "LP"
                    }),
                    SourceRecord.FromSource(new Source
                    {
                        Tag = "Art2026",
                        Type = SourceType.Art,
                        Artist = "Kahlo, Frida",
                        Institution = "Museo Dolores Olmedo",
                        City = "Mexico City",
                        Medium = "Oil on masonite"
                    }),
                    SourceRecord.FromSource(new Source
                    {
                        Tag = "Site2026",
                        Type = SourceType.InternetSite,
                        Author = "Example Archive",
                        Publisher = "Example Site",
                        Url = "https://example.test",
                        AccessedYear = "2026"
                    }),
                    SourceRecord.FromSource(new Source
                    {
                        Tag = "Performance2026",
                        Type = SourceType.Performance,
                        Performer = "Royal Shakespeare Company",
                        Conductor = "Doe, Jane",
                        Theater = "Globe Theatre",
                        City = "London",
                        Month = "May",
                        Day = "8",
                        Medium = "Stage performance"
                    }),
                    SourceRecord.FromSource(new Source
                    {
                        Tag = "Case2026",
                        Type = SourceType.Case,
                        Author = "Brown",
                        Title = "Brown v. Board of Education",
                        CaseNumber = "1",
                        Court = "U.S. Supreme Court",
                        Reporter = "347 U.S. 483",
                        CountryRegion = "United States",
                        StateProvince = "District of Columbia",
                        City = "Washington",
                        Month = "May",
                        Day = "17",
                        Year = "1954"
                    })
                ]
            };
            var settingsStore = JsonSettingsStore<MasterSourceStore>.ForPath(path);

            settingsStore.Save(store);
            var sources = JsonSettingsStore<MasterSourceStore>.ForPath(path).Load().ToSources();

            sources.Should().HaveCount(9);
            sources[0].Type.Should().Be(SourceType.Patent);
            sources[0].Inventor.Should().Be("Lovelace, Ada");
            sources[0].PatentNumber.Should().Be("GB-1843-1");
            sources[0].CountryRegion.Should().Be("United Kingdom");
            sources[0].StateProvince.Should().Be("London");
            sources[0].Month.Should().Be("July");
            sources[0].Day.Should().Be("4");
            sources[1].Type.Should().Be(SourceType.Interview);
            sources[1].Interviewee.Should().Be("Hopper, Grace");
            sources[1].Interviewer.Should().Be("Mauchly, Jean");
            sources[1].Medium.Should().Be("Recorded interview");
            sources[2].Type.Should().Be(SourceType.Misc);
            sources[2].SourceKind.Should().Be("Manuscript");
            sources[2].Medium.Should().Be("Scan");
            sources[3].Type.Should().Be(SourceType.Film);
            sources[3].Director.Should().Be("Kubrick, Stanley");
            sources[3].ProductionCompany.Should().Be("Metro-Goldwyn-Mayer");
            sources[4].Type.Should().Be(SourceType.SoundRecording);
            sources[4].Artist.Should().Be("Holiday, Billie");
            sources[4].AlbumTitle.Should().Be("Lady Sings");
            sources[4].RecordingNumber.Should().Be("RS-1");
            sources[5].Type.Should().Be(SourceType.Art);
            sources[5].Artist.Should().Be("Kahlo, Frida");
            sources[5].Institution.Should().Be("Museo Dolores Olmedo");
            sources[6].Type.Should().Be(SourceType.InternetSite);
            sources[6].Url.Should().Be("https://example.test");
            sources[6].AccessedYear.Should().Be("2026");
            sources[7].Type.Should().Be(SourceType.Performance);
            sources[7].Performer.Should().Be("Royal Shakespeare Company");
            sources[7].Theater.Should().Be("Globe Theatre");
            sources[8].Type.Should().Be(SourceType.Case);
            sources[8].CaseNumber.Should().Be("1");
            sources[8].Court.Should().Be("U.S. Supreme Court");
            sources[8].Reporter.Should().Be("347 U.S. 483");
            sources[8].CountryRegion.Should().Be("United States");
            sources[8].StateProvince.Should().Be("District of Columbia");
            sources[8].City.Should().Be("Washington");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MasterStore_AddOrUpdate_ReplacesExistingTag()
    {
        var store = new MasterSourceStore();
        store.AddOrUpdate(new Source { Tag = "X1", Author = "Old Author", Title = "Old", Year = "2000" });
        store.AddOrUpdate(new Source { Tag = "X1", Author = "New Author", Title = "New", Year = "2024" });

        store.Sources.Should().HaveCount(1);
        store.Sources[0].Author.Should().Be("New Author");
    }

    [Fact]
    public void MasterStore_AddOrUpdateAndRemove_UseTrimmedTagIdentity()
    {
        var store = new MasterSourceStore();
        store.AddOrUpdate(new Source { Tag = " Smith2020 ", Author = "Old Author", Title = "Old", Year = "2020" });
        store.AddOrUpdate(new Source { Tag = "Smith2020", Author = "New Author", Title = "New", Year = "2024" });

        store.Sources.Should().ContainSingle();
        store.Sources[0].Tag.Should().Be("Smith2020");
        store.Sources[0].Author.Should().Be("New Author");

        store.Remove(" Smith2020 ").Should().BeTrue();
        store.Sources.Should().BeEmpty();
    }

    [Fact]
    public void MasterStore_AddOrUpdateAndRemove_DoNotCollapseBlankTags()
    {
        var store = new MasterSourceStore();
        store.AddOrUpdate(new Source { Tag = " ", Author = "First Author", Title = "First", Year = "2020" });
        store.AddOrUpdate(new Source { Tag = string.Empty, Author = "Second Author", Title = "Second", Year = "2024" });

        store.Sources.Should().HaveCount(2);
        store.Sources.Select(source => source.Tag).Should().Equal(string.Empty, string.Empty);
        store.Sources.Select(source => source.Author).Should().Equal("First Author", "Second Author");

        store.Remove(" ").Should().BeFalse();
        store.Sources.Should().HaveCount(2);
    }

    [Fact]
    public void MasterStore_Remove_DeletesByTag()
    {
        var store = new MasterSourceStore();
        store.AddOrUpdate(new Source { Tag = "Del1",  Author = "A", Title = "T",  Year = "2020" });
        store.AddOrUpdate(new Source { Tag = "Keep1", Author = "B", Title = "T2", Year = "2021" });

        store.Remove("Del1").Should().BeTrue();
        store.Sources.Should().HaveCount(1);
        store.Sources[0].Tag.Should().Be("Keep1");
    }

    [Fact]
    public void MasterStore_CopyToCurrentDoc_SourceAppearsInList()
    {
        // Simulate the "Copy → Current Doc" operation without a DocumentView:
        // start with a master containing one source and a doc with zero sources;
        // after copy the doc list should contain the master source.
        var masterStore = new MasterSourceStore();
        masterStore.AddOrUpdate(new Source
        {
            Tag    = "Copy1",
            Author = "Copy Author",
            Title  = "Copy Title",
            Year   = "2023"
        });

        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [],
            masterSources: masterStore.ToSources());
        var plan = SourceManagementDialogPlanner.CopyMasterToCurrent(
            state,
            masterSelectedIndex: 0,
            currentSelectedIndex: -1);

        plan.Conflict.Should().BeNull();
        plan.State.CurrentSources.Should().HaveCount(1);
        plan.State.CurrentSources[0].Tag.Should().Be("Copy1");
    }
}
