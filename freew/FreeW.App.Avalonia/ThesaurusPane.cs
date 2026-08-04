using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Avalonia;

internal sealed class ThesaurusPane : Border
{
    private readonly DocumentView _editor;
    private readonly Func<string, Task<bool>>? _copyText;
    private readonly TextBlock _heading;
    private readonly TextBlock _status;
    private readonly StackPanel _senses;
    private readonly List<(Button Insert, Button Copy)> _actionButtons = [];

    internal ThesaurusPane(DocumentView editor)
        : this(editor, (Func<string, Task<bool>>?)null)
    {
    }

    internal ThesaurusPane(DocumentView editor, Func<string, Task> copyText)
        : this(editor, async text =>
        {
            await copyText(text);
            return true;
        })
    {
    }

    internal ThesaurusPane(DocumentView editor, Func<string, Task<bool>>? copyText)
    {
        _editor = editor;
        _copyText = copyText;
        Width = 280;
        Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFB));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
        BorderThickness = new Thickness(1, 0, 0, 0);
        IsVisible = false;

        _heading = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 16, Margin = new Thickness(10, 2), TextWrapping = TextWrapping.Wrap };
        _status = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)), Margin = new Thickness(10, 2, 10, 8), TextWrapping = TextWrapping.Wrap };
        _senses = new StackPanel();
        var layout = new DockPanel { LastChildFill = true };
        var header = new TextBlock { Text = "Thesaurus", FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 8, 10, 6) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_heading, Dock.Top);
        DockPanel.SetDock(_status, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(_heading);
        layout.Children.Add(_status);
        layout.Children.Add(new ScrollViewer { Content = _senses, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Child = layout;
    }

    internal string HeadingForTest => _heading.Text ?? string.Empty;
    internal int SenseCountForTest => _senses.Children.OfType<StackPanel>().Count();
    internal IReadOnlyList<(bool InsertEnabled, bool CopyEnabled)> ActionStatesForTest =>
        _actionButtons.Select(buttons => (buttons.Insert.IsEnabled, buttons.Copy.IsEnabled)).ToArray();

    public void Toggle()
    {
        IsVisible = !IsVisible;
        if (IsVisible)
            Refresh();
    }

    public void Refresh()
    {
        if (!IsVisible)
            return;
        var plan = ThesaurusPresentationPlanner.Lookup(_editor.CurrentProofingWord);
        _heading.Text = plan.HeadingText;
        _status.Text = plan.StatusText;
        _senses.Children.Clear();
        _actionButtons.Clear();
        foreach (var sense in plan.Senses)
        {
            var panel = new StackPanel { Margin = new Thickness(10, 5, 10, 3), Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = sense.DisplayLabel, FontWeight = FontWeight.SemiBold });
            foreach (var action in sense.Actions)
                panel.Children.Add(BuildAction(action));
            _senses.Children.Add(panel);
        }
    }

    internal bool ReplaceForTest(string synonym) => Replace(synonym);
    internal Task<bool> CopyForTestAsync(string synonym) => CopyAsync(synonym);

    private Control BuildAction(ThesaurusActionRow action)
    {
        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = action.DisplayText, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var insert = new Button
        {
            Content = "Insert",
            MinWidth = 68,
            IsEnabled = action.CanInsert() && _editor.CanReplaceCurrentProofingWord(action.DisplayText)
        };
        ToolTip.SetTip(insert, action.InsertToolTip);
        insert.Click += (_, _) => Replace(action.DisplayText);
        var copy = new Button { Content = "Copy", MinWidth = 54 };
        ToolTip.SetTip(copy, action.CopyToolTip);
        copy.Click += async (_, _) => await CopyAsync(action.DisplayText);
        copy.IsEnabled = CanCopy;
        _actionButtons.Add((insert, copy));
        Grid.SetColumn(insert, 1);
        Grid.SetColumn(copy, 2);
        grid.Children.Add(label);
        grid.Children.Add(insert);
        grid.Children.Add(copy);
        return grid;
    }

    private bool CanCopy => _copyText is not null || TopLevel.GetTopLevel(this)?.Clipboard is not null;

    private bool Replace(string synonym)
    {
        var replaced = _editor.ReplaceCurrentProofingWord(synonym);
        if (replaced)
            Refresh();
        _editor.Focus();
        return replaced;
    }

    private async Task<bool> CopyAsync(string synonym)
    {
        if (_copyText is not null)
            return await _copyText(synonym);
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            try
            {
                await clipboard.SetTextAsync(synonym);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        return false;
    }
}
