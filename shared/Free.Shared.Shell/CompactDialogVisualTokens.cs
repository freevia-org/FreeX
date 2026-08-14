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
    public const double FontSize = 12;
    public const double ButtonPaddingHorizontal = 12;
    public const double ButtonPaddingVertical = 3;
    public const double TextBoxPaddingHorizontal = 5;
    public const double TextBoxPaddingVertical = 3;
    public const double ButtonCornerRadius = 3;
    public const double BorderThickness = 1;

    public const string BorderHex = "#C8C8C8";
    public const string FieldBorderHex = "#B7BCC2";
    public const string DisabledForegroundHex = "#9AA0A6";
    public const string DisabledBorderHex = "#E0E0E0";
    public const string PrimaryPressedHex = "#093F52";
    public const string PrimaryDisabledHex = "#9FC4CF";
}
