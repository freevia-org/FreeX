using System.Linq;
using System.Windows.Threading;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// W13: Header &amp; Footer > Previous / Next section and Link to Previous act on the header/footer region
/// being edited on a page in Print Layout (the paged editor), as they act on the docked pane in Draft.
/// </summary>
public sealed class HeaderFooterSectionPagedTests
{
    [StaFact]
    public void PreviousAndNext_MoveToTheNeighbouringSectionsHeaderOnItsPage()
    {
        Run(window =>
        {
            var pages = Pages(window);
            window.HeaderFooterSectionSlotForTests.Should().BeNull("the body is being edited, not a header");

            pages.FocusSectionHeaderFooter(0, header: true).Should().BeTrue();
            window.HeaderFooterSectionSlotForTests!.SectionIndex.Should().Be(0);
            window.HeaderFooterSectionSlotForTests!.HasPreviousSection.Should().BeFalse();

            window.MoveHeaderFooterSectionForTests(+1);
            var view = window.HeaderFooterSectionSlotForTests!;
            view.SectionIndex.Should().Be(1);
            view.IsLinkedToPrevious.Should().BeTrue();
            pages.ActiveHeaderFooterRegion!.IsHeader.Should().BeTrue();
            RegionText(pages, 1).Should().Be("one", "a linked section shows the header it inherits");

            window.MoveHeaderFooterSectionForTests(+1);
            window.HeaderFooterSectionSlotForTests!.SectionIndex.Should().Be(2);
            RegionText(pages, 2).Should().Be("three");
            window.HeaderFooterSectionSlotForTests!.HasNextSection.Should().BeFalse();

            window.MoveHeaderFooterSectionForTests(+1);
            window.HeaderFooterSectionSlotForTests!.SectionIndex.Should().Be(2, "already at the last section");
            window.MoveHeaderFooterSectionForTests(-1);
            window.MoveHeaderFooterSectionForTests(-1);
            window.HeaderFooterSectionSlotForTests!.SectionIndex.Should().Be(0);
        });
    }

    [StaFact]
    public void LinkToPrevious_OnAPage_UnlinksWithACopy_AndRelinks()
    {
        Run(window =>
        {
            var body = window.BodyEditorForTests;
            var pages = Pages(window);
            pages.FocusSectionHeaderFooter(1, header: true).Should().BeTrue();

            window.ToggleHeaderFooterLinkToPreviousForTests();
            Drain(() => OwnerOfSection(window, 1) == body.Model.Sections[1].HeadersFooters
                && window.HeaderFooterSectionSlotForTests is not null);

            window.HeaderFooterSectionSlotForTests.Should().NotBeNull("the region being edited is focused again after the pages re-render");
            window.HeaderFooterSectionSlotForTests!.SectionIndex.Should().Be(1);
            window.HeaderFooterSectionSlotForTests!.IsLinkedToPrevious.Should().BeFalse();
            body.Model.Sections[1].HeadersFooters.Header!.PlainText.Should().Be("one", "unlinking starts from a copy");
            pages = Pages(window);
            RegionText(pages, 1).Should().Be("one");

            // Typing in the now separate header changes only this section's.
            var ownerBox = pages.PageBoxes.First(b => b.SectionOrdinal == 2);
            ownerBox.OwnerSectionHf.Should().BeSameAs(body.Model.Sections[1].HeadersFooters, "the page now owns its header");
            var editor = Region(pages, 1);
            editor.CaretPosition = editor.Document.ContentEnd;
            editor.CaretPosition.InsertTextInRun(" two");
            pages.CommitPendingEdits();
            body.Model.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("one");
            body.Model.Sections[1].HeadersFooters.Header!.PlainText.Should().Be("one two");

            pages.FocusSectionHeaderFooter(1, header: true).Should().BeTrue();
            window.ToggleHeaderFooterLinkToPreviousForTests();
            Drain(() => OwnerOfSection(window, 1) == body.Model.Sections[0].HeadersFooters
                && window.HeaderFooterSectionSlotForTests is not null);
            window.HeaderFooterSectionSlotForTests!.IsLinkedToPrevious.Should().BeTrue();
            body.Model.Sections[1].HeadersFooters.Header.Should().BeNull();
            RegionText(Pages(window), 1).Should().Be("one");
        });
    }

    [StaFact]
    public void FooterNavigation_StaysOnFooters()
    {
        Run(window =>
        {
            var pages = Pages(window);
            pages.FocusSectionHeaderFooter(0, header: false).Should().BeTrue();
            window.HeaderFooterSectionSlotForTests!.Slot.Should().Be(HeaderFooterSlotKind.Footer);

            window.MoveHeaderFooterSectionForTests(+1);
            pages.ActiveHeaderFooterRegion.Should().Be(new PaginatedEditorPanel.HeaderFooterRegion(1, HeaderFooterSlotKind.Footer, IsHeader: false));
        });
    }

    private static void Run(Action<MainWindow> test) =>
        ReusableFreeWMainWindowSession.Run(window =>
        {
            window.SetViewModeForTests(DocumentViewMode.PrintLayout);
            window.BodyEditorForTests.LoadModel(ThreeSections());
            Drain();
           
            test(window);
        });

    private static PaginatedEditorPanel Pages(MainWindow window)
    {
        var pages = window.PrintLayoutPagesForTests;
        pages.Should().NotBeNull("Print Layout is edited on pages");
        pages!.FinishLayoutNow();
        return pages;
    }

    private static SectionHeadersFooters? OwnerOfSection(MainWindow window, int section) =>
        window.PrintLayoutPagesForTests?.PageBoxes.FirstOrDefault(box => box.SectionOrdinal == section + 1)?.OwnerSectionHf;

    private static DocumentView Region(PaginatedEditorPanel pages, int section)
    {
        var box = pages.PageBoxes.First(b => b.SectionOrdinal == section + 1);
        pages.RealizeBox(box);
        return box.HeaderSubEditor!;
    }

    private static string RegionText(PaginatedEditorPanel pages, int section)
    {
        var editor = Region(pages, section);
        editor.CommitToModel();
        return string.Join("\n", editor.Model.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText));
    }

    /// <summary>
    /// Runs the dispatcher for a moment, idle work included (a plain idle-priority wait would starve while
    /// the offscreen window keeps re-rendering its adorners).
    /// </summary>
    private static void Drain(Func<bool>? until = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(until is null ? 0.25 : 5);
        do
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        while (DateTime.UtcNow < deadline && until?.Invoke() != true);
    }

    private static TextDocument ThreeSections()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("section one") { SectionBreak = new Section(new PageSettings()) });
        document.Blocks.Add(new Paragraph("section two") { SectionBreak = new Section(new PageSettings()) });
        document.Blocks.Add(new Paragraph("section three"));
        document.Sections[0].HeadersFooters.Header = new HeaderFooter("one");
        document.FinalSectionHeadersFooters.Header = new HeaderFooter("three");
        return document;
    }
}
