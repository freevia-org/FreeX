using System.Windows;
using System.Windows.Controls;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class ConditionalFormatDialog
{
    private static readonly string[] IconOverrideChoices = BuildIconOverrideChoices();

    private static string[] BuildIconOverrideChoices()
    {
        var choices = new List<string>
        {
            UiText.Get("ConditionalFormatDialog_IconOverride_Default"),
            UiText.Get("ConditionalFormatDialog_IconOverride_NoIcon")
        };
        foreach (var option in ConditionalFormatIconSetPlanner.Options)
        {
            for (var i = 0; i < option.IconCount; i++)
                choices.Add($"{option.Label} {i + 1}|{option.Style}|{i}");
        }

        return choices.ToArray();
    }

    private static string IconOverrideToChoice(CfIconOverride? ovr)
    {
        if (ovr is null) return UiText.Get("ConditionalFormatDialog_IconOverride_Default");
        if (string.Equals(ovr.IconSet, "NoIcons", StringComparison.OrdinalIgnoreCase)) return UiText.Get("ConditionalFormatDialog_IconOverride_NoIcon");
        var option = ConditionalFormatIconSetPlanner.Options
            .FirstOrDefault(o => string.Equals(o.Style, ovr.IconSet, StringComparison.Ordinal));
        if (option is null) return UiText.Get("ConditionalFormatDialog_IconOverride_Default");
        return $"{option.Label} {ovr.IconId + 1}|{option.Style}|{ovr.IconId}";
    }

    private static CfIconOverride? ChoiceToIconOverride(string? choice)
    {
        if (choice is null || choice == UiText.Get("ConditionalFormatDialog_IconOverride_Default")) return null;
        if (choice == UiText.Get("ConditionalFormatDialog_IconOverride_NoIcon")) return new CfIconOverride("NoIcons", 0);
        var parts = choice.Split('|');
        if (parts.Length == 3 && int.TryParse(parts[2], out var iconId))
            return new CfIconOverride(parts[1], iconId);
        return null;
    }

    private void BuildIconSetThresholdPanel(string? style, IReadOnlyList<CfThresholdModel>? existing = null,
        IReadOnlyList<CfIconOverride>? existingOverrides = null)
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

            var existingOvr = existingOverrides is not null && i < existingOverrides.Count
                ? existingOverrides[i]
                : null;
            var overrideBox = new ComboBox
            {
                Width = 160,
                Margin = new Thickness(10, 0, 0, 0),
                ItemsSource = IconOverrideChoices,
                SelectedItem = IconOverrideToChoice(existingOvr)
            };
            if (overrideBox.SelectedIndex < 0)
                overrideBox.SelectedIndex = 0;

            _iconSetThresholdPanel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2),
                Children =
                {
                    new TextBlock { Text = UiText.Format("ConditionalFormatDialog_IconThresholdTextFormat", i + 1), Width = 110, VerticalAlignment = System.Windows.VerticalAlignment.Center },
                    typeBox,
                    valueBox,
                    overrideBox
                }
            });
            _iconSetThresholdRows.Add((typeBox, valueBox, overrideBox));
        }
    }
}
