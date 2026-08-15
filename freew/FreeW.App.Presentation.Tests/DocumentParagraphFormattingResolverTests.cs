using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentParagraphFormattingResolverTests
{
    [Fact]
    public void Resolve_combines_complete_style_chain_direct_formatting_and_tab_operations()
    {
        var document = TextDocument.CreateEmpty();
        document.DefaultParagraph = new ParagraphFormatting
        {
            ContextualSpacing = true,
            LineRule = LineSpacingRule.Exact,
            LineHeightPt = 20,
            LineSpacingIsSet = true,
        };
        var border = new ParagraphBorder("#224466", 1.25) { LineStyle = BorderLineStyle.Double };
        document.Styles["Base"] = new DocumentStyle
        {
            Id = "Base",
            Name = "Base",
            Paragraph = new ParagraphFormatting
            {
                IndentLeftPt = 18,
                Border = border,
                ShadingColorHex = "#D9EAF7",
                ShadingPattern = ShadingPattern.Pct25,
                LineRule = LineSpacingRule.AtLeast,
                LineHeightPt = 16,
                LineSpacingIsSet = true,
                TabStops = [new TabStop(36)],
            },
        };
        document.Styles["Derived"] = new DocumentStyle
        {
            Id = "Derived",
            Name = "Derived",
            BasedOnStyleId = "Base",
            Paragraph = new ParagraphFormatting
            {
                Alignment = TextAlignment.Center,
                SpaceAfterPt = 4,
                SpaceAfterIsSet = true,
                TabStops = [new TabStop(36, IsClear: true), new TabStop(72)],
            },
        };
        var paragraph = new Paragraph
        {
            StyleId = "Derived",
            Formatting = new ParagraphFormatting
            {
                ListKind = ListKind.Bullet,
                SpaceBeforePt = 2,
                SpaceBeforeIsSet = true,
                TabStops = [new TabStop(72, IsClear: true), new TabStop(108)],
            },
        };

        var resolved = DocumentParagraphFormattingResolver.Resolve(document, paragraph);

        resolved.Alignment.Should().Be(TextAlignment.Center);
        resolved.IndentLeftPt.Should().Be(18);
        resolved.SpaceBeforePt.Should().Be(2);
        resolved.SpaceAfterPt.Should().Be(4);
        resolved.LineRule.Should().Be(LineSpacingRule.AtLeast);
        resolved.LineHeightPt.Should().Be(16);
        resolved.ContextualSpacing.Should().BeTrue();
        resolved.Border.Should().Be(border);
        resolved.ShadingColorHex.Should().Be("#D9EAF7");
        resolved.ShadingPattern.Should().Be(ShadingPattern.Pct25);
        resolved.ListKind.Should().Be(ListKind.Bullet, "list membership remains paragraph-intrinsic");
        resolved.TabStops.Should().Equal(new TabStop(108));
    }

    [Fact]
    public void Resolve_uses_document_line_defaults_without_a_style()
    {
        var document = TextDocument.CreateEmpty();
        document.DefaultParagraph = new ParagraphFormatting
        {
            LineRule = LineSpacingRule.Exact,
            LineHeightPt = 14,
            LineSpacingIsSet = true,
            ContextualSpacing = false,
        };

        var resolved = DocumentParagraphFormattingResolver.Resolve(document, new Paragraph());

        resolved.LineRule.Should().Be(LineSpacingRule.Exact);
        resolved.LineHeightPt.Should().Be(14);
        resolved.LineSpacingIsSet.Should().BeTrue();
        resolved.ContextualSpacing.Should().BeFalse();
    }

    [Fact]
    public void Resolve_preserves_style_intrinsics_when_paragraph_has_no_direct_formatting()
    {
        var document = TextDocument.CreateEmpty();
        document.Styles["ListBase"] = new DocumentStyle
        {
            Id = "ListBase",
            Name = "List Base",
            Paragraph = new ParagraphFormatting
            {
                ListKind = ListKind.Number,
                ListLevel = 2,
                KeepWithNext = true,
            },
        };
        document.Styles["ListDerived"] = new DocumentStyle
        {
            Id = "ListDerived",
            Name = "List Derived",
            BasedOnStyleId = "ListBase",
            Paragraph = ParagraphFormatting.Default,
        };

        var resolved = DocumentParagraphFormattingResolver.Resolve(
            document,
            new Paragraph { StyleId = "ListDerived" });

        resolved.ListKind.Should().Be(ListKind.Number);
        resolved.ListLevel.Should().Be(2);
        resolved.KeepWithNext.Should().BeTrue();
    }

    [Fact]
    public void Probe_resolves_run_and_paragraph_formatting_at_model_offset()
    {
        var document = TextDocument.CreateEmpty();
        document.Styles["Styled"] = new DocumentStyle
        {
            Id = "Styled",
            Name = "Styled",
            Run = new RunFormatting { Bold = true },
            Paragraph = new ParagraphFormatting { IndentRightPt = 12 },
        };
        var paragraph = new Paragraph { StyleId = "Styled" };
        paragraph.Runs.Add(new Run("one", new RunFormatting { Underline = true }));
        paragraph.Runs.Add(new Run("two", new RunFormatting { Italic = true }));

        var resolved = DocumentFormattingProbePlanner.Resolve(document, paragraph, offset: 4);

        resolved.Run.Bold.Should().BeTrue();
        resolved.Run.Italic.Should().BeTrue();
        resolved.Run.Underline.Should().BeFalse();
        resolved.Paragraph.IndentRightPt.Should().Be(12);
    }

    [Fact]
    public void Both_renderers_consume_shared_paragraph_resolution_and_Avalonia_probes_all_text_stories()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = Read(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = Read(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        foreach (var renderer in new[] { wpf, avalonia })
        {
            renderer.Should().Contain("DocumentParagraphFormattingResolver.Resolve(")
                .And.NotContain("private static ParagraphFormatting OverlayParagraph(")
                .And.NotContain("private static IReadOnlyList<TabStop> MergeTabStops(");
        }

        avalonia.Should().Contain("DocumentFormattingProbePlanner.Resolve(")
            .And.Contain("PrimaryFormattingProbeTarget()")
            .And.Contain("NormalizedHfSelection()")
            .And.Contain("if (_cellCaret is { } cellCaret)")
            .And.Contain("if (SelectedCellRange is { } cellRange")
            .And.Contain("NormalizedSelection()?.Start ?? _caret")
            .And.Contain("SelectedParagraphIndices().FirstOrDefault(-1)");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts])).ReplaceLineEndings("\n");
}
