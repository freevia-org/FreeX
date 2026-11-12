using System.Linq;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// The docked Header/Footer pane edits the section containing the caret, walks sections with
/// Previous/Next, and toggles Link to Previous (Word's Header &amp; Footer > Navigation group).
/// </summary>
public sealed class HeaderFooterSectionPaneTests
{
    [StaFact]
    public void Pane_TargetsTheSectionContainingTheCaret_NotTheFinalSection()
    {
        Run(window =>
        {
            var body = window.BodyEditorForTests;
            body.LoadModel(ThreeSections(firstHeader: "one", finalHeader: "three"));
            body.MoveCaretToBlockForTest(0, 0);

            window.OpenHeaderFooterPaneForTests("header");

            window.HeaderFooterSectionSlotForTests!.SectionIndex.Should().Be(0);
            PaneText(window).Should().Be("one");
            window.HeaderFooterPaneLabelForTests.Should().Contain("-Section 1-").And.NotContain("Same as Previous");

            TypeInPane(window, " edited");
            window.CloseHeaderFooterPaneForTests();

            body.Model.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("one edited");
            body.Model.Sections[2].HeadersFooters.Header!.PlainText.Should().Be("three");
        });
    }

    [StaFact]
    public void LinkedSection_ShowsSameAsPrevious_AndEditingItEditsTheSharedHeader()
    {
        Run(window =>
        {
            var body = window.BodyEditorForTests;
            body.LoadModel(ThreeSections(firstHeader: "one", finalHeader: "three"));
            body.MoveCaretToBlockForTest(1, 0);

            window.OpenHeaderFooterPaneForTests("header");

            var view = window.HeaderFooterSectionSlotForTests!;
            view.SectionIndex.Should().Be(1);
            view.IsLinkedToPrevious.Should().BeTrue();
            PaneText(window).Should().Be("one", "a linked section shows the header it inherits");
            window.HeaderFooterPaneLabelForTests.Should().Contain("-Section 2-").And.Contain("Same as Previous");

            TypeInPane(window, "!");
            window.CloseHeaderFooterPaneForTests();

            body.Model.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("one!");
            body.Model.Sections[1].HeadersFooters.Header.Should().BeNull("editing must not break the link");
        });
    }

    [StaFact]
    public void OpeningAndClosingWithoutTyping_LeavesEverySectionUntouched()
    {
        Run(window =>
        {
            var body = window.BodyEditorForTests;
            body.LoadModel(ThreeSections(firstHeader: "one", finalHeader: "three"));
            var first = body.Model.Sections[0].HeadersFooters.Header;
            body.MoveCaretToBlockForTest(1, 0);

            window.OpenHeaderFooterPaneForTests("footer");
            window.MoveHeaderFooterSectionForTests(+1);
            window.MoveHeaderFooterSectionForTests(-1);
            window.CloseHeaderFooterPaneForTests();

            body.Model.Sections[0].HeadersFooters.Header.Should().BeSameAs(first);
            body.Model.Sections.Should().OnlyContain(section => section.HeadersFooters.Footer == null);
            body.Model.Sections[1].HeadersFooters.Header.Should().BeNull();
        });
    }

    [StaFact]
    public void UnlinkingCopiesTheInheritedHeader_SoTheSectionCanDiverge_AndRelinkingFollowsAgain()
    {
        Run(window =>
        {
            var body = window.BodyEditorForTests;
            body.LoadModel(ThreeSections(firstHeader: "one", finalHeader: null));
            body.MoveCaretToBlockForTest(1, 0);
            window.OpenHeaderFooterPaneForTests("header");

            window.ToggleHeaderFooterLinkToPreviousForTests();

            window.HeaderFooterSectionSlotForTests!.IsLinkedToPrevious.Should().BeFalse();
            window.HeaderFooterPaneLabelForTests.Should().NotContain("Same as Previous");
            PaneText(window).Should().Be("one", "unlinking starts from a copy of the inherited header");

            TypeInPane(window, " two");
            window.CloseHeaderFooterPaneForTests();
            body.Model.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("one");
            body.Model.Sections[1].HeadersFooters.Header!.PlainText.Should().Be("one two");
            body.Model.Sections[2].HeadersFooters.Header.Should().BeNull("section 3 follows section 2");

            body.MoveCaretToBlockForTest(1, 0);
            window.OpenHeaderFooterPaneForTests("header");
            window.ToggleHeaderFooterLinkToPreviousForTests();

            window.HeaderFooterSectionSlotForTests!.IsLinkedToPrevious.Should().BeTrue();
            PaneText(window).Should().Be("one");
            body.Model.Sections[1].HeadersFooters.Header.Should().BeNull();
        });
    }

    [StaFact]
    public void PreviousAndNext_WalkTheSections_AndStopAtTheDocumentEnds()
    {
        Run(window =>
        {
            var body = window.BodyEditorForTests;
            body.LoadModel(ThreeSections(firstHeader: "one", finalHeader: "three"));
            body.MoveCaretToBlockForTest(0, 0);
            window.OpenHeaderFooterPaneForTests("header");

            window.MoveHeaderFooterSectionForTests(-1);
            window.HeaderFooterSectionSlotForTests!.SectionIndex.Should().Be(0, "already at the first section");

            window.MoveHeaderFooterSectionForTests(+1);
            window.HeaderFooterSectionSlotForTests!.SectionIndex.Should().Be(1);
            body.CurrentSectionIndex().Should().Be(1, "section-scoped options follow the section being edited");

            window.MoveHeaderFooterSectionForTests(+1);
            window.HeaderFooterSectionSlotForTests!.SectionIndex.Should().Be(2);
            PaneText(window).Should().Be("three");
            window.HeaderFooterSectionSlotForTests!.HasNextSection.Should().BeFalse();

            window.MoveHeaderFooterSectionForTests(+1);
            window.HeaderFooterSectionSlotForTests!.SectionIndex.Should().Be(2);
        });
    }

