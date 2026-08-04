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

        var items = options.Select(option => new TargetOption(option.Id, option.DisplayName)).ToArray();
        _targetList = new ListBox
        {
            ItemsSource = items,
            SelectionMode = SelectionMode.Multiple,
            Height = 210,
        };
        foreach (var item in items)
            if (selectedTargetIds?.Contains(item.Id, StringComparer.OrdinalIgnoreCase) == true)
                _targetList.SelectedItems?.Add(item);

        var ok = MakeButton("OK", true, Apply);
        ok.IsEnabled = items.Length >= 2;
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
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { ok, MakeButton("Cancel", false, () => Close(false)) },
                },
            },
        };
    }

    private void Apply()
    {
        var selected = _targetList.SelectedItems?
            .OfType<TargetOption>()
            .Select(option => option.Id)
            .ToArray() ?? Array.Empty<string>();
        if (selected.Length >= 2)
        {
            SelectedTargetSectionIds = selected;
            Close(true);
        }
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
