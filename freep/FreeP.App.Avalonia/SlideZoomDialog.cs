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

    internal SlideZoomDialog(IReadOnlyList<(string Id, string DisplayName)> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Title = SlideZoomInsertionPlanner.DialogTitle;
        Width = 420;
        Height = 160;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AvaloniaCompactDialogChrome.ApplyWindow(this, DialogChromeStyle);

        var items = options.Select(option => new TargetOption(option.Id, option.DisplayName)).ToArray();
        _targetCombo = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = items.Length == 0 ? -1 : 0,
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
