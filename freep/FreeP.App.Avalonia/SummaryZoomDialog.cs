using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class SummaryZoomDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly ListBox _targetList;
    private readonly ObservableCollection<TargetOption> _items;

    internal IReadOnlyList<string> SelectedTargetSectionIds { get; private set; } = Array.Empty<string>();

    internal SummaryZoomDialog(
        IReadOnlyList<(string Id, string DisplayName)> options,
        string? title = null,
        IReadOnlyCollection<string>? selectedTargetIds = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Title = title ?? SummaryZoomInsertionPlanner.DialogTitle;
        Width = 460;
        Height = 360;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AvaloniaCompactDialogChrome.ApplyWindow(this, DialogChromeStyle);

        _items = new ObservableCollection<TargetOption>(
            options.Select(option => new TargetOption(option.Id, option.DisplayName)));
        _targetList = new ListBox
        {
            ItemsSource = _items,
            SelectionMode = SelectionMode.Multiple,
            Height = 210,
        };
        foreach (var item in _items)
            if (selectedTargetIds?.Contains(item.Id, StringComparer.OrdinalIgnoreCase) == true)
                _targetList.SelectedItems?.Add(item);

        var moveUp = MakeButton("Move Up", false, () => MoveSelected(_items, -1));
        var moveDown = MakeButton("Move Down", false, () => MoveSelected(_items, 1));
        var ok = MakeButton("OK", true, Apply);
        ok.IsEnabled = _items.Count >= 2;
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Target sections (select at least two):" },
                _targetList,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { moveUp, moveDown },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { ok, MakeButton("Cancel", false, () => Close(false)) },
                },
            },
        };
    }

    private void Apply()
    {
        var selectedIds = _targetList.SelectedItems?
            .OfType<TargetOption>()
            .Select(option => option.Id)
            .ToArray() ?? Array.Empty<string>();
        var selected = SummaryZoomTargetPlanner.SelectOrderedTargets(
            _items.Select(option => option.Id),
            selectedTargetIds: selectedIds);
        if (selected.Count >= 2)
        {
            SelectedTargetSectionIds = selected;
            Close(true);
        }
    }

    private void MoveSelected(ObservableCollection<TargetOption> items, int delta)
    {
        var selected = _targetList.SelectedItems?.OfType<TargetOption>().ToArray();
        if (selected is not { Length: 1 })
            return;

        var index = items.IndexOf(selected[0]);
        var targetIndex = index + delta;
        if (index < 0 || targetIndex < 0 || targetIndex >= items.Count)
            return;

        items.Move(index, targetIndex);
        _targetList.SelectedItem = selected[0];
    }

    private static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }

    private sealed record TargetOption(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
