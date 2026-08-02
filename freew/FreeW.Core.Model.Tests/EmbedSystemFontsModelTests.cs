namespace FreeW.Core.Model.Tests;

public sealed class EmbedSystemFontsModelTests
{
    [Fact]
    public void SettingDefaultsOffAndCloneStyleOperationsRetainIt()
    {
        var template = new TextDocument { EmbedSystemFonts = true };
        template.Blocks.Add(new Paragraph("Font report"));
        var revised = new TextDocument { EmbedSystemFonts = true };
        revised.Blocks.Add(new Paragraph("Revised font report"));
        var alternate = new TextDocument { EmbedSystemFonts = true };
        alternate.Blocks.Add(new Paragraph("Alternate font report"));

        new TextDocument().EmbedSystemFonts.Should().BeFalse();
        MailMerge.MergeRecord(template, new Dictionary<string, string>()).EmbedSystemFonts.Should().BeTrue();
        DocumentCompare.Compare(template, revised, "Reviewer", "2026-08-02T00:00:00Z")
            .EmbedSystemFonts.Should().BeTrue();
        DocumentCombine.Combine(
                template, revised, "Reviewer A", alternate, "Reviewer B", "2026-08-02T00:00:00Z")
            .EmbedSystemFonts.Should().BeTrue();
    }
}
