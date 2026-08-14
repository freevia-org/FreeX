using System.Windows;
using System.Windows.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Adapts renderer-neutral compact-dialog metrics to WPF value types for XAML consumption.
/// </summary>
public static class WpfCompactDialogMetrics
{
    public static Color BorderColor { get; } = ParseColor(CompactDialogVisualTokens.BorderHex);

    public static Color FieldBorderColor { get; } = ParseColor(CompactDialogVisualTokens.FieldBorderHex);

    public static Brush DisabledForegroundBrush { get; } = CreateBrush(CompactDialogVisualTokens.DisabledForegroundHex);

    public static Brush DisabledBorderBrush { get; } = CreateBrush(CompactDialogVisualTokens.DisabledBorderHex);

    public static Brush PrimaryPressedBrush { get; } = CreateBrush(CompactDialogVisualTokens.PrimaryPressedHex);

    public static Brush PrimaryDisabledBrush { get; } = CreateBrush(CompactDialogVisualTokens.PrimaryDisabledHex);

    public static Thickness ButtonPadding { get; } = new(
        CompactDialogVisualTokens.ButtonPaddingHorizontal,
        CompactDialogVisualTokens.ButtonPaddingVertical,
        CompactDialogVisualTokens.ButtonPaddingHorizontal,
        CompactDialogVisualTokens.ButtonPaddingVertical);

    public static Thickness UniformBorderThickness { get; } = new(
        CompactDialogVisualTokens.BorderThickness);

    public static CornerRadius ButtonCornerRadius { get; } = new(
        CompactDialogVisualTokens.ButtonCornerRadius);

    private static Color ParseColor(string value) =>
        (Color)ColorConverter.ConvertFromString(value)!;

    private static Brush CreateBrush(string value)
    {
        var brush = new SolidColorBrush(ParseColor(value));
        brush.Freeze();
        return brush;
    }
}
