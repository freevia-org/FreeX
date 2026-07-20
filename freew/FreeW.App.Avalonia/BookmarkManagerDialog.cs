using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class BookmarkManagerDialog : FreeWDialogWindow
{
    private readonly DocumentView _editor;
    private readonly ListBox _list;
    private readonly TextBlock _status;
    private readonly Button _goTo;
    private readonly Button _delete;

    internal BookmarkManagerDialog(DocumentView editor)
    {
        _editor = editor;
        Title = "Bookmark Manager";
        Width = 390;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        _list = new ListBox { MinWidth = 310, MinHeight = 180 };
        _list.SelectionChanged += (_, _) => UpdateButtons();
        _list.DoubleTapped += (_, _) => GoTo();
        _status = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)), Margin = new Thickness(0, 8, 0, 0) };
        _goTo = Button("Go To", GoTo);
        _delete = Button("Delete", Delete);
        var close = Button("Close", Close);
        close.IsCancel = true;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { _goTo, _delete, close },
        };
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                new TextBlock { Text = "Bookmarks:", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) },
                _list,
                buttons,
                _status,
            },
        };
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            Close();
            args.Handled = true;
        };
        RefreshList();
    }

    public static Task ShowAsync(Window owner, DocumentView editor) =>
        new BookmarkManagerDialog(editor).ShowDialog(owner);

    internal int ItemCountForTest => _list.ItemCount;
    internal void SelectForTest(int index) => _list.SelectedIndex = index;
    internal void DeleteForTest() => Delete();
    internal void GoToForTest() => GoTo();

    private void RefreshList(string? preserveName = null)
    {
        var items = Bookmarks.List(_editor.Document).Select(location => new Item(location.Name, location.BlockIndex)).ToArray();
        _list.ItemsSource = items;
        if (items.Length == 0)
        {
            _status.Text = "This document has no bookmarks.";
            _list.SelectedIndex = -1;
        }
        else
        {
            var index = preserveName is null ? 0 : Array.FindIndex(items, item => item.Name == preserveName);
            _list.SelectedIndex = index >= 0 ? index : 0;
            _status.Text = string.Empty;
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var enabled = _list.SelectedItem is Item;
        _goTo.IsEnabled = enabled;
        _delete.IsEnabled = enabled;
    }

    private void GoTo()
    {
        if (_list.SelectedItem is not Item item)
            return;
        _editor.GoToBookmark(item.Name);
        Close();
    }

    private void Delete()
    {
        if (_list.SelectedItem is not Item item)
            return;
        _editor.DeleteBookmark(item.Name);
        RefreshList();
        _status.Text = $"Removed bookmark \"{item.Name}\".";
    }

    private static Button Button(string text, Action click)
    {
        var button = new Button { Content = text, MinWidth = 78 };
        button.Click += (_, _) => click();
        return button;
    }

    private sealed record Item(string Name, int BlockIndex)
    {
        public override string ToString() => Name;
    }
}
