using Avalonia;
using System.Threading.Tasks;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r160, finding r159-open F3: a clipboard that carries both an image and independent plain text (a
/// screenshot tool that also copies the saved file path, say) reaches <c>MainWindow.ApplyClipboardPastePlan</c>,
/// which splices the synthesized single-paragraph image document in first via
/// <see cref="DocumentView.PasteKeepSourceFormatting"/>, then calls <see cref="DocumentView.PasteMergeFormatting"/>
/// for the accompanying text — landing the caret in that very same now-image-bearing paragraph.
/// <c>InsertText</c>'s body branch used to refuse ANY paragraph holding an image outright (<c>IsTextReplaceable</c>),
/// so the text vanished with no error, no undo entry, nothing — <c>PasteNormalizedText</c> returns true
/// unconditionally once it has non-empty text, regardless of whether the underlying <c>InsertText</c> call
/// actually inserted anything, so only inspecting the model afterward reveals the drop.
/// </summary>
public sealed class DocumentViewPasteImageTextTests
{
    private static Task<bool> OnUiThread(System.Action action) => HeadlessUiThread.Run(action);

    private static DocumentView LoadEmptyDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph());
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static TextDocument SynthesizedImageDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        var image = new InlineImage([1, 2, 3, 4], 72, 54) { Wrapping = ImageWrapping.Inline };
        paragraph.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = image });
        document.Blocks.Add(paragraph);
        return document;
    }

    [Fact]
    public async Task PasteMergeFormatting_after_an_image_paste_inserts_the_text_instead_of_dropping_it()
    {
        string? plainTextAfter = null;
        bool imageSurvivedFirstPaste = false, imageSurvivedAfterward = false;

        var ran = await OnUiThread(() =>
        {
            var view = LoadEmptyDocument();

            view.PasteKeepSourceFormatting(SynthesizedImageDocument()).Should().BeTrue(
                "the synthesized image document splices in as a rich paste");

            var paragraph = view.Document.Paragraphs.Single();
            imageSurvivedFirstPaste = paragraph.Runs.Any(r => r.Image is not null);

            view.PasteMergeFormatting("C:/Users/ann/Pictures/screenshot.png");

            paragraph = view.Document.Paragraphs.Single();
            plainTextAfter = paragraph.PlainText;
            imageSurvivedAfterward = paragraph.Runs.Any(r => r.Image is not null);
        });

        if (!ran) return;
        imageSurvivedFirstPaste.Should().BeTrue("the image must survive the first paste");
        plainTextAfter.Should().Be(
            "C:/Users/ann/Pictures/screenshot.png",
            "the clipboard's accompanying plain text must land beside the image, not be silently dropped");
        imageSurvivedAfterward.Should().BeTrue("the image must still be there afterward");
    }

    [Fact]
    public async Task PasteMergeFormatting_after_an_image_paste_is_undoable_as_its_own_step()
    {
        string? textBeforeUndo = null;
        string? textAfterUndo = null;
        bool imageSurvivesUndo = false;

        var ran = await OnUiThread(() =>
        {
            var view = LoadEmptyDocument();
            view.PasteKeepSourceFormatting(SynthesizedImageDocument()).Should().BeTrue();

            view.PasteMergeFormatting("path.png");
            textBeforeUndo = view.Document.Paragraphs.Single().PlainText;

            view.Undo();
            var afterUndo = view.Document.Paragraphs.Single();
            textAfterUndo = afterUndo.PlainText;
            imageSurvivesUndo = afterUndo.Runs.Any(r => r.Image is not null);
        });

        if (!ran) return;
        textBeforeUndo.Should().Be("path.png");
        textAfterUndo.Should().Be(string.Empty, "undo should remove just the text insertion");
        imageSurvivesUndo.Should().BeTrue("the earlier image paste is a separate undo step");
    }

    // ── Sibling: typing beside an equation preserves its structural run and one-step history ────────

    [Fact]
    public async Task InsertText_beside_an_equation_preserves_its_identity_and_undoes_only_text()
    {
        var ran = await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            var equation = Equation.FromText("x^2");
            var equationRun = new Run(string.Empty, RunFormatting.Default) { Equation = equation };
            paragraph.Runs.Add(equationRun);
            document.Blocks.Add(paragraph);
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(800, 2000));

            view.MoveCaretToBlockForTest(0, 0);
            view.InsertText("hello");

            paragraph.PlainText.Should().Be("hello");
            paragraph.Runs.First().Text.Should().Be("hello");
            paragraph.Runs.Last().Should().BeSameAs(equationRun);
            equationRun.Equation.Should().BeSameAs(equation);
            equation.LinearText.Should().Be("x^2");
            view.CaretOffsetForTest.Should().Be(5);
            view.AutomationSnapshot().CaretOffset.Should().Be(5);

            view.Undo();
            paragraph.Runs.Should().ContainSingle().Which.Should().BeSameAs(equationRun);
            equation.LinearText.Should().Be("x^2");
            view.CaretOffsetForTest.Should().Be(0);
            view.CanUndo.Should().BeFalse("the equation was fixture content, and typing is one edit");

            view.Redo();
            paragraph.PlainText.Should().Be("hello");
            paragraph.Runs.Last().Should().BeSameAs(equationRun);
            equation.LinearText.Should().Be("x^2");
            view.CaretOffsetForTest.Should().Be(5);
        });

        if (!ran) return;
    }

    // ── Sibling / no-regression: ordinary typing into a plain paragraph is unaffected ────────────────

    [Fact]
    public async Task InsertText_into_an_ordinary_paragraph_is_unaffected()
    {
        string? plainTextAfter = null;

        var ran = await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph("Head tail"));
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(800, 2000));

            view.MoveCaretToBlockForTest(0, 5);
            view.InsertText("XX");

            plainTextAfter = view.Document.Paragraphs.Single().PlainText;
        });

        if (!ran) return;
        plainTextAfter.Should().Be("Head XXtail");
    }
}
