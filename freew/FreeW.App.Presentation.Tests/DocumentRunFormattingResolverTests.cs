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
                NoProof = true,
                CharacterSpacingPt = 0.5,
                CharacterBorder = new ParagraphBorder("#778899", 1),
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
                HighlightColorHex = "#FFFF00",
                Ligatures = LigatureMode.Contextual,
            }
        };
        var paragraph = new Paragraph { StyleId = "Derived" };

        var resolved = DocumentRunFormattingResolver.Resolve(
            document,
            paragraph,
            new RunFormatting
            {
                Underline = true,
                FontFamily = "Aptos",
                NumberSpacing = NumberSpacing.Tabular,
            });

        resolved.FontFamily.Should().Be("Aptos");
        resolved.FontSizePt.Should().Be(14);
        resolved.LanguageTag.Should().Be("en-US");
        resolved.ColorHex.Should().Be("#112233");
        resolved.HighlightColorHex.Should().Be("#FFFF00");
        resolved.Bold.Should().BeTrue();
        resolved.Italic.Should().BeTrue();
        resolved.Underline.Should().BeTrue();
        resolved.NoProof.Should().BeTrue();
        resolved.CharacterSpacingPt.Should().Be(0.5);
        resolved.CharacterBorder.Should().Be(new ParagraphBorder("#778899", 1));
        resolved.Ligatures.Should().Be(LigatureMode.Contextual);
        resolved.NumberSpacing.Should().Be(NumberSpacing.Tabular);
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
    public void Resolve_layers_the_runs_own_linked_character_style_between_the_paragraph_chain_and_direct_formatting()
    {
        // R142 fix: a run styled purely via w:rPr/w:rStyle (no baked-in direct formatting) must still
        // pick up the linked character style's look -- Word's paragraph-style -> character-style ->
        // direct-formatting cascade, mirroring how the paragraph chain already resolves.
        var document = TextDocument.CreateEmpty();
        document.DefaultRun = new RunFormatting { FontFamily = "Calibri", FontSizePt = 11 };
        document.Styles["Heading1"] = new DocumentStyle
        {
            Id = "Heading1",
            Name = "Heading 1",
            Run = new RunFormatting { Bold = true, FontSizePt = 16 },
        };
        document.Styles["IntenseEmphasis"] = new DocumentStyle
        {
            Id = "IntenseEmphasis",
            Name = "Intense Emphasis",
            Run = new RunFormatting { Italic = true, ColorHex = "#4472C4" },
        };
        var paragraph = new Paragraph { StyleId = "Heading1" };
        var run = new Run("styled via character style only") { StyleId = "IntenseEmphasis" };
        paragraph.Runs.Add(run);

        var resolved = DocumentRunFormattingResolver.Resolve(document, paragraph, run.Formatting, run.StyleId);

        resolved.FontFamily.Should().Be("Calibri", "the document default still applies");
        resolved.FontSizePt.Should().Be(16, "the paragraph style chain still applies");
        resolved.Bold.Should().BeTrue("the paragraph style chain still applies");
        resolved.Italic.Should().BeTrue("the run's linked character style must now be resolved");
        resolved.ColorHex.Should().Be("#4472C4", "the run's linked character style must now be resolved");
    }

    [Fact]
    public void Resolve_lets_the_runs_direct_formatting_win_over_its_linked_character_style()
    {
        var document = TextDocument.CreateEmpty();
        document.Styles["IntenseEmphasis"] = new DocumentStyle
        {
            Id = "IntenseEmphasis",
            Name = "Intense Emphasis",
            Run = new RunFormatting { ColorHex = "#4472C4", FontSizePt = 11 },
        };
        var paragraph = new Paragraph();

        var resolved = DocumentRunFormattingResolver.Resolve(
            document,
            paragraph,
            new RunFormatting { ColorHex = "#FF0000", FontSizePt = 20 },
            runStyleId: "IntenseEmphasis");

        resolved.ColorHex.Should().Be("#FF0000", "direct formatting wins over the linked character style");
        resolved.FontSizePt.Should().Be(20, "direct formatting wins over the linked character style");
    }

    [Fact]
    public void Resolve_Run_overload_reads_StyleId_from_the_run_itself()
    {
        var document = TextDocument.CreateEmpty();
        document.Styles["Emphasis"] = new DocumentStyle
        {
            Id = "Emphasis",
            Name = "Emphasis",
            Run = new RunFormatting { Italic = true },
        };
        var paragraph = new Paragraph();
        var run = new Run("text") { StyleId = "Emphasis" };
        paragraph.Runs.Add(run);

        var resolved = DocumentRunFormattingResolver.Resolve(document, paragraph, run);

        resolved.Italic.Should().BeTrue();
    }

    [Fact]
    public void Resolve_leaves_a_run_with_no_StyleId_unaffected_by_unrelated_character_styles()
    {
        // Sibling/no-regression check: a run that carries no character style link must resolve exactly
        // as it did before this fix -- direct formatting over the paragraph chain only.
        var document = TextDocument.CreateEmpty();
        document.DefaultRun = new RunFormatting { FontFamily = "Calibri" };
        document.Styles["IntenseEmphasis"] = new DocumentStyle
        {
            Id = "IntenseEmphasis",
            Name = "Intense Emphasis",
            Run = new RunFormatting { Italic = true, ColorHex = "#4472C4" },
        };
        var paragraph = new Paragraph();
        var run = new Run("plain text");

        var resolved = DocumentRunFormattingResolver.Resolve(document, paragraph, run);

        resolved.Italic.Should().BeFalse();
        resolved.ColorHex.Should().BeNull();
        resolved.FontFamily.Should().Be("Calibri");
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
    public void Accessibility_formatter_exposes_advanced_effective_character_formatting()
    {
        var formatting = new RunFormatting
        {
            ColorHex = "#4472C4",
            ThemeColor = new WordThemeColor("accent1", "4472C4", TintHex: "33", ShadeHex: "80"),
            HighlightColorHex = "#FFFF00",
            CharacterShadingHex = "#D9EAF7",
            CharacterShadingPattern = ShadingPattern.Pct25,
            CharacterBorder = new ParagraphBorder("#112233", 1.5)
            {
                LineStyle = BorderLineStyle.Double,
                Top = false,
                Left = false,
                Bottom = true,
                Right = false,
            },
            CharacterSpacingPt = -0.75,
            KerningMinSizePt = 9,
            PositionPt = 2.5,
            Ligatures = LigatureMode.StandardContextual,
            NumberForm = NumberForm.OldStyle,
            NumberSpacing = NumberSpacing.Tabular,
            StylisticSet = 7,
        };

        var description = DocumentRunAccessibilityFormatter.Describe(formatting, new Run("Advanced"));

        description.Should().Be(
            "raised 2.5 point, text color #4472C4, theme color accent1 tint 33 shade 80, " +
            "character shading #D9EAF7, 25 percent pattern, character border double, 1.5 point, #112233, bottom edge, " +
            "condensed by 0.75 point, kerning at 9 point and above, standard and contextual ligatures, " +
            "old-style numerals, tabular numeral spacing, stylistic set 7");
        description.Should().NotContain("highlight #FFFF00",
            "character shading is the effective background when both authored values are present");
    }

    [Fact]
    public void Accessibility_formatter_distinguishes_disabled_and_expanded_typography_controls()
    {
        var formatting = new RunFormatting
        {
            CharacterSpacingPt = 1.25,
            KerningMinSizePt = 0,
            PositionPt = -1,
            Ligatures = LigatureMode.NoneExplicit,
            NumberForm = NumberForm.Lining,
            NumberSpacing = NumberSpacing.Proportional,
        };

        DocumentRunAccessibilityFormatter.Describe(formatting, new Run("Typography"))
            .Should().Be(
                "lowered 1 point, expanded by 1.25 point, kerning disabled, ligatures disabled, " +
                "lining numerals, proportional numeral spacing");
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
