using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentSemanticAutomationPeerTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task Dispatch(Action action) => Session.Dispatch(action, CancellationToken.None);

    [Fact]
    public async Task Root_exposes_stable_ordered_paragraph_and_table_peers()
    {
        await Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph("Introduction"));
            var table = new Table();
            var row = new TableRow();
            row.Cells.Add(new TableCell("Merged") { GridSpan = 2 });
            row.Cells.Add(new TableCell("Final"));
            table.Rows.Add(row);
            document.Blocks.Add(table);
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(900, 1200));

            var root = ControlAutomationPeer.CreatePeerForElement(view);
            var firstRead = root.GetChildren();
            var secondRead = root.GetChildren();

            firstRead.Select(peer => peer.GetAutomationControlType()).Should().Equal(
                AutomationControlType.Text,
                AutomationControlType.DataGrid);
            secondRead.Should().Equal(firstRead);
            secondRead[0].Should().BeSameAs(firstRead[0]);

            var rowPeer = firstRead[1].GetChildren().Should().ContainSingle().Subject;
            rowPeer.GetAutomationControlType().Should().Be(AutomationControlType.Group);
            var cells = rowPeer.GetChildren();
            cells.Should().HaveCount(2);
            cells.Select(peer => peer.GetAutomationId()).Should().Equal(
                "block:1:table:row:0:column:0",
                "block:1:table:row:0:column:2");
            cells.Should().OnlyContain(peer => peer.GetAutomationControlType() == AutomationControlType.DataItem);
            cells[0].GetParent().Should().BeSameAs(rowPeer);
            cells[0].GetBoundingRectangle().Width.Should().BeGreaterThan(0);
            cells[0].GetProvider<IValueProvider>()!.Value.Should().Be("Merged");
            cells[0].GetChildren().Should().ContainSingle()
                .Which.GetAutomationControlType().Should().Be(AutomationControlType.Text);

            var scrollRequests = 0;
            view.ScrollToCaretRequested += () => scrollRequests++;
            cells[0].BringIntoView();
            view.CellCaretInfo.Should().Be((1, 0, 0, 0, 0));
            scrollRequests.Should().Be(1);
        });
    }

    [Fact]
    public async Task Paragraph_exposes_coalesced_invokable_link_and_alt_named_image()
    {
        await Dispatch(() =>
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Open ") { HyperlinkUrl = "https://example.test" });
            paragraph.Runs.Add(new Run("site", new RunFormatting { Bold = true }) { HyperlinkUrl = "https://example.test" });
            paragraph.Runs.Add(Run.FromImage(new InlineImage([], 120, 80) { AltText = "Architecture diagram" }));
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(paragraph);
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(900, 1200));
            string? invokedUrl = null;
            view.HyperlinkActivated += url => invokedUrl = url;

            var root = ControlAutomationPeer.CreatePeerForElement(view);
            var paragraphPeer = root.GetChildren().Should().ContainSingle().Subject;
            paragraphPeer.GetProvider<IValueProvider>()!.Value.Should().Be("Open site");
            paragraphPeer.GetBoundingRectangle().Width.Should().BeGreaterThan(0);
            paragraphPeer.GetProvider<ISelectionProvider>().Should().BeNull();

            var children = paragraphPeer.GetChildren();
            children.Should().HaveCount(2);
            var link = children.Single(peer => peer.GetAutomationControlType() == AutomationControlType.Hyperlink);
            link.GetName().Should().Be("Open site");
            link.GetProvider<IValueProvider>()!.Value.Should().Be("Open site");
            link.GetProvider<IInvokeProvider>()!.Invoke();
            invokedUrl.Should().Be("https://example.test");
            var image = children.Single(peer => peer.GetAutomationControlType() == AutomationControlType.Image);
            image.GetName().Should().Be("Architecture diagram");
            image.GetBoundingRectangle().Width.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public async Task Drawing_objects_expose_names_values_bounds_and_floating_selection_status()
    {
        await Dispatch(() =>
        {
            var floatingImage = new InlineImage([], 90, 50)
            {
                AltText = "Floating architecture diagram",
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 12,
                VerticalOffsetPt = 8
            };
            var floatingShape = Shape.TextBoxWith("Release approved", 110, 45);
            floatingShape.AltText = "Approval callout";
            floatingShape.Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 130,
                VerticalOffsetPt = 8
            };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromImage(floatingImage));
            paragraph.Runs.Add(Run.FromShape(floatingShape));
            paragraph.Runs.Add(Run.FromChart(new Chart { Title = "Revenue trend", Kind = ChartKind.Line }));
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(paragraph);
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(900, 1200));

            var root = ControlAutomationPeer.CreatePeerForElement(view);
            var objectPeers = root.GetChildren().Single().GetChildren();
            var selection = root.GetProvider<ISelectionProvider>();

            objectPeers.Should().HaveCount(3);
            selection.Should().NotBeNull();
            selection!.CanSelectMultiple.Should().BeTrue();
            selection.GetSelection().Should().BeEmpty();
            objectPeers.Should().OnlyContain(peer => peer.GetAutomationControlType() == AutomationControlType.Image);
            var imagePeer = objectPeers.Single(peer => peer.GetName() == "Floating architecture diagram");
            imagePeer.GetBoundingRectangle().Width.Should().BeGreaterThan(0);
            var imageSelection = imagePeer.GetProvider<ISelectionItemProvider>();
            imageSelection.Should().NotBeNull();
            var shapePeer = objectPeers.Single(peer => peer.GetName() == "Approval callout");
            shapePeer.GetProvider<IValueProvider>()!.Value.Should().Be("Release approved");
            shapePeer.GetBoundingRectangle().Width.Should().BeGreaterThan(0);
            var shapeSelection = shapePeer.GetProvider<ISelectionItemProvider>();
            shapeSelection.Should().NotBeNull();
            var chartPeer = objectPeers.Single(peer => peer.GetName() == "Revenue trend");
            chartPeer.GetBoundingRectangle().Width.Should().BeGreaterThan(0);

            var imageSelectionEvents = 0;
            imagePeer.PropertyChanged += (_, args) =>
            {
                if (args.Property == SelectionItemPatternIdentifiers.IsSelectedProperty)
                    imageSelectionEvents++;
            };
            imageSelection!.Select();

            view.SelectedFloatingInfo.Should().NotBeNull();
            view.SelectedFloatingInfo!.Value.Kind.Should().Be("Image");
            imageSelection.IsSelected.Should().BeTrue();
            imageSelectionEvents.Should().Be(1);
            selection.GetSelection().Should().ContainSingle().Which.Should().BeSameAs(imagePeer);
            root.GetItemStatus().Should().Be("Selected Image: Floating architecture diagram");

            shapeSelection!.AddToSelection();
            selection.GetSelection().Should().HaveCount(2);
            shapeSelection.IsSelected.Should().BeTrue();
            imageSelection.RemoveFromSelection();
            selection.GetSelection().Should().ContainSingle().Which.Should().BeSameAs(shapePeer);
            imageSelection.IsSelected.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Drawing_group_exposes_nested_children_and_focuses_the_selected_child()
    {
        await Dispatch(() =>
        {
            var group = new DrawingGroup { WidthPt = 150, HeightPt = 60 };
            group.Children.Add(new InlineImage([], 40, 30) { AltText = "Grouped logo" });
            group.Children.Add(Shape.TextBoxWith("Grouped note", 80, 30));
            group.ChildOffsets.Add((0, 0));
            group.ChildOffsets.Add((50, 0));
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromDrawingGroup(group));
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(paragraph);
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(900, 1200));

            var root = ControlAutomationPeer.CreatePeerForElement(view);
            var groupPeer = root.GetChildren().Single().GetChildren().Should().ContainSingle().Subject;
            groupPeer.GetAutomationControlType().Should().Be(AutomationControlType.Group);
            groupPeer.GetBoundingRectangle().Width.Should().BeGreaterThan(0);
            var children = groupPeer.GetChildren();
            children.Select(peer => peer.GetName()).Should().Equal("Grouped logo", "Grouped note");
            children.Should().OnlyContain(peer => peer.GetBoundingRectangle().Width > 0);

            var childSelection = children[1].GetProvider<ISelectionItemProvider>();
            childSelection.Should().NotBeNull();
            childSelection!.Select();

            view.SelectedFloatingInfo!.Value.Kind.Should().Be("Group");
            view.SelectedFloatingGroupChildPath.Should().Equal(1);
            childSelection.IsSelected.Should().BeTrue();
            root.GetProvider<ISelectionProvider>()!.GetSelection()
                .Should().ContainSingle().Which.Should().BeSameAs(children[1]);
            root.GetItemStatus().Should().Be("Selected Shape: Grouped note");
        });
    }

    [Fact]
    public async Task Text_change_preserves_peer_identity_and_structure_change_refreshes_children()
    {
        await Dispatch(() =>
        {
            var paragraph = new Paragraph("Before");
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(paragraph);
            var view = new DocumentView();
            view.LoadDocument(document);
            var root = ControlAutomationPeer.CreatePeerForElement(view);
            var paragraphPeer = root.GetChildren().Should().ContainSingle().Subject;
            var valueChanges = 0;
            paragraphPeer.PropertyChanged += (_, args) =>
            {
                if (args.Property == ValuePatternIdentifiers.ValueProperty)
                    valueChanges++;
            };
            var childrenChanges = 0;
            root.ChildrenChanged += (_, _) => childrenChanges++;

            paragraph.Runs[0].Text = "After";
            view.InvalidateAfterExternalMutation();

            root.GetChildren().Should().ContainSingle().Which.Should().BeSameAs(paragraphPeer);
            paragraphPeer.GetProvider<IValueProvider>()!.Value.Should().Be("After");
            valueChanges.Should().Be(1);
            childrenChanges.Should().Be(0);

            document.Blocks.Add(new Paragraph("Second"));
            view.InvalidateAfterExternalMutation();

            childrenChanges.Should().Be(1);
            root.GetChildren().Should().HaveCount(2);
        });
    }

    [Fact]
    public async Task Alt_text_change_preserves_image_peer_and_raises_name_and_help_notifications()
    {
        await Dispatch(() =>
        {
            var imageModel = new InlineImage([], 80, 60) { AltText = "Before diagram" };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromImage(imageModel));
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(paragraph);
            var view = new DocumentView();
            view.LoadDocument(document);
            var root = ControlAutomationPeer.CreatePeerForElement(view);
            var imagePeer = root.GetChildren().Single().GetChildren().Single();
            var nameChanges = 0;
            var helpChanges = 0;
            imagePeer.PropertyChanged += (_, args) =>
            {
                if (args.Property == AutomationElementIdentifiers.NameProperty)
                    nameChanges++;
                if (args.Property == AutomationElementIdentifiers.HelpTextProperty)
                    helpChanges++;
            };

            imageModel.AltText = "After diagram";
            view.InvalidateAfterExternalMutation();

            var currentImagePeer = root.GetChildren().Single().GetChildren().Single();
            currentImagePeer.Should().BeSameAs(imagePeer);
            currentImagePeer.GetName().Should().Be("After diagram");
            nameChanges.Should().Be(1);
            helpChanges.Should().Be(1);
        });
    }
}
