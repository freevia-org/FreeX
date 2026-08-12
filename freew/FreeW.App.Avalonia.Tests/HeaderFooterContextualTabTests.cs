using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

public sealed class HeaderFooterContextualTabTests
{
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
            ToggleOrientation: () => { },
            ApplyMarginPreset: _ => { },
            ApplyPaperSize: _ => { },
            InsertPicture: () => { },
            OpenWordCountDialog: () => { },
            ApplyZoom: (_, _) => { });

    [Fact]
    public void Avalonia_definition_exposes_header_footer_design_contextual_tab()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);

        var tab = definition.FindTab("header-footer-design");

        tab.Should().NotBeNull("Avalonia has editable header/footer regions and should expose the Word-like contextual surface");
        tab!.Context!.ActivationKey.Should().Be(HeaderFooterRibbonContextSource.HeaderFooterContextKey);
        tab.Groups.Select(g => g.Id).Should()
            .Equal("hf-header-footer", "hf-insert", "hf-navigation", "hf-options", "hf-position", "hf-close");
    }

    [Fact]
    public void Header_footer_contextual_commands_are_registered()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());

        var expected = new[]
        {
            "freew.hf-edit-header",
            "freew.hf-edit-footer",
            "freew.hf-edit-first-header",
            "freew.hf-edit-first-footer",
            "freew.hf-edit-even-header",
            "freew.hf-edit-even-footer",
            "freew.hf-go-to-header",
            "freew.hf-go-to-footer",
            "freew.hf-close",
            "freew.hf-different-first-page",
            "freew.hf-different-odd-even",
            "freew.hf-header-from-top",
            "freew.hf-footer-from-bottom",
            "freew.hf-insert-page-number",
            "freew.hf-insert-page-number-footer",
            "freew.hf-insert-datetime",
            "freew.hf-insert-field",
        };

        foreach (var id in expected)
            registry.TryGet(new RibbonCommandId(id), out _).Should().BeTrue($"{id} should be backed in Avalonia");
    }

    [Fact]
    public void Header_footer_options_and_distances_report_current_model_state()
    {
        var initial = new TextDocument();
        initial.Page.DifferentFirstPage = true;
        initial.Page.DifferentOddEvenPages = false;
        initial.Page.HeaderDistancePt = 22.5;
        initial.Page.FooterDistancePt = 31;
        var view = new DocumentView();
        view.LoadDocument(initial);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        State(registry, "freew.hf-different-first-page").IsChecked.Should().BeTrue();
        State(registry, "freew.hf-different-odd-even").IsChecked.Should().BeFalse();
        State(registry, "freew.hf-header-from-top").Value.Should()
            .Be(HeaderFooterDialogPlanner.FormatDistance(22.5));
        State(registry, "freew.hf-footer-from-bottom").Value.Should()
            .Be(HeaderFooterDialogPlanner.FormatDistance(31));

        Execute(registry, "freew.hf-different-first-page");
        Execute(registry, "freew.hf-different-odd-even");
        Execute(registry, "freew.hf-header-from-top", RibbonCommandContext.ForSelectedValue("27.25"));
        Execute(registry, "freew.hf-footer-from-bottom", RibbonCommandContext.ForSelectedValue("44"));

        State(registry, "freew.hf-different-first-page").IsChecked.Should().BeFalse();
        State(registry, "freew.hf-different-odd-even").IsChecked.Should().BeTrue();
        State(registry, "freew.hf-header-from-top").Value.Should()
            .Be(HeaderFooterDialogPlanner.FormatDistance(27.25));
        State(registry, "freew.hf-footer-from-bottom").Value.Should()
            .Be(HeaderFooterDialogPlanner.FormatDistance(44));

        var replacement = new TextDocument();
        replacement.Page.DifferentFirstPage = true;
        replacement.Page.DifferentOddEvenPages = true;
        replacement.Page.HeaderDistancePt = 18;
        replacement.Page.FooterDistancePt = 24;
        view.LoadDocument(replacement);

        State(registry, "freew.hf-different-first-page").IsChecked.Should().BeTrue();
        State(registry, "freew.hf-different-odd-even").IsChecked.Should().BeTrue();
        State(registry, "freew.hf-header-from-top").Value.Should()
            .Be(HeaderFooterDialogPlanner.FormatDistance(18));
        State(registry, "freew.hf-footer-from-bottom").Value.Should()
            .Be(HeaderFooterDialogPlanner.FormatDistance(24));
    }

    [Fact]
    public void Header_footer_context_source_tracks_header_footer_caret()
    {
        var view = new DocumentView();
        var source = new HeaderFooterRibbonContextSource(view);

        source.Current.IsActive(HeaderFooterRibbonContextSource.HeaderFooterContextKey).Should().BeFalse();

        view.PlaceCaretInHeaderFooter(footer: false);

        source.Current.IsActive(HeaderFooterRibbonContextSource.HeaderFooterContextKey).Should().BeTrue();

        view.ExitHeaderFooterCaret();

        source.Current.IsActive(HeaderFooterRibbonContextSource.HeaderFooterContextKey).Should().BeFalse();
    }

    [Fact]
    public void Header_footer_contextual_commands_mutate_existing_model_state()
    {
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.hf-edit-header");
        view.IsHeaderFooterCaretActive.Should().BeTrue("Edit Header should enter the editable header region");

        Execute(registry, "freew.hf-insert-page-number-footer");
        view.Document.Footer.Should().NotBeNull();
        view.Document.Footer!.Paragraphs.SelectMany(p => p.Runs)
            .Should().Contain(r => r.FieldKind == RunFieldKind.PageNumber);

        Execute(registry, "freew.hf-different-odd-even");
        view.Document.Page.DifferentOddEvenPages.Should().BeTrue();

        Execute(registry, "freew.hf-close");
        view.IsHeaderFooterCaretActive.Should().BeFalse("Close Header and Footer should exit the header/footer caret");
    }

    [Fact]
    public void Header_footer_distance_combo_commands_update_page_settings()
    {
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.hf-header-from-top", RibbonCommandContext.ForSelectedValue("54"));
        Execute(registry, "freew.hf-footer-from-bottom", RibbonCommandContext.ForSelectedValue("72"));

        view.Document.Page.HeaderDistancePt.Should().Be(54);
        view.Document.Page.FooterDistancePt.Should().Be(72);
    }

    [Fact]
    public async Task Production_MainWindow_top_level_header_footer_uses_prompt_apply_and_cancel()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freew-wave38-");
        var settingsPath = Path.Combine(temporaryDirectory.Path, "settings.json");
        {
            await Session.Dispatch(() =>
            {
                var window = new MainWindow(
                    [],
                    new FreeWOptions(),
                    ApplicationOptionsStore<FreeWOptions>.ForPath(settingsPath),
                    askHeaderFooterText: (footer, seed) =>
                        Task.FromResult<string?>(footer ? null : "Header from Avalonia prompt"));

                window.RibbonRegistryForTests!.TryGet(new RibbonCommandId("freew.header"), out var header)
                    .Should().BeTrue();
                header!.Execute(RibbonCommandContext.Empty);

                window.Editor.Document.Header.Should().NotBeNull();
                window.Editor.Document.Header!.PlainText.Should().Be("Header from Avalonia prompt");

                window.RibbonRegistryForTests.TryGet(new RibbonCommandId("freew.footer"), out var footer)
                    .Should().BeTrue();
                footer!.Execute(RibbonCommandContext.Empty);

                window.Editor.Document.Footer.Should().BeNull("Cancel must leave the footer untouched");
            }, CancellationToken.None);
        }
    }

    [Fact]
    public void Insert_page_number_format_command_updates_page_settings()
    {
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.page-number-format", RibbonCommandContext.ForSelectedValue(
            PageNumberFormatDialogPlanner.BuildCommandValue(PageNumberFormat.UpperLetter, 5)));

        view.Document.Page.PageNumberFormat.Should().Be(PageNumberFormat.UpperLetter);
        view.Document.Page.PageNumberStartAt.Should().Be(5);
    }

    [Fact]
    public void Insert_page_number_current_position_uses_formatted_page_number()
    {
        var view = new DocumentView();
        view.Document.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        view.Document.Page.PageNumberStartAt = 4;
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.page-number-current");

        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Should().Contain(r => r.FieldKind == RunFieldKind.PageNumber && r.Text == "IV");
    }

    private static void Execute(RibbonCommandRegistry registry, string id) =>
        Execute(registry, id, RibbonCommandContext.Empty);

    private static void Execute(RibbonCommandRegistry registry, string id, RibbonCommandContext context)
    {
        registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
        command!.Execute(context);
    }

    private static RibbonCommandState State(RibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
        command.Should().BeAssignableTo<IRibbonStatefulCommand>();
        return ((IRibbonStatefulCommand)command!).GetState();
    }
}
