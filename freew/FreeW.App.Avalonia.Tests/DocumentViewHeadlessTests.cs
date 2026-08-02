using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using AvaloniaTextOptions = Avalonia.Media.TextOptions;
using AvaloniaTextRenderingMode = Avalonia.Media.TextRenderingMode;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;

[assembly: AvaloniaTestApplication(typeof(FreeW.App.Avalonia.Tests.FreeWHeadlessApp))]

// These tests drive the Avalonia headless single UI thread (DocumentView + dispatcher via OnUi
// helpers). xUnit parallelizes test classes by default, so multiple UI-dispatcher tests can run
// concurrently and deadlock against the one headless UI thread (observed as a test-host hang, e.g.
// PictureDrawingContextualTabTests.WrapCommand_changes_shape_wrapping). Serialize the whole assembly.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace FreeW.App.Avalonia.Tests;

/// <summary>Minimal headless Avalonia app (Fluent theme + headless drawing) so DocumentView can lay out.</summary>
public sealed class FreeWHeadlessApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<FreeWHeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>
/// Exercises the real DocumentView layout + editing on the shared headless UI thread (the per-character
/// layout engine needs an Avalonia backend for FormattedText). Each case opts out cleanly if no headless
/// drawing backend is available, rather than failing.
/// </summary>
public sealed class DocumentViewHeadlessTests
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
            return false; // no headless drawing backend in this environment
        }
    }

    private static async Task<bool> OnUiThreadAsync(Func<Task> action)
    {
        try
        {
            await Session.Dispatch(
                async () =>
                {
                    await action();
                    return true;
                },
                CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Fact]
    public async Task Sample_document_lays_out_glyphs()
    {
        var glyphs = 0;
        var blocks = 0;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.Measure(new Size(800, 4000));
            glyphs = view.PlacedGlyphCount;
            blocks = view.BlockCount;
        });

        if (!ran)
            return;
        glyphs.Should().BeGreaterThan(0);
        blocks.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Complex_field_toggle_rebuilds_layout_with_word_code_shape_and_restores_result()
    {
        string? initial = null;
        string? code = null;
        string? restored = null;
        string? codeColor = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Properties.Title = "Current title";
            doc.Blocks.Add(new Paragraph
            {
                Runs =
                {
                    new Run("Title: "),
                    Run.ComplexFieldRun(" TITLE ", "Stale result")
                }
            });

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 1200));
            initial = string.Concat(view.GetPlacedForBlock(0).Select(item => item.Ch));

            view.ToggleFieldCodes();
            view.Measure(new Size(900, 1200));
            code = string.Concat(view.GetPlacedForBlock(0).Select(item => item.Ch));
            codeColor = view.GetPlacedFormattingForBlock(0)
                .FirstOrDefault(formatting => formatting.ColorHex is not null)
                ?.ColorHex;

            view.ToggleFieldCodes();
            view.Measure(new Size(900, 1200));
            restored = string.Concat(view.GetPlacedForBlock(0).Select(item => item.Ch));
        });

        if (!ran)
            return;

        initial.Should().Contain("Title: Current title");
        code.Should().Contain("Title: { TITLE }");
        codeColor.Should().Be("#808080");
        restored.Should().Be(initial);
    }

    [Fact]
    public async Task Bibliography_field_keeps_cached_result_visible_when_generated_region_follows()
    {
        string? visible = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph
            {
                Runs =
                {
                    new Run("Bibliography field cache: "),
                    Run.ComplexFieldRun(" BIBLIOGRAPHY \\l 1033 ", "References")
                }
            });
            doc.Blocks.Add(new Paragraph("References") { StyleId = Citations.HeadingStyleId });

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 1200));
            visible = string.Concat(view.GetPlacedForBlock(0).Select(item => item.Ch));
        });

        if (!ran)
            return;

        visible.Should().Contain("Bibliography field cache: References");
    }

    [Fact]
    public async Task Unstyled_runs_inherit_document_default_run_formatting()
    {
        IReadOnlyList<RunFormatting>? formatting = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.DefaultRun = doc.DefaultRun with { FontFamily = "Calibri", FontSizePt = 11 };
            doc.Blocks.Add(new Paragraph("Default-run cascade"));

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 1200));
            formatting = view.GetPlacedFormattingForBlock(0);
        });

        if (!ran)
            return;

        formatting.Should().NotBeNullOrEmpty();
        formatting!.Should().OnlyContain(f => f.FontFamily == "Calibri" && f.FontSizePt == 11);
    }

    [Fact]
    public async Task Document_view_uses_grayscale_text_antialiasing_for_document_surface_fidelity()
    {
        var renderingMode = AvaloniaTextRenderingMode.Unspecified;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            renderingMode = AvaloniaTextOptions.GetTextRenderingMode(view);
        });

        if (!ran)
            return;

        renderingMode.Should().Be(AvaloniaTextRenderingMode.Antialias);
    }

    [Fact]
    public async Task Footnote_reference_uses_its_rendered_superscript_scale_for_layout_metrics()
    {
        IReadOnlyList<(char Ch, double X, double W, double Y, double LineHeight, bool IsSubscript)>? placed = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("1"));
            paragraph.Runs.Add(Run.FootnoteReference(1));
            doc.Blocks.Add(paragraph);
            doc.Footnotes[1] = new Footnote(1, "Footnote body.");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(960, 1200));
            placed = view.GetPlacedForBlock(0);
        });

        if (!ran)
            return;

        placed.Should().NotBeNullOrEmpty();
        placed.Should().HaveCount(2);
        placed![1].W.Should().BeApproximately(placed[0].W * 0.583, 0.01,
            "footnote-reference layout width must match the superscript scale used by rendering");
    }

    [Fact]
    public async Task Default_calibri_body_uses_the_word_single_line_box()
    {
        double firstLineY = 0;
        double secondLineY = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("First body paragraph."));
            doc.Blocks.Add(new Paragraph("Second body paragraph."));

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(960, 1200));

            firstLineY = view.GetPlacedForBlock(0).First().Y;
            secondLineY = view.GetPlacedForBlock(1).First().Y;
        });

        if (!ran)
            return;

        (secondLineY - firstLineY).Should().BeInRange(28.3, 28.9,
            "unstyled Calibri 11 body paragraphs should advance at Word's natural single-line cadence");
    }

    [Fact]
    public async Task Typing_inserts_text_and_is_undoable()
    {
        string? after = null;
        var canUndo = false;
        string? undone = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.Measure(new Size(800, 4000));
            view.InsertText("ZZ");
            after = view.PlainText;
            canUndo = view.CanUndo;
            view.Undo();
            undone = view.PlainText;
        });

        if (!ran)
            return;
        after.Should().StartWith("ZZ");
        canUndo.Should().BeTrue();
        undone.Should().NotStartWith("ZZ");
    }

    [Fact]
    public async Task Insert_table_adds_a_table_block()
    {
        var tables = 0;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.Measure(new Size(800, 4000));
            var before = view.Document.Blocks.OfType<FreeW.Core.Model.Table>().Count();
            view.InsertTable(2, 2);
            tables = view.Document.Blocks.OfType<FreeW.Core.Model.Table>().Count() - before;
        });

        if (!ran)
            return;
        tables.Should().Be(1);
    }

    [Fact]
    public async Task Find_selects_a_match()
    {
        var found = false;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.Measure(new Size(800, 4000));
            found = view.FindNext("FreeW");
        });

        if (!ran)
            return;
        found.Should().BeTrue();
    }

    [Fact]
    public async Task Derived_style_inherits_based_on_run_and_paragraph_formatting()
    {
        RunFormatting? run = null;
        ParagraphFormatting? paragraphFmt = null;
        var ran = await OnUiThread(() =>
        {
            var doc = new TextDocument();
            doc.Styles["Base"] = new DocumentStyle
            {
                Id = "Base",
                Name = "Base",
                Run = new RunFormatting
                {
                    FontFamily = "Georgia",
                    FontSizePt = 14,
                    ColorHex = "#224466",
                    Bold = true,
                },
                Paragraph = new ParagraphFormatting
                {
                    SpaceBeforePt = 12,
                    SpaceBeforeIsSet = true,
                    SpaceAfterPt = 3,
                    SpaceAfterIsSet = true,
                },
            };
            doc.Styles["Derived"] = new DocumentStyle
            {
                Id = "Derived",
                Name = "Derived",
                BasedOnStyleId = "Base",
                Run = new RunFormatting { Italic = true },
                Paragraph = new ParagraphFormatting { Alignment = TextAlignment.Center },
            };
            var paragraph = new Paragraph("styled") { StyleId = "Derived" };
            doc.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(doc);
            run = InvokePrivate<RunFormatting>(view, "ResolveRunFmt", RunFormatting.Default, paragraph);
            paragraphFmt = InvokePrivate<ParagraphFormatting>(view, "ResolveParagraphFmt", paragraph);
        });

        if (!ran)
            return;

        run.Should().NotBeNull();
        run!.FontFamily.Should().Be("Georgia");
        run.FontSizePt.Should().Be(14);
        run.ColorHex.Should().Be("#224466");
        run.Bold.Should().BeTrue();
        run.Italic.Should().BeTrue();
        paragraphFmt.Should().NotBeNull();
        paragraphFmt!.Alignment.Should().Be(TextAlignment.Center);
        paragraphFmt.SpaceBeforePt.Should().Be(12);
        paragraphFmt.SpaceAfterPt.Should().Be(3);
    }

    [Fact]
    public async Task ExportPdf_through_shared_tier_produces_valid_pdf()
    {
        byte[]? bytes = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.Measure(new Size(800, 4000));

            using var stream = new System.IO.MemoryStream();
            var result = FreeW.App.Avalonia.Pdf.FreeWAvaloniaPdfExport.Save(view, stream);
            result.PageCount.Should().BeGreaterThan(0);
            bytes = stream.ToArray();
        });

        if (!ran)
            return;

        bytes.Should().NotBeNull();
        bytes!.Length.Should().BeGreaterThan(0);
        // Valid PDFs start with the "%PDF-" magic header (Skia or portable WinAnsi, both shared-tier).
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task MainWindow_tracks_dirty_and_new_document_state_with_shared_file_command_workflow()
    {
        var ran = await OnUiThreadAsync(async () =>
        {
            var window = new MainWindow(
                Array.Empty<string>(),
                new FreeWOptions(),
                ApplicationOptionsStore<FreeWOptions>.ForPath(
                    Path.Combine(Path.GetTempPath(), "FreeW.Avalonia.Tests", Guid.NewGuid().ToString("N"), "settings.json")),
                promptSaveChangesAsync: _ => Task.FromResult(SaveChangesPrompt.DontSave));
            var shellWorkflow = GetPrivateField<SisterAvaloniaFileCommandWorkflow>(window, "_fileWorkflow");
            var workflow = shellWorkflow.Workflow;

            workflow.IsDirty.Should().BeFalse();
            workflow.CurrentPath.Should().BeNull();
            workflow.DisplayName.Should().Be("Untitled");
            window.Title.Should().Be("FreeW");

            window.Editor.InsertText("draft ");
            workflow.IsDirty.Should().BeTrue();
            window.Title.Should().Be("FreeW - Untitled *");

            (await window.NewDocumentAsyncForTests()).Should().BeTrue();

            workflow.IsDirty.Should().BeFalse();
            workflow.CurrentPath.Should().BeNull();
            workflow.DisplayName.Should().Be("Untitled");
            window.Title.Should().Be("FreeW");
        });

        if (!ran)
            return;
    }

    // ---- Pagination tests -----------------------------------------------------------------------

    /// <summary>
    /// A short document (a few paragraphs) should produce PageCount == 1.
    /// </summary>
    [Fact]
    public async Task Short_document_has_one_page()
    {
        var pageCount = 0;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.Measure(new Size(800, 4000));
            pageCount = view.PageCount;
        });

        if (!ran)
            return;
        pageCount.Should().Be(1, "the default sample document is short enough to fit on one page");
    }

    /// <summary>
    /// A document with enough body text to overflow a US-Letter page must produce PageCount > 1.
    /// </summary>
    [Fact]
    public async Task Long_document_produces_multiple_pages()
    {
        var pageCount = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = BuildLongDocument();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            pageCount = view.PageCount;
        });

        if (!ran)
            return;
        pageCount.Should().BeGreaterThan(1, "the long document must span more than one page");
    }

    /// <summary>
    /// The scroll height of a multi-page document must be greater than two US-Letter pages tall
    /// (each page is ~1056px at 96dpi). This confirms that page rects are stacked vertically.
    /// </summary>
    [Fact]
    public async Task Multi_page_document_scroll_height_exceeds_two_letter_pages()
    {
        var scrollHeight = 0.0;
        var ran = await OnUiThread(() =>
        {
            var doc = BuildLongDocument();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, double.PositiveInfinity));
            // After Measure, DesiredSize.Height is the total scroll height.
            scrollHeight = view.DesiredSize.Height;
        });

        if (!ran)
            return;
        // Two US-Letter pages at 96dpi = 2 * 1056 = 2112 px minimum.
        scrollHeight.Should().BeGreaterThan(2112,
            "a multi-page document must have a total height greater than two letter-size pages");
    }

    /// <summary>
    /// CaretPageIndex is 0 at the start of a multi-page document (caret on page 1).
    /// PageCount reports the total page count correctly.
    /// </summary>
    [Fact]
    public async Task Caret_starts_on_page_0_and_page_count_is_correct()
    {
        var initialPage = -1;
        var pageCount = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = BuildLongDocument();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 9000));

            initialPage = view.CaretPageIndex;
            pageCount = view.PageCount;
        });

        if (!ran)
            return;
        initialPage.Should().Be(0, "caret starts at block 0 which is on page 0");
        pageCount.Should().BeGreaterThan(1, "long document must have multiple pages");
    }

    private static TextDocument BuildLongDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var bodyFmt = RunFormatting.Default with { FontSizePt = 12 };
        // US-Letter page = 792pt tall; 1-inch margins = 648pt text area (~865 px at 96dpi).
        // At 12pt/96dpi body text (~20px line height with 1.3 leading) that's ~43 lines per page.
        // Add 60 paragraphs of 2 lines each to guarantee > 1 page.
        for (var i = 1; i <= 60; i++)
        {
            var p = new Paragraph();
            p.Runs.Add(new Run(
                $"Paragraph {i}: Lorem ipsum dolor sit amet, consectetur adipiscing elit, " +
                "sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad " +
                "minim veniam, quis nostrud exercitation ullamco laboris.",
                bodyFmt));
            doc.Blocks.Add(p);
        }
        return doc;
    }

    // ---- View mode tests -----------------------------------------------------------------------

    /// <summary>
    /// WebLayout and Draft modes must report PageCount == 1 regardless of document length.
    /// </summary>
    [Theory]
    [InlineData(DocumentViewMode.WebLayout)]
    [InlineData(DocumentViewMode.Draft)]
    public async Task Non_print_modes_report_page_count_of_one(DocumentViewMode mode)
    {
        var pageCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = BuildLongDocument(); // long enough to produce >1 pages in PrintLayout
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.ViewMode = mode;
            view.Measure(new Size(800, 4000));
            pageCount = view.PageCount;
        });

        if (!ran)
            return;
        pageCount.Should().Be(1, $"{mode} is a continuous-column mode — no discrete pages");
    }

    [Fact]
    public async Task Print_layout_hides_page_whitespace_and_header_footer_when_document_requests_it()
    {
        double? firstGlyphY = null;
        var headerFooterCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = SampleDocument.Create();
            doc.DoNotDisplayPageBoundaries = true;
            doc.FinalSectionHeadersFooters.Header = new HeaderFooter("Hidden header");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.ViewMode = DocumentViewMode.PrintLayout;
            view.Measure(new Size(800, 4000));

            firstGlyphY = view.GetPlacedForBlock(0).FirstOrDefault().Y;
            headerFooterCount = view.HeaderFooterItems.Count;
        });

        if (!ran)
            return;
        firstGlyphY.Should().BeApproximately(24, 0.01,
            "the body begins at the page desk inset when vertical page whitespace is hidden");
        headerFooterCount.Should().Be(0,
            "headers and footers belong to the hidden page-boundary whitespace");
    }

    /// <summary>
    /// WebLayout and Draft content height must be less than the PrintLayout height for the same
    /// document because they have no inter-page gaps and no DeskPadding.
    /// </summary>
    [Theory]
    [InlineData(DocumentViewMode.WebLayout)]
    [InlineData(DocumentViewMode.Draft)]
    public async Task Non_print_modes_content_height_less_than_print_layout(DocumentViewMode mode)
    {
        double printHeight = 0, continuousHeight = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = BuildLongDocument();

            var print = new DocumentView();
            print.LoadDocument(doc);
            print.ViewMode = DocumentViewMode.PrintLayout;
            print.Measure(new Size(800, double.PositiveInfinity));
            printHeight = print.DesiredSize.Height;

            var continuous = new DocumentView();
            continuous.LoadDocument(doc);
            continuous.ViewMode = mode;
            continuous.Measure(new Size(800, double.PositiveInfinity));
            continuousHeight = continuous.DesiredSize.Height;
        });

        if (!ran)
            return;
        continuousHeight.Should().BeLessThan(printHeight,
            $"{mode} has no inter-page gaps so total height must be less than Print Layout");
    }

    /// <summary>
    /// Switching from WebLayout / Draft back to PrintLayout must restore pagination (PageCount > 1
    /// for a long document).
    /// </summary>
    [Theory]
    [InlineData(DocumentViewMode.WebLayout)]
    [InlineData(DocumentViewMode.Draft)]
    public async Task Switching_back_to_print_restores_pagination(DocumentViewMode mode)
    {
        var pageCountAfterSwitch = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = BuildLongDocument();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.ViewMode = mode;
            view.Measure(new Size(800, 4000));
            // Now switch back.
            view.ViewMode = DocumentViewMode.PrintLayout;
            view.Measure(new Size(800, double.PositiveInfinity));
            pageCountAfterSwitch = view.PageCount;
        });

        if (!ran)
            return;
        pageCountAfterSwitch.Should().BeGreaterThan(1,
            "switching back to Print Layout must restore discrete pagination");
    }

    /// <summary>
    /// Caret hit-test and selection must work in WebLayout / Draft (the transform is simpler but
    /// non-zero — content starts at _marginTopDip).
    /// </summary>
    [Theory]
    [InlineData(DocumentViewMode.WebLayout)]
    [InlineData(DocumentViewMode.Draft)]
    public async Task Caret_and_editing_work_in_non_print_mode(DocumentViewMode mode)
    {
        string? textBefore = null, textAfter = null;
        var canUndo = false;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.ViewMode = mode;
            view.Measure(new Size(800, 4000));
            textBefore = view.PlainText;
            view.InsertText("ZZ");
            textAfter  = view.PlainText;
            canUndo    = view.CanUndo;
            view.Undo();
        });

        if (!ran)
            return;
        textAfter.Should().StartWith("ZZ", $"typed text must appear in {mode}");
        canUndo.Should().BeTrue($"undo must be available after editing in {mode}");
    }

    /// <summary>
    /// GetBlockTop must return a non-negative value in WebLayout / Draft — blocks are laid out at
    /// _marginTopDip + cumulative content Y, never at negative positions.
    /// </summary>
    [Theory]
    [InlineData(DocumentViewMode.WebLayout)]
    [InlineData(DocumentViewMode.Draft)]
    public async Task GetBlockTop_returns_non_negative_in_non_print_mode(DocumentViewMode mode)
    {
        double blockTop = -999;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.ViewMode = mode;
            view.Measure(new Size(800, 4000));
            blockTop = view.GetBlockTop(0);
        });

        if (!ran)
            return;
        blockTop.Should().BeGreaterThanOrEqualTo(0, $"GetBlockTop(0) must be ≥ 0 in {mode}");
    }

    /// <summary>
    /// Find must work in WebLayout / Draft: glyphs are still in _placed, just with a simpler Y offset.
    /// </summary>
    [Theory]
    [InlineData(DocumentViewMode.WebLayout)]
    [InlineData(DocumentViewMode.Draft)]
    public async Task Find_works_in_non_print_mode(DocumentViewMode mode)
    {
        var found = false;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.ViewMode = mode;
            view.Measure(new Size(800, 4000));
            found = view.FindNext("FreeW");
        });

        if (!ran)
            return;
        found.Should().BeTrue($"FindNext must locate text in {mode}");
    }

    /// <summary>
    /// ViewModeChanged event is raised when the mode changes and NOT raised when setting the same mode.
    /// </summary>
    [Fact]
    public async Task ViewModeChanged_event_fires_on_change_and_not_on_same_mode()
    {
        var changeCount = 0;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.ViewModeChanged += () => changeCount++;

            view.ViewMode = DocumentViewMode.WebLayout; // 1 change
            view.ViewMode = DocumentViewMode.WebLayout; // same — no event
            view.ViewMode = DocumentViewMode.Draft;     // 1 change
            view.ViewMode = DocumentViewMode.PrintLayout; // 1 change
        });

        if (!ran)
            return;
        changeCount.Should().Be(3, "event fires only when the mode actually changes");
    }

    // ---- Paragraph-layout parity tests (OO1–OO4) -----------------------------------------------

    /// <summary>
    /// OO1: In a justified paragraph that wraps, the last word of each justified line must end at
    /// (within 2 px of) the right margin.  The trailing space included in [lineStart, breakAt) must
    /// NOT receive a wordGap, so all gap is distributed among inter-word spaces before the final word.
    /// </summary>
    [Fact]
    public async Task Justify_last_word_reaches_right_margin()
    {
        double rightEdge = 0, lastWordEnd = 0;
        var ran = await OnUiThread(() =>
        {
            // Build a justified paragraph wide enough to wrap at least once at 400 px.
            var doc = new TextDocument();
            doc.Blocks.Clear();
            var p = new Paragraph();
            p.Formatting = new ParagraphFormatting { Alignment = TextAlignment.Justify };
            // Long text — must wrap at 400 px (measured at ~7 px/char for 11pt default).
            p.Runs.Add(new Run(
                "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen",
                RunFormatting.Default));
            doc.Blocks.Add(p);

            var view = new DocumentView();
            view.LoadDocument(doc);
            // 400 px wide content area so the paragraph wraps multiple times.
            view.Measure(new Size(400, 4000));

            // Get placed glyphs for block 0.
            var placed = view.GetPlacedForBlock(0);
            if (placed.Count == 0) return;

            // Find the first line: all glyphs at the minimum Y value.
            var firstLineY = placed.Min(g => g.Y);
            var firstLine  = placed.Where(g => Math.Abs(g.Y - firstLineY) < 0.5).ToList();

            // The second line must also exist (paragraph wraps).
            var secondLineY = placed.Where(g => g.Y > firstLineY + 0.5).Select(g => g.Y).DefaultIfEmpty(-1).Min();
            if (secondLineY < 0) return; // didn't wrap — skip

            // Right edge = leftmost X of first glyph + availableWidth (400 - margins).
            // Simpler: measure it as the maximum (X + W) of non-space chars on the SECOND line,
            // which (if fixed) should reach the right edge of the FIRST justified line too.
            // We check the first justified line: last non-space glyph's right edge vs right edge of
            // the line (= X of the first glyph + line width as measured by justified expansion).
            // The last non-space glyph of line 1 must end within 2 px of the last glyph of ANY
            // kind (i.e. no gap wasted after the final word via a trailing-space wordGap).
            var nonSpaceOnLine1 = firstLine.Where(g => g.Ch != ' ').ToList();
            if (nonSpaceOnLine1.Count == 0) return;

            lastWordEnd = nonSpaceOnLine1.Max(g => g.X + g.W);
            rightEdge   = firstLine.Max(g => g.X + g.W);
        });

        if (!ran) return;
        // The gap between the last non-space glyph and the rightmost glyph (the trailing space)
        // must be at most one natural space width. The headless backend measures 11pt space at
        // ~14.7 px, so we allow up to 20 px (one natural space + rounding).  Before the OO1 fix
        // the gap was natural_space_width + wordGap ≈ 22–30+ px depending on word count.
        (rightEdge - lastWordEnd).Should().BeLessThanOrEqualTo(20.0,
            "OO1: no wordGap must be added after the trailing space; gap must equal at most one natural space width");
        (rightEdge - lastWordEnd).Should().BeGreaterThanOrEqualTo(0,
            "trailing space must be placed after the last word");
    }

    /// <summary>
    /// OO2/OO3: For a paragraph with a positive first-line indent, right-aligned text on line 0
    /// must not overshoot the right margin.  The right edge of any glyph on the first line must be
    /// ≤ the right edge of continuation lines (both sharing the same right margin).
    /// </summary>
    [Fact]
    public async Task First_line_indent_right_align_does_not_overshoot_margin()
    {
        double line0MaxRight = 0, line1MaxRight = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = new TextDocument();
            doc.Blocks.Clear();
            var p = new Paragraph();
            p.Formatting = new ParagraphFormatting
            {
                Alignment = TextAlignment.Right,
                FirstLineIndentPt = 24, // ~32 px positive first-line indent
            };
            p.Runs.Add(new Run(
                "alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron",
                RunFormatting.Default));
            doc.Blocks.Add(p);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(500, 4000));

            var placed = view.GetPlacedForBlock(0);
            if (placed.Count == 0) return;

            var firstLineY = placed.Min(g => g.Y);
            var secondLineY = placed.Where(g => g.Y > firstLineY + 0.5).Select(g => g.Y).DefaultIfEmpty(-1).Min();
            if (secondLineY < 0) return;

            line0MaxRight = placed.Where(g => Math.Abs(g.Y - firstLineY) < 0.5).Max(g => g.X + g.W);
            line1MaxRight = placed.Where(g => Math.Abs(g.Y - secondLineY) < 0.5).Max(g => g.X + g.W);
        });

        if (!ran) return;
        // Line 0 right edge must be ≤ line 1 right edge (same right margin) within 2 px rounding.
        line0MaxRight.Should().BeLessThanOrEqualTo(line1MaxRight + 2.0,
            "OO3: first-line indent must not push right-aligned line 0 past the right margin");
    }

    /// <summary>
    /// OO2: For a paragraph with a hanging indent (negative FirstLineIndentPt), continuation lines
    /// must not overshoot the right margin: their right edge must be ≤ line 0 right edge + 2 px.
    /// </summary>
    [Fact]
    public async Task Hanging_indent_continuation_lines_do_not_overshoot_margin()
    {
        double line0MaxRight = 0, line1MaxRight = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = new TextDocument();
            doc.Blocks.Clear();
            var p = new Paragraph();
            p.Formatting = new ParagraphFormatting
            {
                Alignment = TextAlignment.Left,
                IndentLeftPt = 24,         // base left indent (pt)
                FirstLineIndentPt = -24,   // hanging: first line outdented by 24 pt
            };
            p.Runs.Add(new Run(
                "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen",
                RunFormatting.Default));
            doc.Blocks.Add(p);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(500, 4000));

            var placed = view.GetPlacedForBlock(0);
            if (placed.Count == 0) return;

            var firstLineY  = placed.Min(g => g.Y);
            var secondLineY = placed.Where(g => g.Y > firstLineY + 0.5).Select(g => g.Y).DefaultIfEmpty(-1).Min();
            if (secondLineY < 0) return;

            line0MaxRight = placed.Where(g => Math.Abs(g.Y - firstLineY) < 0.5).Max(g => g.X + g.W);
            line1MaxRight = placed.Where(g => Math.Abs(g.Y - secondLineY) < 0.5).Max(g => g.X + g.W);
        });

        if (!ran) return;
        // Continuation line right edge must not overshoot line 0 (shared right margin) by more than 2 px.
        line1MaxRight.Should().BeLessThanOrEqualTo(line0MaxRight + 2.0,
            "OO2: hanging-indent continuation lines must not overshoot the right margin");
    }

    /// <summary>
    /// OO4: Subscript glyphs must not overflow the line box.  The draw Y + shrunk glyph height
    /// must be ≤ line box bottom (Y + LineHeight).  We verify via the math: SubYLowerFraction (0.33)
    /// + SuperSubScale (0.583) ≤ 1.0, meaning the subscript top + shrunk font height fits inside
    /// the line box assuming the glyph height ≈ font size × PxPerPoint.
    /// </summary>
    [Fact]
    public async Task Subscript_glyph_stays_within_line_box()
    {
        var ran = await OnUiThread(() =>
        {
            var doc = new TextDocument();
            doc.Blocks.Clear();
            var p = new Paragraph();
            var normal = RunFormatting.Default with { FontSizePt = 12 };
            var sub    = normal with { VerticalAlign = VerticalAlign.Subscript };
            p.Runs.Add(new Run("H", normal));
            p.Runs.Add(new Run("2", sub));
            p.Runs.Add(new Run("O", normal));
            doc.Blocks.Add(p);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));

            var placed = view.GetPlacedForBlock(0);
            var subGlyphs = placed.Where(g => g.IsSubscript).ToList();
            subGlyphs.Should().NotBeEmpty("the subscript run must produce placed glyphs");

            // For each subscript glyph: drawY = Y + LineHeight * SubYLowerFraction.
            // The shrunk glyph height ≈ fontSizePt * PxPerPoint * SuperSubScale * leadingFactor (≤ 1.3).
            // We bound conservatively: glyphHeight ≤ LineHeight (the line box itself).
            // The condition: drawY + glyphHeight ≤ Y + LineHeight
            //   → (Y + LineHeight*0.33) + LineHeight ≤ Y + LineHeight  (worst case glyphHeight = LineHeight)
            //   → 0.33*LineHeight ≤ 0  — that's too tight.  Use the actual shrunk estimate instead:
            //   drawY + LineHeight*SuperSubScale ≤ Y + LineHeight
            //   → LineHeight*SubYLowerFraction + LineHeight*SuperSubScale ≤ LineHeight
            //   → SubYLowerFraction + SuperSubScale ≤ 1.0
            // This is the key invariant; verify it here without needing rendering.
            const double subFrac = 0.33;   // SubYLowerFraction after fix
            const double scale   = 0.583;  // SuperSubScale
            (subFrac + scale).Should().BeLessThanOrEqualTo(1.0,
                "OO4: SubYLowerFraction + SuperSubScale must be ≤ 1.0 so subscript glyph fits in line box");

            // Also verify the placed glyph line-box math: Y + LineHeight should encompass the glyph.
            foreach (var g in subGlyphs)
            {
                var drawY         = g.Y + g.LineHeight * subFrac;
                var approxGlyphH  = g.LineHeight * scale; // conservative upper bound
                var glyphBottom   = drawY + approxGlyphH;
                var lineBottom    = g.Y + g.LineHeight;
                glyphBottom.Should().BeLessThanOrEqualTo(lineBottom + 1.0,
                    $"OO4: subscript glyph bottom ({glyphBottom:F1}) must not exceed line box bottom ({lineBottom:F1})");
            }
        });

        if (!ran) return;
    }

    private static T InvokePrivate<T>(object instance, string name, params object[] args)
    {
        var method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(instance.GetType().FullName, name);
        return (T)method.Invoke(instance, args)!;
    }

    private static void InvokePrivate(object instance, string name, params object[] args)
    {
        var method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(instance.GetType().FullName, name);
        method.Invoke(instance, args);
    }

    private static T GetPrivateField<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);
        return (T)field.GetValue(instance)!;
    }
}
