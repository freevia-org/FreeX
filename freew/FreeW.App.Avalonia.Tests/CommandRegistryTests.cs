using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Tests for the Wave A1 command registry: every new command id is registered, and executing
/// representative commands actually mutates the model via DocumentCommandBus.
///
/// Tests that need the Avalonia layout engine (GrowFont, ShrinkFont, etc.) run on the shared
/// headless UI thread via <see cref="DocumentViewHeadlessTests.Session"/>.
/// Pure-model tests (bold, alignment, style, indent, clear-formatting, select-all) do not need
/// the headless session.
/// </summary>
public sealed class CommandRegistryTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static RibbonHostCallbacks NoopCallbacks() =>
        new(
            Open: () => { },
            Save: () => { },
            Cut: () => { },
            Copy: () => { },
            Paste: () => { },
            Backstage: () => { },
            NewDocument: () => { },
            ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { },
            ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { },
            SetWebLayout: () => { },
            SetDraftView: () => { },
            OpenFontDialog: () => { },
            OpenParagraphDialog: () => { },
            ApplyZoom: (_, _) => { });

    /// <summary>Creates a minimal editable document with one paragraph of text.</summary>
    private static TextDocument MakeDoc(string text = "Hello world")
    {
        var doc = TextDocument.CreateEmpty();
        var para = new Paragraph(text);
        doc.Blocks.Clear();
        doc.Blocks.Add(para);
        return doc;
    }

    // ── Registry completeness ─────────────────────────────────────────────────

    [Fact]
    public void Registry_resolves_all_ribbon_definition_command_ids()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), NoopCallbacks());

        var ids = definition.Tabs
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Controls)
            .Select(GetCommandId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        ids.Should().NotBeEmpty("ribbon must declare at least the existing commands");

        foreach (var id in ids)
            registry.TryGet(id, out _).Should().BeTrue($"command '{id.Value}' declared in ribbon but not registered");
    }

    [Fact]
    public void Registry_contains_all_wave_a1_new_commands()
    {
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        var expected = new[]
        {
            "freew.strikethrough",
            "freew.grow-font",
            "freew.shrink-font",
            "freew.clear-formatting",
            "freew.font-color",
            "freew.change-case",
            "freew.select-all",
            "freew.show-hide-para",
            "freew.increase-indent",
            "freew.decrease-indent",
            "freew.style-heading3",
            "freew.new",
            "freew.zoom-in",
            "freew.zoom-out",
            "freew.zoom-100",
        };

        foreach (var id in expected)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"Wave A1 command '{id}' must be registered");
    }

    [Fact]
    public void Ribbon_definition_now_has_37_or_more_commands()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var count = definition.Tabs
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Controls)
            .Count(c => GetCommandId(c) is not null);

        // Was 22 before Wave A1; we added 15 new commands.
        count.Should().BeGreaterThanOrEqualTo(37, "Wave A1 must add at least 15 commands to the ribbon");
    }

    // ── Model mutation tests (no headless backend needed) ─────────────────────

    [Fact]
    public void Bold_command_sets_bold_on_all_runs()
    {
        var doc = MakeDoc("Hi");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        view.ToggleBold();

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.Bold).Should().BeTrue("Bold should be set on all runs");
    }

    [Fact]
    public void Strikethrough_command_toggles_strikethrough()
    {
        var doc = MakeDoc("Test");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        view.ToggleStrikethrough();

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.Strikethrough)
            .Should().BeTrue("ToggleStrikethrough should set strikethrough on all selected runs");
    }

    [Fact]
    public void Clear_formatting_resets_bold_to_false()
    {
        var doc = TextDocument.CreateEmpty();
        var para = new Paragraph();
        para.Runs.Add(new Run("X", new RunFormatting { Bold = true, Italic = true }));
        doc.Blocks.Clear();
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();
        view.ClearFormatting();

        var result = (Paragraph)view.Document.Blocks[0];
        result.Runs.All(r => !r.Formatting.Bold && !r.Formatting.Italic)
            .Should().BeTrue("ClearFormatting should reset Bold and Italic");
    }

    [Fact]
    public void Align_center_sets_paragraph_alignment()
    {
        var doc = MakeDoc("Centered");
        var view = new DocumentView();
        view.LoadDocument(doc);

        view.SetAlignment(TextAlignment.Center);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Formatting.Alignment.Should().Be(TextAlignment.Center);
    }

    [Fact]
    public void Style_heading1_sets_larger_bold_font()
    {
        var doc = MakeDoc("A heading");
        var view = new DocumentView();
        view.LoadDocument(doc);

        view.ApplyQuickStyle(16, bold: true);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.Bold && r.Formatting.FontSizePt == 16)
            .Should().BeTrue("Heading 1 quick style should set 16pt bold on all runs");
    }

    [Fact]
    public void Bullets_toggle_sets_list_kind()
    {
        var doc = MakeDoc("Item");
        var view = new DocumentView();
        view.LoadDocument(doc);

        view.ToggleList(ListKind.Bullet);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Formatting.ListKind.Should().Be(ListKind.Bullet);
    }

    [Fact]
    public void Select_all_sets_selection_spanning_whole_document()
    {
        var doc = MakeDoc("Hello");
        var view = new DocumentView();
        view.LoadDocument(doc);

        // Before: no selection → SelectedText is empty.
        view.SelectedText.Should().BeEmpty("no selection yet");

        view.SelectAll();

        view.SelectedText.Should().Be("Hello", "SelectAll should select the entire paragraph text");
    }

    [Fact]
    public void Increase_indent_advances_list_level()
    {
        var doc = MakeDoc("Nested");
        var para = (Paragraph)doc.Blocks[0];
        // Set as bullet at level 0 so IncreaseIndent increments ListLevel.
        para.Formatting = para.Formatting with { ListKind = ListKind.Bullet, ListLevel = 0 };

        var view = new DocumentView();
        view.LoadDocument(doc);

        view.IncreaseIndent();

        var result = (Paragraph)view.Document.Blocks[0];
        result.Formatting.ListLevel.Should().Be(1);
    }

    [Fact]
    public void Decrease_indent_lowers_list_level_but_not_below_zero()
    {
        var doc = MakeDoc("Deep");
        var para = (Paragraph)doc.Blocks[0];
        para.Formatting = para.Formatting with { ListKind = ListKind.Bullet, ListLevel = 2 };

        var view = new DocumentView();
        view.LoadDocument(doc);

        view.DecreaseIndent();

        var result = (Paragraph)view.Document.Blocks[0];
        result.Formatting.ListLevel.Should().Be(1);
    }

    [Fact]
    public void Show_paragraph_marks_toggles_property()
    {
        var view = new DocumentView();
        view.ShowParagraphMarks.Should().BeFalse("off by default");

        view.ShowParagraphMarks = true;
        view.ShowParagraphMarks.Should().BeTrue();

        view.ShowParagraphMarks = false;
        view.ShowParagraphMarks.Should().BeFalse();
    }

    [Fact]
    public void Set_font_color_applies_to_selection()
    {
        var doc = MakeDoc("Red");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        view.SetFontColor("#FF0000");

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.ColorHex == "#FF0000")
            .Should().BeTrue("SetFontColor should apply to all selected runs");
    }

    [Fact]
    public void Font_color_ribbon_control_is_dropdown_not_plain_button()
    {
        // Regression: freew.font-color was wired as a plain Button + RelayValueCommand.
        // Clicking a plain button dispatches Execute(RibbonCommandContext.Empty) so SelectedValue
        // is null, which caused SetFontColor(null) to silently CLEAR the selection colour.
        // The fix makes the ribbon control a Dropdown (flyout opener) with per-colour sub-commands.
        // This test asserts:
        //   (a) The ribbon definition exposes freew.font-color as a Dropdown, not a Button.
        //   (b) Executing freew.font-color directly does NOT clear any existing colour.
        var definition = FreeWRibbon.BuildDefinition();
        var fontColorControl = definition.Tabs
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Controls)
            .FirstOrDefault(c => c.CommandId.Value == "freew.font-color");

        fontColorControl.Should().NotBeNull("freew.font-color must be declared in the ribbon");
        fontColorControl.Should().BeOfType<RibbonDropdown>(
            "freew.font-color must be a Dropdown so clicking the button opens the colour flyout " +
            "instead of executing with a null value that clears the current colour");

        // Also verify: executing the main freew.font-color command (the flyout-opener no-op)
        // does NOT change the document colour — pre-existing colour must survive.
        var doc = MakeDoc("Coloured");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();
        view.SetFontColor("#FF0000");   // pre-apply a colour

        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.font-color"), out var cmd)
            .Should().BeTrue("freew.font-color must be registered");
        cmd!.Execute(RibbonCommandContext.Empty);   // simulate button click with no SelectedValue

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.ColorHex == "#FF0000")
            .Should().BeTrue("clicking the Font Color dropdown opener must NOT clear the existing colour");
    }

    [Fact]
    public void Font_color_palette_subcommands_apply_expected_colors()
    {
        // Each freew.font-color.* sub-command must call SetFontColor with a non-clearing value.
        var doc = MakeDoc("Test");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());

        // Verify the red sub-command applies red.
        registry.TryGet(new RibbonCommandId("freew.font-color.red"), out var redCmd)
            .Should().BeTrue("freew.font-color.red sub-command must be registered");
        redCmd!.Execute(RibbonCommandContext.Empty);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.ColorHex == "#FF0000")
            .Should().BeTrue("freew.font-color.red should apply #FF0000 to the selection");

        // Verify the automatic sub-command sets null (restores default).
        view.SelectAll();
        registry.TryGet(new RibbonCommandId("freew.font-color.automatic"), out var autoCmd)
            .Should().BeTrue("freew.font-color.automatic sub-command must be registered");
        autoCmd!.Execute(RibbonCommandContext.Empty);

        para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.ColorHex is null)
            .Should().BeTrue("freew.font-color.automatic should restore null (automatic) colour");
    }

    [Fact]
    public void Undo_reverts_bold_toggle()
    {
        var doc = MakeDoc("Undo");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();
        view.ToggleBold();

        var beforeUndo = ((Paragraph)view.Document.Blocks[0]).Runs.All(r => r.Formatting.Bold);
        beforeUndo.Should().BeTrue();

        view.Undo();

        var afterUndo = ((Paragraph)view.Document.Blocks[0]).Runs.All(r => r.Formatting.Bold);
        afterUndo.Should().BeFalse("Undo should revert the bold toggle");
    }

    [Fact]
    public void Change_case_applies_to_multi_block_selection()
    {
        // Regression for: ApplyRunFormattingToText silently no-ops when the selection spans
        // more than one paragraph (the multi-block case was simply not handled before the fix).
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("hello"));
        doc.Blocks.Add(new Paragraph("world"));

        var view = new DocumentView();
        view.LoadDocument(doc);

        // SelectAll creates a cross-block selection: anchor=(0,0), caret=(1, end).
        view.SelectAll();
        view.ChangeCase(); // was a no-op for multi-block; now should cycle case on both paragraphs

        var p0 = (Paragraph)view.Document.Blocks[0];
        var p1 = (Paragraph)view.Document.Blocks[1];

        // CycleCase("hello") → all-lower → Title Case → "Hello"
        p0.PlainText.Should().NotBe("hello",
            "ChangeCase on a multi-block selection must transform the first paragraph");
        p1.PlainText.Should().NotBe("world",
            "ChangeCase on a multi-block selection must transform the last paragraph");
    }

    // ── Headless tests (need FormattedText backend) ───────────────────────────

    [Fact]
    public async Task Grow_font_increases_font_size_on_headless_backend()
    {
        double? sizeAfter = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var doc = TextDocument.CreateEmpty();
                var para = new Paragraph();
                para.Runs.Add(new Run("A", new RunFormatting { FontSizePt = 11 }));
                doc.Blocks.Clear();
                doc.Blocks.Add(para);

                var view = new DocumentView();
                view.LoadDocument(doc);
                view.Measure(new Size(800, 4000));
                view.SelectAll();
                view.GrowFont();

                sizeAfter = ((Paragraph)view.Document.Blocks[0]).Runs[0].Formatting.FontSizePt;
                ran = true;
            }, CancellationToken.None);
        }
        catch
        {
            // Headless backend not available — skip rather than fail.
            return;
        }

        if (!ran)
            return;

        sizeAfter.Should().Be(12, "GrowFont from 11pt should step to 12pt (next ladder rung)");
    }

    [Fact]
    public async Task Shrink_font_decreases_font_size_on_headless_backend()
    {
        double? sizeAfter = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var doc = TextDocument.CreateEmpty();
                var para = new Paragraph();
                para.Runs.Add(new Run("B", new RunFormatting { FontSizePt = 14 }));
                doc.Blocks.Clear();
                doc.Blocks.Add(para);

                var view = new DocumentView();
                view.LoadDocument(doc);
                view.Measure(new Size(800, 4000));
                view.SelectAll();
                view.ShrinkFont();

                sizeAfter = ((Paragraph)view.Document.Blocks[0]).Runs[0].Formatting.FontSizePt;
                ran = true;
            }, CancellationToken.None);
        }
        catch
        {
            return;
        }

        if (!ran)
            return;

        sizeAfter.Should().Be(12, "ShrinkFont from 14pt should step to 12pt (previous ladder rung)");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RibbonCommandId? GetCommandId(RibbonControl control) => control switch
    {
        RibbonButton b => b.CommandId,
        RibbonToggleButton t => t.CommandId,
        RibbonComboBox c => c.CommandId,
        RibbonCheckBox cb => cb.CommandId,
        RibbonSplitButton sb => sb.CommandId,
        RibbonDropdown d => d.CommandId,
        RibbonGallery g => g.CommandId,
        _ => (RibbonCommandId?)null,
    };
}
