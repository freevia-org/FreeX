using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for the Header &amp; Footer Design ribbon tab's Different-First-Page /
/// Different-Odd-Even / header- and footer-distance toggles seeding their displayed
/// (<see cref="IRibbonStatefulCommand.GetState"/>) state from the caret's actual section, via
/// <see cref="DocumentView.CurrentSectionPageSettings"/>, instead of unconditionally
/// <see cref="TextDocument.Page"/> (the document's final section). Before this fix, opening the Header
/// &amp; Footer Design tab while the caret sat in a non-final section showed the FINAL section's
/// checked/unchecked state and distance value even though clicking the toggle commits to the caret's own
/// section (<see cref="DocumentView.ApplyPageSettings"/>) -- so a toggle shown "off" that was actually
/// already "on" for the current section got turned back off instead of on, the opposite of the user's
/// intent. See <see cref="PageSetupFamilySeedSectionScopeTests"/> for the sibling Page-Setup-family
/// dialogs covering the same <see cref="DocumentView.CurrentSectionPageSettings"/> choke point.
/// Runs on STA (WPF FlowDocument/caret).
/// </summary>
public sealed class HeaderFooterRibbonToggleSectionScopeTests
{
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
        view.LoadModel(doc);
        return view;
    }

    private static IRibbonStatefulCommand Stateful(DocumentView view, FreeWRibbonCommandAction action)
    {
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        var id = FreeWRibbonCommandWorkflow.GetPrimaryCommandId(action);
        registry.TryGet(id, out var command).Should().BeTrue();
        return command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
    }

    [StaFact]
    public void DifferentFirstPageToggle_WithCaretInFirstSection_ReflectsFirstSectionNotFinal()
    {
        var view = TwoSectionView(out _);
        var firstParagraph = view.Document.Blocks.OfType<WpfParagraph>().First();
        view.CaretPosition = firstParagraph.ContentStart;

        var toggle = Stateful(view, FreeWRibbonCommandAction.HfDifferentFirstPage);

        // The caret's own (first) section has DifferentFirstPage = true. Before the fix this read the
        // document's final section (false) instead.
        toggle.GetState().IsChecked.Should().BeTrue();
    }

    [StaFact]
    public void DifferentOddEvenToggle_WithCaretInFirstSection_ReflectsFirstSectionNotFinal()
    {
        var view = TwoSectionView(out _);
        var firstParagraph = view.Document.Blocks.OfType<WpfParagraph>().First();
        view.CaretPosition = firstParagraph.ContentStart;

        var toggle = Stateful(view, FreeWRibbonCommandAction.HfDifferentOddEven);

        toggle.GetState().IsChecked.Should().BeTrue();
    }

    [StaFact]
    public void HeaderFromTopDistance_WithCaretInFirstSection_ReflectsFirstSectionNotFinal()
    {
        var view = TwoSectionView(out _);
        var firstParagraph = view.Document.Blocks.OfType<WpfParagraph>().First();
        view.CaretPosition = firstParagraph.ContentStart;

        var distance = Stateful(view, FreeWRibbonCommandAction.HfHeaderFromTop);

        distance.GetState().Value.Should().Be("11");
    }

    [StaFact]
    public void ClickingDifferentFirstPageToggle_WithCaretInFirstSection_TurnsTheCaretsOwnFlagOff()
    {
        // Reproduces the actual user-visible harm: the toggle displays the caret section's true state
        // (checked), and clicking it must flip that SAME section off -- not silently no-op because the
        // display disagreed with the write target.
        var view = TwoSectionView(out var section0Page);
        var firstParagraph = view.Document.Blocks.OfType<WpfParagraph>().First();
        view.CaretPosition = firstParagraph.ContentStart;

        var toggle = Stateful(view, FreeWRibbonCommandAction.HfDifferentFirstPage);
        toggle.GetState().IsChecked.Should().BeTrue();

        toggle.Execute(RibbonCommandContext.Empty);

        section0Page.DifferentFirstPage.Should().BeFalse();
        view.Model.Page.DifferentFirstPage.Should().BeFalse("final section was already off and must stay untouched");
    }

    [StaFact]
    public void DifferentFirstPageToggle_WithCaretInFinalSection_StillReflectsFinalSection()
    {
        // Sibling no-regression case: caret already in the (final) section must keep reading
        // TextDocument.Page exactly as before this fix -- the overwhelmingly common single-section and
        // caret-at-end cases.
        var view = TwoSectionView(out _);
        var secondParagraph = view.Document.Blocks.OfType<WpfParagraph>().Last();
        view.CaretPosition = secondParagraph.ContentStart;

        var toggle = Stateful(view, FreeWRibbonCommandAction.HfDifferentFirstPage);

        toggle.GetState().IsChecked.Should().BeFalse();
    }

    [StaFact]
    public void DifferentFirstPageToggle_SingleSectionDocument_StillReflectsDocumentPageSettings()
    {
        // Sibling no-regression case: a plain single-section document (the default) must keep reading
        // TextDocument.Page exactly as before this fix.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Only section."));
        doc.Page.DifferentFirstPage = true;

        var view = new DocumentView();
        view.LoadModel(doc);
        view.CaretPosition = view.Document.Blocks.OfType<WpfParagraph>().Single().ContentStart;

        var toggle = Stateful(view, FreeWRibbonCommandAction.HfDifferentFirstPage);

        toggle.GetState().IsChecked.Should().BeTrue();
    }
}
