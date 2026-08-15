using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentRunFormattingResolverTests
{
    [Fact]
    public void Resolve_cascades_document_and_complete_based_on_style_chain_before_direct_formatting()
    {
        var document = TextDocument.CreateEmpty();
        document.DefaultRun = new RunFormatting
        {
            FontFamily = "Calibri",
            FontSizePt = 11,
            LanguageTag = "en-US"
        };
        document.Styles["Base"] = new DocumentStyle
        {
            Id = "Base",
            Name = "Base",
            Run = new RunFormatting
            {
                Bold = true,
                FontSizePt = 14,
                ColorHex = "#112233",
                NoProof = true
            }
        };
        document.Styles["Derived"] = new DocumentStyle
        {
            Id = "Derived",
            Name = "Derived",
            BasedOnStyleId = "Base",
            Run = new RunFormatting
            {
                Italic = true,
                HighlightColorHex = "#FFFF00"
            }
        };
        var paragraph = new Paragraph { StyleId = "Derived" };

        var resolved = DocumentRunFormattingResolver.Resolve(
            document,
            paragraph,
            new RunFormatting { Underline = true, FontFamily = "Aptos" });

        resolved.FontFamily.Should().Be("Aptos");
        resolved.FontSizePt.Should().Be(14);
        resolved.LanguageTag.Should().Be("en-US");
        resolved.ColorHex.Should().Be("#112233");
        resolved.HighlightColorHex.Should().Be("#FFFF00");
        resolved.Bold.Should().BeTrue();
        resolved.Italic.Should().BeTrue();
        resolved.Underline.Should().BeTrue();
        resolved.NoProof.Should().BeTrue();
    }

    [Fact]
    public void Resolve_terminates_a_cyclic_style_chain_deterministically()
    {
        var document = TextDocument.CreateEmpty();
        document.Styles["A"] = new DocumentStyle
        {
            Id = "A",
            Name = "A",
            BasedOnStyleId = "B",
            Run = new RunFormatting { Bold = true }
        };
        document.Styles["B"] = new DocumentStyle
        {
            Id = "B",
            Name = "B",
            BasedOnStyleId = "A",
            Run = new RunFormatting { Italic = true }
        };

        var resolved = DocumentRunFormattingResolver.Resolve(
            document,
            new Paragraph { StyleId = "A" },
            RunFormatting.Default);

        resolved.Bold.Should().BeTrue();
        resolved.Italic.Should().BeTrue();
    }

    [Fact]
    public void Accessibility_tree_exposes_effective_run_formatting_and_stable_text_ranges()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.DefaultRun = new RunFormatting { FontFamily = "Calibri", FontSizePt = 11 };
        document.Styles["Emphasis"] = new DocumentStyle
        {
            Id = "Emphasis",
            Name = "Emphasis",
            Run = new RunFormatting { Italic = true, ColorHex = "#4472C4" }
        };
        var paragraph = new Paragraph { StyleId = "Emphasis" };
        paragraph.Runs.Add(new Run("Formatted", new RunFormatting
        {
            Bold = true,
            Underline = true,
            LanguageTag = "fr-FR"
        }) { Revision = RevisionKind.Inserted });
        document.Blocks.Add(paragraph);

        var node = DocumentAccessibilityNodePlanner.Build(document)
            .Children.Single().SemanticChildren.Single();

        node.Kind.Should().Be(DocumentAccessibilityNodeKind.TextRun);
        node.Id.Should().Be("block:0:paragraph:run:0:text");
        node.Value.Should().Be("Formatted");
        node.TextStart.Should().Be(0);
        node.TextLength.Should().Be(9);
        node.HelpText.Should().Be(
            "Calibri, 11 point, bold, italic, underlined, text color #4472C4, language fr-FR, tracked insertion");
    }

    [Fact]
    public void Both_renderers_and_the_Avalonia_peer_consume_the_shared_run_contract()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = Read(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = Read(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var peer = Read(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentViewAutomationPeer.cs");
        var semantics = Read(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.AutomationSemantics.cs");

        foreach (var renderer in new[] { wpf, avalonia })
        {
            renderer.Should().Contain("DocumentRunFormattingResolver.Resolve(")
                .And.NotContain("private static RunFormatting OverlayRun(")
                .And.NotContain("private static RunFormatting StyleRun(");
        }
        peer.Should().Contain("DocumentAccessibilityNodeKind.TextRun => AutomationControlType.Text")
            .And.Contain("DocumentAccessibilityNodeKind.TextRun =>\n                new DocumentValueAutomationPeer");
        semantics.Should().Contain("case DocumentAccessibilityNodeKind.TextRun:")
            .And.Contain("IsAutomationTextRangeNode(")
            .And.Contain("AutomationTextLayoutRange(")
            .And.Contain("AutomationCaretWithinTextRange(");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts])).ReplaceLineEndings("\n");
}
