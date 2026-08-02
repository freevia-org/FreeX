namespace FreeW.Core.Model.Tests;

public sealed class DoNotAutoCompressPicturesModelTests
{
    private sealed class CommandContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    [Fact]
    public void SettingDefaultsOffAndCanBeEnabled()
    {
        var document = new TextDocument();

        document.DoNotAutoCompressPictures.Should().BeFalse();

        document.DoNotAutoCompressPictures = true;

        document.DoNotAutoCompressPictures.Should().BeTrue();
    }

    [Fact]
    public void CloneStyleDocumentOperationsRetainImageCompressionPolicy()
    {
        var template = new TextDocument { DoNotAutoCompressPictures = true };
        template.Blocks.Add(new Paragraph("Image report"));
        var revised = new TextDocument { DoNotAutoCompressPictures = true };
        revised.Blocks.Add(new Paragraph("Revised image report"));

        var merged = MailMerge.MergeRecord(template, new Dictionary<string, string>());
        var compared = DocumentCompare.Compare(template, revised, "Reviewer", "2026-08-02T00:00:00Z");

        merged.DoNotAutoCompressPictures.Should().BeTrue();
        compared.DoNotAutoCompressPictures.Should().BeTrue();
    }

    [Fact]
    public void BodyCommandApplyAndRevertRetainImageCompressionPolicy()
    {
        var document = new TextDocument { DoNotAutoCompressPictures = true };
        document.Blocks.Add(new Paragraph("Existing"));
        var command = new InsertParagraphCommand(1, new Paragraph("Inserted"));
        var context = new CommandContext(document);

        command.Apply(context);
        document.DoNotAutoCompressPictures.Should().BeTrue();

        command.Revert(context);
        document.DoNotAutoCompressPictures.Should().BeTrue();
        document.Blocks.Should().ContainSingle();
    }
}
