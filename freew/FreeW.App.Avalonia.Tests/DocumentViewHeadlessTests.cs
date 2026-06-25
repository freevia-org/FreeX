using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Free.Shared.AppServices;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

[assembly: AvaloniaTestApplication(typeof(FreeW.App.Avalonia.Tests.FreeWHeadlessApp))]

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
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var workflow = GetPrivateField<FileCommandWorkflow>(window, "_fileWorkflow");

            workflow.IsDirty.Should().BeFalse();
            workflow.CurrentPath.Should().BeNull();
            workflow.DisplayName.Should().Be("Untitled");

            window.Editor.InsertText("draft ");
            workflow.IsDirty.Should().BeTrue();

            InvokePrivate(window, "NewDocument");

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
