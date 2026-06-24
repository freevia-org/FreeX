using System.Linq;
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
        view.SetProofingLanguage(null);

        foreach (var paragraph in view.Model.Blocks.OfType<Paragraph>())
        foreach (var run in paragraph.Runs)
            run.Formatting.LanguageTag.Should().BeNull();
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
