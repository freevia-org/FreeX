using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class ProofingLanguageMutationCoordinatorTests
{
    [Fact]
    public void Apply_formats_mixed_content_without_discarding_field_or_link_metadata()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Alpha "));
        paragraph.Runs.Add(new Run(string.Empty) { FieldKind = RunFieldKind.PageNumber });
        paragraph.Runs.Add(new Run("beta")
        {
            HyperlinkUrl = "https://example.com",
            CommentId = 9,
        });
        var field = paragraph.Runs[1];
        var document = DocumentWith(paragraph);
        var bus = new DocumentCommandBus(new Context(document));

        var applied = ProofingLanguageMutationCoordinator.Apply(
            document,
            bus,
            new ProofingLanguageApplyPlan(
                "fr-FR",
                [new ProofingLanguageTextRange(0, 6, 10)]));

        applied.Should().Be(1);
        paragraph.PlainText.Should().Be("Alpha beta");
        paragraph.Runs.Single(run => run.FieldKind == RunFieldKind.PageNumber)
            .FieldKind.Should().Be(field.FieldKind);
        var formatted = paragraph.Runs.Single(run => run.Text == "beta");
        formatted.Formatting.LanguageTag.Should().Be("fr-FR");
        formatted.HyperlinkUrl.Should().Be("https://example.com");
        formatted.CommentId.Should().Be(9);
        bus.Undo().Should().BeTrue();
        paragraph.Runs.Should().HaveCount(3);
        paragraph.Runs[2].Formatting.LanguageTag.Should().BeNull();
    }

    [Fact]
    public void Apply_filters_invalid_ranges_and_groups_multiple_paragraphs_into_one_undo()
    {
        var first = new Paragraph("First");
        var second = new Paragraph("Second");
        var document = DocumentWith(first, new Table(), second);
        var bus = new DocumentCommandBus(new Context(document));
        var plan = new ProofingLanguageApplyPlan(
            "de-DE",
            [
                new ProofingLanguageTextRange(-1, 0, 1),
                new ProofingLanguageTextRange(0, 0, int.MaxValue),
                new ProofingLanguageTextRange(1, 0, 1),
                new ProofingLanguageTextRange(2, 0, int.MaxValue),
                new ProofingLanguageTextRange(99, 0, 1),
            ]);

        var applied = ProofingLanguageMutationCoordinator.Apply(document, bus, plan);

        applied.Should().Be(2);
        first.Runs.Should().OnlyContain(run => run.Formatting.LanguageTag == "de-DE");
        second.Runs.Should().OnlyContain(run => run.Formatting.LanguageTag == "de-DE");
        bus.Undo().Should().BeTrue();
        first.Runs.Should().OnlyContain(run => run.Formatting.LanguageTag == null);
        second.Runs.Should().OnlyContain(run => run.Formatting.LanguageTag == null);
        bus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Apply_with_no_text_range_does_not_create_an_undo_entry()
    {
        var document = DocumentWith(new Paragraph("Text"));
        var bus = new DocumentCommandBus(new Context(document));

        var applied = ProofingLanguageMutationCoordinator.Apply(
            document,
            bus,
            new ProofingLanguageApplyPlan(
                "en-US",
                [new ProofingLanguageTextRange(0, 2, 2)]));

        applied.Should().Be(0);
        bus.CanUndo.Should().BeFalse();
    }

    private static TextDocument DocumentWith(params Block[] blocks)
    {
        var document = new TextDocument();
        document.Blocks.AddRange(blocks);
        return document;
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
