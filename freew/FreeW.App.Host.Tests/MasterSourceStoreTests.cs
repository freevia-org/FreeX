using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

public sealed class MasterSourceStoreTests
{
    [Fact]
    public void MasterStore_AddSource_PersistsAndReloads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"master-sources-test-{Guid.NewGuid()}.json");
        try
        {
            var store1 = new MasterSourceStore();
            store1.AddOrUpdate(new Source
            {
                Tag       = "Smith2020",
                Author    = "Smith, John",
                Title     = "Test Book",
                Year      = "2020",
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
        var path = Path.Combine(Path.GetTempPath(), $"master-sources-structured-{Guid.NewGuid()}.json");
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
    public void MasterStore_AddOrUpdate_ReplacesExistingTag()
    {
        var store = new MasterSourceStore();
        store.AddOrUpdate(new Source { Tag = "X1", Author = "Old Author", Title = "Old", Year = "2000" });
        store.AddOrUpdate(new Source { Tag = "X1", Author = "New Author", Title = "New", Year = "2024" });

        store.Sources.Should().HaveCount(1);
        store.Sources[0].Author.Should().Be("New Author");
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

        // Simulate document-level source list (mutable list as document keeps internally).
        var docSources = new System.Collections.Generic.List<Source>();

        // Perform copy: take all master sources and add to doc (simulate dialog Copy→ action).
        var masterSources = masterStore.ToSources();
        foreach (var src in masterSources)
        {
            if (!docSources.Any(s => s.Tag == src.Tag))
                docSources.Add(src);
        }

        docSources.Should().HaveCount(1);
        docSources[0].Tag.Should().Be("Copy1");
    }
}
