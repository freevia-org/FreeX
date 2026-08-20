using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

/// <summary>
/// Inline embedded objects are addressed by a logical text position, and a nested inline-table
/// cell editor counts positions in its own cell body. Resolving a cell-local position against the
/// shape's body finds a different object -- or, since in-place commits now write the live model,
/// overwrites one. The activation callback therefore carries the editor that owns the caret, so
/// the shape-level route can recognise a position that is not in its coordinate space and refuse
/// it. This pins that contract: without the editor identity the guard cannot exist.
/// </summary>
public sealed class InlineOleCaretOwnerRoutingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    private static TextBody BodyWithInlineOle()
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run
        {
            Text = "￼",
            InlineOleObject = new InlineOleObjectInfo
            {
                EmbeddedBytes = [1, 2, 3],
                FileName = "Book.xlsx",
                ClassName = "Excel.Sheet.12",
            },
        });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    [Fact]
    public async Task Activation_ReportsTheEditorThatOwnsTheCaret()
    {
        await Session.Dispatch(() =>
        {
            var editor = new AvaloniaRichTextEditor(BodyWithInlineOle(), backgroundAlpha: 0xCC);
            AvaloniaRichTextEditor? reportedOwner = null;

            editor.TryActivateInlineOleObject((caretOwner, _) =>
            {
                reportedOwner = caretOwner;
                return true;
            });

            reportedOwner.Should().BeSameAs(
                editor,
                "with no nested cell edit open the caret belongs to this editor, so its positions " +
                "address the body this editor was built from");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Activation_OffersBothTheCaretPositionAndTheCharacterBeforeIt()
    {
        await Session.Dispatch(() =>
        {
            var editor = new AvaloniaRichTextEditor(BodyWithInlineOle(), backgroundAlpha: 0xCC)
            {
                SelectionStart = 1,
                SelectionEnd = 1,
            };
            var offered = new List<int>();

            editor.TryActivateInlineOleObject((_, position) =>
            {
                offered.Add(position);
                return false;
            });

            offered.Should().Equal(
                [1, 0],
                "a caret sitting just after the object marker must still find it, which is what " +
                "the second attempt is for");
        }, CancellationToken.None);
    }
}
