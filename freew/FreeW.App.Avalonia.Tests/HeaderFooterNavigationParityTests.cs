using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class HeaderFooterNavigationParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public Task SectionNavigationPreservesLinkedOwnershipAndPageVariant() => Session.Dispatch(() =>
    {
        var document = ThreeSections();
        document.Sections[0].Page.DifferentFirstPage = true;
        document.Sections[1].Page.DifferentFirstPage = true;
        document.Sections[0].HeadersFooters.FirstHeader = new HeaderFooter("Shared first header");
        document.Sections[0].HeadersFooters.FirstFooter = new HeaderFooter("Shared first footer");
        var view = new DocumentView();
        view.LoadDocument(document);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, FreeWRibbonHostExecutionPorts.Empty);
        view.MoveCaretToBlockForTest(0, 0);
        view.EditHeaderFooterSlot(HeaderFooterSlotKind.FirstHeader);

        State(registry, "freew.hf-previous-section").IsEnabled.Should().BeFalse();
        State(registry, "freew.hf-next-section").IsEnabled.Should().BeTrue();
        Execute(registry, "freew.hf-next-section");

        view.HeaderFooterCaretInfo!.Value.SectionIndex.Should().Be(1);
        view.ActiveHeaderFooterSectionSlot.Slot.Should().Be(HeaderFooterSlotKind.FirstHeader);
        view.ActiveHeaderFooterSectionSlot.IsLinkedToPrevious.Should().BeTrue();
        document.Sections[1].HeadersFooters.FirstHeader.Should().BeNull();
        view.CurrentSectionPageSettings().Should().BeSameAs(document.Sections[1].Page);

        Execute(registry, "freew.hf-go-to-footer");
        view.ActiveHeaderFooterSectionSlot.Slot.Should().Be(HeaderFooterSlotKind.FirstFooter);
        view.ActiveHeaderFooterSectionSlot.SectionIndex.Should().Be(1);
        Execute(registry, "freew.hf-next-section");
        view.ActiveHeaderFooterSectionSlot.SectionIndex.Should().Be(2);
        view.ActiveHeaderFooterSectionSlot.Slot.Should().Be(HeaderFooterSlotKind.Footer,
            "the last section does not enable a separate first-page footer");
        State(registry, "freew.hf-next-section").IsEnabled.Should().BeFalse();
        view.CanUndo.Should().BeFalse("navigation must not create an editing history entry");
    }, CancellationToken.None);

    [Theory]
    [InlineData(false, "DATE")]
    [InlineData(true, "FILENAME")]
    public Task ContextualFieldsEditTheActiveFooterAtItsCaretAndUndo(bool documentInfo, string instruction) =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Header = new HeaderFooter("Untouched header");
            document.Footer = new HeaderFooter("AB");
            var view = new DocumentView();
            view.LoadDocument(document);
            view.PlaceCaretInHeaderFooter(footer: true, offset: 1);
            var changes = 0;
            view.DocumentChanged += () => changes++;

            if (documentInfo)
                view.InsertHeaderFooterDocumentInfo();
            else
                view.InsertHeaderFooterDateTime();

            document.Header!.PlainText.Should().Be("Untouched header");
            var runs = document.Footer!.Paragraphs[0].Runs;
            runs.First().Text.Should().Be("A");
            runs.Last().Text.Should().Be("B");
            runs.Should().Contain(run => run.ComplexField != null
                && run.ComplexField.Instruction.Trim() == instruction);
            changes.Should().BeGreaterThan(0, "the shell must see the edit for save and autosave tracking");
            view.CanUndo.Should().BeTrue();
            view.Undo();
            document.Footer!.PlainText.Should().Be("AB");
            view.Redo();
            document.Footer!.Paragraphs.SelectMany(paragraph => paragraph.Runs)
                .Should().Contain(run => run.ComplexField != null
                    && run.ComplexField.Instruction.Trim() == instruction);
        }, CancellationToken.None);

    [Fact]
    public Task PageNumberEditsTheCurrentSectionsLinkedOwnerAndTracksHistory() => Session.Dispatch(() =>
    {
        var document = ThreeSections();
        document.Sections[0].HeadersFooters.Footer = new HeaderFooter("Shared footer");
        document.Sections[2].HeadersFooters.Footer = new HeaderFooter("Final footer");
        var view = new DocumentView();
        view.LoadDocument(document);
        view.MoveCaretToBlockForTest(1, 0);
        var changes = 0;
        view.DocumentChanged += () => changes++;

        view.InsertHeaderFooterPageNumber(footer: true);

        HeaderFooterDialogPlanner.HasPageNumber(document.Sections[0].HeadersFooters.Footer).Should().BeTrue();
        document.Sections[1].HeadersFooters.Footer.Should().BeNull();
        document.Sections[2].HeadersFooters.Footer!.PlainText.Should().Be("Final footer");
        changes.Should().BeGreaterThan(0);
        view.Undo();
        document.Sections[0].HeadersFooters.Footer!.PlainText.Should().Be("Shared footer");
        view.Redo();
        HeaderFooterDialogPlanner.HasPageNumber(document.Sections[0].HeadersFooters.Footer).Should().BeTrue();
    }, CancellationToken.None);

    [Fact]
    public Task LinkToPreviousUndoRedoRebindsTheEditingCaretToItsRestoredOwner() => Session.Dispatch(() =>
    {
        var document = ThreeSections();
        document.Sections[0].HeadersFooters.Header = new HeaderFooter("Shared");
        var view = new DocumentView();
        view.LoadDocument(document);
        view.MoveCaretToBlockForTest(1, 0);
        view.EditHeaderFooterSlot(HeaderFooterSlotKind.Header);
        view.ToggleHeaderFooterLinkToPrevious();
        view.ActiveHeaderFooterSectionSlot.IsLinkedToPrevious.Should().BeFalse();
        view.Undo();
        view.ActiveHeaderFooterSectionSlot.IsLinkedToPrevious.Should().BeTrue();
        view.Redo();
        view.ActiveHeaderFooterSectionSlot.IsLinkedToPrevious.Should().BeFalse();

        view.InsertText("own ");

        document.Sections[1].HeadersFooters.Header!.PlainText.Should().Be("own Shared");
        document.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("Shared");
        view.Undo();
        view.Undo();
        var restoredOffset = view.HeaderFooterCaretInfo!.Value.Offset;
        view.InsertText("linked ");
        document.Sections[1].HeadersFooters.Header.Should().BeNull();
        document.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("Shared".Insert(restoredOffset, "linked "));
        view.HeaderFooterCaretInfo!.Value.SectionIndex.Should().Be(1);
    }, CancellationToken.None);

    [Fact]
    public Task EnteringHeaderFromShapeTextExitsTheShapeStoryBeforeTyping() => Session.Dispatch(() =>
    {
        var document = TextDocument.CreateEmpty();
        var body = (Paragraph)document.Blocks[0];
        body.Runs.Add(new Run("Body text"));
        var shape = Shape.TextBoxWith("Shape text", 120, 60);
        shape.Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront };
        body.Runs.Add(new Run(string.Empty) { Shape = shape });
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        view.SelectFloating(0, body.Runs.Count - 1);
        view.EnterSelectedShapeTextEditing().Should().BeTrue();

        view.GoToHeaderFooterSlot(HeaderFooterSlotKind.Header);
        view.InsertText("Header text");

        view.ShapeTextCaretInfo.Should().BeNull();
        view.IsHeaderFooterCaretActive.Should().BeTrue();
        document.Header!.PlainText.Should().Be("Header text");
        shape.PlainText.Should().Be("Shape text");
        body.Runs[0].Text.Should().Be("Body text");
    }, CancellationToken.None);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task DefaultHeaderFooterEntryFindsItsRenderedCaretIncludingInheritedStories(bool footer, bool linked) =>
        Session.Dispatch(() =>
        {
            var document = linked ? ThreeSections() : TextDocument.CreateEmpty();
            var store = document.Sections[0].HeadersFooters;
            if (footer)
                store.Footer = new HeaderFooter("Footer text");
            else
                store.Header = new HeaderFooter("Header text");
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(816, 4000));

            view.PlaceCaretInHeaderFooter(footer, offset: 1);

            view.HfCaretRectForTest.Should().NotBeNull();
            view.HeaderFooterCaretInfo!.Value.SectionIndex.Should().Be(document.Sections.Count - 1);
            if (linked)
                HeaderFooterDialogPlanner.GetSlot(document.FinalSectionHeadersFooters,
                    footer ? HeaderFooterSlotKind.Footer : HeaderFooterSlotKind.Header).Should().BeNull();
        }, CancellationToken.None);

    [Fact]
    public Task NavigationToAnEmptyLockedHeaderLeavesAllPersistentSlotsUntouched() => Session.Dispatch(() =>
    {
        var document = ThreeSections();
        var view = new DocumentView();
        view.LoadDocument(document);
        view.MoveCaretToBlockForTest(1, 0);
        view.SetMarkedAsFinal(true);

        view.GoToHeaderFooterSlot(HeaderFooterSlotKind.Header);

        document.Sections.Select(section => section.HeadersFooters.Header).Should().OnlyContain(header => header == null);
        view.CanUndo.Should().BeFalse();
        view.IsHeaderFooterCaretActive.Should().BeTrue();
        view.HfCaretRectForTest.Should().NotBeNull();
    }, CancellationToken.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FirstEditOfAnEmptyStoryMaterializesItsSlotInTheSameUndoEntry(bool paragraphBreak) =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            var view = new DocumentView();
            view.LoadDocument(document);
            view.PlaceCaretInHeaderFooter(footer: true);
            document.Footer.Should().BeNull("entering an empty story is a view operation");
            view.HfCaretRectForTest.Should().NotBeNull();

            if (paragraphBreak)
                view.RaiseKeyDownForContextMenuTests(new KeyEventArgs { Key = Key.Enter });
            else
                view.InsertText("First edit");

            document.Footer.Should().NotBeNull();
            if (paragraphBreak)
                document.Footer!.Paragraphs.Should().HaveCount(2);
            else
                document.Footer!.PlainText.Should().Be("First edit");
            view.Undo();
            document.Footer.Should().BeNull("one Undo must restore the original missing slot");
            view.CanUndo.Should().BeFalse();
            view.Redo();
            document.Footer.Should().NotBeNull();
        }, CancellationToken.None);

    [Fact]
    public Task LinkingToAnUndefinedPredecessorAndUndoingDoesNotCreateAHeaderArtifact() => Session.Dispatch(() =>
    {
        var document = ThreeSections();
        document.Sections[1].HeadersFooters.Header = new HeaderFooter("Local header");
        var view = new DocumentView();
        view.LoadDocument(document);
        view.MoveCaretToBlockForTest(1, 0);
        view.EditHeaderFooterSlot(HeaderFooterSlotKind.Header);

        view.ToggleHeaderFooterLinkToPrevious();

        document.Sections[0].HeadersFooters.Header.Should().BeNull();
        document.Sections[1].HeadersFooters.Header.Should().BeNull();
        view.HfCaretRectForTest.Should().NotBeNull();
        view.Undo();
        document.Sections[0].HeadersFooters.Header.Should().BeNull();
        document.Sections[1].HeadersFooters.Header!.PlainText.Should().Be("Local header");
        view.Redo();
        view.InsertText("Shared edit");
        document.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("Shared edit");
        document.Sections[1].HeadersFooters.Header.Should().BeNull();
        view.Undo();
        document.Sections[0].HeadersFooters.Header.Should().BeNull();
        view.Undo();
        document.Sections[1].HeadersFooters.Header!.PlainText.Should().Be("Local header");
    }, CancellationToken.None);

    [Fact]
    public Task HeaderCharacterNavigationKeepsEmojiAndCombiningSequencesTogether() => Session.Dispatch(() =>
    {
        var document = TextDocument.CreateEmpty();
        document.Footer = new HeaderFooter("A😀e\u0301B");
        var view = new DocumentView();
        view.LoadDocument(document);
        view.PlaceCaretInHeaderFooter(footer: true, offset: 5);

        void Left(KeyModifiers modifiers)
        {
            var args = new KeyEventArgs { Key = Key.Left, KeyModifiers = modifiers };
            view.RaiseKeyDownForContextMenuTests(args);
            args.Handled.Should().BeTrue();
        }

        Left(KeyModifiers.Shift);
        view.HeaderFooterCaretInfo!.Value.Offset.Should().Be(3);
        view.SelectedText.Should().Be("e\u0301");
        Left(KeyModifiers.None);
        view.HeaderFooterCaretInfo!.Value.Offset.Should().Be(3, "Left collapses the current selection first");
        Left(KeyModifiers.None);
        view.HeaderFooterCaretInfo!.Value.Offset.Should().Be(1, "the emoji is one text element");
    }, CancellationToken.None);

    [Fact]
    public Task ContextualDateAndFieldCommandsUseTheHostsExistingDialogs() => Session.Dispatch(() =>
    {
        var dateDialogs = 0;
        var fieldDialogs = 0;
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, FreeWRibbonHostExecutionPorts.Empty with
        {
            OpenDateTimeDialog = () => dateDialogs++,
            OpenFieldDialog = () => fieldDialogs++,
        });

        Execute(registry, "freew.hf-insert-datetime");
        Execute(registry, "freew.hf-insert-field");

        dateDialogs.Should().Be(1);
        fieldDialogs.Should().Be(1);
        view.CanUndo.Should().BeFalse("opening the picker alone must not insert a hardcoded field");
    }, CancellationToken.None);

    [Fact]
    public Task SpellingRibbonStartsTheSameReviewPaneAsF7() => Session.Dispatch(() =>
    {
        var window = new MainWindow([], new FreeWOptions(), new InMemoryApplicationOptionsStore<FreeWOptions>(),
            promptSaveChangesAsync: _ => Task.FromResult(SaveChangesPrompt.DontSave),
            suppressStartupRecoveryOffer: true);
        try
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            // Arbitrary unknown words are outside the portable correction catalog. This
            // language-independent grammar issue is deterministic even with a user dictionary.
            document.Blocks.Add(new Paragraph("The the"));
            window.Editor.LoadDocument(document);
            var registry = window.RibbonRegistryForTests!;
            registry.TryGet(new RibbonCommandId("freew.spelling-grammar"), out var command).Should().BeTrue();
            if (command is IRibbonStatefulCommand stateful)
                stateful.GetState().IsEnabled.Should().BeTrue();

            command!.Execute(RibbonCommandContext.Empty);

            var pane = window.GetLogicalDescendants().OfType<SpellingGrammarPane>().Single();
            pane.IsReviewing.Should().BeTrue();
            pane.IsPaneVisible.Should().BeTrue();
            pane.IssueText.Should().Be("the");
            pane.ChangeEnabled.Should().BeTrue();

            pane.Hide();
            pane.IsReviewing.Should().BeFalse();
            var f7 = new KeyEventArgs { Key = Key.F7 };
            window.RaiseKeyDownForTest(f7);
            f7.Handled.Should().BeTrue();
            pane.IsReviewing.Should().BeTrue();
            pane.IsPaneVisible.Should().BeTrue();
            pane.IssueText.Should().Be("the");
            pane.ChangeEnabled.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }, CancellationToken.None);

    private static TextDocument ThreeSections()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("First") { SectionBreak = new Section(new PageSettings()) });
        document.Blocks.Add(new Paragraph("Second") { SectionBreak = new Section(new PageSettings()) });
        document.Blocks.Add(new Paragraph("Third"));
        return document;
    }

    private static void Execute(RibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static RibbonCommandState State(RibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
        return ((IRibbonStatefulCommand)command!).GetState();
    }
}
