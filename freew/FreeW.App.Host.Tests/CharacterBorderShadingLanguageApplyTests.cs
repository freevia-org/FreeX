using System.Linq;
using System.Windows.Media;
using Free.Shared.Ribbon;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// STA coverage for the W20 character border / shading / language apply paths on
/// <see cref="DocumentView"/>: <see cref="DocumentView.SetCharacterBorder"/>,
/// <see cref="DocumentView.SetCharacterShading"/>, and <see cref="DocumentView.SetProofingLanguage"/>.
/// Each test verifies the model fields are applied via the undo bus and that the values survive a
/// LoadModel → CommitToModel round-trip (the CharacterFormatMarker tag carries them through).
/// </summary>
public sealed class CharacterBorderShadingLanguageApplyTests
{
    private static DocumentView ViewWith(params string[] texts)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var text in texts)
            doc.Blocks.Add(new Paragraph(text));
        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static void SelectAllParagraphs(DocumentView view)
    {
        var paragraphs = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();
        view.Selection.Select(paragraphs[0].ContentStart, paragraphs[^1].ContentEnd);
    }

    private static void SelectTextRange(
        DocumentView view,
        int startParagraphIndex,
        int startOffset,
        int endParagraphIndex,
        int endOffset)
    {
        var paragraphs = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();
        view.Selection.Select(
            PositionAtTextOffset(paragraphs[startParagraphIndex], startOffset),
            PositionAtTextOffset(paragraphs[endParagraphIndex], endOffset));
    }

    private static System.Windows.Documents.TextPointer PositionAtTextOffset(
        System.Windows.Documents.Paragraph paragraph,
        int offset)
    {
        var remaining = Math.Max(0, offset);
        var pointer = paragraph.ContentStart;
        while (pointer is not null && pointer.CompareTo(paragraph.ContentEnd) < 0)
        {
            if (pointer.GetPointerContext(System.Windows.Documents.LogicalDirection.Forward) == System.Windows.Documents.TextPointerContext.Text)
            {
                var text = pointer.GetTextInRun(System.Windows.Documents.LogicalDirection.Forward);
                if (remaining <= text.Length)
                    return pointer.GetPositionAtOffset(remaining, System.Windows.Documents.LogicalDirection.Forward) ?? pointer;

                remaining -= text.Length;
                pointer = pointer.GetPositionAtOffset(text.Length, System.Windows.Documents.LogicalDirection.Forward);
            }
            else
            {
                pointer = pointer.GetNextContextPosition(System.Windows.Documents.LogicalDirection.Forward);
            }
        }

        return paragraph.ContentEnd;
    }

    private static string DumpLanguageTags(Paragraph paragraph) =>
        string.Join("|", paragraph.Runs.Select(run => $"{run.Text}:{run.Formatting.LanguageTag}"));

    // ---- SetCharacterBorder ----

    [StaFact]
    public void SetCharacterBorder_AppliesFieldToAllRuns()
    {
        var view = ViewWith("hello world");
        SelectAllParagraphs(view);

        var border = new ParagraphBorder("#0070C0", 1.5)
        {
            LineStyle = BorderLineStyle.Single,
            Top = true, Left = true, Bottom = true, Right = true
        };
        view.SetCharacterBorder(border);

        foreach (var paragraph in view.Model.Blocks.OfType<Paragraph>())
        foreach (var run in paragraph.Runs)
        {
            run.Formatting.CharacterBorder.Should().NotBeNull();
            run.Formatting.CharacterBorder!.ColorHex.Should().Be("#0070C0");
            run.Formatting.CharacterBorder.LineStyle.Should().Be(BorderLineStyle.Single);
        }
    }

    [StaFact]
    public void SetCharacterBorder_Clear_RemovesBorder()
    {
        var view = ViewWith("hello");
        SelectAllParagraphs(view);

        view.SetCharacterBorder(new ParagraphBorder("#000000", 0.5));
        view.SetCharacterBorder(null);

        foreach (var paragraph in view.Model.Blocks.OfType<Paragraph>())
        foreach (var run in paragraph.Runs)
            run.Formatting.CharacterBorder.Should().BeNull();
    }

    [StaFact]
    public void SetCharacterBorder_SurvivesRenderAndCommit()
    {
        var view = ViewWith("test");
        SelectAllParagraphs(view);

        var border = new ParagraphBorder("#FF0000", 2.0)
        {
            LineStyle = BorderLineStyle.Dashed,
        };
        view.SetCharacterBorder(border);

        // Round-trip through LoadModel + CommitToModel: the CharacterFormatMarker tag must carry the
        // border back so CommitToModel recovers it and writes it back to the model run.
        view.LoadModel(view.Model);
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>().First().Runs.First();
        run.Formatting.CharacterBorder.Should().NotBeNull();
        run.Formatting.CharacterBorder!.ColorHex.Should().Be("#FF0000");
        run.Formatting.CharacterBorder.LineStyle.Should().Be(BorderLineStyle.Dashed);
    }

    [StaFact]
    public void CharacterBorderAndShading_RenderToFlowRunStructure()
    {
        var border = new ParagraphBorder("#0070C0", 1.5)
        {
            LineStyle = BorderLineStyle.Single,
        };
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("visible", RunFormatting.Default with
                {
                    CharacterBorder = border,
                    CharacterShadingHex = "#92D050",
                    CharacterShadingPattern = ShadingPattern.Pct25,
                }),
            },
        });
        var view = new DocumentView();

        view.LoadModel(doc);

        var wpfParagraph = view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .Single();
        var wpfRun = wpfParagraph.Inlines
            .OfType<System.Windows.Documents.Run>()
            .Single();
        var background = wpfRun.Background.Should().BeOfType<SolidColorBrush>().Subject.Color;
        background.R.Should().Be(0x92);
        background.G.Should().Be(0xD0);
        background.B.Should().Be(0x50);
        wpfRun.TextDecorations.Should().NotBeNull();
        wpfRun.TextDecorations!.Should()
            .Contain(decoration => decoration.Location == System.Windows.TextDecorationLocation.OverLine);
        wpfRun.TextDecorations!.Should()
            .Contain(decoration => decoration.Location == System.Windows.TextDecorationLocation.Underline);
    }

    [StaFact]
    public void CharacterBorderAndShading_FromParagraphStyle_RenderToFlowRunStructure()
    {
        var border = new ParagraphBorder("#C00000", 1.0)
        {
            LineStyle = BorderLineStyle.Single,
        };
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Styles["DecoratedRun"] = new DocumentStyle
        {
            Id = "DecoratedRun",
            Name = "Decorated Run",
            Run = RunFormatting.Default with
            {
                CharacterBorder = border,
                CharacterShadingHex = "#D9EAD3",
                CharacterShadingPattern = ShadingPattern.Pct10,
            },
        };
        doc.Blocks.Add(new Paragraph
        {
            StyleId = "DecoratedRun",
            Runs =
            {
                new Run("styled", RunFormatting.Default),
            },
        });
        var view = new DocumentView();

        view.LoadModel(doc);

        var wpfRun = view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .Single()
            .Inlines
            .OfType<System.Windows.Documents.Run>()
            .Single();
        var background = wpfRun.Background.Should().BeOfType<SolidColorBrush>().Subject.Color;
        background.R.Should().Be(0xD9);
        background.G.Should().Be(0xEA);
        background.B.Should().Be(0xD3);
        wpfRun.TextDecorations.Should().NotBeNull();
        wpfRun.TextDecorations!.Should()
            .Contain(decoration => decoration.Location == System.Windows.TextDecorationLocation.OverLine);
        wpfRun.TextDecorations!.Should()
            .Contain(decoration => decoration.Location == System.Windows.TextDecorationLocation.Underline);
    }

    // ---- SetCharacterShading ----

    [StaFact]
    public void SetCharacterShading_AppliesFieldToAllRuns()
    {
        var view = ViewWith("shaded text");
        SelectAllParagraphs(view);

        view.SetCharacterShading("#FFFF00", ShadingPattern.Clear);

        foreach (var paragraph in view.Model.Blocks.OfType<Paragraph>())
        foreach (var run in paragraph.Runs)
            run.Formatting.CharacterShadingHex.Should().Be("#FFFF00");
    }

    [StaFact]
    public void SetCharacterShading_Clear_RemovesShading()
    {
        var view = ViewWith("text");
        SelectAllParagraphs(view);

        view.SetCharacterShading("#FFC000");
        view.SetCharacterShading(null);

        foreach (var paragraph in view.Model.Blocks.OfType<Paragraph>())
        foreach (var run in paragraph.Runs)
            run.Formatting.CharacterShadingHex.Should().BeNull();
    }

    [StaFact]
    public void SetCharacterShading_SurvivesRenderAndCommit()
    {
        var view = ViewWith("shading round-trip");
        SelectAllParagraphs(view);

        view.SetCharacterShading("#92D050", ShadingPattern.Pct10);

        view.LoadModel(view.Model);
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>().First().Runs.First();
        run.Formatting.CharacterShadingHex.Should().Be("#92D050");
        run.Formatting.CharacterShadingPattern.Should().Be(ShadingPattern.Pct10);
    }

    // ---- SetProofingLanguage ----

    [StaFact]
    public void SetProofingLanguage_AppliesTagToAllRuns()
    {
        var view = ViewWith("bonjour");
        SelectAllParagraphs(view);

        view.SetProofingLanguage("fr-FR");

        foreach (var paragraph in view.Model.Blocks.OfType<Paragraph>())
        foreach (var run in paragraph.Runs)
            run.Formatting.LanguageTag.Should().Be("fr-FR");
    }

    [StaFact]
    public void SetProofingLanguage_Null_ClearsTag()
    {
        var view = ViewWith("text");
        SelectAllParagraphs(view);

        view.SetProofingLanguage("en-US");
        SelectAllParagraphs(view);
        view.SetProofingLanguage(null);

        foreach (var paragraph in view.Model.Blocks.OfType<Paragraph>())
        foreach (var run in paragraph.Runs)
            run.Formatting.LanguageTag.Should().BeNull();
    }

    [StaFact]
    public void SetProofingLanguage_AppliesTagOnlyToSelectedSubrange()
    {
        var view = ViewWith("hello world");
        SelectTextRange(view, 0, 3, 0, 8);

        view.SetProofingLanguage(" fr-FR ");

        var paragraph = view.Model.Blocks.OfType<Paragraph>().Single();
        DumpLanguageTags(paragraph).Should().Be("hel:|lo wo:fr-FR|rld:");
    }

    [StaFact]
    public void SetProofingLanguage_AppliesTagOnlyToSelectedMultiParagraphRanges()
    {
        var view = ViewWith("alpha bravo", "charlie delta", "echo foxtrot");
        SelectTextRange(view, 0, 6, 2, 4);

        view.SetProofingLanguage("de-DE");

        var paragraphs = view.Model.Blocks.OfType<Paragraph>().ToList();
        DumpLanguageTags(paragraphs[0]).Should().Be("alpha :|bravo:de-DE");
        DumpLanguageTags(paragraphs[1]).Should().Be("charlie delta:de-DE");
        DumpLanguageTags(paragraphs[2]).Should().Be("echo:de-DE| foxtrot:");
    }

    [StaFact]
    public void SetProofingLanguage_Blank_ClearsOnlySelectedSubrange()
    {
        var view = ViewWith("clear range");
        SelectAllParagraphs(view);
        view.SetProofingLanguage("en-US");
        SelectTextRange(view, 0, 6, 0, 11);

        view.SetProofingLanguage(" ");

        var paragraph = view.Model.Blocks.OfType<Paragraph>().Single();
        DumpLanguageTags(paragraph).Should().Be("clear :en-US|range:");
    }

    [StaFact]
    public void SetProofingLanguage_CollapsedCaret_DoesNotRetagExistingText()
    {
        var view = ViewWith("caret text");
        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = PositionAtTextOffset(paragraph, 2);

        view.SetProofingLanguage("fr-FR");

        var modelParagraph = view.Model.Blocks.OfType<Paragraph>().Single();
        DumpLanguageTags(modelParagraph).Should().Be("caret text:");
        view.Commands.CanUndo.Should().BeFalse();
    }

    [StaFact]
    public void SetProofingLanguage_SurvivesRenderAndCommit()
    {
        var view = ViewWith("language round-trip");
        SelectAllParagraphs(view);

        view.SetProofingLanguage("de-DE");

        view.LoadModel(view.Model);
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>().First().Runs.First();
        run.Formatting.LanguageTag.Should().Be("de-DE");
    }

    [StaFact]
    public void SetProofingLanguage_MultiParagraphSelection_IsReversibleWithSingleUndo()
    {
        var view = ViewWith("first line", "second line");
        SelectTextRange(view, 0, 6, 1, 6);

        view.SetProofingLanguage("it-IT");
        view.Commands.Undo().Should().BeTrue();

        foreach (var paragraph in view.Model.Blocks.OfType<Paragraph>())
        foreach (var run in paragraph.Runs)
            run.Formatting.LanguageTag.Should().BeNull();
    }

    // ---- Command parity: the three new commands must be backed ----

    [StaFact]
    public void CharBorder_CharShading_SetProofingLanguage_CommandsAreBacked()
    {
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        registry.TryGet("freew.char-border", out _).Should().BeTrue("freew.char-border must be backed");
        registry.TryGet("freew.char-shading", out _).Should().BeTrue("freew.char-shading must be backed");
        registry.TryGet("freew.set-proofing-language", out _).Should().BeTrue("freew.set-proofing-language must be backed");
    }
}
