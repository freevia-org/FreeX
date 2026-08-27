using System.Linq;
using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using WpfParagraph = System.Windows.Documents.Paragraph;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for freew-headers-footers-diff F1: the Insert tab's plain-text "Header" and
/// "Footer" ribbon commands (<see cref="FreeWRibbonCommands"/>'s private HeaderFooterCommand) must read
/// and write the <see cref="SectionHeadersFooters"/> that owns the caret's OWN section's default
/// header/footer slot (via <see cref="HeaderFooterPagePlanner.ResolveSlotOwner"/> and
/// <see cref="DocumentView.CurrentSectionIndex"/>), not always
/// <see cref="TextDocument.FinalSectionHeadersFooters"/> (the document's LAST section, which
/// <see cref="TextDocument.Header"/>/<see cref="TextDocument.Footer"/> are a passthrough view onto).
/// Before this fix, typing a header while the caret sat in an earlier section (e.g. before a Next-Page
/// section break) silently wrote into -- and could overwrite -- the final section's header instead.
/// See <see cref="HeaderFooterRibbonToggleSectionScopeTests"/> for the sibling Header &amp; Footer
/// Design tab toggle fix that resolved the identical caret-vs-final-section class of bug for
/// <see cref="PageSettings"/>. Runs on STA (WPF FlowDocument/caret).
/// </summary>
public sealed class HeaderFooterInsertCommandSectionScopeTests
{
    private static DocumentView TwoSectionView(out HeaderFooter finalHeaderBefore)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Section one body.")
        {
            SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage)
        });
        doc.Blocks.Add(new Paragraph("Section two (final) body."));

        finalHeaderBefore = new HeaderFooter("Final section original header");
        doc.Header = finalHeaderBefore;

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static IRibbonCommand HeaderCommand(DocumentView view, string typedText)
    {
        var registry = FreeWRibbonCommands.Build(
            view,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty,
            new FreeWWpfRibbonNativeExecutionPorts(
                AskHeaderFooterText: (_, _) => typedText));
        var id = FreeWRibbonCommandWorkflow.GetPrimaryCommandId(FreeWRibbonCommandAction.Header);
        registry.TryGet(id, out var command).Should().BeTrue();
        return command!;
    }

    [StaFact]
    public void InsertHeaderCommand_WithCaretInFirstSection_WritesTheFirstSectionOwnHeader()
    {
        var view = TwoSectionView(out var finalHeaderBefore);
        var firstParagraph = view.Document.Blocks.OfType<WpfParagraph>().First();
        view.CaretPosition = firstParagraph.ContentStart;

        var command = HeaderCommand(view, "First section header");
        command.Execute(RibbonCommandContext.Empty);

        // The edit must land on the caret's OWN (first, non-final) section.
        var firstSection = view.Model.Sections[0];
        firstSection.HeadersFooters.Header.Should().NotBeNull();
        firstSection.HeadersFooters.Header!.PlainText.Should().Be("First section header");

        // The final section's pre-existing header must be untouched -- before the fix this command
        // always wrote through TextDocument.Header (a passthrough to FinalSectionHeadersFooters),
        // silently replacing it even though the caret never visited the final section.
        view.Model.Header.Should().BeSameAs(finalHeaderBefore);
        view.Model.Header!.PlainText.Should().Be("Final section original header");
    }

    [StaFact]
    public void InsertHeaderCommand_WithCaretInFinalSection_StillWritesTheFinalSectionHeader()
    {
        // Sibling no-regression case: a caret already in the (final) section must keep writing
        // TextDocument.Header / FinalSectionHeadersFooters exactly as before this fix.
        var view = TwoSectionView(out _);
        var secondParagraph = view.Document.Blocks.OfType<WpfParagraph>().Last();
        view.CaretPosition = secondParagraph.ContentStart;

        var command = HeaderCommand(view, "Updated final header");
        command.Execute(RibbonCommandContext.Empty);

        view.Model.Header.Should().NotBeNull();
        view.Model.Header!.PlainText.Should().Be("Updated final header");
        view.Model.Sections[0].HeadersFooters.Header.Should().BeNull("caret never visited the first section");
    }

    [StaFact]
    public void InsertHeaderCommand_SingleSectionDocument_StillWritesDocumentHeader()
    {
        // Sibling no-regression case: a plain single-section document (the overwhelmingly common case)
        // must keep behaving exactly as before this fix.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Only section."));

        var view = new DocumentView();
        view.LoadModel(doc);
        view.CaretPosition = view.Document.Blocks.OfType<WpfParagraph>().Single().ContentStart;

        var command = HeaderCommand(view, "Single section header");
        command.Execute(RibbonCommandContext.Empty);

        view.Model.Header.Should().NotBeNull();
        view.Model.Header!.PlainText.Should().Be("Single section header");
    }
}
