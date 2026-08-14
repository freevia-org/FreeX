using System.Windows;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Adapts renderer-neutral compact-dialog metrics to WPF value types for XAML consumption.
/// </summary>
public static class WpfCompactDialogMetrics
{
    public static Thickness ButtonPadding { get; } = new(
        CompactDialogVisualTokens.ButtonPaddingHorizontal,
        CompactDialogVisualTokens.ButtonPaddingVertical,
        CompactDialogVisualTokens.ButtonPaddingHorizontal,
        CompactDialogVisualTokens.ButtonPaddingVertical);

    public static Thickness TextBoxPadding { get; } = new(
        CompactDialogVisualTokens.TextBoxPaddingHorizontal,
        CompactDialogVisualTokens.TextBoxPaddingVertical,
        CompactDialogVisualTokens.TextBoxPaddingHorizontal,
        CompactDialogVisualTokens.TextBoxPaddingVertical);

    public static Thickness UniformBorderThickness { get; } = new(
        CompactDialogVisualTokens.BorderThickness);

    public static CornerRadius ButtonCornerRadius { get; } = new(
        CompactDialogVisualTokens.ButtonCornerRadius);
}
