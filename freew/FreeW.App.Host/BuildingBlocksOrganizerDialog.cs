using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.QuickParts;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// FreeW's Building Blocks Organizer (Word's Insert › Quick Parts › Building Blocks Organizer). It lists the
/// saved building blocks from the shared <see cref="QuickPartLibrary"/> — the very same snippet store that
/// "Save Selection to Quick Parts" writes and "Insert Quick Part" reads, persisted as
/// <c>quickparts.json</c> under FreeW's data folder — showing each block's name, gallery and category, plus a
/// read-only preview of the selected block's content. It offers two actions on the selected block:
/// <b>Insert</b> drops the block's text at the caret (through <see cref="DocumentView.InsertText(string)"/>,
/// so it is reversible) and closes; <b>Delete</b> removes the block from the library (persisting the change)
/// and refreshes the list. View-only over the library: it touches no docx I/O and changes no model shapes.
/// Mirrors <see cref="BookmarkManagerDialog"/>'s list + actions pattern and reuses the shared
/// <see cref="Free.Shared.Ribbon.Wpf.DialogWindow"/> chrome.
/// </summary>
internal sealed class BuildingBlocksOrganizerDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly DocumentView _editor;
    private readonly BuildingBlocksOrganizerSession _session;
    private readonly ListBox _list = new()
    {
        MinWidth = BuildingBlocksOrganizerPlanner.ListMinWidth,
        MinHeight = BuildingBlocksOrganizerPlanner.ListMinHeight
    };
    private readonly TextBox _preview = new()
    {
        MinWidth = BuildingBlocksOrganizerPlanner.PreviewMinWidth,
        MinHeight = BuildingBlocksOrganizerPlanner.PreviewMinHeight,
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Segoe UI"),
        Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7))
    };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _insertButton;
    private readonly Button _deleteButton;
    private bool _updatingProjection;

    private BuildingBlocksOrganizerDialog(Window? owner, DocumentView editor, QuickPartLibrary library)
    {
        _editor = editor;
        _session = BuildingBlocksOrganizerPlanner.CreateSession(library);
        Owner = owner;
        Title = BuildingBlocksOrganizerPlanner.Title;
        Width = BuildingBlocksOrganizerPlanner.Width;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = BuildingBlocksOrganizerPlanner.ListLabel, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });

        _list.SelectionChanged += (_, _) => OnSelectionChanged();
        _list.MouseDoubleClick += (_, _) => Insert();

        // Two side-by-side columns: the block list on the left, a read-only preview on the right.
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BuildingBlocksOrganizerPlanner.ColumnGap) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftColumn = new StackPanel();
        leftColumn.Children.Add(_list);
        Grid.SetColumn(leftColumn, 0);
        columns.Children.Add(leftColumn);

        var rightColumn = new StackPanel();
        Grid.SetColumn(rightColumn, 2);
        rightColumn.Children.Add(new TextBlock { Text = BuildingBlocksOrganizerPlanner.PreviewLabel, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        rightColumn.Children.Add(_preview);
        columns.Children.Add(rightColumn);

        panel.Children.Add(columns);

        _insertButton = MakeButton(BuildingBlocksOrganizerPlanner.InsertText, (_, _) => Insert());
        _insertButton.IsDefault = true;
        _deleteButton = MakeButton(BuildingBlocksOrganizerPlanner.DeleteText, (_, _) => Delete());
        var closeButton = MakeButton(BuildingBlocksOrganizerPlanner.CloseText, (_, _) => Close());
        closeButton.IsCancel = true;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        buttons.Children.Add(_insertButton);
        buttons.Children.Add(_deleteButton);
        buttons.Children.Add(closeButton);
        panel.Children.Add(buttons);

        panel.Children.Add(_status);
        Content = panel;

        RefreshList();
    }

    /// <summary>Opens the Building Blocks Organizer modally over <paramref name="owner"/>.</summary>
    public static void Show(Window? owner, DocumentView editor, QuickPartLibrary library) =>
        new BuildingBlocksOrganizerDialog(owner, editor, library).ShowDialog();

    private void RefreshList()
    {
        var state = _session.Current;
        _updatingProjection = true;
        _list.Items.Clear();
        foreach (var item in state.Items)
            _list.Items.Add(item);
        _list.SelectedIndex = state.SelectedIndex;
        _updatingProjection = false;
        ApplyState(state);
    }

    private void OnSelectionChanged()
    {
        if (_updatingProjection)
            return;

        ApplyState(_session.SelectIndex(_list.SelectedIndex));
    }

    private void ApplyState(BuildingBlocksOrganizerState state)
    {
        _insertButton.IsEnabled = state.CanInsert;
        _deleteButton.IsEnabled = state.CanDelete;
        _preview.Text = state.PreviewText;
        _status.Text = state.StatusText;
    }

    private void Insert()
    {
        if (_session.AcceptSelection() is not { } action)
            return;

        // Insert through the editor's normal edit path so it is reversible, then close.
        _editor.Focus();
        _editor.InsertText(action.Text);
        Close();
    }

    private void Delete()
    {
        if (!_session.Current.CanDelete)
            return;

        _session.DeleteSelection();
        RefreshList();
    }

    private static Button MakeButton(string content, RoutedEventHandler onClick)
    {
        var button = new Button { Content = content, MinWidth = 84, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 3, 6, 3) };
        button.Click += onClick;
        return button;
    }
}
