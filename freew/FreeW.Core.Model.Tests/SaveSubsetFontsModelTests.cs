namespace FreeW.Core.Model.Tests;

public sealed class SaveSubsetFontsModelTests
{
    private sealed class CommandContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    [Fact]
    public void SettingDefaultsOffAndCanBeEnabled()
    {
        var document = new TextDocument();

        document.SaveSubsetFonts.Should().BeFalse();

        document.SaveSubsetFonts = true;

        document.SaveSubsetFonts.Should().BeTrue();
    }

    [Fact]
    public void CloneStyleDocumentOperationsRetainFontSubsettingPolicy()
    {
        var template = new TextDocument { SaveSubsetFonts = true };
        template.Blocks.Add(new Paragraph("Font report"));
        var revised = new TextDocument { SaveSubsetFonts = true };
        revised.Blocks.Add(new Paragraph("Revised font report"));
        var alternate = new TextDocument { SaveSubsetFonts = true };
        alternate.Blocks.Add(new Paragraph("Alternate font report"));

        var merged = MailMerge.MergeRecord(template, new Dictionary<string, string>());
        var compared = DocumentCompare.Compare(template, revised, "Reviewer", "2026-08-02T00:00:00Z");
        var combined = DocumentCombine.Combine(
            template, revised, "Reviewer A", alternate, "Reviewer B", "2026-08-02T00:00:00Z");

        merged.SaveSubsetFonts.Should().BeTrue();
        compared.SaveSubsetFonts.Should().BeTrue();
        combined.SaveSubsetFonts.Should().BeTrue();
    }

    [Fact]
    public void BodyCommandApplyAndRevertRetainFontSubsettingPolicy()
    {
        var document = new TextDocument { SaveSubsetFonts = true };
        document.Blocks.Add(new Paragraph("Existing"));
        var command = new InsertParagraphCommand(1, new Paragraph("Inserted"));
        var context = new CommandContext(document);

        command.Apply(context);
        document.SaveSubsetFonts.Should().BeTrue();

        command.Revert(context);
        document.SaveSubsetFonts.Should().BeTrue();
        document.Blocks.Should().ContainSingle();
    }
}
