using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class ConditionalFormatDialog
{
    private void BuildIconSetThresholdPanel(string? style, IReadOnlyList<CfThresholdModel>? existing = null)
    {
        _iconSetThresholdPanel.Children.Clear();
        _iconSetThresholdRows.Clear();

        var count = ConditionalFormatIconSetPlanner.GetIconCount(style);
        var defaults = ConditionalFormatIconSetPlanner.CreateThresholds(style);

        for (var i = 0; i < count; i++)
        {
            var threshold = existing is not null && i < existing.Count ? existing[i] : defaults[i];

            var typeBox = new ComboBox
            {
                Width = 100,
                Margin = new Thickness(6, 0, 6, 0),
                ItemsSource = Enum.GetValues<CfThresholdType>(),
                SelectedItem = threshold.Type
            };
            if (typeBox.SelectedIndex < 0)
                typeBox.SelectedIndex = 0;

            var valueBox = new TextBox
            {
                Width = 80,
                Text = threshold.Value ?? "",
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Padding = new Thickness(2)
            };

            _iconSetThresholdPanel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2),
                Children =
                {
                    new TextBlock { Text = $"Icon {i + 1}  when  \u2265", Width = 110, VerticalAlignment = System.Windows.VerticalAlignment.Center },
                    typeBox,
                    valueBox
                }
            });
            _iconSetThresholdRows.Add((typeBox, valueBox));
        }
    }
}
