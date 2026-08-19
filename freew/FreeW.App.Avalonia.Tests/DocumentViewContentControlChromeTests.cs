using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-CCEDIT: a content control has to LOOK like one. The WPF host shades the field's run, gives it a
/// descriptive tooltip and synthesises the check-box glyph from the checked state; Avalonia drew fields
/// as ordinary body text, so the same document read as plain prose on Linux/macOS.
/// </summary>
public sealed class DocumentViewContentControlChromeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task A_fields_glyphs_are_the_shaded_region_and_body_text_is_not() =>
        await Session.Dispatch(
            () =>
            {
                var view = LoadParagraph(
                    new Run("Name: "),
                    Run.PlainTextControl("Bob", tag: "Applicant"),
                    new Run(" (staff)"));

                var glyphs = view.ContentControlGlyphsForTest(0);

                new string(glyphs.Select(glyph => glyph.Ch).ToArray()).Should().Be("Bob");
                glyphs.Select(glyph => glyph.Kind).Distinct().Should().Equal(ContentControlKind.PlainText);
                glyphs.Should().OnlyContain(glyph => glyph.Rect.Width > 0 && glyph.Rect.Height > 0);
            },
            CancellationToken.None);

    [Fact]
    public async Task A_check_box_renders_its_state_glyph_without_rewriting_the_model() =>
        await Session.Dispatch(
            () =>
            {
                // Word stores the box as a symbol-font codepoint that means nothing in the body font.
                var checkBox = Run.CheckBoxControl(@checked: true);
                checkBox.Text = "";
                var view = LoadParagraph(checkBox);

                view.ContentControlGlyphsForTest(0).Select(glyph => glyph.Ch)
                    .Should().Equal(FreeW.Core.Model.ContentControl.CheckedGlyph[0]);
                view.Document.Paragraphs.Single().Runs[0].Text
                    .Should().Be("", "the render must not rewrite the stored text");
            },
            CancellationToken.None);

    [Fact]
    public async Task Hovering_a_field_shows_its_description_and_leaving_clears_it() =>
        await Session.Dispatch(
            () =>
            {
                var view = LoadParagraph(
                    new Run("Name: "),
                    Run.PlainTextControl("Bob", alias: "Applicant"));

                var glyph = view.ContentControlGlyphsForTest(0).First();
                var inside = new Point(glyph.Rect.X + glyph.Rect.Width / 2, glyph.Rect.Y + glyph.Rect.Height / 2);

                view.ContentControlHoverTipForTest(inside).Should().Be("Content control: Applicant");
                view.ContentControlHoverTipForTest(new Point(glyph.Rect.X, glyph.Rect.Y + 5000))
                    .Should().BeNull("the pointer left the field");
            },
            CancellationToken.None);

    [Fact]
    public async Task A_field_inside_a_table_cell_gets_the_same_chrome() =>
        await Session.Dispatch(
            () =>
            {
                var document = TextDocument.CreateEmpty();
                document.Blocks.Clear();
                var table = new Table();
                var row = new TableRow();
                var cell = new TableCell();
                cell.Paragraphs.Clear();
                var cellParagraph = new Paragraph();
                cellParagraph.Runs.Add(new Run("Name: "));
                cellParagraph.Runs.Add(Run.PlainTextControl("Bob", alias: "Applicant"));
                cellParagraph.Runs.Add(Run.CheckBoxControl(@checked: true));
                cell.Paragraphs.Add(cellParagraph);
                row.Cells.Add(cell);
                table.Rows.Add(row);
                document.Blocks.Add(table);

                var view = new DocumentView();
                view.LoadDocument(document);
                view.Measure(new Size(800, 2000));

                var glyphs = view.ContentControlGlyphsForTest(0);

                // Forms usually lay their fields out in a table, so the cell path must carry the control
                // through to the placed glyphs exactly as the body path does.
                new string(glyphs.Select(glyph => glyph.Ch).ToArray())
                    .Should().Be("Bob" + FreeW.Core.Model.ContentControl.CheckedGlyph);
                glyphs.Select(glyph => glyph.Kind).Distinct()
                    .Should().Equal(ContentControlKind.PlainText, ContentControlKind.CheckBox);

                var firstGlyph = glyphs[0].Rect;
                view.ContentControlHoverTipForTest(
                        new Point(firstGlyph.X + firstGlyph.Width / 2, firstGlyph.Y + firstGlyph.Height / 2))
                    .Should().Be("Content control: Applicant");
            },
            CancellationToken.None);

    /// <summary>
    /// The paint itself cannot be sampled here: this suite runs Avalonia's headless drawing stub
    /// (<c>UseHeadlessDrawing = true</c>, see <see cref="FreeWHeadlessApp"/>), so
    /// <c>CaptureRenderedFrame</c> yields nothing and a pixel assertion would pass whatever the render
    /// did. The glyph-level tests above prove which glyphs the field owns; this one pins the render loop
    /// to actually filling them with the shade brush, the way the repo's other source-contract tests pin
    /// renderer-neutral ownership.
    /// </summary>
    [Fact]
    public void The_glyph_render_loop_fills_a_fields_glyphs_with_the_shade_brush()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.App.Avalonia",
            "Editing",
            "DocumentView.cs"));

        var normalized = new string(source.Where(character => !char.IsWhiteSpace(character)).ToArray());

        normalized.Should().Contain(
            "if(pc.Controlisnotnull)context.FillRectangle(ContentControlShadeBrush,",
            "a content control's glyphs must be painted with the field shade");
    }

    private static DocumentView LoadParagraph(params Run[] runs)
    {
        var paragraph = new Paragraph();
        foreach (var run in runs)
            paragraph.Runs.Add(run);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }
}
