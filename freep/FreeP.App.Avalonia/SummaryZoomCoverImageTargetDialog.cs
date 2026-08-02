using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class SummaryZoomCoverImageTargetDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly ComboBox _target;
    private readonly IReadOnlyList<(string Id, string DisplayName)> _options;

    internal string? SelectedTargetSectionId { get; private set; }

    internal SummaryZoomCoverImageTargetDialog(IReadOnlyList<(string Id, string DisplayName)> options)
    {
        _options = options;
        Title = ZoomCoverImagePlanner.DialogTitle;
        Width = 420;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AvaloniaCompactDialogChrome.ApplyWindow(this, DialogChromeStyle);

        _target = new ComboBox
        {
            ItemsSource = options.Select(option => option.DisplayName).ToArray(),
            SelectedIndex = options.Count == 0 ? -1 : 0,
            MinWidth = 230,
        };
        var ok = MakeButton("OK", true, Apply);
        ok.IsEnabled = options.Count > 0;
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Summary Zoom tile:" },
                _target,
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
        var index = _target.SelectedIndex;
        if (index >= 0 && index < _options.Count)
        {
            SelectedTargetSectionId = _options[index].Id;
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
}
