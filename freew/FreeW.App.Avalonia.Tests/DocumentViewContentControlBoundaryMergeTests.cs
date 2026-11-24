using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewContentControlBoundaryMergeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    public static IEnumerable<object[]> LiteralControlsAndLocks()
    {
        foreach (var backward in new[] { false, true })
        foreach (var kind in Enum.GetValues<ContentControlKind>())
        {
            var textControl = kind is ContentControlKind.PlainText or ContentControlKind.RichText;
            var modes = textControl
                ? new[] { ContentControlLockMode.Unlocked, ContentControlLockMode.ContentLocked,
                    ContentControlLockMode.ControlLocked, ContentControlLockMode.ControlAndContentLocked }
                : new[] { ContentControlLockMode.ControlAndContentLocked };
            foreach (var mode in modes)
            foreach (var empty in textControl ? new[] { false, true } : new[] { false })
                yield return [backward, kind, mode, empty];
        }
    }

    [Theory]
    [MemberData(nameof(LiteralControlsAndLocks))]
    public Task BoundaryMergePreservesWholeLiteralControlItsLocksAndHistory(bool backward, ContentControlKind kind,
        ContentControlLockMode mode, bool empty) => Session.Dispatch(() =>
    {
        var control = new ContentControl(kind, Tag: "Applicant", Alias: "Name", LockMode: mode);
        var earlier = new Paragraph("Head");
        earlier.SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage);
        var later = new Paragraph();
        later.Runs.Add(new Run(empty ? "" : "B", RunFormatting.Default with { Bold = true }) { Control = control });
        later.Runs.Add(new Run(empty ? "" : "ob", RunFormatting.Default with { Italic = true }) { Control = control });
        later.Runs.Add(new Run(" tail"));
        later.SectionBreak = new Section(new PageSettings(), SectionBreakKind.Continuous);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.AddRange([earlier, later]);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        view.MoveCaretToBlockForTest(backward ? 1 : 0, backward ? 0 : 4);

        if (backward) view.BackspaceForTest(); else view.DeleteForwardForTest();

        AssertMerged();
        view.Undo();
        document.Blocks.Should().Equal(earlier, later);
        later.Runs.Where(run => run.Control is not null).Should().OnlyContain(run => ReferenceEquals(run.Control, control));
        (view.CaretBlockForTest, view.CaretOffsetForTest).Should().Be(backward ? (1, 0) : (0, 4));
        view.CanUndo.Should().BeFalse();
        view.Redo();
        AssertMerged();

        void AssertMerged()
        {
            var merged = document.Paragraphs.Should().ContainSingle().Subject;
            merged.PlainText.Should().Be(empty ? "Head tail" : "HeadBob tail");
            merged.SectionBreak!.BreakKind.Should().Be(SectionBreakKind.Continuous);
            var runs = merged.Runs.Where(run => run.Control is not null).ToArray();
            runs.Should().HaveCount(2, "merging the paragraph mark must keep complete formatted control runs");
            runs.Should().OnlyContain(run => ReferenceEquals(run.Control, control));
            runs[0].Formatting.Bold.Should().BeTrue();
            runs[1].Formatting.Italic.Should().BeTrue();
            runs.Select(run => run.Text).Should().Equal(new[] { empty ? "" : "B", empty ? "" : "ob" });
            (view.CaretBlockForTest, view.CaretOffsetForTest).Should().Be((0, 4));
        }
    }, CancellationToken.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FillingFormsKeepsTheParagraphBoundaryOutsideTheControl(bool backward) => Session.Dispatch(() =>
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var earlier = new Paragraph("Head");
        var later = new Paragraph();
        later.Runs.Add(Run.PlainTextControl("Bob", tag: "Applicant"));
        document.Blocks.AddRange([earlier, later]);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.SetProtection(ProtectionMode.FillingForms);
        view.MoveCaretToBlockForTest(backward ? 1 : 0, backward ? 0 : 4);

        if (backward) view.BackspaceForTest(); else view.DeleteForwardForTest();

        document.Blocks.Should().Equal(earlier, later);
        earlier.PlainText.Should().Be("Head");
        later.PlainText.Should().Be("Bob");
    }, CancellationToken.None);
}
