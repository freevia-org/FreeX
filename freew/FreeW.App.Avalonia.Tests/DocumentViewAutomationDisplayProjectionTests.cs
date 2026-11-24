using Avalonia;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Input;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewAutomationDisplayProjectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData(false, "field")]
    [InlineData(true, "field")]
    [InlineData(false, "note")]
    [InlineData(true, "note")]
    [InlineData(false, "equation")]
    [InlineData(true, "equation")]
    public Task SnapshotSelectionAndNativeValueUseTheSameResolvedText(bool cell, string kind) => OnUi(() =>
    {
        var paragraph = new Paragraph();
        var prefix = kind switch
        {
            "field" => Run.ComplexFieldRun(" DATE ", "", showCode: true),
            "note" => Run.FootnoteReference(403),
            _ => new Run("") { Equation = Equation.FromText("x+y") },
        };
        paragraph.Runs.Add(prefix);
        paragraph.Runs.Add(new Run("a😀b"));
        var document = new TextDocument();
        if (cell)
        {
            var table = Table.Create(1, 1);
            table.Rows[0].Cells[0].Paragraphs.Clear();
            table.Rows[0].Cells[0].Paragraphs.Add(paragraph);
            document.Blocks.Add(table);
        }
        else document.Blocks.Add(paragraph);
        document.Footnotes[403] = new Footnote(403, "Note content");
        var before = paragraph.PlainText;
        var display = kind switch
        {
            "field" => "{ DATE }",
            "note" => "1",
            _ => "x+y",
        };
        var view = View(document);
        if (cell) view.SetCellTextSelectionForTest(0, 0, 0, 0, display.Length + 4, 0, 0, 0, display.Length + 1);
        else view.SetBodySelectionForTest(0, display.Length + 4, 0, display.Length + 1);

        var snapshot = view.AutomationSnapshot();
        var value = view.CreateAutomationPeerForTests().GetProvider<IValueProvider>()!;

        snapshot.Text.Should().Be(display + "a😀b");
        snapshot.CaretOffset.Should().Be(display.Length + 1);
        snapshot.GetText(snapshot.Selection!).Should().Be("😀b");
        snapshot.GetText(snapshot.Selection!).Should().Be(view.SelectedText);
        value.Value.Should().Be(snapshot.Text);
        paragraph.PlainText.Should().Be(before);
        paragraph.Runs.First().Should().BeSameAs(prefix);
    });

    [Fact]
    public Task SharedParagraphUsesEachAddressProjectionAndPreservesGlobalCellOffsets() => OnUi(() =>
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.ComplexFieldRun(" DATE ", "", showCode: true));
        paragraph.Runs.Add(new Run("x\r\ny"));
        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0] = new TableCell("A");
        table.Rows[0].Cells[1].Paragraphs.Clear();
        table.Rows[0].Cells[1].Paragraphs.Add(paragraph);
        document.Blocks.Add(table);
        document.Blocks.Add(new Paragraph("After"));
        var view = View(document);
        const string body = "{ DATE }x\r\ny";
        const string cell = "{ DATE }x\ny";
        var expected = body + "\nA\t" + cell + "\nAfter";
        view.SetCellTextSelectionForTest(1, 0, 1, 0, cell.Length, 0, 1, 0, 8);

        var snapshot = view.AutomationSnapshot();

        snapshot.Text.Should().Be(expected);
        snapshot.CaretOffset.Should().Be(body.Length + "\nA\t".Length + 8);
        snapshot.GetText(snapshot.Selection!).Should().Be("x\ny");
        snapshot.ParagraphNumber.Should().Be(3);
        view.MoveCaretToBlockForTest(2, 0);
        view.AutomationSnapshot().CaretOffset.Should().Be(expected.Length - "After".Length);
        paragraph.PlainText.Should().Be("x\r\ny");
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task NativeSemanticFocusUsesResolvedRunBoundariesBeforeTyping(bool cell, bool hyperlink) => OnUi(() =>
    {
        var field = Run.ComplexFieldRun(" DATE ", "", showCode: true);
        var suffix = new Run("a😀b");
        if (hyperlink) suffix.HyperlinkUrl = "https://example.com/target";
        var paragraph = new Paragraph();
        paragraph.Runs.Add(field);
        paragraph.Runs.Add(suffix);
        var document = new TextDocument();
        if (cell)
        {
            var table = Table.Create(1, 1);
            table.Rows[0].Cells[0].Paragraphs.Clear();
            table.Rows[0].Cells[0].Paragraphs.Add(paragraph);
            document.Blocks.Add(table);
        }
        else document.Blocks.Add(paragraph);
        document.FinalSectionHeadersFooters.Header = new HeaderFooter("header unchanged");
        var view = View(document);
        var kind = hyperlink ? DocumentAccessibilityNodeKind.Hyperlink : DocumentAccessibilityNodeKind.TextRun;
        var node = view.AutomationSemanticTree().ById.Values.Single(item => item.Kind == kind && item.RunIndex == 1);
        view.PlaceCaretInHeaderFooter(false, 0, 2);

        view.AutomationFocusNode(node);

        (cell ? view.CellCaretInfo!.Value.Offset : view.CaretPositionForTest.Offset).Should().Be(8);
        view.AutomationSnapshot().CaretOffset.Should().Be(8);
        view.InsertText("X");
        paragraph.Runs.First().Should().BeSameAs(field);
        string.Concat(paragraph.Runs.Skip(1).Select(run => run.Text)).Should().Be("Xa😀b");
        document.FinalSectionHeadersFooters.Header!.PlainText.Should().Be("header unchanged");
        view.CreateAutomationPeerForTests().GetProvider<IValueProvider>()!.Value.Should().Be("{ DATE }Xa😀b");
    });

    [Fact]
    public Task ActiveHeaderAndShapeSnapshotsKeepTheirModelOffsetContracts() => OnUi(() =>
    {
        Paragraph Story()
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.ComplexFieldRun(" DATE ", "OLD", showCode: true));
            paragraph.Runs.Add(new Run("tail"));
            return paragraph;
        }
        var document = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(Run.ComplexFieldRun(" DATE ", "", showCode: true));
        body.Runs.Add(new Run("body"));
        document.Blocks.Add(body);
        document.FinalSectionHeadersFooters.Header = new HeaderFooter();
        document.FinalSectionHeadersFooters.Header.Paragraphs.Add(Story());
        var shape = new Shape(ShapeKind.TextBox, 144, 108, "#FFFFFF")
        {
            Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront },
        };
        shape.TextParagraphs.Add(Story());
        var drawing = new Paragraph();
        drawing.Runs.Add(Run.FromShape(shape));
        document.Blocks.Add(drawing);
        var view = View(document);
        view.PlaceCaretInHeaderFooter(false, 0, 2);

        view.AutomationSnapshot().Text.Should().Be("OLDtail");
        view.AutomationSnapshot().CaretOffset.Should().Be(2);
        view.ExitHeaderFooterCaret();
        view.SelectFloating(1, 0);
        view.EnterSelectedShapeTextEditing().Should().BeTrue();
        // The helper returns whether a noncollapsed selection exists, not whether caret
        // placement succeeded. Collapse an actual range through the production key route.
        view.SelectShapeTextRangeForTest(0, 1, 2).Should().BeTrue();
        var right = new KeyEventArgs { Key = Key.Right, RoutedEvent = DocumentView.KeyDownEvent };
        view.RaiseKeyDownForContextMenuTests(right);
        right.Handled.Should().BeTrue();
        view.SelectedText.Should().BeEmpty();
        view.AutomationSnapshot().Text.Should().Be("OLDtail");
        view.AutomationSnapshot().CaretOffset.Should().Be(2);
        view.AutomationSnapshot().Selection.Should().BeNull();
        view.CreateAutomationPeerForTests().GetProvider<IValueProvider>()!.Value.Should().Be("{ DATE }body\n");
    });

    private static DocumentView View(TextDocument document)
    {
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        view.Arrange(new Rect(0, 0, 816, 4000));
        return view;
    }

    private static Task OnUi(Action action) => Session.Dispatch(action, CancellationToken.None);
}
