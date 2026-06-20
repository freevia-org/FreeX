namespace FreeW.Core.Model.Tests;

public class QuickPartsTests
{
    [Fact]
    public void Add_ThenGet_RoundTripsContent()
    {
        var store = new QuickPartStore();

        store.Add("Greeting", ["Hello there", "Best regards"]);

        var part = store.Get("Greeting");
        part.Should().NotBeNull();
        part!.Name.Should().Be("Greeting");
        part.Lines.Should().Equal("Hello there", "Best regards");
        part.Text.Should().Be("Hello there\nBest regards");
    }

    [Fact]
    public void Get_MissingName_ReturnsNull()
    {
        var store = new QuickPartStore();

        store.Get("nope").Should().BeNull();
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        var store = new QuickPartStore();
        store.Add("Sig", ["Jane Doe"]);

        store.Get("sig").Should().NotBeNull();
        store.Get("SIG").Should().NotBeNull();
        store.Contains("sIg").Should().BeTrue();
    }

    [Fact]
    public void Add_SameName_OverwritesAndDoesNotDuplicate()
    {
        var store = new QuickPartStore();

        store.Add("Block", ["first version"]);
        store.Add("block", ["second version"]); // different case, same entry

        store.Count.Should().Be(1);
        store.Get("Block")!.Text.Should().Be("second version");
        store.Names.Should().ContainSingle();
    }

    [Fact]
    public void Names_AreSortedCaseInsensitively()
    {
        var store = new QuickPartStore();
        store.Add("zeta", ["z"]);
        store.Add("Alpha", ["a"]);
        store.Add("beta", ["b"]);

        store.Names.Should().Equal("Alpha", "beta", "zeta");
    }

    [Fact]
    public void Remove_ExistingName_ReturnsTrueAndDropsEntry()
    {
        var store = new QuickPartStore();
        store.Add("Temp", ["x"]);

        store.Remove("temp").Should().BeTrue(); // case-insensitive
        store.Get("Temp").Should().BeNull();
        store.Count.Should().Be(0);
    }

    [Fact]
    public void Remove_MissingName_ReturnsFalseAndIsNotAnError()
    {
        var store = new QuickPartStore();
        store.Add("Keep", ["x"]);

        store.Remove("absent").Should().BeFalse();
        store.Count.Should().Be(1);
    }

    [Fact]
    public void Add_BlankName_Throws()
    {
        var store = new QuickPartStore();

        var act = () => store.Add("   ", ["x"]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void QuickPart_TrimsName()
    {
        var part = new QuickPart("  Spaced  ", ["x"]);

        part.Name.Should().Be("Spaced");
    }

    [Fact]
    public void Get_TrimsLookupName()
    {
        var store = new QuickPartStore();
        store.Add("Sig", ["Jane"]);

        store.Get("  Sig  ").Should().NotBeNull();
    }

    [Fact]
    public void FromText_SplitsOnNewlines_BothLineEndings()
    {
        var part = QuickPart.FromText("Multi", "one\r\ntwo\nthree");

        part.Lines.Should().Equal("one", "two", "three");
    }

    [Fact]
    public void FromParagraphs_FlattensToPlainText()
    {
        var paragraphs = new List<Paragraph>
        {
            BuildParagraph("Hello ", "world"),
            new("Second line")
        };

        var part = QuickPart.FromParagraphs("Snippet", paragraphs);

        part.Lines.Should().Equal("Hello world", "Second line");
    }

    [Fact]
    public void ToParagraphs_ProducesOneParagraphPerLine()
    {
        var part = new QuickPart("Snippet", ["alpha", "beta"]);

        var paragraphs = part.ToParagraphs();

        paragraphs.Should().HaveCount(2);
        paragraphs[0].PlainText.Should().Be("alpha");
        paragraphs[1].PlainText.Should().Be("beta");
    }

    [Fact]
    public void BuildingBlock_Metadata_DefaultsWhenOmitted()
    {
        var part = new QuickPart("Block", ["body"]);

        part.Gallery.Should().Be(QuickPart.DefaultGallery);
        part.Category.Should().Be(QuickPart.DefaultCategory);
        part.Description.Should().BeEmpty();
    }

    [Fact]
    public void BuildingBlock_Metadata_IsTrimmedAndPreserved()
    {
        var part = new QuickPart("Block", ["body"], "  AutoText ", " Header ", "  A reusable header.  ");

        part.Gallery.Should().Be("AutoText");
        part.Category.Should().Be("Header");
        part.Description.Should().Be("A reusable header.");
    }

    [Fact]
    public void BuildingBlock_BlankMetadata_FallsBackToDefaults()
    {
        var part = new QuickPart("Block", ["body"], "   ", "", null);

        part.Gallery.Should().Be(QuickPart.DefaultGallery);
        part.Category.Should().Be(QuickPart.DefaultCategory);
        part.Description.Should().BeEmpty();
    }

    [Fact]
    public void FromText_CarriesBuildingBlockMetadata()
    {
        var part = QuickPart.FromText("Sig", "Jane\nDoe", gallery: "AutoText", category: "Signatures", description: "My sign-off");

        part.Lines.Should().Equal("Jane", "Doe");
        part.Gallery.Should().Be("AutoText");
        part.Category.Should().Be("Signatures");
        part.Description.Should().Be("My sign-off");
    }

    [Fact]
    public void Store_AddListDeleteGet_RoundTripsBlocksWithMetadata()
    {
        var store = new QuickPartStore();
        store.Add(new QuickPart("Greeting", ["Hello"], "AutoText", "General", "A greeting"));
        store.Add(new QuickPart("Footer", ["Page"], "Quick Parts", "Footers", "Standard footer"));

        // List: snippets come back in case-insensitive name order, carrying their metadata.
        store.Snippets.Select(p => p.Name).Should().Equal("Footer", "Greeting");
        var footer = store.Snippets[0];
        footer.Gallery.Should().Be("Quick Parts");
        footer.Category.Should().Be("Footers");

        // Get: retrieves a specific block with its description intact.
        store.Get("greeting")!.Description.Should().Be("A greeting");

        // Delete: drops just that block.
        store.Remove("Greeting").Should().BeTrue();
        store.Get("Greeting").Should().BeNull();
        store.Snippets.Should().ContainSingle().Which.Name.Should().Be("Footer");
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var store = new QuickPartStore();
        store.Add("a", ["1"]);
        store.Add("b", ["2"]);

        store.Clear();

        store.Count.Should().Be(0);
        store.Names.Should().BeEmpty();
    }

    private static Paragraph BuildParagraph(params string[] runTexts)
    {
        var paragraph = new Paragraph();
        foreach (var text in runTexts)
            paragraph.Runs.Add(new Run(text));
        return paragraph;
    }
}
