using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Avalonia;

internal sealed class ThesaurusPane : Border
{
    private readonly DocumentView _editor;
    private readonly Func<string, Task<bool>>? _copyText;
    private readonly ThesaurusPaneSession _session = new();
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
        IsVisible = _session.IsVisible;

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
        ApplyTransition(_session.Toggle(_editor.CurrentProofingWord));
    }

    public void Refresh()
    {
        ApplyTransition(_session.Refresh(_editor.CurrentProofingWord));
    }

    private void ApplyTransition(ThesaurusPaneTransition transition)
    {
        IsVisible = transition.IsVisible;
        if (!transition.ShouldRender)
            return;

        var plan = transition.DisplayPlan;
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

    internal bool ReplaceForTest(string synonym)
    {
        var action = FindAction(synonym);
        var availability = action is null
            ? null
            : _session.PlanAction(
                action,
                _editor.CanReplaceCurrentProofingWord(action.DisplayText),
                CanCopy);
        return availability?.ReplaceIntent is { } intent && Replace(intent);
    }

    internal Task<bool> CopyForTestAsync(string synonym)
    {
        var action = FindAction(synonym);
        var availability = action is null
            ? null
            : _session.PlanAction(
                action,
                _editor.CanReplaceCurrentProofingWord(action.DisplayText),
                CanCopy);
        return availability?.CopyIntent is { } intent
            ? CopyAsync(intent)
            : Task.FromResult(false);
    }

    private Control BuildAction(ThesaurusActionRow action)
    {
        var availability = _session.PlanAction(
            action,
            _editor.CanReplaceCurrentProofingWord(action.DisplayText),
            CanCopy);
        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = action.DisplayText, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var insert = new Button
        {
            Content = "↵",
            MinWidth = 68,
            IsEnabled = availability.CanReplace
        };
        ToolTip.SetTip(insert, action.InsertToolTip);
        insert.Click += (_, _) =>
        {
            if (availability.ReplaceIntent is { } intent)
                Replace(intent);
        };
        var copy = new Button { Content = "Copy", MinWidth = 54 };
        ToolTip.SetTip(copy, action.CopyToolTip);
        copy.Click += async (_, _) =>
        {
            if (availability.CopyIntent is { } intent)
                await CopyAsync(intent);
        };
        copy.IsEnabled = availability.CanCopy;
        _actionButtons.Add((insert, copy));
        Grid.SetColumn(insert, 1);
        Grid.SetColumn(copy, 2);
        grid.Children.Add(label);
        grid.Children.Add(insert);
        grid.Children.Add(copy);
        return grid;
    }

    private bool CanCopy => _copyText is not null;

    private ThesaurusActionRow? FindAction(string synonym) =>
        _session.CurrentPlan.Senses
            .SelectMany(sense => sense.Actions)
            .FirstOrDefault(action => action.DisplayText == synonym);

    private bool Replace(ThesaurusPaneActionIntent intent)
    {
        var replaced = _editor.ReplaceCurrentProofingWord(intent.Text);
        ApplyTransition(_session.CompleteReplacement(replaced, _editor.CurrentProofingWord));
        _editor.Focus();
        return replaced;
    }

    private async Task<bool> CopyAsync(ThesaurusPaneActionIntent intent)
    {
        if (_copyText is not null)
            return await _copyText(intent.Text);
        return false;
    }
}
