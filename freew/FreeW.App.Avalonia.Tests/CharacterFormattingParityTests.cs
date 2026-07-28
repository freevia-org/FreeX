using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// WPF-authority coverage for the character border and shading commands. WPF applies these
/// model-only character effects to every run in each paragraph touched by the selection, rather
/// than only to the selected character span.
/// </summary>
public sealed class CharacterFormattingParityTests
{
    private static TextDocument DocumentWithRuns()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var first = new Paragraph();
        first.Runs.Add(new Run("first"));
        first.Runs.Add(new Run(" paragraph", RunFormatting.Default with { Bold = true }));
        document.Blocks.Add(first);

        var second = new Paragraph();
        second.Runs.Add(new Run("second"));
        second.Runs.Add(new Run(" paragraph", RunFormatting.Default with { Italic = true }));
        document.Blocks.Add(second);
        return document;
    }

    [Fact]
    public void Character_border_formats_all_runs_in_each_touched_paragraph()
    {
        var view = new DocumentView();
        view.LoadDocument(DocumentWithRuns());
        view.SetSelectionRangePublic(0, 2, 1, 3);

        view.SetCharacterBorder(new ParagraphBorder("#0070C0", 1.5));

        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .All(run => run.Formatting.CharacterBorder?.ColorHex == "#0070C0")
            .Should().BeTrue();
    }

    [Fact]
    public void Character_shading_at_collapsed_caret_formats_the_whole_current_paragraph()
    {
        var view = new DocumentView();
        view.LoadDocument(DocumentWithRuns());
        view.MoveCaretToBlock(0, 2);

        view.SetCharacterShading("#FFF2CC", ShadingPattern.Solid);

        view.Document.Blocks.OfType<Paragraph>().First().Runs
            .All(run => run.Formatting.CharacterShadingHex == "#FFF2CC")
            .Should().BeTrue();
        view.Document.Blocks.OfType<Paragraph>().Skip(1).First().Runs
            .All(run => run.Formatting.CharacterShadingHex is null)
            .Should().BeTrue();
    }

    [Fact]
    public void Character_formatting_across_paragraphs_is_one_undoable_operation()
    {
        var view = new DocumentView();
        view.LoadDocument(DocumentWithRuns());
        view.SetSelectionRangePublic(0, 1, 1, 4);

        view.SetCharacterShading("#D9EAD3");
        view.CanUndo.Should().BeTrue();
        view.Undo();

        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .All(run => run.Formatting.CharacterShadingHex is null)
            .Should().BeTrue();
    }
}
