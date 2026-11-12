using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class HeaderFooterSectionLinkPlannerTests
{
    [Fact]
    public void A_section_without_its_own_slot_is_linked_and_shows_the_previous_sections_content()
    {
        var document = ThreeSections();
        document.Sections[0].HeadersFooters.Header = new HeaderFooter("one");

        var second = HeaderFooterSectionLinkPlanner.Describe(document.Sections, 1, HeaderFooterSlotKind.Header);
        var third = HeaderFooterSectionLinkPlanner.Describe(document.Sections, 2, HeaderFooterSlotKind.Header);

        second.IsLinkedToPrevious.Should().BeTrue();
        second.Content!.PlainText.Should().Be("one");
        third.IsLinkedToPrevious.Should().BeTrue();
        third.Content.Should().BeSameAs(second.Content, "the whole chain shows one shared header");
        third.HasNextSection.Should().BeFalse();
        third.HasPreviousSection.Should().BeTrue();
    }

    [Fact]
    public void The_first_section_is_never_linked_and_cannot_link()
    {
        var document = ThreeSections();

        var first = HeaderFooterSectionLinkPlanner.Describe(document.Sections, 0, HeaderFooterSlotKind.Footer);

        first.IsLinkedToPrevious.Should().BeFalse();
        first.CanLinkToPrevious.Should().BeFalse();
        first.Content.Should().BeNull();
        HeaderFooterSectionLinkPlanner.ToggleLinkToPrevious(document.Sections, 0, HeaderFooterSlotKind.Footer)
            .Should().BeFalse();
        document.Sections[0].HeadersFooters.Footer.Should().BeNull();
    }

    [Fact]
    public void Unlinking_copies_the_inherited_content_so_the_section_can_diverge()
    {
        var document = ThreeSections();
        var inherited = new HeaderFooter();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("shared") { Formatting = RunFormatting.Default with { Bold = true } });
        inherited.Paragraphs.Add(paragraph);
        document.Sections[0].HeadersFooters.Header = inherited;

        HeaderFooterSectionLinkPlanner.ToggleLinkToPrevious(document.Sections, 1, HeaderFooterSlotKind.Header)
            .Should().BeTrue();

        var own = document.Sections[1].HeadersFooters.Header;
        own.Should().NotBeNull().And.NotBeSameAs(inherited);
        own!.Paragraphs.Single().Should().NotBeSameAs(paragraph);
        own.Paragraphs.Single().Runs.Single().Text.Should().Be("shared");
        own.Paragraphs.Single().Runs.Single().Formatting.Bold.Should().BeTrue();
        HeaderFooterSectionLinkPlanner.IsLinkedToPrevious(document.Sections, 1, HeaderFooterSlotKind.Header)
            .Should().BeFalse();
        HeaderFooterSectionLinkPlanner.IsLinkedToPrevious(document.Sections, 2, HeaderFooterSlotKind.Header)
            .Should().BeTrue("section 3 now follows section 2's own header");

        own.Paragraphs.Single().Runs.Single().Text = "diverged";
        inherited.PlainText.Should().Be("shared");
        HeaderFooterSectionLinkPlanner.Describe(document.Sections, 2, HeaderFooterSlotKind.Header)
            .Content!.PlainText.Should().Be("diverged");
    }

    [Fact]
    public void Unlinking_with_nothing_to_inherit_creates_an_own_blank_slot()
    {
        var document = ThreeSections();

        HeaderFooterSectionLinkPlanner.SetLinkedToPrevious(
            document.Sections, 1, HeaderFooterSlotKind.EvenFooter, linked: false).Should().BeTrue();

        document.Sections[1].HeadersFooters.EvenFooter.Should().NotBeNull();
        document.Sections[1].HeadersFooters.EvenFooter!.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Relinking_discards_the_sections_own_definition_for_that_slot_only()
    {
        var document = ThreeSections();
        document.Sections[0].HeadersFooters.Header = new HeaderFooter("one");
        document.Sections[1].HeadersFooters.Header = new HeaderFooter("two");
        document.Sections[1].HeadersFooters.Footer = new HeaderFooter("two footer");

        HeaderFooterSectionLinkPlanner.ToggleLinkToPrevious(document.Sections, 1, HeaderFooterSlotKind.Header)
            .Should().BeTrue();

        document.Sections[1].HeadersFooters.Header.Should().BeNull();
        document.Sections[1].HeadersFooters.Footer!.PlainText.Should().Be("two footer");
        HeaderFooterSectionLinkPlanner.Describe(document.Sections, 1, HeaderFooterSlotKind.Header)
            .Content!.PlainText.Should().Be("one");
    }

    [Fact]
    public void Editing_a_linked_slot_edits_the_shared_owner_and_keeps_the_link()
    {
        var document = ThreeSections();
        document.Sections[0].HeadersFooters.Header = new HeaderFooter("one");

        var owner = HeaderFooterSectionLinkPlanner.CommitSlotContent(
            document.Sections, 2, HeaderFooterSlotKind.Header, new HeaderFooter("edited from section 3"));

        owner.Should().Be(0);
        document.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("edited from section 3");
        document.Sections[2].HeadersFooters.Header.Should().BeNull("editing must not silently unlink");
    }

    [Fact]
    public void Editing_an_unlinked_slot_changes_only_that_section()
    {
        var document = ThreeSections();
        document.Sections[0].HeadersFooters.Header = new HeaderFooter("one");
        document.Sections[1].HeadersFooters.Header = new HeaderFooter("two");

        HeaderFooterSectionLinkPlanner.CommitSlotContent(
            document.Sections, 1, HeaderFooterSlotKind.Header, new HeaderFooter("two edited")).Should().Be(1);

        document.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("one");
        document.Sections[1].HeadersFooters.Header!.PlainText.Should().Be("two edited");
    }

    [Fact]
    public void Content_typed_where_no_section_defines_the_slot_lands_at_the_start_of_the_link_chain()
    {
        var document = ThreeSections();

        HeaderFooterSectionLinkPlanner.CommitSlotContent(
            document.Sections, 1, HeaderFooterSlotKind.Footer, new HeaderFooter()).Should().Be(-1);
        document.Sections.Should().OnlyContain(section => section.HeadersFooters.Footer == null);

        HeaderFooterSectionLinkPlanner.CommitSlotContent(
            document.Sections, 1, HeaderFooterSlotKind.Footer, new HeaderFooter("typed")).Should().Be(0);
        HeaderFooterSectionLinkPlanner.Describe(document.Sections, 2, HeaderFooterSlotKind.Footer)
            .Content!.PlainText.Should().Be("typed", "every linked section shows the shared footer");
    }

    [Theory]
    [InlineData(0, -1, null)]
    [InlineData(0, 1, 1)]
    [InlineData(1, -1, 0)]
    [InlineData(2, 1, null)]
    [InlineData(1, 0, null)]
    public void Previous_and_next_stop_at_the_document_ends(int from, int direction, int? expected)
    {
        HeaderFooterSectionLinkPlanner.PlanAdjacentSection(ThreeSections().Sections, from, direction)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(HeaderFooterSlotKind.Header, false, HeaderFooterSlotKind.Footer)]
    [InlineData(HeaderFooterSlotKind.Footer, true, HeaderFooterSlotKind.Header)]
    [InlineData(HeaderFooterSlotKind.FirstHeader, false, HeaderFooterSlotKind.FirstFooter)]
    [InlineData(HeaderFooterSlotKind.EvenFooter, true, HeaderFooterSlotKind.EvenHeader)]
    [InlineData(HeaderFooterSlotKind.EvenFooter, false, HeaderFooterSlotKind.EvenFooter)]
    public void Go_to_header_or_footer_keeps_the_page_type(
        HeaderFooterSlotKind slot,
        bool header,
        HeaderFooterSlotKind expected)
    {
        HeaderFooterSectionLinkPlanner.CounterpartSlot(slot, header).Should().Be(expected);
    }

    [Fact]
    public void Moving_to_a_section_that_does_not_show_the_slot_falls_back_to_its_default_slot()
    {
        var document = ThreeSections();
        document.Sections[1].Page.DifferentFirstPage = true;

        HeaderFooterSectionLinkPlanner.ResolveSlotForSection(document.Sections[1], HeaderFooterSlotKind.FirstFooter)
            .Should().Be(HeaderFooterSlotKind.FirstFooter);
        HeaderFooterSectionLinkPlanner.ResolveSlotForSection(document.Sections[2], HeaderFooterSlotKind.FirstFooter)
            .Should().Be(HeaderFooterSlotKind.Footer);
    }

    [Fact]
    public void Editing_label_names_the_section_and_marks_a_linked_slot_same_as_previous()
    {
        var document = ThreeSections();
        document.Sections[0].HeadersFooters.Header = new HeaderFooter("one");

        HeaderFooterSectionLinkPlanner.BuildEditingLabel(
                HeaderFooterSectionLinkPlanner.Describe(document.Sections, 0, HeaderFooterSlotKind.Header))
            .Should().Be("Default Header -Section 1-");
        HeaderFooterSectionLinkPlanner.BuildEditingLabel(
                HeaderFooterSectionLinkPlanner.Describe(document.Sections, 1, HeaderFooterSlotKind.Header))
            .Should().Be("Default Header -Section 2- (Same as Previous)");

        var single = TextDocument.CreateEmpty();
        HeaderFooterSectionLinkPlanner.BuildEditingLabel(
                HeaderFooterSectionLinkPlanner.Describe(single.Sections, 0, HeaderFooterSlotKind.Footer))
            .Should().Be("Default Footer");
    }

    private static TextDocument ThreeSections()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("section one") { SectionBreak = new Section(new PageSettings()) });
        document.Blocks.Add(new Paragraph("section two") { SectionBreak = new Section(new PageSettings()) });
        document.Blocks.Add(new Paragraph("section three"));
        return document;
    }
}
