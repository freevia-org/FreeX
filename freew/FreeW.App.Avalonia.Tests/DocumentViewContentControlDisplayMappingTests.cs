using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewContentControlDisplayMappingTests
{
    private const string DisplayPrefix = "{ PAGE }";
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    public static IEnumerable<object[]> StoriesCachesAndProtection()
    {
        foreach (var cell in new[] { false, true })
        foreach (var cache in new[] { "", "XX", "a much longer cached field value" })
        foreach (var forms in new[] { false, true })
            yield return [cell, cache, forms];
    }

    [Theory]
    [MemberData(nameof(StoriesCachesAndProtection))]
    public Task ControlStartEndTypingAndDeletionUseResolvedCaretOffsets(bool cell, string cache, bool forms) =>
        Session.Dispatch(() =>
        {
            var (view, paragraph, prefix) = Create(cell, cache, "abcdef", forms);
            Move(view, cell, DisplayPrefix.Length);
            Press(view, Key.Back);
            ControlText(paragraph).Should().Be("abcdef", "a caret before a must not be treated as the cached span's end");
            paragraph.Runs.First().Should().BeSameAs(prefix);
            view.CanUndo.Should().BeFalse();

            view.InsertText("X");
            ControlText(paragraph).Should().Be("Xabcdef");
            Offset(view, cell).Should().Be(DisplayPrefix.Length + 1);
            view.CanUndo.Should().BeTrue("form-field classification keeps history available in forms mode");
            view.Undo();
            ControlText(paragraph).Should().Be("abcdef");
            Offset(view, cell).Should().Be(DisplayPrefix.Length);
            Press(view, Key.End);
            Offset(view, cell).Should().Be(DisplayPrefix.Length + 6);
            Press(view, Key.Left);
            Offset(view, cell).Should().Be(DisplayPrefix.Length + 5);
            Press(view, Key.Right);
            Press(view, Key.Delete);
            ControlText(paragraph).Should().Be("abcdef");
            Press(view, Key.Back);
            ControlText(paragraph).Should().Be("abcde");
            Offset(view, cell).Should().Be(DisplayPrefix.Length + 5);
            paragraph.Runs.First().Should().BeSameAs(prefix);
            prefix.Text.Should().Be(cache);
            view.Undo();
            ControlText(paragraph).Should().Be("abcdef");
            Offset(view, cell).Should().Be(DisplayPrefix.Length + 6);
        }, CancellationToken.None);

    [Theory]
    [MemberData(nameof(StoriesCachesAndProtection))]
    public Task EmptyControlRemainsReachableAfterAResolvedField(bool cell, string cache, bool forms) =>
        Session.Dispatch(() =>
        {
            var (view, paragraph, prefix) = Create(cell, cache, "", forms);
            Move(view, cell, DisplayPrefix.Length);
            view.InsertText("X");

            ControlText(paragraph).Should().Be("X");
            paragraph.Runs.First().Should().BeSameAs(prefix);
            paragraph.Runs.Single(run => run.Control is not null).Control!.Tag.Should().Be("editable");
            Offset(view, cell).Should().Be(DisplayPrefix.Length + 1);
            view.Undo();
            paragraph.Runs.Single(run => run.Control is not null).Text.Should().BeEmpty();
            Offset(view, cell).Should().Be(DisplayPrefix.Length);
            view.InsertText("Y");
            ControlText(paragraph).Should().Be("Y");
        }, CancellationToken.None);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task CellControlCrLfMapsDeletionAndResultingCaretBothDirections(bool forms, bool backward) =>
        Session.Dispatch(() =>
        {
            var (view, paragraph, prefix) = Create(true, "XX", "a\r\nb", forms);
            Move(view, true, DisplayPrefix.Length + (backward ? 2 : 1));
            Press(view, backward ? Key.Back : Key.Delete);

            ControlText(paragraph).Should().Be("ab");
            Offset(view, true).Should().Be(DisplayPrefix.Length + 1);
            paragraph.Runs.First().Should().BeSameAs(prefix);
            view.Undo();
            ControlText(paragraph).Should().Be("a\r\nb");
            Offset(view, true).Should().Be(DisplayPrefix.Length + (backward ? 2 : 1));
            if (!backward) Press(view, Key.Right);
            Offset(view, true).Should().Be(DisplayPrefix.Length + 2);
            view.InsertText("X");
            ControlText(paragraph).Should().Be("a\r\nXb");
            Offset(view, true).Should().Be(DisplayPrefix.Length + 3);
        }, CancellationToken.None);

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, "XX")]
    [InlineData(false, "a much longer cached field value")]
    [InlineData(true, "")]
    [InlineData(true, "XX")]
    [InlineData(true, "a much longer cached field value")]
    public Task FormTabSelectsTheDisplayedControlAndReplacementStaysAFormEdit(bool cell, string cache) =>
        Session.Dispatch(() =>
        {
            var (view, paragraph, prefix) = Create(cell, cache, "a\r\nb", true);
            Move(view, cell, 0);

            Press(view, Key.Tab);

            view.SelectedText.Should().Be(cell ? "a\nb" : "a\r\nb");
            Offset(view, cell).Should().Be(DisplayPrefix.Length + (cell ? 3 : 4));
            view.InsertText("X");
            ControlText(paragraph).Should().Be("X");
            paragraph.Runs.First().Should().BeSameAs(prefix);
            view.CanUndo.Should().BeTrue();
            view.Undo();
            ControlText(paragraph).Should().Be("a\r\nb");
            view.SelectedText.Should().Be(cell ? "a\nb" : "a\r\nb");
            view.CanUndo.Should().BeFalse();
        }, CancellationToken.None);

    private static (DocumentView View, Paragraph Paragraph, Run Prefix) Create(bool cell, string cache, string text, bool forms)
    {
        var prefix = Run.ComplexFieldRun(" PAGE ", cache, showCode: true);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(prefix);
        paragraph.Runs.Add(Run.PlainTextControl(text, tag: "editable"));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        if (cell)
        {
            var table = Table.Create(1, 1);
            table.Rows[0].Cells[0].Paragraphs.Clear();
            table.Rows[0].Cells[0].Paragraphs.Add(paragraph);
            document.Blocks.Add(table);
        }
        else document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        view.Arrange(new Rect(0, 0, 816, 4000));
        if (forms) view.SetProtection(ProtectionMode.FillingForms);
        return (view, paragraph, prefix);
    }

    private static string ControlText(Paragraph paragraph) =>
        string.Concat(paragraph.Runs.Where(run => run.Control is not null).Select(run => run.Text));

    private static int Offset(DocumentView view, bool cell) =>
        cell ? view.CellCaretInfo!.Value.Offset : view.CaretPositionForTest.Offset;

    private static void Move(DocumentView view, bool cell, int offset)
    {
        if (cell) view.PlaceCaretInCell(0, 0, 0, 0, offset);
        else view.MoveCaretToBlockForTest(0, offset);
        Offset(view, cell).Should().Be(offset);
    }

    private static void Press(DocumentView view, Key key)
    {
        var args = new KeyEventArgs { Key = key, RoutedEvent = DocumentView.KeyDownEvent };
        view.RaiseKeyDownForContextMenuTests(args);
        args.Handled.Should().BeTrue();
    }
}
