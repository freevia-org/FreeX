using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class SummaryZoomDialog : FreePDialogWindow
{
    private readonly SummaryZoomDialogSession _session;
    private readonly ListBox _targetList;
    private readonly ObservableCollection<ZoomTargetOption> _items;

    internal IReadOnlyList<string> SelectedTargetSectionIds => _session.SelectedTargetIds;

    internal SummaryZoomDialog(
        IReadOnlyList<(string Id, string DisplayName)> options,
        string? title = null,
        IReadOnlyCollection<string>? selectedTargetIds = null)
    {
        _session = new SummaryZoomDialogSession(options, selectedTargetIds, title);
        var surface = _session.Surface;
        Title = surface.Title;
        Width = 460;
        Height = 360;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ZoomDialogChrome.Apply(this, surface);

        _items = new ObservableCollection<ZoomTargetOption>(_session.Options);
        _targetList = new ListBox
        {
            ItemsSource = _items,
            SelectionMode = SelectionMode.Multiple,
            Height = 210,
        };
        ZoomDialogChrome.ApplyField(_targetList, surface.Field(ZoomTargetDialogField.Target));
        foreach (var item in _items)
            if (_session.InitialSelectedTargetIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
                _targetList.SelectedItems?.Add(item);

        var moveUp = ZoomDialogChrome.MakeButton(
            surface.Action(ZoomTargetDialogAction.MoveUp),
            () => MoveSelected(_items, -1));
        var moveDown = ZoomDialogChrome.MakeButton(
            surface.Action(ZoomTargetDialogAction.MoveDown),
            () => MoveSelected(_items, 1));
        var ok = ZoomDialogChrome.MakeButton(
            surface.Action(ZoomTargetDialogAction.Accept),
            Apply,
            _session.CanAccept);
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = surface.Field(ZoomTargetDialogField.Target).Label },
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
                    Children =
                    {
                        ok,
                        ZoomDialogChrome.MakeButton(
                            surface.Action(ZoomTargetDialogAction.Cancel),
                            () => Close(false)),
                    },
                },
            },
        };
    }

    private void Apply()
    {
        var selectedIds = _targetList.SelectedItems?
            .OfType<ZoomTargetOption>()
            .Select(option => option.Id)
            .ToArray() ?? Array.Empty<string>();
        if (_session.TryAccept(selectedIds))
            Close(true);
    }

    private void MoveSelected(ObservableCollection<ZoomTargetOption> items, int delta)
    {
        var selectedIds = _targetList.SelectedItems?
            .OfType<ZoomTargetOption>()
            .Select(option => option.Id)
            .ToArray() ?? Array.Empty<string>();
        if (!_session.TryMoveSelected(selectedIds, delta, out var plan))
            return;

        items.Move(plan!.FromIndex, plan.ToIndex);
        _targetList.SelectedItem = items[plan.ToIndex];
    }
}
