using Free.Shared.AppServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
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

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // A â€” GetCaretFormatting() DocumentView API
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
        // The caret starts at block 0, offset 0 â€” the run formatting at that position is the first run's.
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

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // B â€” RevealFormatting model (pure, no Avalonia needed)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // C â€” RevealFormattingPane toggle (Avalonia headless)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

    [Fact]
    public async Task FindReplaceDialog_reuse_updates_open_mode_for_both_shortcuts()
    {
        FindReplaceOpenMode? initialFocus = null;
        FindReplaceOpenMode? afterReplace = null;
        FindReplaceOpenMode? afterFind = null;
        var ran = await OnUiThread(() =>
        {
            var dialog = new FindReplaceDialog(new DocumentView(), FindReplaceOpenMode.Find);
            try
            {
                dialog.Show();
                dialog.Activate();
                initialFocus = dialog.FocusedFieldForTest;

                dialog.Activate();
                dialog.ActivateFor(FindReplaceOpenMode.Replace);
                afterReplace = dialog.FocusedFieldForTest;

                dialog.Activate();
                dialog.ActivateFor(FindReplaceOpenMode.Find);
                afterFind = dialog.FocusedFieldForTest;
            }
            finally
            {
                dialog.Close();
            }
        });

        if (!ran)
            return;

        initialFocus.Should().Be(FindReplaceOpenMode.Find);
        afterReplace.Should().Be(FindReplaceOpenMode.Replace);
        afterFind.Should().Be(FindReplaceOpenMode.Find);
    }

}
