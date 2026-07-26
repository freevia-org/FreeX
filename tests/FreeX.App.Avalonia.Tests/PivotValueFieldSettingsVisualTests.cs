using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace FreeX.App.Avalonia.Tests;

public sealed class PivotValueFieldSettingsVisualTests
{
    [Fact]
    public void WpfCaptureMetrics_AreEncodedInTheRouteContract()
    {
        PivotValueFieldSettingsVisual.WindowWidth.Should().Be(430);
        PivotValueFieldSettingsVisual.WindowHeight.Should().Be(430);
        PivotValueFieldSettingsVisual.ClientWidth.Should().Be(414);
        PivotValueFieldSettingsVisual.ClientHeight.Should().Be(391);
        PivotValueFieldSettingsVisual.OuterMargin.Should().Be(14);
        PivotValueFieldSettingsVisual.LabelColumnWidth.Should().Be(118);
        PivotValueFieldSettingsVisual.TabContentMargin.Should().Be(10);
        PivotValueFieldSettingsVisual.LabelControlSpacing.Should().Be(6);
        PivotValueFieldSettingsVisual.ControlHeight.Should().Be(24);
        PivotValueFieldSettingsVisual.TextBoxHeight.Should().Be(18);
        PivotValueFieldSettingsVisual.ButtonHeight.Should().Be(20);
        PivotValueFieldSettingsVisual.ButtonWidth.Should().Be(78);
        PivotValueFieldSettingsVisual.NumberFormatButtonWidth.Should().Be(128);
        PivotValueFieldSettingsVisual.ButtonSpacing.Should().Be(8);
        PivotValueFieldSettingsVisual.ButtonTopMargin.Should().Be(12);
    }

    [Fact]
    public void TextBoxChrome_MatchesWpfCompactInput()
    {
        var textBox = new TextBox();

        PivotValueFieldSettingsVisual.ApplyTextBox(textBox);

        textBox.Height.Should().Be(18);
        textBox.MinHeight.Should().Be(18);
        textBox.MaxHeight.Should().Be(18);
        textBox.FontSize.Should().Be(12);
        textBox.CornerRadius.Should().Be(new CornerRadius(0));
        textBox.Background.Should().Be(Brushes.White);
        ((ImmutableSolidColorBrush)textBox.BorderBrush!).Color.Should().Be(Color.FromRgb(130, 130, 130));
        textBox.BorderThickness.Should().Be(new Thickness(1));
    }

    [Fact]
    public void ComboBoxChrome_MatchesWpfCompactInput()
    {
        var comboBox = new ComboBox();

        PivotValueFieldSettingsVisual.ApplyComboBox(comboBox);

        comboBox.Height.Should().Be(24);
        comboBox.MinHeight.Should().Be(24);
        comboBox.MaxHeight.Should().Be(24);
        comboBox.FontSize.Should().Be(12);
        comboBox.CornerRadius.Should().Be(new CornerRadius(0));
        ((ImmutableSolidColorBrush)comboBox.Background!).Color.Should().Be(Color.FromRgb(240, 240, 240));
        ((ImmutableSolidColorBrush)comboBox.BorderBrush!).Color.Should().Be(Color.FromRgb(130, 130, 130));
        comboBox.BorderThickness.Should().Be(new Thickness(1));
    }

    [Fact]
    public void ConditionalTextBoxChrome_UsesWpfControlHeight()
    {
        var textBox = new TextBox();

        PivotValueFieldSettingsVisual.ApplyTextBox(textBox, PivotValueFieldSettingsVisual.ControlHeight);

        textBox.Height.Should().Be(24);
        textBox.MinHeight.Should().Be(24);
        textBox.MaxHeight.Should().Be(24);
        textBox.CornerRadius.Should().Be(new CornerRadius(0));
    }

    [Fact]
    public void ButtonChrome_MatchesWpfCompactActionRow()
    {
        var defaultButton = new Button { IsDefault = true };
        var regularButton = new Button();

        PivotValueFieldSettingsVisual.ApplyButton(defaultButton, isDefault: true);
        PivotValueFieldSettingsVisual.ApplyButton(regularButton, isDefault: false);

        foreach (var button in new[] { defaultButton, regularButton })
        {
            button.Height.Should().Be(20);
            button.MinHeight.Should().Be(20);
            button.MaxHeight.Should().Be(20);
            button.FontSize.Should().Be(12);
            button.Padding.Should().Be(new Thickness(12, 0));
            button.HorizontalContentAlignment.Should().Be(HorizontalAlignment.Center);
            button.VerticalContentAlignment.Should().Be(VerticalAlignment.Center);
            button.CornerRadius.Should().Be(new CornerRadius(0));
            button.Background.Should().NotBeNull();
            button.BorderThickness.Should().Be(new Thickness(1));
        }

        ((ImmutableSolidColorBrush)defaultButton.BorderBrush!).Color.Should().Be(Color.FromRgb(0, 120, 215));
        ((ImmutableSolidColorBrush)regularButton.BorderBrush!).Color.Should().Be(Color.FromRgb(200, 200, 200));
    }
}
