using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class FreeWContextMenuInteractionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public Task EditorMenu_OpensFromBothKeyboardGesturesAndEscapeClosesIt() =>
        Session.Dispatch(() =>
        {
            var view = new DocumentView();
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph("context menu"));
            view.LoadDocument(document);
            var window = new Window { Content = view };
            try
            {
                window.Show();
                window.Measure(new Size(800, 600));
                window.Arrange(new Rect(0, 0, 800, 600));

                var apps = new KeyEventArgs { Key = Key.Apps };
                view.RaiseKeyDownForContextMenuTests(apps);

                apps.Handled.Should().BeTrue();
                var activeMenu = view.ActiveContextMenuForTests;
                activeMenu.Should().NotBeNull();
                var firstMenu = activeMenu!;
                firstMenu.IsOpen.Should().BeTrue();
                firstMenu.Items.OfType<MenuItem>().Should().HaveCount(7);

                var escape = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Escape,
                    Source = firstMenu,
                };
                firstMenu.RaiseEvent(escape);

                escape.Handled.Should().BeTrue();
                firstMenu.IsOpen.Should().BeFalse();
                view.ActiveContextMenuForTests.Should().BeNull();

                var shiftF10 = new KeyEventArgs { Key = Key.F10, KeyModifiers = KeyModifiers.Shift };
                view.RaiseKeyDownForContextMenuTests(shiftF10);

                shiftF10.Handled.Should().BeTrue();
                view.ActiveContextMenuForTests.Should().NotBeNull();
                view.ActiveContextMenuForTests!.Close();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

    [Fact]
    public Task TableCellContentControlMenu_TracksCheckedProtectionAndUndoableEffect() =>
        Session.Dispatch(() =>
        {
            var choices = new[]
            {
                new ContentControlListItem("Red", "R"),
                new ContentControlListItem("Green", "G"),
            };
            var controlledRun = Run.DropDownListControl(choices);
            controlledRun.Text = "Red";
            var paragraph = new Paragraph();
            paragraph.Runs.Add(controlledRun);
            var cell = new TableCell();
            cell.Paragraphs.Add(paragraph);
            var table = Table.Create(1, 1);
            table.Rows[0].Cells[0] = cell;
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(table);

            var view = new DocumentView();
            view.LoadDocument(document);
            var window = new Window { Content = view };
            try
            {
                window.Show();
                window.Measure(new Size(800, 600));
                window.Arrange(new Rect(0, 0, 800, 600));
                view.PlaceCaretInCell(0, 0, 0, 0, 0);

                view.RaiseKeyDownForContextMenuTests(new KeyEventArgs { Key = Key.Apps });
                var activeMenu = view.ActiveContextMenuForTests;
                activeMenu.Should().NotBeNull();
                var menu = activeMenu!;
                var items = menu.Items.OfType<MenuItem>().ToArray();
                items.Should().HaveCount(2);
                items[0].IsChecked.Should().BeTrue();
                items[1].IsChecked.Should().BeFalse();

                items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                paragraph.Runs[0].Text.Should().Be("Green");
                view.CanUndo.Should().BeTrue();
                view.Undo();
                paragraph.Runs[0].Text.Should().Be("Red");
                menu.Close();

                view.SetProtection(ProtectionMode.ReadOnly);
                view.PlaceCaretInCell(0, 0, 0, 0, 0);
                view.RaiseKeyDownForContextMenuTests(new KeyEventArgs { Key = Key.Apps });
                view.ActiveContextMenuForTests!.Items.OfType<MenuItem>()
                    .Should().OnlyContain(item => !item.IsEnabled);
                view.ActiveContextMenuForTests.Close();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

    [Fact]
    public Task EffectsAndTableStyleCommands_MutateAndUndoRealDocumentState() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            var table = Table.Create(1, 1);
            document.Blocks.Add(table);
            var view = new DocumentView();
            view.LoadDocument(document);

            var effect = DocumentEffectSet.Catalog[2];
            var originalEffect = document.Theme.EffectSetName;
            view.ApplyEffectSet(effect);
            document.Theme.EffectSetName.Should().Be(effect.Name);
            view.Undo();
            document.Theme.EffectSetName.Should().Be(originalEffect);

            var tableIndex = document.Blocks.IndexOf(table);
            view.PlaceCaretInCell(tableIndex, 0, 0, 0, 0);
            var originalStyle = table.TableStyleId;
            var originalFormatting = table.Formatting;
            var style = DocumentTableStyle.Catalog.First(candidate => candidate.WordStyleId != originalStyle);
            view.ApplyTableStyle(style);
            table.TableStyleId.Should().Be(style.WordStyleId);
            table.Formatting.Borders.Should().Be(style.Borders);
            view.Undo();
            table.TableStyleId.Should().Be(originalStyle);
            table.Formatting.Should().Be(originalFormatting);
        }, CancellationToken.None);

    [Fact]
    public void OutlineCommands_MutateMoveCollapseAndUndoThroughTheEditor()
    {
        var first = Heading("Heading2", "First");
        var body = new Paragraph("Body");
        var second = Heading("Heading1", "Second");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(first);
        document.Blocks.Add(body);
        document.Blocks.Add(second);
        var view = new DocumentView();
        view.LoadDocument(document);

        view.PromoteHeading(0);
        first.StyleId.Should().Be("Heading1");
        view.Undo();
        first.StyleId.Should().Be("Heading2");

        view.DemoteHeading(0);
        first.StyleId.Should().Be("Heading3");
        view.Undo();
        first.StyleId.Should().Be("Heading2");

        view.CollapseHeading(0);
        view.IsHeadingCollapsed(0).Should().BeTrue();
        view.ExpandHeading(0);
        view.IsHeadingCollapsed(0).Should().BeFalse();

        first.StyleId = "Heading1";
        var movedIndex = view.MoveHeading(2, moveUp: true);
        movedIndex.Should().Be(0);
        document.Blocks[0].Should().BeSameAs(second);
        view.Undo();
        document.Blocks[2].Should().BeSameAs(second);
    }

    private static Paragraph Heading(string styleId, string text) =>
        new(text) { StyleId = styleId };
}
