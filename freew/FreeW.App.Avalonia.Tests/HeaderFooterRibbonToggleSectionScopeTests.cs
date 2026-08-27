using Avalonia.Headless;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Avalonia-side sibling of <c>FreeW.App.Host.Tests.HeaderFooterRibbonToggleSectionScopeTests</c>: the
/// Header &amp; Footer Design ribbon tab's Different-First-Page / Different-Odd-Even / header- and
/// footer-distance toggles must seed their displayed <see cref="IRibbonStatefulCommand.GetState"/> state
/// from the caret's actual section (via <see cref="DocumentView.CurrentSectionPageSettings"/>), not
/// unconditionally <see cref="TextDocument.Page"/> (the document's final section). Before this fix,
/// <c>FreeWAvaloniaRibbonCommands</c> wired the ports' <c>GetPageSettings</c> to <c>editor.Document.Page</c>
/// directly. See <see cref="HeaderFooterContextualTabTests.Header_footer_options_and_distances_report_current_model_state"/>
/// for the sibling single-section case (unaffected by this fix, since the only section IS the final one).
/// </summary>
public sealed class HeaderFooterRibbonToggleSectionScopeTests
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

    private static DocumentView TwoSectionView(out PageSettings section0Page)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        section0Page = new PageSettings
        {
            DifferentFirstPage = true,
            DifferentOddEvenPages = true,
            HeaderDistancePt = 11,
            FooterDistancePt = 13,
        };
        doc.Blocks.Add(new Paragraph("Section one body (title page).")
        {
            SectionBreak = new Section(section0Page, SectionBreakKind.NextPage)
        });
        doc.Blocks.Add(new Paragraph("Section two (final) body."));
        doc.Page.DifferentFirstPage = false;
        doc.Page.DifferentOddEvenPages = false;
        doc.Page.HeaderDistancePt = 36;
        doc.Page.FooterDistancePt = 36;

        var view = new DocumentView();
        view.LoadDocument(doc);
        return view;
    }

    private static RibbonCommandState State(RibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
        command.Should().BeAssignableTo<IRibbonStatefulCommand>();
        return ((IRibbonStatefulCommand)command!).GetState();
    }

    private static void Execute(RibbonCommandRegistry registry, string id) =>
        Execute(registry, id, RibbonCommandContext.Empty);

    private static void Execute(RibbonCommandRegistry registry, string id, RibbonCommandContext context)
    {
        registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
        command!.Execute(context);
    }

    [Fact]
    public void DifferentFirstPageToggle_WithCaretInFirstSection_ReflectsFirstSectionNotFinal()
    {
        var view = TwoSectionView(out _);
        view.MoveCaretToBlockForTest(0, 0);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        // The caret's own (first) section has DifferentFirstPage = true. Before the fix this read the
        // document's final section (false) instead.
        State(registry, "freew.hf-different-first-page").IsChecked.Should().BeTrue();
    }

    [Fact]
    public void DifferentOddEvenToggle_WithCaretInFirstSection_ReflectsFirstSectionNotFinal()
    {
        var view = TwoSectionView(out _);
        view.MoveCaretToBlockForTest(0, 0);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        State(registry, "freew.hf-different-odd-even").IsChecked.Should().BeTrue();
    }

    [Fact]
    public void HeaderFromTopDistance_WithCaretInFirstSection_ReflectsFirstSectionNotFinal()
    {
        var view = TwoSectionView(out _);
        view.MoveCaretToBlockForTest(0, 0);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        State(registry, "freew.hf-header-from-top").Value.Should().Be("11");
    }

    [Fact]
    public void ClickingDifferentFirstPageToggle_WithCaretInFirstSection_TurnsTheCaretsOwnFlagOff()
    {
        // Reproduces the actual user-visible harm: the toggle displays the caret section's true state
        // (checked), and clicking it must flip that SAME section off -- not silently no-op because the
        // display disagreed with the write target.
        var view = TwoSectionView(out var section0Page);
        view.MoveCaretToBlockForTest(0, 0);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
        State(registry, "freew.hf-different-first-page").IsChecked.Should().BeTrue();

        Execute(registry, "freew.hf-different-first-page");

        section0Page.DifferentFirstPage.Should().BeFalse();
        view.Document.Page.DifferentFirstPage.Should().BeFalse("final section was already off and must stay untouched");
    }

    [Fact]
    public void DifferentFirstPageToggle_WithCaretInFinalSection_StillReflectsFinalSection()
    {
        // Sibling no-regression case: caret already in the (final) section must keep reading
        // TextDocument.Page exactly as before this fix.
        var view = TwoSectionView(out _);
        var finalBlockIndex = view.Document.Blocks.Count - 1;
        view.MoveCaretToBlockForTest(finalBlockIndex, 0);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        State(registry, "freew.hf-different-first-page").IsChecked.Should().BeFalse();
    }

    [Fact]
    public void DifferentFirstPageToggle_SingleSectionDocument_StillReflectsDocumentPageSettings()
    {
        // Sibling no-regression case: a plain single-section document (the default) must keep reading
        // TextDocument.Page exactly as before this fix.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Only section."));
        doc.Page.DifferentFirstPage = true;

        var view = new DocumentView();
        view.LoadDocument(doc);
        view.MoveCaretToBlockForTest(0, 0);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        State(registry, "freew.hf-different-first-page").IsChecked.Should().BeTrue();
    }
}
