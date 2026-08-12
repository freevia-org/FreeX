using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests.DocumentView;

public sealed class RichClipboardDocumentPlannerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryReadRtf_rejects_missing_clipboard_text(string? rtf)
    {
        RichClipboardDocumentPlanner.TryReadRtf(rtf, out var document).Should().BeFalse();
        document.Should().BeNull();
    }

    [Fact]
    public void TryReadRtf_preserves_runs_paragraphs_and_source_code_page_bytes()
    {
        const string rtf = @"{\rtf1\ansi\ansicpg1252\b Bold\b0  caf\'e9\par\i Second\i0}";

        RichClipboardDocumentPlanner.TryReadRtf(rtf, out var document).Should().BeTrue();

        var paragraphs = document!.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().HaveCount(2);
        paragraphs[0].PlainText.Should().Be("Bold café");
        paragraphs[0].Runs.Should().Contain(run => run.Text == "Bold" && run.Formatting.Bold);
        paragraphs[1].PlainText.Should().Be("Second");
        paragraphs[1].Runs.Should().Contain(run => run.Text == "Second" && run.Formatting.Italic);
    }

    [Fact]
    public void Renderer_adapters_do_not_own_RtfReader_policy()
    {
        var workspace = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var sources = new[]
        {
            Path.Combine(workspace, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs"),
            Path.Combine(workspace, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"),
        }.Select(File.ReadAllText).ToArray();

        sources.Should().AllSatisfy(source =>
        {
            source.Should().NotContain("RtfReader.Read(");
            source.Should().NotContain("TryReadRtfClipboardDocument");
        });

        sources[0].Should().Contain("RichClipboardDocumentPlanner.TryReadRtf(");
        File.ReadAllText(Path.Combine(workspace, "freew", "FreeW.App.Avalonia", "MainWindow.cs"))
            .Should().Contain("RichClipboardDocumentPlanner.TryReadRtf(");
    }
}
