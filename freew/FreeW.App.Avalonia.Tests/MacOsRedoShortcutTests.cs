using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class MacOsRedoShortcutTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData("body")]
    [InlineData("shape")]
    [InlineData("header")]
    public async Task CommandShiftZRestoresTheUndoneEditInEachStory(string story)
    {
        await Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            var paragraph = (Paragraph)document.Blocks[0];
            paragraph.Runs.Add(new Run("Body"));
            var view = new DocumentView();
            if (story == "shape")
            {
                var shape = Shape.TextBoxWith("Shape", 120, 60);
                shape.Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront };
                paragraph.Runs.Add(new Run(string.Empty) { Shape = shape });
            }
            view.LoadDocument(document);
            view.Measure(new Size(816, 2000));
            if (story == "shape")
            {
                view.SelectFloating(0, paragraph.Runs.Count - 1);
                view.EnterSelectedShapeTextEditing().Should().BeTrue();
            }
            else if (story == "header")
            {
                view.PlaceCaretInHeaderFooter(footer: false);
                view.IsHeaderFooterCaretActive.Should().BeTrue();
            }

            string Text() => story switch
            {
                "shape" => view.SelectedFloatingShape()!.PlainText,
                "header" => view.Document.Sections[0].HeadersFooters.Header!.PlainText,
                _ => view.Document.PlainText,
            };

            var original = Text();
            view.InsertText("edited");
            var edited = Text();
            edited.Should().NotBe(original);
            var undo = Gesture(KeyModifiers.Meta);
            view.RaiseKeyDownForContextMenuTests(undo);
            undo.Handled.Should().BeTrue();
            Text().Should().Be(original);

            var redo = Gesture(KeyModifiers.Meta | KeyModifiers.Shift);
            view.RaiseKeyDownForContextMenuTests(redo);
            redo.Handled.Should().BeTrue();
            Text().Should().Be(edited);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommandShiftZRestoresTheShellHistoryIncludingSplitPaneEdits(bool split)
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                if (split)
                    window.ToggleSplit();
                var editor = split ? window.SplitEditorForTests! : window.Editor;
                var original = editor.Document.PlainText;
                editor.InsertText("edited");
                var edited = editor.Document.PlainText;
                edited.Should().NotBe(original);

                void Press(KeyModifiers modifiers)
                {
                    var args = Gesture(modifiers);
                    if (split)
                    {
                        args.RoutedEvent = InputElement.KeyDownEvent;
                        window.RaiseEvent(args);
                    }
                    else
                        window.RaiseKeyDownForTest(args);
                    args.Handled.Should().BeTrue();
                }

                Press(KeyModifiers.Meta);
                editor.Document.PlainText.Should().Be(original);
                Press(KeyModifiers.Meta | KeyModifiers.Shift);
                editor.Document.PlainText.Should().Be(edited);
                window.Editor.Document.PlainText.Should().Be(edited);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static KeyEventArgs Gesture(KeyModifiers modifiers) =>
        new() { Key = Key.Z, KeyModifiers = modifiers };
}
