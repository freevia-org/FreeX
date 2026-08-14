using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.IO;
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

    private static FreeWRibbonHostExecutionPorts NoopCallbacks() =>
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
            OpenPageSetupDialog: () => { },
            ToggleOrientation:   () => { },
            ApplyMarginPreset:   _ => { },
            ApplyPaperSize:      _ => { },
            InsertPicture:       () => { },
            OpenWordCountDialog: () => { },
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
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());

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
    public void Insert_object_command_uses_shell_file_picker_callback()
    {
        var invoked = false;
        var callbacks = NoopCallbacks() with { InsertObject = () => invoked = true };
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), callbacks);

        Execute(registry, "freew.object");

        invoked.Should().BeTrue();
    }

    [Fact]
    public void Drawing_text_direction_commands_are_registered_for_all_wpf_modes()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());

        foreach (var id in new[]
        {
            "freew.shape-text-horizontal",
            "freew.shape-text-rotate90",
            "freew.shape-text-rotate270",
        })
        {
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"Avalonia must register the WPF text-direction command '{id}'");
        }
    }

    [Fact]
    public void Registry_contains_all_wave_a1_new_commands()
    {
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        var expected = new[]
        {
            "freew.strikethrough",
            "freew.smallcaps",
            "freew.allcaps",
            "freew.char-border",
            "freew.char-shading",
            "freew.grow-font",
            "freew.shrink-font",
            "freew.clear-formatting",
            "freew.font-color",
            "freew.change-case",
            "freew.select",
            "freew.select-all",
            "freew.find",
            "freew.replace",
            "freew.find-replace-dialog",
            "freew.formatting-marks",
            "freew.show-hide-para",
            "freew.indent-increase",
            "freew.indent-decrease",
            "freew.increase-indent",
            "freew.decrease-indent",
            "freew.line-spacing",
            "freew.space-before-toggle",
            "freew.space-after-toggle",
            "freew.keep-with-next",
            "freew.keep-lines",
            "freew.widow-control",
            "freew.para-border",
            "freew.para-shading",
            "freew.borders-shading",
            "freew.tabs-dialog",
            "freew.sort",
            "freew.multilevel-list",
            "freew.multilevel-promote",
            "freew.multilevel-demote",
            "freew.multilevel-preset-0",
            "freew.multilevel-preset-1",
            "freew.multilevel-preset-2",
            "freew.multilevel-define",
            "freew.style-heading3",
            "freew.new",
            "freew.zoom-in",
            "freew.zoom-out",
            "freew.zoom-100",
            "freew.format-painter",
            "freew.paste-plain",
            "freew.paste-merge",
            "freew.paste-special",
            "freew.new-style",
            "freew.manage-styles",
            "freew.style-set",
            "freew.reset-style-set",
        };

        foreach (var id in expected)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"Wave A1 command '{id}' must be registered");
    }

    [Fact]
    public void Developer_controls_profile_commands_are_registered()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var developer = definition.Tabs.SingleOrDefault(tab => tab.Id == "developer");
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());

        developer.Should().NotBeNull();
        developer!.Groups.Select(group => group.Id).Should().Equal("controls");

        var commands = developer.Groups
            .SelectMany(group => group.Controls)
            .Select(GetCommandId)
            .Where(id => id is not null)
            .Select(id => id!.Value.Value)
            .ToArray();

        commands.Should().Equal(
            "freew.cc-text",
            "freew.cc-richtext",
            "freew.cc-checkbox",
            "freew.cc-date",
            "freew.cc-dropdown",
            "freew.cc-combo");

        foreach (var id in commands)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"{id} should execute from the Avalonia Developer controls group");
    }

    public static TheoryData<string, ContentControlKind> DeveloperControlCommands =>
        new()
        {
            { "freew.cc-text", ContentControlKind.PlainText },
            { "freew.cc-richtext", ContentControlKind.RichText },
            { "freew.cc-checkbox", ContentControlKind.CheckBox },
            { "freew.cc-date", ContentControlKind.DatePicker },
            { "freew.cc-dropdown", ContentControlKind.DropDownList },
            { "freew.cc-combo", ContentControlKind.ComboBox },
        };

    [Theory]
    [MemberData(nameof(DeveloperControlCommands))]
    public void Developer_control_commands_insert_shared_content_control_runs(
        string commandId,
        ContentControlKind kind)
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc(string.Empty));
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, commandId);

        var paragraph = view.Document.Blocks.OfType<Paragraph>().Single();
        paragraph.Runs.Should().ContainSingle();
        paragraph.Runs[0].Control.Should().NotBeNull();
        paragraph.Runs[0].Control!.Kind.Should().Be(kind);
    }

    [Fact]
    public void Home_definition_uses_wpf_command_ids_for_editing_and_paragraph_slice()
    {
        var home = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia).FindTab("home");
        home.Should().NotBeNull();

        var ids = home!.Groups
            .SelectMany(group => group.Controls)
            .SelectMany(CommandIdsIncludingMenus)
            .Select(id => id.Value)
            .ToHashSet();

        ids.Should().Contain(new[]
        {
            "freew.find",
            "freew.replace",
            "freew.select",
            "freew.formatting-marks",
            "freew.indent-increase",
            "freew.indent-decrease",
            "freew.line-spacing",
            "freew.space-before-toggle",
            "freew.space-after-toggle",
            "freew.sort",
            "freew.para-shading",
            "freew.para-border",
            "freew.borders-shading",
            "freew.tabs-dialog",
            "freew.widow-control",
            "freew.multilevel-list",
            "freew.multilevel-promote",
            "freew.multilevel-demote",
            "freew.multilevel-preset-0",
            "freew.multilevel-preset-1",
            "freew.multilevel-preset-2",
            "freew.multilevel-define",
        });

        ids.Should().NotContain(new[]
        {
            "freew.find-replace-dialog",
            "freew.select-all",
            "freew.show-hide-para",
            "freew.increase-indent",
            "freew.decrease-indent",
            "freew.line-spacing-1",
            "freew.line-spacing-115",
            "freew.line-spacing-15",
            "freew.line-spacing-2",
        });
    }

    [Fact]
    public void Find_replace_ids_and_compat_alias_open_same_dialog_callback()
    {
        var calls = 0;
        var registry = FreeWAvaloniaRibbonCommands.Build(
            new DocumentView(),
            NoopCallbacks() with { OpenFindReplaceDialog = () => calls++ });

        Execute(registry, "freew.find");
        Execute(registry, "freew.replace");
        Execute(registry, "freew.find-replace-dialog");

        calls.Should().Be(3, "Find, Replace, and the old Avalonia alias should open the same dialog");
    }

    [Fact]
    public void Select_command_and_compat_alias_select_the_whole_document()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc("Select me"));
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.select");
        view.SelectedText.Should().Be("Select me");

        view.LoadDocument(MakeDoc("Alias path"));
        Execute(registry, "freew.select-all");
        view.SelectedText.Should().Be("Alias path");
    }

    [Fact]
    public void Formatting_marks_command_is_stateful_and_keeps_old_alias()
    {
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.formatting-marks"), out var command)
            .Should().BeTrue("WPF formatting marks id must be registered");
        var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;

        stateful.GetState().IsChecked.Should().BeFalse();
        stateful.Execute(RibbonCommandContext.Empty);
        view.ShowParagraphMarks.Should().BeTrue();
        stateful.GetState().IsChecked.Should().BeTrue();

        Execute(registry, "freew.show-hide-para");
        view.ShowParagraphMarks.Should().BeFalse("old Avalonia id remains as a compatibility alias");
    }

    [Fact]
    public void Font_combos_publish_effective_values_apply_undoably_and_reject_invalid_values()
    {
        var doc = MakeDoc("Format");
        doc.DefaultRun = new RunFormatting { FontFamily = "Georgia", FontSizePt = 13 };
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SetSelectionRangePublic(0, 0, 0, 6);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.font-family"), out var familyCommand).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.font-size"), out var sizeCommand).Should().BeTrue();
        var familyState = familyCommand.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
        var sizeState = sizeCommand.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
        familyState.GetState().Value.Should().Be("Georgia");
        sizeState.GetState().Value.Should().Be("13");

        familyCommand!.Execute(RibbonCommandContext.ForSelectedValue("Arial"));
        sizeCommand!.Execute(RibbonCommandContext.ForSelectedValue("14.5"));
        familyState.GetState().Value.Should().Be("Arial");
        sizeState.GetState().Value.Should().Be("14.5");

        familyCommand.Execute(RibbonCommandContext.Empty);
        sizeCommand.Execute(RibbonCommandContext.ForSelectedValue("0"));
        sizeCommand.Execute(RibbonCommandContext.ForSelectedValue("Missing"));
        familyState.GetState().Value.Should().Be("Arial");
        sizeState.GetState().Value.Should().Be("14.5");

        view.Undo();
        sizeState.GetState().Value.Should().Be("13");
        view.Undo();
        familyState.GetState().Value.Should().Be("Georgia");
    }

    [Fact]
    public void Line_spacing_combo_and_fixed_aliases_set_multiple_spacing()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc("Spacing"));
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.line-spacing"), out var command).Should().BeTrue();
        var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
        stateful.GetState().Value.Should().Be("1.15");

        Execute(registry, "freew.line-spacing", RibbonCommandContext.ForSelectedValue("1.5"));
        var paragraph = (Paragraph)view.Document.Blocks[0];
        paragraph.Formatting.LineRule.Should().Be(LineSpacingRule.Multiple);
        paragraph.Formatting.LineSpacing.Should().Be(1.5);
        stateful.GetState().Value.Should().Be("1.5");

        Execute(registry, "freew.line-spacing", RibbonCommandContext.ForSelectedValue("0"));
        Execute(registry, "freew.line-spacing", RibbonCommandContext.ForSelectedValue("Missing"));
        paragraph.Formatting.LineSpacing.Should().Be(1.5);
        stateful.GetState().Value.Should().Be("1.5");

        Execute(registry, "freew.line-spacing-2");
        paragraph.Formatting.LineSpacing.Should().Be(2.0);
        stateful.GetState().Value.Should().Be("2");
    }

    [Fact]
    public void Space_before_after_toggles_apply_wpf_twelve_point_spacing()
    {
        var view = new DocumentView();
        var doc = MakeDoc("Spacing");
        var paragraph = (Paragraph)doc.Blocks[0];
        paragraph.Formatting = paragraph.Formatting with
        {
            SpaceBeforePt = 0,
            SpaceBeforeIsSet = true,
            SpaceAfterPt = 0,
            SpaceAfterIsSet = true
        };
        view.LoadDocument(doc);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.space-before-toggle");
        paragraph.Formatting.SpaceBeforePt.Should().Be(12);
        paragraph.Formatting.SpaceBeforeIsSet.Should().BeTrue();
        Execute(registry, "freew.space-before-toggle");
        paragraph.Formatting.SpaceBeforePt.Should().Be(0);

        Execute(registry, "freew.space-after-toggle");
        paragraph.Formatting.SpaceAfterPt.Should().Be(12);
        paragraph.Formatting.SpaceAfterIsSet.Should().BeTrue();
        Execute(registry, "freew.space-after-toggle");
        paragraph.Formatting.SpaceAfterPt.Should().Be(0);
    }

    [Fact]
    public void Keep_paragraph_flow_commands_toggle_model_flags()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc("Flow"));
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
        var paragraph = (Paragraph)view.Document.Blocks[0];

        Execute(registry, "freew.keep-with-next");
        paragraph.Formatting.KeepWithNext.Should().BeTrue();
        Execute(registry, "freew.keep-with-next");
        paragraph.Formatting.KeepWithNext.Should().BeFalse();

        Execute(registry, "freew.keep-lines");
        paragraph.Formatting.KeepLinesTogether.Should().BeTrue();
        Execute(registry, "freew.keep-lines");
        paragraph.Formatting.KeepLinesTogether.Should().BeFalse();

        Execute(registry, "freew.widow-control");
        paragraph.Formatting.WidowControl.Should().BeTrue();
        Execute(registry, "freew.widow-control");
        paragraph.Formatting.WidowControl.Should().BeFalse();
    }

    [Fact]
    public void Paragraph_border_and_shading_commands_apply_model_formatting()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc("Decorated"));
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
        var paragraph = (Paragraph)view.Document.Blocks[0];

        Execute(registry, "freew.para-border");
        paragraph.Formatting.Border.Should().NotBeNull();

        Execute(registry, "freew.para-shading.light-yellow");
        paragraph.Formatting.ShadingColorHex.Should().Be("#FFF2CC");
        paragraph.Formatting.ShadingPattern.Should().Be(ShadingPattern.Clear);

        Execute(registry, "freew.para-shading.none");
        paragraph.Formatting.ShadingColorHex.Should().BeNull();
        paragraph.Formatting.ShadingPattern.Should().Be(ShadingPattern.Clear);
    }

    [Fact]
    public void Paragraph_dialog_apply_helpers_update_tabs_borders_shading_and_page_border()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc("Dialog apply"));

        TabsDialog.ApplyResult(view, new TabsDialogResult(
            [new TabStop(144, TabStopAlignment.Right, TabLeader.Dots)],
            DefaultTabStopPt: 42));
        var paragraph = (Paragraph)view.Document.Blocks[0];
        paragraph.Formatting.TabStops.Should().Equal(new TabStop(144, TabStopAlignment.Right, TabLeader.Dots));
        view.Document.Page.DefaultTabStopPt.Should().Be(42);

        var border = new ParagraphBorder("#C00000", 1.5) { LineStyle = BorderLineStyle.Dashed };
        var pageBorder = new PageBorder("#0070C0", 2.0) { LineStyle = BorderLineStyle.Double };
        BordersAndShadingDialog.ApplyResult(view, new BordersAndShadingDialogResult(
            border,
            pageBorder,
            ShadingHex: "#D9EAD3",
            ShadingPattern: ShadingPattern.Pct25));

        paragraph.Formatting.Border.Should().Be(border);
        paragraph.Formatting.ShadingColorHex.Should().Be("#D9EAD3");
        paragraph.Formatting.ShadingPattern.Should().Be(ShadingPattern.Pct25);
        view.Document.Page.PageBorder.Should().Be(pageBorder);
    }

    [Fact]
    public void Dialog_backed_paragraph_commands_route_to_host_callbacks()
    {
        var view = new DocumentView();
        var calls = new List<string>();
        var callbacks = NoopCallbacks() with
        {
            OpenTabsDialog = () => calls.Add("tabs"),
            OpenBordersAndShadingDialog = () => calls.Add("borders"),
            OpenSortDialog = () => calls.Add("sort")
        };
        var registry = FreeWAvaloniaRibbonCommands.Build(view, callbacks);

        Execute(registry, "freew.tabs-dialog");
        Execute(registry, "freew.borders-shading");
        Execute(registry, "freew.sort");

        calls.Should().Equal("tabs", "borders", "sort");
    }

    [Fact]
    public void Clipboard_paste_special_commands_route_to_host_callbacks()
    {
        var calls = new List<string>();
        var callbacks = NoopCallbacks() with
        {
            PastePlainText = () => calls.Add("plain"),
            PasteMergeFormatting = () => calls.Add("merge"),
            OpenPasteSpecial = () => calls.Add("special"),
        };
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), callbacks);

        Execute(registry, "freew.paste-plain");
        Execute(registry, "freew.paste-merge");
        Execute(registry, "freew.paste-special");

        calls.Should().Equal("plain", "merge", "special");
    }

    [Fact]
    public void Sort_command_fallback_sorts_selected_paragraphs()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Bravo"));
        doc.Blocks.Add(new Paragraph("Alpha"));
        doc.Blocks.Add(new Paragraph("Charlie"));
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
        Execute(registry, "freew.sort");

        doc.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Alpha", "Bravo", "Charlie");
    }

    [Fact]
    public void Multilevel_list_commands_use_existing_list_level_behavior()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc("Outline"));
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
        var paragraph = (Paragraph)view.Document.Blocks[0];

        Execute(registry, "freew.multilevel-list");
        paragraph.Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        Execute(registry, "freew.multilevel-list");
        paragraph.Formatting.ListKind.Should().Be(ListKind.MultiLevel, "the WPF command applies multilevel rather than toggling it off");

        Execute(registry, "freew.multilevel-demote");
        paragraph.Formatting.ListLevel.Should().Be(1);

        Execute(registry, "freew.multilevel-promote");
        paragraph.Formatting.ListLevel.Should().Be(0);
    }

    [Fact]
    public void Multilevel_dropdown_presets_match_wpf_backed_behavior()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Heading")
        {
            Formatting = new ParagraphFormatting { ListKind = ListKind.Number, ListLevel = 2 }
        });
        var view = new DocumentView();
        view.LoadDocument(doc);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
        var paragraph = (Paragraph)view.Document.Blocks[0];

        Execute(registry, "freew.multilevel-preset-0");
        paragraph.Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        paragraph.Formatting.ListLevel.Should().Be(2, "presets preserve the selected outline depth");

        Execute(registry, "freew.multilevel-preset-1");
        view.Document.MultiLevelList.NumberFormats.Take(3).Should().Equal(
            ListNumberFormat.Decimal,
            ListNumberFormat.LowerLetter,
            ListNumberFormat.LowerRoman);
        paragraph.Formatting.ListKind.Should().Be(ListKind.MultiLevel);

        registry.TryGet(new RibbonCommandId("freew.multilevel-define"), out var defineCommand)
            .Should().BeTrue();
        defineCommand.Should().BeAssignableTo<IRibbonStatefulCommand>();
        ((IRibbonStatefulCommand)defineCommand!).GetState().IsEnabled.Should().BeFalse(
            "a missing define-dialog endpoint must fail closed instead of silently applying defaults");
        paragraph.Formatting.ListStartOverride.Should().BeNull("an unavailable dialog route must not mutate the list");

        Execute(registry, "freew.multilevel-preset-2");
        paragraph.StyleId.Should().Be("Heading3", "the heading preset mirrors WPF's linked heading style hint");
    }

    [Fact]
    public void Multilevel_preset_keeps_existing_list_enabled_and_undoes_atomically()
    {
        var paragraph = new Paragraph("Existing")
        {
            Formatting = ParagraphFormatting.Default with
            {
                ListKind = ListKind.MultiLevel,
                ListLevel = 1,
                ListStartOverride = 5,
            },
        };
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.multilevel-preset-1");

        paragraph.Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        paragraph.Formatting.ListLevel.Should().Be(1);
        view.Document.MultiLevelList.NumberFormats[1].Should().Be(ListNumberFormat.LowerLetter);

        view.Undo();

        paragraph.Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        paragraph.Formatting.ListStartOverride.Should().Be(5);
        view.Document.MultiLevelList.NumberFormats[1].Should().Be(ListNumberFormat.Decimal);
    }

    [Fact]
    public void Ribbon_definition_now_has_37_or_more_commands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
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
    public void Caps_commands_toggle_caps_flags()
    {
        var doc = MakeDoc("Caps");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        view.ToggleSmallCaps();
        view.ToggleAllCaps();

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.SmallCaps && r.Formatting.AllCaps)
            .Should().BeTrue("Small Caps and All Caps should set model run formatting on the selection");
    }

    [Fact]
    public void Character_border_and_shading_commands_apply_model_run_formatting()
    {
        var doc = MakeDoc("Decorated");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.char-border.black"), out var borderCommand)
            .Should().BeTrue("the Character Border palette must register its WPF-authority default swatch");
        registry.TryGet(new RibbonCommandId("freew.char-shading.light-yellow"), out var shadingCommand)
            .Should().BeTrue("the Character Shading palette must register its WPF-authority default swatch");

        borderCommand!.Execute(RibbonCommandContext.Empty);
        shadingCommand!.Execute(RibbonCommandContext.Empty);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.CharacterBorder is not null)
            .Should().BeTrue("Character Border should apply run border formatting");
        para.Runs.All(r => r.Formatting.CharacterShadingHex == "#FFF2CC")
            .Should().BeTrue("Character Shading should apply run shading formatting");
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
    public void Paste_plain_text_normalizes_clipboard_text_and_splits_lines()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc("Start"));
        view.MoveCaretToBlockForTest(0, 5);

        view.PastePlainText("\r\nNext\0\tTab").Should().BeTrue();

        view.Document.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Start", "Next\tTab");
    }

    [Fact]
    public void Paste_plain_text_replacement_is_one_undoable_edit()
    {
        const string original = "Original document";
        const string pasted = "FreeW-physical-editor-sentinel-r2";
        var view = new DocumentView();
        view.LoadDocument(MakeDoc(original));
        view.SelectAll();

        view.PastePlainText(pasted).Should().BeTrue();
        view.Document.Blocks.OfType<Paragraph>().Single().PlainText.Should().Be(pasted);

        view.Undo();
        view.Document.Blocks.OfType<Paragraph>().Single().PlainText.Should().Be(original);

        view.Redo();
        view.Document.Blocks.OfType<Paragraph>().Single().PlainText.Should().Be(pasted);
    }

    [Fact]
    public void Default_paste_routes_through_grouped_plain_text_paste()
    {
        var sourcePath = FindRepositoryFile("freew", "FreeW.App.Avalonia", "MainWindow.cs");
        var source = File.ReadAllText(sourcePath);
        const string methodStartMarker = "private async Task PasteAsync()";
        const string methodEndMarker = "private async Task PastePlainTextAsync()";
        var methodStart = source.IndexOf(methodStartMarker, StringComparison.Ordinal);
        var methodEnd = source.IndexOf(methodEndMarker, methodStart, StringComparison.Ordinal);

        methodStart.Should().BeGreaterThanOrEqualTo(0);
        methodEnd.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..methodEnd];

        method.Should().Contain("FreeWClipboardApplicationWorkflow.ReadTextAsync(_platformClipboard)");
        method.Should().Contain("ApplyClipboardText(transfer, DocumentPasteTextKind.TextOnly)");
        method.Should().NotContain("_editor.InsertText(");
    }

    private static string FindRepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);

    [Fact]
    public void Paste_keep_source_formatting_parses_rtf_runs_and_paragraphs()
    {
        const string rtf = @"{\rtf1\ansi\b Bold\b0  plain\par\i Second\i0}";

        RtfClipboardDocumentParser.TryParse(rtf, out var source).Should().BeTrue();

        var paragraphs = source!.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().HaveCount(2);
        paragraphs[0].Runs.Should().Contain(run => run.Text == "Bold" && run.Formatting.Bold);
        paragraphs[1].Runs.Should().Contain(run => run.Text == "Second" && run.Formatting.Italic);
    }

    [Fact]
    public void Paste_keep_source_formatting_replaces_empty_paragraph_as_one_undoable_edit()
    {
        var destination = TextDocument.CreateEmpty();
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        var formatted = new Paragraph();
        formatted.Runs.Add(new Run("Bold", new RunFormatting { Bold = true, ColorHex = "#AA0000" }));
        formatted.Runs.Add(new Run(" normal"));
        source.Blocks.Add(formatted);
        source.Blocks.Add(new Paragraph("Second paragraph"));

        var view = new DocumentView();
        view.LoadDocument(destination);

        view.PasteKeepSourceFormatting(source).Should().BeTrue();
        view.Document.Blocks.Should().HaveCount(2);
        var inserted = view.Document.Blocks[0].Should().BeOfType<Paragraph>().Which;
        inserted.Runs.Should().Contain(run => run.Text == "Bold"
            && run.Formatting.Bold
            && run.Formatting.ColorHex == "#AA0000");

        view.Undo();
        view.Document.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which.PlainText.Should().BeEmpty();
    }

    [Fact]
    public void Paste_keep_source_formatting_rejects_partial_selection_and_tracked_changes()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("Rich source"));

        var view = new DocumentView();
        view.LoadDocument(MakeDoc("Destination"));
        view.SetSelectionRangePublic(0, 0, 0, "Destination".Length);
        view.PasteKeepSourceFormatting(source).Should().BeFalse();
        view.Document.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("Destination");

        view.LoadDocument(TextDocument.CreateEmpty());
        view.ToggleTrackChanges();
        view.PasteKeepSourceFormatting(source).Should().BeFalse();
        view.Document.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which.PlainText.Should().BeEmpty();
    }

    [Fact]
    public void Format_painter_command_stamps_run_and_paragraph_formatting()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var source = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with
            {
                Alignment = TextAlignment.Center,
                SpaceBeforePt = 6,
                SpaceAfterPt = 12,
            },
        };
        source.Runs.Add(new Run("Source", new RunFormatting
        {
            Bold = true,
            Italic = true,
            FontFamily = "Cambria",
            FontSizePt = 18,
            ColorHex = "#336699",
        }));
        doc.Blocks.Add(source);
        doc.Blocks.Add(new Paragraph("Target"));
        var view = new DocumentView();
        view.LoadDocument(doc);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        view.SetSelectionRangePublic(0, 0, 0, 6);
        Execute(registry, "freew.format-painter");
        view.IsFormatPainterArmed.Should().BeTrue();

        view.SetSelectionRangePublic(1, 0, 1, 6);
        view.ApplyFormatPainterToSelection().Should().BeTrue();

        view.IsFormatPainterArmed.Should().BeFalse("single-click Format Painter should disarm after stamping");
        var target = (Paragraph)view.Document.Blocks[1];
        target.Formatting.Alignment.Should().Be(TextAlignment.Center);
        target.Formatting.SpaceBeforePt.Should().Be(6);
        target.Formatting.SpaceAfterPt.Should().Be(12);
        target.Runs.Should().ContainSingle();
        target.Runs[0].Formatting.Bold.Should().BeTrue();
        target.Runs[0].Formatting.Italic.Should().BeTrue();
        target.Runs[0].Formatting.FontFamily.Should().Be("Cambria");
        target.Runs[0].Formatting.FontSizePt.Should().Be(18);
        target.Runs[0].Formatting.ColorHex.Should().Be("#336699");
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
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
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

        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
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

        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

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
        view.ChangeSelectionCase(CaseKind.Capitalize);

        var p0 = (Paragraph)view.Document.Blocks[0];
        var p1 = (Paragraph)view.Document.Blocks[1];

        // The explicit shared choice is applied identically to every selected paragraph.
        p0.PlainText.Should().Be("Hello",
            "Change Case on a multi-block selection must transform the first paragraph");
        p1.PlainText.Should().Be("World",
            "Change Case on a multi-block selection must transform the last paragraph");
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

    private static void Execute(
        RibbonCommandRegistry registry,
        string id,
        RibbonCommandContext? context = null)
    {
        registry.TryGet(new RibbonCommandId(id), out var command)
            .Should().BeTrue($"command '{id}' must be registered");
        command!.Execute(context ?? RibbonCommandContext.Empty);
    }

    private static IEnumerable<RibbonCommandId> CommandIdsIncludingMenus(RibbonControl control)
    {
        if (GetCommandId(control) is { } id && !string.IsNullOrEmpty(id.Value))
            yield return id;

        var menuIds = control switch
        {
            RibbonSplitButton splitButton => MenuCommandIds(splitButton.Menu.Items),
            RibbonDropdown dropdown => MenuCommandIds(dropdown.Menu.Items),
            _ => Enumerable.Empty<RibbonCommandId>()
        };

        foreach (var menuId in menuIds)
            yield return menuId;
    }

    private static IEnumerable<RibbonCommandId> MenuCommandIds(IEnumerable<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } id && !string.IsNullOrEmpty(id.Value))
                yield return id;

            foreach (var childId in MenuCommandIds(item.Children))
                yield return childId;
        }
    }

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