    [StaFact]
    public void RibbonCommands_AreBacked_AndLinkToPreviousReportsCheckedState()
    {
        Run(window =>
        {
            var body = window.BodyEditorForTests;
            body.LoadModel(ThreeSections(firstHeader: "one", finalHeader: null));
            body.MoveCaretToBlockForTest(1, 0);
            window.OpenHeaderFooterPaneForTests("header");
            var commands = HeaderFooterRibbonWorkflow.CreateSectionNavigationCommands(
                new HeaderFooterSectionNavigationPorts(
                    GetActiveSlot: () => window.HeaderFooterSectionSlotForTests,
                    MoveToAdjacentSection: window.MoveHeaderFooterSectionForTests,
                    ToggleLinkToPrevious: window.ToggleHeaderFooterLinkToPreviousForTests));

            commands.LinkToPrevious.GetState().Should().BeEquivalentTo(
                new Free.Shared.Ribbon.RibbonCommandState(IsEnabled: true, IsChecked: true));
            commands.PreviousSection.GetState().IsEnabled.Should().BeTrue();
            commands.NextSection.GetState().IsEnabled.Should().BeTrue();

            commands.LinkToPrevious.Execute(Free.Shared.Ribbon.RibbonCommandContext.Empty);
            commands.LinkToPrevious.GetState().IsChecked.Should().BeFalse();
            body.Model.Sections[1].HeadersFooters.Header!.PlainText.Should().Be("one");

            commands.PreviousSection.Execute(Free.Shared.Ribbon.RibbonCommandContext.Empty);
            window.HeaderFooterSectionSlotForTests!.SectionIndex.Should().Be(0);
            commands.PreviousSection.GetState().IsEnabled.Should().BeFalse();
            commands.LinkToPrevious.GetState().IsEnabled.Should().BeFalse("section 1 cannot link");
        });
    }

    [StaFact]
    public void HeadlessRegistry_BacksTheNewNavigationCommands_OnTheCaretSection()
    {
        var view = new FreeW.App.Host.Editing.DocumentView();
        view.LoadModel(ThreeSections(firstHeader: "one", finalHeader: null));
        view.MoveCaretToBlockForTest(1, 0);
        var registry = FreeWRibbonCommands.Build(view, new Free.Shared.Ribbon.RibbonStateStore());

        foreach (var id in new[] { "freew.hf-previous-section", "freew.hf-next-section", "freew.hf-link-to-previous" })
            registry.TryGet(id, out _).Should().BeTrue($"{id} must be backed");

        registry.TryGet("freew.hf-link-to-previous", out var link).Should().BeTrue();
        ((Free.Shared.Ribbon.IRibbonStatefulCommand)link!).GetState().Should().BeEquivalentTo(
            new Free.Shared.Ribbon.RibbonCommandState(IsEnabled: true, IsChecked: true));
        registry.TryGet("freew.hf-next-section", out var next).Should().BeTrue();
        ((Free.Shared.Ribbon.IRibbonStatefulCommand)next!).GetState().IsEnabled
            .Should().BeFalse("without a shell there is no editing surface to move");

        link.Execute(Free.Shared.Ribbon.RibbonCommandContext.Empty);
        view.Model.Sections[1].HeadersFooters.Header!.PlainText.Should().Be("one");
        ((Free.Shared.Ribbon.IRibbonStatefulCommand)link).GetState().IsChecked.Should().BeFalse();
    }

    private static void Run(Action<MainWindow> test) =>
        ReusableFreeWMainWindowSession.Run(window =>
        {
            try
            {
                test(window);
            }
            finally
            {
                if (window.IsHeaderFooterPaneOpenForTests)
                    window.CloseHeaderFooterPaneForTests();
            }
        });

    private static string PaneText(MainWindow window)
    {
        var sub = window.HeaderFooterSubEditorForTests;
        sub.CommitToModel();
        return string.Join("\n", sub.Model.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText));
    }

    private static void TypeInPane(MainWindow window, string text)
    {
        var sub = window.HeaderFooterSubEditorForTests;
        sub.CaretPosition = sub.Document.ContentEnd;
        sub.CaretPosition.InsertTextInRun(text);
    }

    private static TextDocument ThreeSections(string? firstHeader, string? finalHeader)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("section one") { SectionBreak = new Section(new PageSettings()) });
        document.Blocks.Add(new Paragraph("section two") { SectionBreak = new Section(new PageSettings()) });
        document.Blocks.Add(new Paragraph("section three"));
        if (firstHeader is not null)
            document.Sections[0].HeadersFooters.Header = new HeaderFooter(firstHeader);
        if (finalHeader is not null)
            document.FinalSectionHeadersFooters.Header = new HeaderFooter(finalHeader);
        return document;
    }
}
