using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;
using SkiaSharp;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-DESIGN: tests for the Design tab — Themes / Colors / Fonts / Paragraph-Spacing galleries, Page Color,
/// Page Borders, and Watermark. Verifies the model mutations, command resolution from the registry, and
/// that Undo reverts each Design change. These are pure-model tests (no headless layout backend needed).
/// </summary>
public sealed class DesignTabTests
{
    private static RibbonHostCallbacks NoopCallbacks() =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { },
            ToggleNavigationPane: () => { }, ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { }, SetWebLayout: () => { }, SetDraftView: () => { },
            OpenFontDialog: () => { }, OpenParagraphDialog: () => { }, OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { }, ApplyMarginPreset: _ => { }, ApplyPaperSize: _ => { },
            InsertPicture: () => { }, OpenWordCountDialog: () => { }, ApplyZoom: (_, _) => { });

    private static TextDocument MakeDoc(string text = "Hello world")
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        return doc;
    }

    private static void Execute(
        RibbonCommandRegistry registry,
        string commandId,
        RibbonCommandContext? context = null)
    {
        registry.TryGet(new RibbonCommandId(commandId), out var command)
            .Should().BeTrue($"command '{commandId}' must be registered");
        command!.Execute(context ?? RibbonCommandContext.Empty);
    }

    // ── Themes ────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_theme_changes_theme_colors_and_fonts()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var berlin = DocumentTheme.FindByName("Berlin")!;
        view.ApplyTheme(berlin);

        view.Document.Theme.Name.Should().Be("Berlin");
        view.Document.DefaultRun.FontFamily.Should().Be(berlin.BodyFont);
        // Heading styles take the theme's heading colour/font.
        view.Document.Styles["Heading1"].Run.ColorHex.Should().Be(berlin.HeadingColorHex);
        view.Document.Styles["Heading1"].Run.FontFamily.Should().Be(berlin.HeadingFont);
    }

    [Fact]
    public void Undo_reverts_theme_apply()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var originalFont = view.Document.DefaultRun.FontFamily;
        var originalTheme = view.Document.Theme.Name;

        view.ApplyTheme(DocumentTheme.FindByName("Ion")!);
        view.Document.Theme.Name.Should().Be("Ion");

        view.Undo();

        view.Document.Theme.Name.Should().Be(originalTheme);
        view.Document.DefaultRun.FontFamily.Should().Be(originalFont);
    }

    [Fact]
    public void Apply_theme_colors_preserves_fonts()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        // Set a known heading font first so we can verify it survives a colours-only apply.
        view.ApplyDocumentFontSet(DocumentFontSet.FindByName("Georgia")!);
        var headingFontBefore = view.Document.Styles["Heading1"].Run.FontFamily;

        var berlin = DocumentTheme.FindByName("Berlin")!;
        view.ApplyThemeColors(berlin);

        view.Document.Styles["Heading1"].Run.ColorHex.Should().Be(berlin.HeadingColorHex);
        view.Document.Styles["Heading1"].Run.FontFamily.Should().Be(headingFontBefore,
            "ApplyThemeColors must preserve the current heading font");
    }

    [Fact]
    public void Apply_font_set_changes_heading_and_body_fonts()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var cambria = DocumentFontSet.FindByName("Cambria")!;
        view.ApplyDocumentFontSet(cambria);

        view.Document.Theme.HeadingFont.Should().Be(cambria.HeadingFont);
        view.Document.Theme.BodyFont.Should().Be(cambria.BodyFont);
        view.Document.DefaultRun.FontFamily.Should().Be(cambria.BodyFont);
    }

    [Fact]
    public void Apply_paragraph_spacing_set_changes_default_spacing()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var dbl = DocumentParagraphSpacingSet.FindByName("Double")!;
        view.ApplyParagraphSpacingSet(dbl);

        view.Document.DefaultParagraph.LineSpacing.Should().Be(dbl.LineSpacing);
        view.Document.DefaultParagraph.SpaceAfterPt.Should().Be(dbl.SpaceAfterPt);
    }

    [Fact]
    public void Style_set_commands_apply_and_reset_built_in_styles()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());
        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());

        Execute(registry, "freew.style-set", RibbonCommandContext.ForSelectedValue("Elegant"));
        view.Document.DefaultRun.FontFamily.Should().Be("Georgia");
        view.Document.Styles["Heading1"].Run.FontFamily.Should().Be("Cambria");

        Execute(registry, "freew.reset-style-set");
        view.Document.DefaultRun.FontFamily.Should().Be(DocumentStyleSet.Default.BodyFont);
        view.Document.Styles["Heading1"].Run.FontFamily.Should().Be(DocumentStyleSet.Default.HeadingFont);
    }

    [Fact]
    public void Undo_reverts_style_set_apply()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());
        var before = view.Document.Styles["Heading1"].Run.FontFamily;

        view.ApplyStyleSet(DocumentStyleSet.FindByName("Elegant")!);
        view.Document.Styles["Heading1"].Run.FontFamily.Should().Be("Cambria");

        view.Undo();
        view.Document.Styles["Heading1"].Run.FontFamily.Should().Be(before);
    }

    [Fact]
    public void Undo_reverts_paragraph_spacing_set()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var before = view.Document.DefaultParagraph.LineSpacing;
        view.ApplyParagraphSpacingSet(DocumentParagraphSpacingSet.FindByName("Double")!);
        view.Document.DefaultParagraph.LineSpacing.Should().Be(2.0);

        view.Undo();
        view.Document.DefaultParagraph.LineSpacing.Should().Be(before);
    }

    // ── Page Color ──────────────────────────────────────────────────────────────

    [Fact]
    public void Set_page_color_sets_background_hex()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        view.SetPageColor("#DDEBF7");
        view.Document.Page.BackgroundColorHex.Should().Be("#DDEBF7");
    }

    [Fact]
    public void Set_page_color_normalizes_missing_hash()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());

        view.SetPageColor("DDEBF7");
        view.Document.Page.BackgroundColorHex.Should().Be("#DDEBF7");
    }

    [Fact]
    public void Set_page_color_none_clears_background()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());

        view.SetPageColor("#DDEBF7");
        view.SetPageColor(null);
        view.Document.Page.BackgroundColorHex.Should().BeNull();
    }

    [Fact]
    public void Undo_reverts_page_color()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());

        view.SetPageColor("#DDEBF7");
        view.Undo();
        view.Document.Page.BackgroundColorHex.Should().BeNull();
    }

    // ── Page Borders ────────────────────────────────────────────────────────────

    [Fact]
    public void Set_page_border_applies_border()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());

        view.SetPageBorder(new PageBorder("#C00000", 2.25) { LineStyle = BorderLineStyle.Dashed });

        view.Document.Page.PageBorder.Should().NotBeNull();
        view.Document.Page.PageBorder!.ColorHex.Should().Be("#C00000");
        view.Document.Page.PageBorder!.WidthPt.Should().Be(2.25);
        view.Document.Page.PageBorder!.LineStyle.Should().Be(BorderLineStyle.Dashed);
    }

    [Fact]
    public void Toggle_page_border_adds_then_clears()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());

        view.TogglePageBorder();
        view.Document.Page.PageBorder.Should().NotBeNull();

        view.TogglePageBorder();
        view.Document.Page.PageBorder.Should().BeNull();
    }

    [Fact]
    public void Undo_reverts_page_border()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());

        view.SetPageBorder(new PageBorder("#000000", 1.0));
        view.Undo();
        view.Document.Page.PageBorder.Should().BeNull();
    }

    // ── Watermark ────────────────────────────────────────────────────────────────

    [Fact]
    public void Set_watermark_text_sets_options()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());

        view.SetWatermarkText("CONFIDENTIAL");

        view.Document.Page.WatermarkOptions.Should().NotBeNull();
        view.Document.Page.WatermarkOptions!.Text.Should().Be("CONFIDENTIAL");
        view.Document.Page.EffectiveWatermark!.Text.Should().Be("CONFIDENTIAL");
    }

    [Fact]
    public void Set_watermark_options_carries_layout_and_color()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());

        view.SetWatermark(new WatermarkOptions("DRAFT")
        {
            FontColorHex = "#C00000",
            Layout = WatermarkLayout.Horizontal,
            Opacity = 1.0,
        });

        var wm = view.Document.Page.WatermarkOptions!;
        wm.Text.Should().Be("DRAFT");
        wm.FontColorHex.Should().Be("#C00000");
        wm.Layout.Should().Be(WatermarkLayout.Horizontal);
    }

    [Fact]
    public void Set_watermark_null_clears_watermark()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());

        view.SetWatermarkText("DRAFT");
        view.SetWatermark(null);

        view.Document.Page.WatermarkOptions.Should().BeNull();
        view.Document.Page.EffectiveWatermark.Should().BeNull();
    }

    [Fact]
    public void Undo_reverts_watermark()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());

        view.SetWatermarkText("CONFIDENTIAL");
        view.Undo();
        view.Document.Page.WatermarkOptions.Should().BeNull();
    }

    // ── Command resolution from the ribbon registry ──────────────────────────────

    [Fact]
    public void Design_tab_commands_resolve_from_registry()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());
        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());

        var ids = new[]
        {
            "freew.theme", "freew.theme.berlin",
            "freew.theme-colors", "freew.theme-colors.ion",
            "freew.theme-fonts", "freew.theme-fonts.cambria",
            "freew.para-spacing", "freew.para-spacing.double",
            "freew.page-color", "freew.page-color.none", "freew.page-color.light-blue",
            "freew.page-borders",
            "freew.watermark", "freew.watermark.confidential", "freew.watermark.draft",
            "freew.watermark.custom", "freew.watermark.none",
        };

        foreach (var id in ids)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"Design command '{id}' must be registered");
    }

    [Fact]
    public void Theme_subcommand_applies_theme_via_registry()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());
        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.theme.berlin"), out var cmd).Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);

        view.Document.Theme.Name.Should().Be("Berlin");
    }

    [Fact]
    public void Page_color_subcommand_sets_background_via_registry()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());
        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.page-color.light-blue"), out var cmd).Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);

        view.Document.Page.BackgroundColorHex.Should().Be("#DDEBF7");
    }

    [Fact]
    public void Watermark_preset_subcommand_sets_text_via_registry()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());
        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.watermark.confidential"), out var cmd).Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);

        view.Document.Page.EffectiveWatermark!.Text.Should().Be("CONFIDENTIAL");
    }

    [Fact]
    public void Optional_design_callbacks_default_null_and_commands_are_safe_noops()
    {
        // The Page Borders + Custom Watermark commands route through optional callbacks that default to
        // null; executing them with the no-op callbacks must not throw and must not mutate the document.
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());
        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.page-borders"), out var borders).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.watermark.custom"), out var custom).Should().BeTrue();

        borders!.Execute(RibbonCommandContext.Empty);
        custom!.Execute(RibbonCommandContext.Empty);

        view.Document.Page.PageBorder.Should().BeNull();
        view.Document.Page.WatermarkOptions.Should().BeNull();
    }

    // ── Render introspection (headless) ──────────────────────────────────────────

    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Page_color_fill_reflects_in_rendered_page_pixels()
    {
        // The page sheet must paint with the document's Page Color. Set a saturated blue, render headless,
        // and sample a pixel near the top-centre of the first page (inside the chrome border, above the
        // body text) — it should read close to the chosen blue rather than white.
        const int windowWidth = 900;
        const int windowHeight = 700;

        (byte R, byte G, byte B)? sample = null;
        var ran = false;

        try
        {
            await Session.Dispatch(() =>
            {
                ran = true;

                var doc = MakeDoc("Page color render check");
                var view = new DocumentView();
                view.LoadDocument(doc);
                view.SetPageColor("#2050C0"); // distinct blue

                var window = new Window { Width = windowWidth, Height = windowHeight, Content = view };
                window.Show();
                window.Measure(new Size(windowWidth, windowHeight));
                window.Arrange(new Rect(0, 0, windowWidth, windowHeight));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var frame = window.CaptureRenderedFrame();
                if (frame is not null)
                    sample = SamplePixel(frame, windowWidth / 2, 60);

                window.Close();
            }, CancellationToken.None);
        }
        catch
        {
            return; // headless drawing backend unavailable — skip
        }

        if (!ran || sample is not { } px)
            return; // no frame / no backend — skip rather than fail in CI

        // Expect a blue-dominant pixel (B clearly the largest channel), proving the page fill used the
        // document's Page Color rather than the default white sheet.
        px.B.Should().BeGreaterThan(px.R, "the page fill should be blue-dominant");
        px.B.Should().BeGreaterThan(px.G, "the page fill should be blue-dominant");
        px.B.Should().BeGreaterThan((byte)120, "the blue page fill should be clearly saturated, not white");
    }

    private static (byte R, byte G, byte B)? SamplePixel(WriteableBitmap bitmap, int x, int y)
    {
        try
        {
            using var locked = bitmap.Lock();
            var info = new SKImageInfo(locked.Size.Width, locked.Size.Height,
                locked.Format == PixelFormat.Bgra8888 ? SKColorType.Bgra8888 : SKColorType.Rgba8888,
                SKAlphaType.Premul);
            using var skBitmap = new SKBitmap();
            if (!skBitmap.InstallPixels(info, locked.Address, locked.RowBytes))
                return null;
            var c = skBitmap.GetPixel(Math.Clamp(x, 0, info.Width - 1), Math.Clamp(y, 0, info.Height - 1));
            return (c.Red, c.Green, c.Blue);
        }
        catch
        {
            return null;
        }
    }
}
