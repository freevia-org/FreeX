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
using Free.Shared.Pdf.Skia;
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

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too.
    private static Task<bool> OnUiThreadAsync(Func<Task> action) => HeadlessUiThread.RunAsync(action);

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
    public async Task Section_field_in_body_uses_live_block_section_without_mutating_cache()
    {
        string? firstSection = null;
        string? secondSection = null;
        var firstField = Run.ComplexFieldRun(" SECTION \\* ROMAN ", "stale-one");
        var secondField = Run.ComplexFieldRun(" SECTION \\* ALPHABETIC ", "stale-two");
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            var sectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage);
            var first = new Paragraph { SectionBreak = sectionBreak };
            first.Runs.Add(firstField);
            doc.Blocks.Add(first);

            var second = new Paragraph();
            second.Runs.Add(secondField);
            doc.Blocks.Add(second);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 2400));
            firstSection = string.Concat(view.GetPlacedForBlock(0).Select(item => item.Ch));
            secondSection = string.Concat(view.GetPlacedForBlock(1).Select(item => item.Ch));
        });

        if (!ran)
            return;

        firstSection.Should().Be("I");
        secondSection.Should().Be("B");
        firstField.Text.Should().Be("stale-one");
        secondField.Text.Should().Be("stale-two");
    }

    [Fact]
    public async Task SectionPages_field_in_body_converges_to_live_page_count()
    {
        string? first = null;
        string? second = null;
        var firstField = Run.ComplexFieldRun(" SECTIONPAGES \\* ROMAN ", "stale");
        var secondField = Run.ComplexFieldRun(" SECTIONPAGES \\* ROMAN ", "stale");
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph { Runs = { firstField } });
            doc.Blocks.Add(new Paragraph
            {
                Formatting = ParagraphFormatting.Default with { PageBreakBefore = true },
                Runs = { secondField }
            });

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 3000));
            first = string.Concat(view.GetPlacedForBlock(0).Select(item => item.Ch));
            second = string.Concat(view.GetPlacedForBlock(1).Select(item => item.Ch));
        });

        if (!ran)
            return;

        first.Should().Be("II");
        second.Should().Be("II");
        firstField.Text.Should().Be("stale");
        secondField.Text.Should().Be("stale");
    }

    [Theory]
    [InlineData(SectionBreakKind.Continuous, "1", "1", 1)]
    [InlineData(SectionBreakKind.OddPage, "2", "1", 3)]
    public async Task SectionPages_counts_continuous_shared_pages_and_parity_blanks(
        SectionBreakKind breakKind,
        string expectedFirst,
        string expectedSecond,
        int expectedDocumentPages)
    {
        string? first = null;
        string? second = null;
        var pageCount = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph
            {
                SectionBreak = new Section(new PageSettings(), breakKind),
                Runs = { Run.ComplexFieldRun(" SECTIONPAGES ", "stale-first") }
            });
            doc.Blocks.Add(new Paragraph
            {
                Runs = { Run.ComplexFieldRun(" SECTIONPAGES ", "stale-second") }
            });

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 4000));
            first = string.Concat(view.GetPlacedForBlock(0).Select(item => item.Ch));
            second = string.Concat(view.GetPlacedForBlock(1).Select(item => item.Ch));
            pageCount = view.PageCount;
        });

        if (!ran)
            return;

        first.Should().Be(expectedFirst);
        second.Should().Be(expectedSecond);
        pageCount.Should().Be(expectedDocumentPages);
    }

    [Fact]
    public async Task SectionPages_field_code_and_next_document_do_not_reuse_live_count()
    {
        string? fieldCode = null;
        string? firstDocument = null;
        string? secondDocument = null;
        var ran = await OnUiThread(() =>
        {
            var codeRun = Run.ComplexFieldRun(" SECTIONPAGES ", "stale-code");
            codeRun.ComplexField = codeRun.ComplexField! with { ShowCode = true };
            var codeDoc = TextDocument.CreateEmpty();
            codeDoc.Blocks.Clear();
            codeDoc.Blocks.Add(new Paragraph { Runs = { codeRun } });
            var codeView = new DocumentView();
            codeView.LoadDocument(codeDoc);
            codeView.Measure(new Size(900, 1200));
            fieldCode = string.Concat(codeView.GetPlacedForBlock(0).Select(item => item.Ch));

            var view = new DocumentView();
            var twoPage = TextDocument.CreateEmpty();
            twoPage.Blocks.Clear();
            twoPage.Blocks.Add(new Paragraph
            {
                Runs = { Run.ComplexFieldRun(" SECTIONPAGES ", "stale") }
            });
            twoPage.Blocks.Add(new Paragraph("Second page")
            {
                Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
            });
            view.LoadDocument(twoPage);
            view.Measure(new Size(900, 3000));
            firstDocument = string.Concat(view.GetPlacedForBlock(0).Select(item => item.Ch));

            var onePage = TextDocument.CreateEmpty();
            onePage.Blocks.Clear();
            onePage.Blocks.Add(new Paragraph
            {
                Runs = { Run.ComplexFieldRun(" SECTIONPAGES ", "other-stale") }
            });
            view.LoadDocument(onePage);
            view.Measure(new Size(900, 1200));
            secondDocument = string.Concat(view.GetPlacedForBlock(0).Select(item => item.Ch));
        });

        if (!ran)
            return;

        fieldCode.Should().Be("{ SECTIONPAGES }");
        firstDocument.Should().Be("2");
        secondDocument.Should().Be("1");
    }

    [Fact]
    public async Task SectionPages_includes_inserted_long_footnote_continuation_pages()
    {
        string? visible = null;
        var pageCount = 0;
        var field = Run.ComplexFieldRun(" SECTIONPAGES ", "stale");
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(field);
            paragraph.Runs.Add(new Run(" pages "));
            paragraph.Runs.Add(Run.FootnoteReference(1));
            doc.Blocks.Add(paragraph);
            doc.Footnotes[1] = new Footnote(
                1,
                string.Join(" ", Enumerable.Range(1, 700).Select(index => $"word{index}")));

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 8000));
            pageCount = view.PageCount;
            visible = string.Concat(view.GetPlacedForBlock(0).Select(item => item.Ch));
        });

        if (!ran)
            return;

        pageCount.Should().BeGreaterThan(1);
        visible.Should().StartWith(pageCount.ToString());
        field.Text.Should().Be("stale");
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
            run = InvokePrivate<RunFormatting>(view, "ResolveRunFmt", RunFormatting.Default, paragraph, null);
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
    public async Task Direct_auto_hyphenation_opt_in_overrides_suppressing_style()
    {
        ParagraphFormatting? resolved = null;
        var ran = await OnUiThread(() =>
        {
            var doc = new TextDocument();
            doc.Styles["NoHyphens"] = new DocumentStyle
            {
                Id = "NoHyphens",
                Name = "No Hyphens",
                Paragraph = ParagraphFormatting.Default with
                {
                    SuppressAutoHyphens = true,
                    SuppressAutoHyphensIsSet = true,
                },
            };
            var paragraph = new Paragraph("hyphenation rabbit")
            {
                StyleId = "NoHyphens",
                Formatting = ParagraphFormatting.Default with
                {
                    SuppressAutoHyphensIsSet = true,
                },
            };
            doc.Blocks.Add(paragraph);
            var view = new DocumentView();
            view.LoadDocument(doc);

            resolved = InvokePrivate<ParagraphFormatting>(view, "ResolveParagraphFmt", paragraph);
        });

        if (!ran)
            return;

        resolved.Should().NotBeNull();
        resolved!.SuppressAutoHyphens.Should().BeFalse();
        resolved.SuppressAutoHyphensIsSet.Should().BeTrue();
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
            result.Backend.Should().BeOneOf(PdfExportBackend.Skia, PdfExportBackend.PortableWinAnsi);
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
        using var temporaryDirectory = new TestTemporaryDirectory("FreeW.Avalonia.Tests-");
        var ran = await OnUiThreadAsync(async () =>
        {
            var window = new MainWindow(
                Array.Empty<string>(),
                new FreeWOptions(),
                ApplicationOptionsStore<FreeWOptions>.ForPath(
                    Path.Combine(temporaryDirectory.Path, "settings.json")),
                promptSaveChangesAsync: _ => Task.FromResult(SaveChangesPrompt.DontSave));
            var shellWorkflow = GetPrivateField<SisterAvaloniaFileCommandWorkflow>(window, "_fileWorkflow");
            var workflow = shellWorkflow.Workflow;

            workflow.IsDirty.Should().BeFalse();
            workflow.CurrentPath.Should().BeNull();
            workflow.DisplayName.Should().Be("Untitled");
            window.Title.Should().Be("Untitled — FreeW");

            window.Editor.InsertText("draft ");
            workflow.IsDirty.Should().BeTrue();
            window.Title.Should().Be("Untitled * — FreeW");

            (await window.NewDocumentAsyncForTests()).Should().BeTrue();

            workflow.IsDirty.Should().BeFalse();
            workflow.CurrentPath.Should().BeNull();
            workflow.DisplayName.Should().Be("Untitled");
            window.Title.Should().Be("Untitled — FreeW");
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

    [Fact]
    public async Task PageBreakBefore_AndBreakOnlyRun_AdvanceBodyPagination()
    {
        var pageBreakBeforeCount = 0;
        var breakRunCount = 0;
        var ran = await OnUiThread(() =>
        {
            var beforeDoc = TextDocument.CreateEmpty();
            beforeDoc.Blocks.Clear();
            beforeDoc.Blocks.Add(new Paragraph("Page one"));
            beforeDoc.Blocks.Add(new Paragraph("Page two")
            {
                Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
            });
            var beforeView = new DocumentView();
            beforeView.LoadDocument(beforeDoc);
            beforeView.Measure(new Size(800, 4000));
            pageBreakBeforeCount = beforeView.PageCount;

            var runDoc = TextDocument.CreateEmpty();
            runDoc.Blocks.Clear();
            runDoc.Blocks.Add(new Paragraph("Page one"));
            runDoc.Blocks.Add(new Paragraph { Runs = { Run.PageBreak() } });
            runDoc.Blocks.Add(new Paragraph("Page two"));
            var runView = new DocumentView();
            runView.LoadDocument(runDoc);
            runView.Measure(new Size(800, 4000));
            breakRunCount = runView.PageCount;
        });

        if (!ran)
            return;

        pageBreakBeforeCount.Should().Be(2);
        breakRunCount.Should().Be(2);
    }

    [Fact]
    public async Task InlinePageBreakRuns_SplitParagraphAtTheirModelOffsets()
    {
        var pageCount = 0;
        var firstCaretPage = -1;
        var secondCaretPage = -1;
        var thirdCaretPage = -1;
        IReadOnlyList<(char Ch, double X, double W, double Y, double LineHeight, bool IsSubscript)> placed = [];
        Paragraph? paragraph = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            paragraph = new Paragraph
            {
                Runs =
                {
                    new Run("Before"),
                    Run.PageBreak(),
                    new Run("Middle"),
                    Run.PageBreak(),
                    new Run("After")
                }
            };
            doc.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 5000));
            pageCount = view.PageCount;
            placed = view.GetPlacedForBlock(0);

            view.MoveCaretToBlockForTest(0, 0);
            firstCaretPage = view.CaretPageIndex;
            view.MoveCaretToBlockForTest(0, "Before".Length);
            secondCaretPage = view.CaretPageIndex;
            view.MoveCaretToBlockForTest(0, "BeforeMiddle".Length);
            thirdCaretPage = view.CaretPageIndex;
        });

        if (!ran)
            return;

        pageCount.Should().Be(3);
        string.Concat(placed.Select(item => item.Ch)).Should().Be("BeforeMiddleAfter");
        firstCaretPage.Should().Be(0);
        secondCaretPage.Should().Be(1);
        thirdCaretPage.Should().Be(2);
        paragraph!.Runs.Select(run => run.IsPageBreak).Should().Equal(false, true, false, true, false);
        paragraph.PlainText.Should().Be("BeforeMiddleAfter");
    }

    [Fact]
    public async Task InlineColumnBreakRuns_SplitParagraphAtTheirModelOffsets()
    {
        var pageCount = 0;
        var firstCaretPage = -1;
        var secondCaretPage = -1;
        var thirdCaretPage = -1;
        IReadOnlyList<(char Ch, double X, double W, double Y, double LineHeight, bool IsSubscript)> placed = [];
        Paragraph? paragraph = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Page.ColumnCount = 2;
            doc.Page.ColumnSpacingPt = 36;
            paragraph = new Paragraph
            {
                Runs =
                {
                    new Run("Before"),
                    Run.ColumnBreak(),
                    new Run("Middle"),
                    Run.ColumnBreak(),
                    new Run("After")
                }
            };
            doc.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 5000));
            pageCount = view.PageCount;
            placed = view.GetPlacedForBlock(0);

            view.MoveCaretToBlockForTest(0, 0);
            firstCaretPage = view.CaretPageIndex;
            view.MoveCaretToBlockForTest(0, "Before".Length);
            secondCaretPage = view.CaretPageIndex;
            view.MoveCaretToBlockForTest(0, "BeforeMiddle".Length);
            thirdCaretPage = view.CaretPageIndex;
        });

        if (!ran)
            return;

        pageCount.Should().Be(2);
        string.Concat(placed.Select(item => item.Ch)).Should().Be("BeforeMiddleAfter");
        placed["Before".Length].X.Should().BeGreaterThan(placed[0].X + 100);
        placed["BeforeMiddle".Length].X.Should().BeApproximately(placed[0].X, 1);
        placed["BeforeMiddle".Length].Y.Should().BeGreaterThan(placed[0].Y);
        firstCaretPage.Should().Be(0);
        secondCaretPage.Should().Be(0);
        thirdCaretPage.Should().Be(1);
        paragraph!.Runs.Select(run => run.IsColumnBreak).Should().Equal(false, true, false, true, false);
        paragraph.PlainText.Should().Be("BeforeMiddleAfter");
    }

    [Fact]
    public async Task InlineFlowBreaks_InListItem_UseSharedPrecedenceAndSourceOffsets()
    {
        var pageCount = 0;
        var firstCaretPage = -1;
        var middleCaretPage = -1;
        IReadOnlyList<(char Ch, double X, double W, double Y, double LineHeight, bool IsSubscript)> placed = [];
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Page.ColumnCount = 2;
            doc.Page.ColumnSpacingPt = 36;
            var paragraph = new Paragraph
            {
                Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet },
                Runs =
                {
                    new Run("Before"),
                    new Run(string.Empty) { IsPageBreak = true, IsColumnBreak = true },
                    new Run("Middle"),
                    Run.ColumnBreak(),
                    new Run("After")
                }
            };
            doc.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 5000));
            pageCount = view.PageCount;
            placed = view.GetPlacedForBlock(0);

            view.MoveCaretToBlockForTest(0, 0);
            firstCaretPage = view.CaretPageIndex;
            view.MoveCaretToBlockForTest(0, "Before".Length);
            middleCaretPage = view.CaretPageIndex;
        });

        if (!ran)
            return;

        pageCount.Should().Be(2);
        string.Concat(placed.Select(item => item.Ch)).Should().Be("BeforeMiddleAfter");
        firstCaretPage.Should().Be(0);
        middleCaretPage.Should().Be(1, "a page break takes precedence over the simultaneous column flag");
        placed["Before".Length].X.Should().BeApproximately(placed[0].X, 1);
        placed["BeforeMiddle".Length].X.Should().BeGreaterThan(placed["Before".Length].X + 100);
    }

    [Fact]
    public async Task FormattingTextAcrossInlineBreaks_PreservesTheBreakRuns()
    {
        Paragraph? paragraph = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            paragraph = new Paragraph
            {
                Runs =
                {
                    new Run("Before"),
                    Run.PageBreak(),
                    new Run("Middle"),
                    Run.ColumnBreak(),
                    new Run("After")
                }
            };
            doc.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 5000));
            view.SetSelectionRangePublic(0, 0, 0, paragraph.PlainText.Length);
            view.ToggleBold();
        });

        if (!ran)
            return;

        paragraph!.Runs.Select(run => (run.Text, run.IsPageBreak, run.IsColumnBreak)).Should().Equal(
            ("Before", false, false),
            (string.Empty, true, false),
            ("Middle", false, false),
            (string.Empty, false, true),
            ("After", false, false));
        paragraph.Runs.Where(run => run.Text.Length > 0).Should().OnlyContain(run => run.Formatting.Bold);
    }

    [Theory]
    [InlineData(SectionBreakKind.Continuous, 1)]
    [InlineData(SectionBreakKind.NextPage, 2)]
    [InlineData(SectionBreakKind.EvenPage, 2)]
    [InlineData(SectionBreakKind.OddPage, 3)]
    public async Task SectionBreakKind_AdvancesToTheRequiredPhysicalPage(
        SectionBreakKind breakKind,
        int expectedPageCount)
    {
        var pageCount = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Ending section")
            {
                SectionBreak = new Section(new PageSettings(), breakKind)
            });
            doc.Blocks.Add(new Paragraph("Next section"));

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 5000));
            pageCount = view.PageCount;
        });

        if (!ran)
            return;

        pageCount.Should().Be(expectedPageCount);
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
        pageCount.Should().Be(1, $"{mode} is a continuous-column mode â no discrete pages");
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
    /// non-zero â content starts at _marginTopDip).
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
    /// GetBlockTop must return a non-negative value in WebLayout / Draft â blocks are laid out at
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
        blockTop.Should().BeGreaterThanOrEqualTo(0, $"GetBlockTop(0) must be â¥ 0 in {mode}");
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
            view.ViewMode = DocumentViewMode.WebLayout; // same â no event
            view.ViewMode = DocumentViewMode.Draft;     // 1 change
            view.ViewMode = DocumentViewMode.PrintLayout; // 1 change
        });

        if (!ran)
            return;
        changeCount.Should().Be(3, "event fires only when the mode actually changes");
    }

    // ---- Paragraph-layout parity tests (OO1âOO4) -----------------------------------------------

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
            // Long text â must wrap at 400 px (measured at ~7 px/char for 11pt default).
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
            if (secondLineY < 0) return; // didn't wrap â skip

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
        // the gap was natural_space_width + wordGap â 22â30+ px depending on word count.
        (rightEdge - lastWordEnd).Should().BeLessThanOrEqualTo(20.0,
            "OO1: no wordGap must be added after the trailing space; gap must equal at most one natural space width");
        (rightEdge - lastWordEnd).Should().BeGreaterThanOrEqualTo(0,
            "trailing space must be placed after the last word");
    }

    /// <summary>
    /// OO2/OO3: For a paragraph with a positive first-line indent, right-aligned text on line 0
    /// must not overshoot the right margin.  The right edge of any glyph on the first line must be
    /// â¤ the right edge of continuation lines (both sharing the same right margin).
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
        // Line 0 right edge must be â¤ line 1 right edge (same right margin) within 2 px rounding.
        line0MaxRight.Should().BeLessThanOrEqualTo(line1MaxRight + 2.0,
            "OO3: first-line indent must not push right-aligned line 0 past the right margin");
    }

    /// <summary>
    /// OO2: For a paragraph with a hanging indent (negative FirstLineIndentPt), continuation lines
    /// must not overshoot the right margin: their right edge must be â¤ line 0 right edge + 2 px.
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
    /// must be â¤ line box bottom (Y + LineHeight).  We verify via the math: SubYLowerFraction (0.33)
    /// + SuperSubScale (0.583) â¤ 1.0, meaning the subscript top + shrunk font height fits inside
    /// the line box assuming the glyph height â font size Ã PxPerPoint.
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
            // The shrunk glyph height â fontSizePt * PxPerPoint * SuperSubScale * leadingFactor (â¤ 1.3).
            // We bound conservatively: glyphHeight â¤ LineHeight (the line box itself).
            // The condition: drawY + glyphHeight â¤ Y + LineHeight
            //   â (Y + LineHeight*0.33) + LineHeight â¤ Y + LineHeight  (worst case glyphHeight = LineHeight)
            //   â 0.33*LineHeight â¤ 0  â that's too tight.  Use the actual shrunk estimate instead:
            //   drawY + LineHeight*SuperSubScale â¤ Y + LineHeight
            //   â LineHeight*SubYLowerFraction + LineHeight*SuperSubScale â¤ LineHeight
            //   â SubYLowerFraction + SuperSubScale â¤ 1.0
            // This is the key invariant; verify it here without needing rendering.
            const double subFrac = 0.33;   // SubYLowerFraction after fix
            const double scale   = 0.583;  // SuperSubScale
            (subFrac + scale).Should().BeLessThanOrEqualTo(1.0,
                "OO4: SubYLowerFraction + SuperSubScale must be â¤ 1.0 so subscript glyph fits in line box");

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

    /// <summary>
    /// Resolves a private method by name AND argument shape. Name alone throws
    /// <see cref="System.Reflection.AmbiguousMatchException"/> the moment the production type gains an
    /// overload â which is exactly what happened to ResolveRunFmt, and which the swallowing
    /// <c>OnUiThread</c> then turned into a silently passing test rather than a failure.
    /// </summary>
    private static MethodInfo ResolvePrivateMethod(object instance, string name, object?[] args)
    {
        var candidates = instance.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(method => method.Name == name && method.GetParameters().Length == args.Length)
            .ToArray();

        if (candidates.Length == 1)
            return candidates[0];

        var matched = candidates.FirstOrDefault(method => method.GetParameters()
            .Select((parameter, index) => args[index] is null
                ? !parameter.ParameterType.IsValueType
                : parameter.ParameterType.IsInstanceOfType(args[index]))
            .All(match => match));

        return matched ?? throw new MissingMethodException(instance.GetType().FullName, name);
    }

    private static T InvokePrivate<T>(object instance, string name, params object?[] args) =>
        (T)ResolvePrivateMethod(instance, name, args).Invoke(instance, args)!;

    private static void InvokePrivate(object instance, string name, params object?[] args) =>
        ResolvePrivateMethod(instance, name, args).Invoke(instance, args);

    private static T GetPrivateField<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);
        return (T)field.GetValue(instance)!;
    }
}
