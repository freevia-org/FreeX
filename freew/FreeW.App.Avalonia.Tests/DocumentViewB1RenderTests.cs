using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Tests for Wave B1 render features in DocumentView:
/// superscript/subscript, text highlight, justify alignment, paragraph
/// spacing (before/after), paragraph indents (left/right/first-line),
/// and line-spacing rules — plus the corresponding ribbon commands.
/// </summary>
public sealed class DocumentViewB1RenderTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    // ── Helper: build a single-paragraph doc with a given run formatting ────────

    private static TextDocument DocWithRun(string text, RunFormatting fmt)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run(text, fmt));
        doc.Blocks.Add(p);
        return doc;
    }

    private static TextDocument DocWithParagraphFmt(string text, ParagraphFormatting pf)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph { Formatting = pf };
        p.Runs.Add(new Run(text, RunFormatting.Default));
        doc.Blocks.Add(p);
        return doc;
    }

    // ── Command registry: all B1 commands must be wired ────────────────────────

    [Fact]
    public void B1_commands_are_all_registered_in_the_registry()
    {
        var callbacks = new FreeWRibbonHostExecutionPorts(
            () => { }, () => { }, () => { }, () => { }, () => { },
            () => { }, () => { }, () => { }, () => { }, () => { },
            () => { }, () => { }, () => { }, () => { }, () => { },
            () => { }, () => { }, () => { }, _ => { }, _ => { }, () => { }, () => { }, (_, _) => { });
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), callbacks);

        var expected = new[]
        {
            "freew.superscript", "freew.subscript", "freew.highlight",
            "freew.align-justify",
            "freew.space-before", "freew.space-after",
            "freew.line-spacing-1", "freew.line-spacing-115",
            "freew.line-spacing-15", "freew.line-spacing-2",
        };

        foreach (var id in expected)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"command '{id}' should be wired in the registry");
    }

    [Fact]
    public void B1_ribbon_definition_includes_all_new_paragraph_controls()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var home = definition.FindTab("home");
        home.Should().NotBeNull();

        var ids = home!.Groups
            .SelectMany(g => g.Controls)
            .Select(c => c switch
            {
                RibbonButton b      => b.CommandId.Value,
                RibbonToggleButton t => t.CommandId.Value,
                RibbonComboBox combo => combo.CommandId.Value,
                RibbonDropdown dropdown => dropdown.CommandId.Value,
                _ => null
            })
            .Where(v => v is not null)
            .ToList();

        ids.Should().Contain("freew.superscript");
        ids.Should().Contain("freew.subscript");
        ids.Should().Contain("freew.highlight");
        ids.Should().Contain("freew.align-justify");
        ids.Should().Contain("freew.line-spacing");
    }

    // ── Superscript / Subscript model mutation + render ─────────────────────────

    [Fact]
    public async Task ToggleSuperscript_sets_VerticalAlign_Superscript_on_paragraph()
    {
        VerticalAlign? va = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(DocWithRun("Hello", RunFormatting.Default));
            view.Measure(new Size(800, 2000));
            view.SelectAll();
            view.ToggleSuperscript();
            var p = (Paragraph)view.Document.Blocks[0];
            va = p.Runs[0].Formatting.VerticalAlign;
        });

        if (!ran) return;
        va.Should().Be(VerticalAlign.Superscript, "ToggleSuperscript should set VerticalAlign to Superscript");
    }

    [Fact]
    public async Task ToggleSuperscript_clears_when_all_already_superscript()
    {
        VerticalAlign? va = null;
        var ran = await OnUiThread(() =>
        {
            var fmt = RunFormatting.Default with { VerticalAlign = VerticalAlign.Superscript };
            var view = new DocumentView();
            view.LoadDocument(DocWithRun("Hello", fmt));
            view.Measure(new Size(800, 2000));
            view.SelectAll();
            view.ToggleSuperscript(); // should clear it
            var p = (Paragraph)view.Document.Blocks[0];
            va = p.Runs[0].Formatting.VerticalAlign;
        });

        if (!ran) return;
        va.Should().Be(VerticalAlign.Baseline, "toggling superscript again should reset to Baseline");
    }

    [Fact]
    public async Task ToggleSubscript_sets_VerticalAlign_Subscript_on_paragraph()
    {
        VerticalAlign? va = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(DocWithRun("CO2", RunFormatting.Default));
            view.Measure(new Size(800, 2000));
            view.SelectAll();
            view.ToggleSubscript();
            var p = (Paragraph)view.Document.Blocks[0];
            va = p.Runs[0].Formatting.VerticalAlign;
        });

        if (!ran) return;
        va.Should().Be(VerticalAlign.Subscript, "ToggleSubscript should set VerticalAlign to Subscript");
    }

    [Fact]
    public async Task Superscript_run_lays_out_at_same_X_as_baseline_but_layout_succeeds()
    {
        // The render test: a superscript run must produce placed glyphs (layout does not crash or skip them).
        var glyphs = 0;
        var ran = await OnUiThread(() =>
        {
            var fmt = RunFormatting.Default with { VerticalAlign = VerticalAlign.Superscript, FontSizePt = 12 };
            var view = new DocumentView();
            view.LoadDocument(DocWithRun("E=mc²", fmt));
            view.Measure(new Size(800, 2000));
            glyphs = view.PlacedGlyphCount;
        });

        if (!ran) return;
        glyphs.Should().BeGreaterThan(0, "superscript characters must still produce placed glyphs");
    }

    // ── Text highlight ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SetHighlightColor_applies_to_selection()
    {
        string? hlHex = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(DocWithRun("Hello", RunFormatting.Default));
            view.Measure(new Size(800, 2000));
            view.SelectAll();
            view.SetHighlightColor("#FFFF00");
            var p = (Paragraph)view.Document.Blocks[0];
            hlHex = p.Runs[0].Formatting.HighlightColorHex;
        });

        if (!ran) return;
        hlHex.Should().Be("#FFFF00", "SetHighlightColor must write the hex to HighlightColorHex");
    }

    [Fact]
    public async Task SetHighlightColor_null_clears_highlight()
    {
        string? hlHex = "not-cleared";
        var ran = await OnUiThread(() =>
        {
            var fmt = RunFormatting.Default with { HighlightColorHex = "#FFFF00" };
            var view = new DocumentView();
            view.LoadDocument(DocWithRun("Hello", fmt));
            view.Measure(new Size(800, 2000));
            view.SelectAll();
            view.SetHighlightColor(null);
            var p = (Paragraph)view.Document.Blocks[0];
            hlHex = p.Runs[0].Formatting.HighlightColorHex;
        });

        if (!ran) return;
        hlHex.Should().BeNullOrEmpty("passing null to SetHighlightColor should clear the highlight");
    }

    [Fact]
    public async Task Highlight_run_still_produces_placed_glyphs()
    {
        var glyphs = 0;
        var ran = await OnUiThread(() =>
        {
            var fmt = RunFormatting.Default with { HighlightColorHex = "#00FF00" };
            var view = new DocumentView();
            view.LoadDocument(DocWithRun("Highlighted", fmt));
            view.Measure(new Size(800, 2000));
            glyphs = view.PlacedGlyphCount;
        });

        if (!ran) return;
        glyphs.Should().BeGreaterThan(0, "highlighted runs must still produce placed glyphs");
    }

    [Fact]
    public async Task Character_shading_takes_precedence_over_highlight_in_resolved_render_plan()
    {
        RunDecorationVisualPlan? plan = null;
        var ran = await OnUiThread(() =>
        {
            var fmt = RunFormatting.Default with
            {
                HighlightColorHex = "#FFFF00",
                CharacterShadingHex = "#92D050",
                CharacterShadingPattern = ShadingPattern.Pct25,
            };
            var view = new DocumentView();
            view.LoadDocument(DocWithRun("Shaded", fmt));
            view.Measure(new Size(800, 2000));
            plan = view.GetGlyphRunDecorationStyle(0, 0);
        });

        if (!ran) return;
        plan.Should().NotBeNull();
        plan!.BackgroundColorHex.Should().Be("#92D050");
        plan.BackgroundIsCharacterShading.Should().BeTrue();
        plan.CharacterShadingPattern.Should().Be(ShadingPattern.Pct25);
    }

    [Fact]
    public async Task Character_border_run_produces_resolved_border_render_plan()
    {
        RunDecorationVisualPlan? plan = null;
        var ran = await OnUiThread(() =>
        {
            var border = new ParagraphBorder("#0070C0", 0.75, BottomOnly: true)
            {
                LineStyle = BorderLineStyle.Dashed,
            };
            var view = new DocumentView();
            view.LoadDocument(DocWithRun("Bordered", RunFormatting.Default with { CharacterBorder = border }));
            view.Measure(new Size(800, 2000));
            plan = view.GetGlyphRunDecorationStyle(0, 0);
        });

        if (!ran) return;
        plan.Should().NotBeNull();
        plan!.HasBorder.Should().BeTrue();
        plan.Border!.ColorHex.Should().Be("#0070C0");
        plan.DrawTopBorder.Should().BeFalse();
        plan.DrawBottomBorder.Should().BeTrue();
        plan.BorderWidthDip.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Character_border_and_shading_from_style_resolve_for_rendering()
    {
        RunDecorationVisualPlan? plan = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Styles["DecoratedRun"] = new DocumentStyle
            {
                Id = "DecoratedRun",
                Name = "Decorated Run",
                Run = RunFormatting.Default with
                {
                    CharacterBorder = new ParagraphBorder("#C00000", 1.0),
                    CharacterShadingHex = "#D9EAD3",
                    CharacterShadingPattern = ShadingPattern.Pct10,
                },
            };
            var p = new Paragraph { StyleId = "DecoratedRun" };
            p.Runs.Add(new Run("Styled", RunFormatting.Default));
            doc.Blocks.Add(p);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            plan = view.GetGlyphRunDecorationStyle(0, 0);
        });

        if (!ran) return;
        plan.Should().NotBeNull();
        plan!.BackgroundColorHex.Should().Be("#D9EAD3");
        plan.CharacterShadingPattern.Should().Be(ShadingPattern.Pct10);
        plan.Border!.ColorHex.Should().Be("#C00000");
    }

    // ── Justify alignment ────────────────────────────────────────────────────────

    [Fact]
    public async Task Justify_command_sets_alignment_to_Justify()
    {
        TextAlignment? align = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(DocWithParagraphFmt("Test", ParagraphFormatting.Default));
            view.Measure(new Size(800, 2000));
            view.SetAlignment(TextAlignment.Justify);
            align = ((Paragraph)view.Document.Blocks[0]).Formatting.Alignment;
        });

        if (!ran) return;
        align.Should().Be(TextAlignment.Justify, "SetAlignment(Justify) should store Justify in the model");
    }

    [Fact]
    public async Task Justify_paragraph_lays_out_without_error()
    {
        var glyphs = 0;
        var ran = await OnUiThread(() =>
        {
            // Long text that will wrap so justify actually distributes gaps on non-last lines.
            var pf = ParagraphFormatting.Default with { Alignment = TextAlignment.Justify };
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var p = new Paragraph { Formatting = pf };
            p.Runs.Add(new Run(
                "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor " +
                "incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam.",
                RunFormatting.Default));
            doc.Blocks.Add(p);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(600, 4000));
            glyphs = view.PlacedGlyphCount;
        });

        if (!ran) return;
        glyphs.Should().BeGreaterThan(0, "justify-aligned text must still lay out and produce placed glyphs");
    }

    // ── Paragraph spacing ────────────────────────────────────────────────────────

    [Fact]
    public async Task SetSpaceBefore_persists_in_model()
    {
        double? before = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(DocWithParagraphFmt("Hello", ParagraphFormatting.Default));
            view.Measure(new Size(800, 2000));
            view.SetSpaceBefore(18.0);
            before = ((Paragraph)view.Document.Blocks[0]).Formatting.SpaceBeforePt;
        });

        if (!ran) return;
        before.Should().BeApproximately(18.0, 0.01, "SetSpaceBefore(18) should write 18 to SpaceBeforePt");
    }

    [Fact]
    public async Task SetSpaceAfter_persists_in_model()
    {
        double? after = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(DocWithParagraphFmt("Hello", ParagraphFormatting.Default));
            view.Measure(new Size(800, 2000));
            view.SetSpaceAfter(12.0);
            after = ((Paragraph)view.Document.Blocks[0]).Formatting.SpaceAfterPt;
        });

        if (!ran) return;
        after.Should().BeApproximately(12.0, 0.01, "SetSpaceAfter(12) should write 12 to SpaceAfterPt");
    }

    [Fact]
    public async Task SpaceBefore_increases_total_layout_height()
    {
        // Use WebLayout so total height directly reflects content (no fixed-page-height domination).
        double heightNoSpace = 0, heightWithSpace = 0;
        var ran = await OnUiThread(() =>
        {
            var viewA = new DocumentView();
            viewA.ViewMode = DocumentViewMode.WebLayout;
            viewA.LoadDocument(DocWithParagraphFmt("Hello", ParagraphFormatting.Default with { SpaceBeforePt = 0 }));
            viewA.Measure(new Size(800, 4000));
            heightNoSpace = viewA.DesiredSize.Height;

            var viewB = new DocumentView();
            viewB.ViewMode = DocumentViewMode.WebLayout;
            viewB.LoadDocument(DocWithParagraphFmt("Hello", ParagraphFormatting.Default with
            {
                SpaceBeforePt = 36,
                SpaceBeforeIsSet = true,
            }));
            viewB.Measure(new Size(800, 4000));
            heightWithSpace = viewB.DesiredSize.Height;
        });

        if (!ran) return;
        heightWithSpace.Should().BeGreaterThan(heightNoSpace,
            "SpaceBeforePt > 0 must increase the total layout height (WebLayout mode, no fixed-page overhead)");
    }

    [Fact]
    public async Task SpaceAfter_increases_total_layout_height()
    {
        // Use WebLayout so total height directly reflects content (no fixed-page-height domination).
        double heightNoSpace = 0, heightWithSpace = 0;
        var ran = await OnUiThread(() =>
        {
            var viewA = new DocumentView();
            viewA.ViewMode = DocumentViewMode.WebLayout;
            viewA.LoadDocument(DocWithParagraphFmt("Hello", ParagraphFormatting.Default with { SpaceAfterPt = 0, SpaceAfterIsSet = true }));
            viewA.Measure(new Size(800, 4000));
            heightNoSpace = viewA.DesiredSize.Height;

            var viewB = new DocumentView();
            viewB.ViewMode = DocumentViewMode.WebLayout;
            viewB.LoadDocument(DocWithParagraphFmt("Hello", ParagraphFormatting.Default with
            {
                SpaceAfterPt = 36,
                SpaceAfterIsSet = true,
            }));
            viewB.Measure(new Size(800, 4000));
            heightWithSpace = viewB.DesiredSize.Height;
        });

        if (!ran) return;
        heightWithSpace.Should().BeGreaterThan(heightNoSpace,
            "SpaceAfterPt > 0 must increase the total layout height (WebLayout mode, no fixed-page overhead)");
    }

    // ── Paragraph indents ────────────────────────────────────────────────────────

    [Fact]
    public async Task SetIndents_left_persists_in_model()
    {
        double? indentLeft = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(DocWithParagraphFmt("Hello", ParagraphFormatting.Default));
            view.Measure(new Size(800, 2000));
            view.SetIndents(leftPt: 36.0);
            indentLeft = ((Paragraph)view.Document.Blocks[0]).Formatting.IndentLeftPt;
        });

        if (!ran) return;
        indentLeft.Should().BeApproximately(36.0, 0.01, "SetIndents(leftPt:36) should write 36 to IndentLeftPt");
    }

    [Fact]
    public async Task SetIndents_first_line_persists_in_model()
    {
        double? firstLine = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(DocWithParagraphFmt("Hello", ParagraphFormatting.Default));
            view.Measure(new Size(800, 2000));
            view.SetIndents(firstLinePt: 18.0);
            firstLine = ((Paragraph)view.Document.Blocks[0]).Formatting.FirstLineIndentPt;
        });

        if (!ran) return;
        firstLine.Should().BeApproximately(18.0, 0.01, "SetIndents(firstLinePt:18) should write 18 to FirstLineIndentPt");
    }

    [Fact]
    public async Task Left_indent_shifts_glyphs_to_the_right()
    {
        double xNoIndent = 0, xWithIndent = 0;
        var ran = await OnUiThread(() =>
        {
            var viewA = new DocumentView();
            viewA.LoadDocument(DocWithParagraphFmt("Hello", ParagraphFormatting.Default with { IndentLeftPt = 0 }));
            viewA.Measure(new Size(800, 2000));
            var firstA = GetPlacedChars(viewA).FirstOrDefault(pc => !IsSentinel(pc));
            xNoIndent = firstA is not null ? GetX(firstA) : 0;

            var viewB = new DocumentView();
            viewB.LoadDocument(DocWithParagraphFmt("Hello", ParagraphFormatting.Default with { IndentLeftPt = 72 }));
            viewB.Measure(new Size(800, 2000));
            var firstB = GetPlacedChars(viewB).FirstOrDefault(pc => !IsSentinel(pc));
            xWithIndent = firstB is not null ? GetX(firstB) : 0;
        });

        if (!ran) return;
        xWithIndent.Should().BeGreaterThan(xNoIndent,
            "a 72pt left indent must push the first glyph further right than no indent");
    }

    [Fact]
    public async Task Indent_paragraph_lays_out_glyphs()
    {
        var glyphs = 0;
        var ran = await OnUiThread(() =>
        {
            var pf = ParagraphFormatting.Default with
            {
                IndentLeftPt      = 36,
                IndentRightPt     = 36,
                FirstLineIndentPt = 18,
            };
            var view = new DocumentView();
            view.LoadDocument(DocWithParagraphFmt("This paragraph has all three indent types set.", pf));
            view.Measure(new Size(800, 2000));
            glyphs = view.PlacedGlyphCount;
        });

        if (!ran) return;
        glyphs.Should().BeGreaterThan(0, "indented paragraph must still produce placed glyphs");
    }

    // ── Line spacing ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetLineSpacing_Multiple_persists_in_model()
    {
        LineSpacingRule? rule = null;
        double? spacing = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(DocWithParagraphFmt("Hello", ParagraphFormatting.Default));
            view.Measure(new Size(800, 2000));
            view.SetLineSpacing(LineSpacingRule.Multiple, 2.0);
            var p = (Paragraph)view.Document.Blocks[0];
            rule    = p.Formatting.LineRule;
            spacing = p.Formatting.LineSpacing;
        });

        if (!ran) return;
        rule.Should().Be(LineSpacingRule.Multiple);
        spacing.Should().BeApproximately(2.0, 0.01, "SetLineSpacing(Multiple, 2.0) should write 2.0 to LineSpacing");
    }

    [Fact]
    public async Task SetLineSpacing_Exact_persists_in_model()
    {
        LineSpacingRule? rule = null;
        double? heightPt = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(DocWithParagraphFmt("Hello", ParagraphFormatting.Default));
            view.Measure(new Size(800, 2000));
            view.SetLineSpacing(LineSpacingRule.Exact, 24.0);
            var p = (Paragraph)view.Document.Blocks[0];
            rule     = p.Formatting.LineRule;
            heightPt = p.Formatting.LineHeightPt;
        });

        if (!ran) return;
        rule.Should().Be(LineSpacingRule.Exact);
        heightPt.Should().BeApproximately(24.0, 0.01, "SetLineSpacing(Exact, 24) should write 24 to LineHeightPt");
    }

    [Fact]
    public async Task Double_line_spacing_increases_layout_height()
    {
        // Use WebLayout so content height is directly measured (no fixed page-height domination).
        double heightSingle = 0, heightDouble = 0;
        var ran = await OnUiThread(() =>
        {
            var viewA = new DocumentView();
            viewA.ViewMode = DocumentViewMode.WebLayout;
            viewA.LoadDocument(DocWithParagraphFmt(
                "Lorem ipsum dolor sit amet.", ParagraphFormatting.Default with { LineRule = LineSpacingRule.Multiple, LineSpacing = 1.0 }));
            viewA.Measure(new Size(800, 4000));
            heightSingle = viewA.DesiredSize.Height;

            var viewB = new DocumentView();
            viewB.ViewMode = DocumentViewMode.WebLayout;
            viewB.LoadDocument(DocWithParagraphFmt(
                "Lorem ipsum dolor sit amet.", ParagraphFormatting.Default with { LineRule = LineSpacingRule.Multiple, LineSpacing = 2.0, LineSpacingIsSet = true }));
            viewB.Measure(new Size(800, 4000));
            heightDouble = viewB.DesiredSize.Height;
        });

        if (!ran) return;
        // The constant spaceAfter term dilutes the ratio. A floor of 1.2× (rather than 2×) is
        // reliable while still catching the "spacing has no effect" regression.
        heightDouble.Should().BeGreaterThan(heightSingle * 1.2,
            "double line spacing (2×) should produce a noticeably taller layout than 1× spacing (WebLayout, no fixed-page overhead)");
    }

    [Fact]
    public async Task Exact_line_spacing_controls_height()
    {
        // Use WebLayout so content height is directly measured (no fixed page-height domination).
        // An "exact" 8pt line height (very tight) should produce a shorter layout than the 11pt default.
        double heightExact = 0, heightDefault = 0;
        var ran = await OnUiThread(() =>
        {
            var viewA = new DocumentView();
            viewA.ViewMode = DocumentViewMode.WebLayout;
            viewA.LoadDocument(DocWithParagraphFmt("Hello world", ParagraphFormatting.Default));
            viewA.Measure(new Size(800, 4000));
            heightDefault = viewA.DesiredSize.Height;

            var viewB = new DocumentView();
            viewB.ViewMode = DocumentViewMode.WebLayout;
            viewB.LoadDocument(DocWithParagraphFmt("Hello world",
                ParagraphFormatting.Default with { LineRule = LineSpacingRule.Exact, LineHeightPt = 8 }));
            viewB.Measure(new Size(800, 4000));
            heightExact = viewB.DesiredSize.Height;
        });

        if (!ran) return;
        heightExact.Should().BeLessThan(heightDefault,
            "exact 8pt line spacing is tighter than the default 11pt×1.15 leading (WebLayout, no fixed-page overhead)");
    }

    [Fact]
    public async Task AtLeast_line_spacing_allows_tall_glyphs_to_expand()
    {
        // At-least 8pt with a 12pt font should still be >= 12pt × leading (natural wins over minimum).
        double heightAtLeast8 = 0, heightDefault12 = 0;
        var ran = await OnUiThread(() =>
        {
            var fmt = RunFormatting.Default with { FontSizePt = 12 };

            var viewA = new DocumentView();
            var docA = TextDocument.CreateEmpty();
            docA.Blocks.Clear();
            var pA = new Paragraph { Formatting = ParagraphFormatting.Default with
                { LineRule = LineSpacingRule.AtLeast, LineHeightPt = 8 } };
            pA.Runs.Add(new Run("Hello", fmt));
            docA.Blocks.Add(pA);
            viewA.LoadDocument(docA);
            viewA.Measure(new Size(800, 4000));
            heightAtLeast8 = viewA.DesiredSize.Height;

            var viewB = new DocumentView();
            var docB = TextDocument.CreateEmpty();
            docB.Blocks.Clear();
            var pB = new Paragraph { Formatting = ParagraphFormatting.Default };
            pB.Runs.Add(new Run("Hello", fmt));
            docB.Blocks.Add(pB);
            viewB.LoadDocument(docB);
            viewB.Measure(new Size(800, 4000));
            heightDefault12 = viewB.DesiredSize.Height;
        });

        if (!ran) return;
        // AtLeast(8) with 12pt font should be similar to default (natural 12pt wins).
        heightAtLeast8.Should().BeApproximately(heightDefault12, heightDefault12 * 0.3,
            "AtLeast(8pt) with a 12pt font should not be dramatically different from the default 12pt layout");
    }

    // ── Undo integration: each B1 mutation is undoable ──────────────────────────

    [Fact]
    public async Task Superscript_toggle_is_undoable()
    {
        VerticalAlign? before = null, after = null, undone = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(DocWithRun("Hello", RunFormatting.Default));
            view.Measure(new Size(800, 2000));
            before = ((Paragraph)view.Document.Blocks[0]).Runs[0].Formatting.VerticalAlign;
            view.SelectAll();
            view.ToggleSuperscript();
            after = ((Paragraph)view.Document.Blocks[0]).Runs[0].Formatting.VerticalAlign;
            view.Undo();
            undone = ((Paragraph)view.Document.Blocks[0]).Runs[0].Formatting.VerticalAlign;
        });

        if (!ran) return;
        before.Should().Be(VerticalAlign.Baseline);
        after.Should().Be(VerticalAlign.Superscript);
        undone.Should().Be(VerticalAlign.Baseline, "undo should restore the original VerticalAlign");
    }

    [Fact]
    public async Task SetSpaceBefore_is_undoable()
    {
        double? before = null, after = null, undone = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(DocWithParagraphFmt("Hello", ParagraphFormatting.Default with { SpaceBeforePt = 0 }));
            view.Measure(new Size(800, 2000));
            before = ((Paragraph)view.Document.Blocks[0]).Formatting.SpaceBeforePt;
            view.SetSpaceBefore(24);
            after  = ((Paragraph)view.Document.Blocks[0]).Formatting.SpaceBeforePt;
            view.Undo();
            undone = ((Paragraph)view.Document.Blocks[0]).Formatting.SpaceBeforePt;
        });

        if (!ran) return;
        before.Should().Be(0);
        after.Should().Be(24);
        undone.Should().Be(0, "undo should restore the original SpaceBeforePt");
    }

    // ── Reflection helpers ────────────────────────────────────────────────────────

    private static System.Collections.Generic.IEnumerable<object> GetPlacedChars(DocumentView view)
    {
        var field = typeof(DocumentView).GetField("_placed", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("_placed");
        return ((System.Collections.IEnumerable)field.GetValue(view)!).Cast<object>();
    }

    private static bool IsSentinel(object placedChar)
    {
        var prop = placedChar.GetType().GetProperty("Sentinel")!;
        return (bool)prop.GetValue(placedChar)!;
    }

    private static double GetX(object placedChar)
    {
        var prop = placedChar.GetType().GetProperty("X")!;
        return (double)prop.GetValue(placedChar)!;
    }
}
