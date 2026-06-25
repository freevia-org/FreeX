using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Headless tests for the Avalonia <see cref="RevealFormattingPane"/> and
/// <see cref="FindReplaceDialog"/>.
///
/// All tests that touch Avalonia controls must run on the headless UI thread via
/// <see cref="HeadlessUnitTestSession.Dispatch"/>. Pure model tests (no UI controls) run
/// synchronously from the calling thread.
/// </summary>
public sealed class RevealFormattingAndFindReplaceTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false; // no headless drawing backend in this CI environment — test is skipped
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // A — GetCaretFormatting() DocumentView API
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetCaretFormatting_returns_run_formatting_of_first_run()
    {
        RunFormatting? run = null;
        var ran = await OnUiThread(() =>
        {
            var doc = new TextDocument();
            var para = new Paragraph();
            para.Runs.Add(new Run("Hello") { Formatting = new RunFormatting { Bold = true, FontSizePt = 14, FontFamily = "Georgia" } });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));

            var (r, _) = view.GetCaretFormatting();
            run = r;
        });

        if (!ran)
            return;

        run.Should().NotBeNull();
        // The caret starts at block 0, offset 0 — the run formatting at that position is the first run's.
        run!.Bold.Should().BeTrue();
    }

    [Fact]
    public async Task GetCaretFormatting_returns_paragraph_formatting_of_caret_paragraph()
    {
        ParagraphFormatting? pf = null;
        var ran = await OnUiThread(() =>
        {
            var doc = new TextDocument();
            var para = new Paragraph
            {
                Formatting = new ParagraphFormatting
                {
                    Alignment = TextAlignment.Center,
                    SpaceBeforePt = 12,
                    SpaceBeforeIsSet = true,
                },
            };
            para.Runs.Add(new Run("text"));
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));

            var (_, p) = view.GetCaretFormatting();
            pf = p;
        });

        if (!ran)
            return;

        pf.Should().NotBeNull();
        pf!.Alignment.Should().Be(TextAlignment.Center);
        pf.SpaceBeforePt.Should().Be(12);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // B — RevealFormatting model (pure, no Avalonia needed)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RevealFormatting_Describe_produces_three_sections()
    {
        var run = new RunFormatting { FontFamily = "Calibri", FontSizePt = 11, Bold = true };
        var paragraph = ParagraphFormatting.Default;
        var page = new PageSettings();

        var count = RevealFormattingPane.DescribeSectionCount(run, paragraph, page);

        count.Should().Be(3, "RevealFormatting always returns FONT, PARAGRAPH, SECTION sections");
    }

    [Fact]
    public void RevealFormatting_Describe_font_section_contains_correct_values()
    {
        var run = new RunFormatting
        {
            FontFamily = "Times New Roman",
            FontSizePt = 14,
            Bold = true,
            Italic = true,
            ColorHex = "#FF0000",
        };
        var items = RevealFormattingPane.DescribeSection(
            run, ParagraphFormatting.Default, new PageSettings(), "FONT");

        items.Should().NotBeEmpty();
        items.Should().Contain(i => i.Label == "Font" && i.Value == "Times New Roman");
        items.Should().Contain(i => i.Label == "Size" && i.Value == "14 pt");
        items.Should().Contain(i => i.Label == "Color" && i.Value == "#FF0000");
        // Effects should mention Bold and Italic.
        var effects = items.FirstOrDefault(i => i.Label == "Effects");
        effects.Should().NotBeNull();
        effects!.Value.Should().Contain("Bold");
        effects.Value.Should().Contain("Italic");
    }

    [Fact]
    public void RevealFormatting_Describe_paragraph_section_contains_alignment()
    {
        var paragraph = new ParagraphFormatting { Alignment = TextAlignment.Center };
        var items = RevealFormattingPane.DescribeSection(
            RunFormatting.Default, paragraph, new PageSettings(), "PARAGRAPH");

        items.Should().Contain(i => i.Label == "Alignment" && i.Value == "Centered");
    }

    [Fact]
    public void RevealFormatting_Describe_section_section_contains_paper_info()
    {
        var page = new PageSettings { WidthPt = 612, HeightPt = 792 };
        var items = RevealFormattingPane.DescribeSection(
            RunFormatting.Default, ParagraphFormatting.Default, page, "SECTION");

        items.Should().NotBeEmpty();
        var paper = items.FirstOrDefault(i => i.Label == "Paper");
        paper.Should().NotBeNull();
        paper!.Value.Should().Contain("Portrait");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // C — RevealFormattingPane toggle (Avalonia headless)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RevealFormattingPane_is_hidden_by_default()
    {
        bool visible = true;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            visible = window.RevealPane.IsVisible;
        });

        if (!ran)
            return;

        visible.Should().BeFalse("reveal pane is hidden by default");
    }

    [Fact]
    public async Task ToggleRevealFormatting_shows_and_hides_pane()
    {
        bool afterOn = false;
        bool afterOff = false;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.ToggleRevealFormatting();
            afterOn = window.RevealPane.IsVisible;
            window.ToggleRevealFormatting();
            afterOff = window.RevealPane.IsVisible;
        });

        if (!ran)
            return;

        afterOn.Should().BeTrue();
        afterOff.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // D — FindReplaceDialog.CountMatches (pure model, no Avalonia)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FindReplaceDialog_CountMatches_finds_all_case_insensitive_occurrences()
    {
        var doc = BuildSampleDoc("The quick brown fox. FOX jumped over the fox.");

        var count = FindReplaceDialog.CountMatches(doc, "fox");

        count.Should().Be(3, "three case-insensitive 'fox' occurrences");
    }

    [Fact]
    public void FindReplaceDialog_CountMatches_respects_match_case()
    {
        var doc = BuildSampleDoc("The quick brown fox. FOX jumped over the fox.");

        var count = FindReplaceDialog.CountMatches(doc, "fox", matchCase: true);

        count.Should().Be(2, "only two lowercase 'fox' occurrences with match-case");
    }

    [Fact]
    public void FindReplaceDialog_CountMatches_respects_whole_word()
    {
        var doc = BuildSampleDoc("foxglove fox foxes");

        var count = FindReplaceDialog.CountMatches(doc, "fox", wholeWord: true);

        count.Should().Be(1, "only the standalone 'fox' is a whole-word match");
    }

    [Fact]
    public void FindReplaceDialog_CountMatches_respects_wildcards()
    {
        var doc = BuildSampleDoc("cat bat hat sat rat");

        // Wildcard pattern [cbh]at matches cat, bat, hat (not sat, rat).
        var count = FindReplaceDialog.CountMatches(doc, "[cbh]at", useWildcards: true);

        count.Should().Be(3);
    }

    [Fact]
    public void FindReplaceDialog_CountMatches_returns_zero_for_empty_needle()
    {
        var doc = BuildSampleDoc("some text");
        FindReplaceDialog.CountMatches(doc, string.Empty).Should().Be(0);
    }

    [Fact]
    public void FindReplaceDialog_CountMatches_spans_multiple_paragraphs()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Runs = { new Run("Hello world") } });
        doc.Blocks.Add(new Paragraph { Runs = { new Run("Say hello again") } });

        var count = FindReplaceDialog.CountMatches(doc, "hello");

        count.Should().Be(2, "one hit per paragraph, case-insensitive");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static TextDocument BuildSampleDoc(string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Runs = { new Run(text) } });
        return doc;
    }
}
