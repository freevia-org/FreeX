namespace Free.Shared.Shell;

/// <summary>
/// Renderer-neutral visual metrics and structural colors for compact desktop dialogs.
/// Product accents and theme-neutral surfaces remain theme resources; these tokens cover the
/// fixed geometry, border, and disabled-state values that WPF and Avalonia must realize identically.
/// </summary>
public static class CompactDialogVisualTokens
{
    public const double ControlHeight = 24;
    public const double ButtonHeight = 26;
    public const double ButtonMinWidth = 84;
    public const double FontSize = 12;
    public const double ButtonPaddingHorizontal = 12;
    public const double ButtonPaddingVertical = 3;
    public const double TextBoxPaddingHorizontal = 5;
    public const double TextBoxPaddingVertical = 3;
    public const double ComboBoxPaddingHorizontal = 5;
    public const double ComboBoxPaddingVertical = 2;
    public const double TogglePaddingLeft = 4;
    public const double LabelPadding = 0;
    public const double GroupBoxMarginVertical = 4;
    public const double GroupBoxPaddingHorizontal = 8;
    public const double GroupBoxPaddingVertical = 6;
    public const double ButtonCornerRadius = 3;
    public const double BorderThickness = 1;
    public const double CheckBoxIndicatorWidth = 14;
    public const double CheckBoxIndicatorHeight = 13;
    public const double CheckBoxCheckMarkWidth = 12;
    public const double CheckBoxCheckMarkHeight = 10;
    public const double CheckBoxIndeterminateMarkWidth = 7;
    public const double CheckBoxIndeterminateMarkHeight = 2;
    public const double RadioButtonIndicatorSize = 13;
    public const double RadioButtonDotSize = 6;
    public const double TabHeaderHeight = 24;
    public const double ListBoxItemMinHeight = 22;
    public const double DisabledToggleOpacity = 0.45;

    public const string BorderHex = "#C8C8C8";
    public const string FieldBorderHex = "#B7BCC2";
    // WPF's native compact input template keeps its keyboard-focus ring blue even when the
    // product's button/default accent is themed. Avalonia routes that opt into WPF input chrome
    // consume this fixed authority token instead of substituting the brand accent.
    public const string FocusedInputBorderHex = "#569DE5";
    public const string DisabledFieldBorderHex = "#D0D1D4";
    public const string DisabledForegroundHex = "#9AA0A6";
    public const string DisabledBorderHex = "#E0E0E0";
    public const string ToggleBorderHex = "#707070";
    public const string ToggleDisabledBackgroundHex = "#E6E6E6";
    public const string ToggleDisabledBorderHex = "#BCBCBC";
    public const string ToggleDisabledMarkHex = "#9E9E9E";
    public const string PrimaryPressedHex = "#093F52";
    public const string PrimaryDisabledHex = "#9FC4CF";
}
