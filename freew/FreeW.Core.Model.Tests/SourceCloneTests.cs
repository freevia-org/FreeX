namespace FreeW.Core.Model.Tests;

public sealed class SourceCloneTests
{
    [Fact]
    public void Clone_PreservesExactIdentityAndPayloadWithoutAliasingContributorCollections()
    {
        var authors = new List<SourceAuthorPerson>
        {
            new(" Ada ", "", " Lovelace ")
        };
        var source = CreateSource("  Ada1843  ", authors);

        var clone = source.Clone();

        clone.Should().BeEquivalentTo(source);
        clone.Tag.Should().Be("  Ada1843  ");
        clone.PersonalAuthors.Should().NotBeSameAs(source.PersonalAuthors);
        clone.Editors.Should().NotBeSameAs(source.Editors);
        clone.Translators.Should().NotBeSameAs(source.Translators);

        authors.Add(SourceAuthorPerson.Create("Charles", "", "Babbage"));
        clone.PersonalAuthors.Should().ContainSingle();
    }

    [Fact]
    public void CloneCanonicalized_NormalizesIdentityAndContributorProjection()
    {
        var source = CreateSource(
            "  Ada1843  ",
            [
                new SourceAuthorPerson(" Ada ", " Augusta ", " Lovelace "),
                new SourceAuthorPerson(" ", "", " ")
            ]);

        var clone = source.CloneCanonicalized();

        clone.Tag.Should().Be("Ada1843");
        clone.PersonalAuthors.Should().Equal(new SourceAuthorPerson("Ada", "Augusta", "Lovelace"));
        source.Tag.Should().Be("  Ada1843  ");
        source.PersonalAuthors.Should().HaveCount(2);
    }

    [Fact]
    public void CloneWithTag_ReplacesOnlyIdentity()
    {
        var source = CreateSource("Shared", [SourceAuthorPerson.Create("Ada", "", "Lovelace")]);

        var clone = source.CloneWithTag("Shared_FreeW1");

        clone.Tag.Should().Be("Shared_FreeW1");
        clone.Should().BeEquivalentTo(source, options => options.Excluding(item => item.Tag));
        clone.PersonalAuthors.Should().NotBeSameAs(source.PersonalAuthors);
    }

    [Fact]
    public void ReplaceSourcesCommand_OwnsDetachedUndoAndRedoSnapshots()
    {
        var oldAuthors = new List<SourceAuthorPerson> { SourceAuthorPerson.Create("Old", "", "Author") };
        var newAuthors = new List<SourceAuthorPerson> { SourceAuthorPerson.Create("New", "", "Author") };
        var document = new TextDocument();
        document.Sources.Add(CreateSource("Old", oldAuthors));
        var command = new ReplaceSourcesCommand([CreateSource("New", newAuthors)]);
        var bus = new DocumentCommandBus(new TestContext(document));

        newAuthors.Add(SourceAuthorPerson.Create("Late", "", "Mutation"));
        bus.Execute(command);
        document.Sources.Single().PersonalAuthors.Should().ContainSingle();

        oldAuthors.Add(SourceAuthorPerson.Create("Late", "", "OldMutation"));
        bus.Undo().Should().BeTrue();
        document.Sources.Single().PersonalAuthors.Should().ContainSingle();

        ((SourceAuthorPerson[])document.Sources.Single().PersonalAuthors)[0] =
            SourceAuthorPerson.Create("Changed", "", "AfterUndo");
        bus.Redo().Should().BeTrue();
        document.Sources.Single().PersonalAuthors.Should().Equal(
            SourceAuthorPerson.Create("New", "", "Author"));
    }

    private static Source CreateSource(string tag, IReadOnlyList<SourceAuthorPerson> authors) => new()
    {
        Tag = tag,
        Type = SourceType.JournalArticle,
        Author = "Ada Lovelace",
        PersonalAuthors = authors,
        CorporateAuthor = "Analytical Society",
        Editors = [SourceAuthorPerson.Create("Edna", "", "Editor")],
        Translators = [SourceAuthorPerson.Create("Tara", "", "Translator")],
        Title = "Notes",
        Journal = "Scientific Memoirs",
        Year = "1843",
        Volume = "3",
        Issue = "1",
        Pages = "1-5",
        Url = "https://example.test/notes",
        Comments = "Preserve all fields"
    };

    private sealed class TestContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
