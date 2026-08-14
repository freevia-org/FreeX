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

    public static Thickness ComboBoxPadding { get; } = new(
        CompactDialogVisualTokens.ComboBoxPaddingHorizontal,
        CompactDialogVisualTokens.ComboBoxPaddingVertical,
        CompactDialogVisualTokens.ComboBoxPaddingHorizontal,
        CompactDialogVisualTokens.ComboBoxPaddingVertical);

    public static Thickness TogglePadding { get; } = new(
        CompactDialogVisualTokens.TogglePaddingLeft,
        0,
        0,
        0);

    public static Thickness LabelPadding { get; } = new(
        CompactDialogVisualTokens.LabelPadding);

    public static Thickness GroupBoxMargin { get; } = new(
        0,
        CompactDialogVisualTokens.GroupBoxMarginVertical,
        0,
        CompactDialogVisualTokens.GroupBoxMarginVertical);

    public static Thickness GroupBoxPadding { get; } = new(
        CompactDialogVisualTokens.GroupBoxPaddingHorizontal,
        CompactDialogVisualTokens.GroupBoxPaddingVertical,
        CompactDialogVisualTokens.GroupBoxPaddingHorizontal,
        CompactDialogVisualTokens.GroupBoxPaddingVertical);

    public static Thickness UniformBorderThickness { get; } = new(
        CompactDialogVisualTokens.BorderThickness);

    public static CornerRadius ButtonCornerRadius { get; } = new(
        CompactDialogVisualTokens.ButtonCornerRadius);
}
