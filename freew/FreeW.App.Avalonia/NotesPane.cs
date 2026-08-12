using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation;
using FreeW.App.Presentation.Panes;

namespace FreeW.App.Avalonia;

internal sealed class NotesPane : Border
{
    private readonly ListBox _list;
    private readonly TextBlock _selectedLabel;
    private readonly DocumentView _subEditor;
    private readonly Button _apply;
    private readonly Button _delete;
    private readonly DocumentNotesPaneSession _session;

    internal NotesPane(DocumentView editor)
    {
        _session = new DocumentNotesPaneSession(
            () => editor.Document,
            new DocumentNotesPaneMutationActions(
                (id, footnote, paragraphs) =>
                {
                    var exists = footnote
                        ? editor.Document.Footnotes.ContainsKey(id)
                        : editor.Document.Endnotes.ContainsKey(id);
                    if (!exists)
                        return false;
                    editor.ReplaceNoteContent(id, footnote, paragraphs);
                    return true;
                },
                (id, footnote) =>
                {
                    var exists = footnote
                        ? editor.Document.Footnotes.ContainsKey(id)
                        : editor.Document.Endnotes.ContainsKey(id);
                    if (!exists)
                        return false;
                    editor.DeleteNote(id, footnote);
                    return true;
                }));
        Width = double.NaN;
        MinHeight = 190;
        MaxHeight = 310;
        Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
        BorderThickness = new Thickness(0, 1, 0, 0);
        IsVisible = false;

        _list = new ListBox { MinHeight = 58, MaxHeight = 92, Margin = new Thickness(8, 0, 8, 4) };
        _list.SelectionChanged += OnSelectionChanged;
        _selectedLabel = new TextBlock
        {
            FontStyle = FontStyle.Italic,
            Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
            Margin = new Thickness(10, 0, 10, 2),
            IsVisible = false,
        };
        _subEditor = new DocumentView { MinHeight = 76, MaxHeight = 145, Margin = new Thickness(8, 0, 8, 4), IsVisible = false };
        _apply = MakeButton(FreeWUiTextCatalog.NotesApply, ApplySelected);
        _delete = MakeButton(FreeWUiTextCatalog.NotesDelete, DeleteSelected);
        _apply.IsVisible = false;
        _delete.IsVisible = false;

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [_apply, _delete],
            new Thickness(8, 0, 8, 6),
            AvaloniaCompactDialogChrome.WindowsStyle with { ActionSpacing = 6 });
        DockPanel.SetDock(buttons, Dock.Bottom);

        var layout = new DockPanel { LastChildFill = true };
        var header = new TextBlock { Text = FreeWUiTextCatalog.NotesHeading, FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 7, 10, 4) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_list, Dock.Top);
        DockPanel.SetDock(_selectedLabel, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(_list);
        layout.Children.Add(_selectedLabel);
        layout.Children.Add(buttons);
        layout.Children.Add(_subEditor);
        Child = layout;
    }

    internal int ItemCountForTest => _list.ItemCount;
    internal DocumentView SubEditorForTest => _subEditor;

    public void Toggle()
    {
        IsVisible = !IsVisible;
        if (IsVisible)
            Refresh();
    }

    public void ShowAndSelect(bool footnote, int id)
    {
        IsVisible = true;
        Render(_session.ShowAndSelect(footnote, id));
    }

    public void Refresh()
    {
        if (IsVisible)
            Render(_session.Refresh());
    }

    internal void SelectForTest(bool footnote, int id) => ShowAndSelect(footnote, id);
    internal void ApplyForTest() => ApplySelected();
    internal void DeleteForTest() => DeleteSelected();

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        Render(_session.SelectIndex(_list.SelectedIndex));
    }

    private void Render(DocumentNotesPaneOutcome outcome)
    {
        var state = outcome.State;
        _list.SelectionChanged -= OnSelectionChanged;
        _list.ItemsSource = state.Items;
        _list.SelectedIndex = state.SelectedIndex;
        _list.SelectionChanged += OnSelectionChanged;

        var selected = state.SelectedNote;
        _selectedLabel.Text = selected?.Label ?? string.Empty;
        _selectedLabel.IsVisible = state.HasSelection;
        _subEditor.IsVisible = state.HasSelection;
        _apply.IsVisible = state.CanApply;
        _delete.IsVisible = state.CanDelete;
        if (state.EditorDocument is { } editorDocument)
            _subEditor.LoadDocument(editorDocument);
    }

    private void ApplySelected()
    {
        Render(_session.Apply(_subEditor.Document.Blocks));
    }

    private void DeleteSelected()
    {
        Render(_session.DeleteSelected());
    }

    private static Button MakeButton(string text, Action click)
    {
        var button = new Button { Content = text, MinWidth = 72, Padding = new Thickness(8, 3) };
        button.Click += (_, _) => click();
        return button;
    }

}

internal sealed class NoteTextDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle Chrome = AvaloniaCompactDialogChrome.WindowsStyle;
    private readonly TextBox _text;

    private NoteTextDialog(bool footnote)
    {
        Title = footnote ? FreeWUiTextCatalog.InsertFootnoteTitle : FreeWUiTextCatalog.InsertEndnoteTitle;
        Width = 390;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        _text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90 };
        AvaloniaCompactDialogChrome.ApplyTextBox(_text, Chrome);
        var ok = new Button { Content = FreeWUiTextCatalog.NoteDialogOk, IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, Chrome, 72, true);
        ok.Click += (_, _) =>
        {
            var value = _text.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                Close(value);
        };
        var cancel = new Button { Content = FreeWUiTextCatalog.NoteDialogCancel, IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, Chrome, 72);
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                new TextBlock { Text = footnote ? FreeWUiTextCatalog.FootnoteTextLabel : FreeWUiTextCatalog.EndnoteTextLabel, Margin = new Thickness(0, 0, 0, 4) },
                _text,
                AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)),
            },
        };
        Opened += (_, _) => _text.Focus();
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            Close(null);
            args.Handled = true;
        };
    }

    public static Task<string?> ShowAsync(Window owner, bool footnote) =>
        new NoteTextDialog(footnote).ShowDialog<string?>(owner);
}
