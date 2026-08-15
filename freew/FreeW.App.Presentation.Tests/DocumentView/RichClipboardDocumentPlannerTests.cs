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
    public void Renderer_adapters_route_RtfReader_policy_through_the_shared_clipboard_workflow()
    {
        var workspace = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var rendererSources = new[]
        {
            Path.Combine(workspace, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs"),
            Path.Combine(workspace, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"),
            Path.Combine(workspace, "freew", "FreeW.App.Avalonia", "MainWindow.cs"),
        }.Select(File.ReadAllText).ToArray();
        var wpfDialog = File.ReadAllText(Path.Combine(
            workspace,
            "freew",
            "FreeW.App.Host",
            "PasteSpecialDialog.cs"));
        var wpfCommands = File.ReadAllText(Path.Combine(
            workspace,
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs"));
        var workflow = File.ReadAllText(Path.Combine(
            workspace,
            "freew",
            "FreeW.App.Presentation",
            "Editing",
            "FreeWClipboardApplicationWorkflow.cs"));
        var parser = File.ReadAllText(Path.Combine(
            workspace,
            "freew",
            "FreeW.Core.IO",
            "RtfClipboardDocumentParser.cs"));

        rendererSources.Should().AllSatisfy(source =>
        {
            source.Should().NotContain("RtfReader.Read(");
            source.Should().NotContain("RtfClipboardDocumentParser.TryParse(");
            source.Should().NotContain("TryReadRtfClipboardDocument");
        });

        rendererSources[0].Should().Contain("FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(");
        rendererSources[2].Should().Contain("FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(");
        wpfDialog.Should().NotContain("ReadTextAsync(");
        wpfDialog.Should().NotContain("IPlatformClipboard");
        wpfCommands.Should().Contain("ReadPasteSpecialAsync(editor.PlatformClipboard)");
        wpfCommands.Should().Contain("FreeWClipboardApplicationWorkflow.PlanPaste(");
        wpfCommands.Should().Contain("editor.ApplyClipboardPastePlan(plan)");
        workflow.Should().Contain("RtfClipboardDocumentParser.TryParse(");
        workflow.Should().NotContain("RtfReader.Read(");
        parser.Should().Contain("RtfReader.Read(");
    }
}
