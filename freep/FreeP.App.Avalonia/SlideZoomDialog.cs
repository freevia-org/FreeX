using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class SlideZoomDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly ComboBox _targetCombo;

    internal string? SelectedTargetSlideId { get; private set; }

    internal SlideZoomDialog(
        IReadOnlyList<(string Id, string DisplayName)> options,
        string? title = null,
        string? selectedTargetId = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Title = title ?? SlideZoomInsertionPlanner.DialogTitle;
        Width = 420;
        Height = 160;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AvaloniaCompactDialogChrome.ApplyWindow(this, DialogChromeStyle);

        var items = options.Select(option => new TargetOption(option.Id, option.DisplayName)).ToArray();
        _targetCombo = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = FindSelectedIndex(items, selectedTargetId),
            MinWidth = 260,
        };

        var ok = MakeButton("OK", true, Apply);
        ok.IsEnabled = items.Length > 0;
        var cancel = MakeButton("Cancel", false, () => Close(false));
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("115, *"),
                    Children =
                    {
                        new TextBlock { Text = "Target slide:", VerticalAlignment = VerticalAlignment.Center },
                        _targetCombo,
                    },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { ok, cancel },
                },
            },
        };
        Grid.SetColumn(_targetCombo, 1);
    }

    private void Apply()
    {
        SelectedTargetSlideId = (_targetCombo.SelectedItem as TargetOption)?.Id;
        if (!string.IsNullOrWhiteSpace(SelectedTargetSlideId))
            Close(true);
    }

    private static int FindSelectedIndex(IReadOnlyList<TargetOption> options, string? selectedTargetId)
    {
        if (options.Count == 0)
            return -1;
        for (var index = 0; index < options.Count; index++)
            if (string.Equals(options[index].Id, selectedTargetId, StringComparison.OrdinalIgnoreCase))
                return index;
        return 0;
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
