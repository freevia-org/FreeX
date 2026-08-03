namespace Free.Shared.Shell;

/// <summary>Shared geometry and typography contract for the WPF-authority About dialog.</summary>
public static class AboutDialogMetrics
{
    public const double Width = 560;
    public const double Height = 420;
    public const double MinWidth = 480;
    public const double MinHeight = 320;
    public const double TextFontSize = 12;
    // Linux text rendering needs this measured 0.3 DIP adjustment to match the WPF authority's
    // line placement at the shared 560x420 client capture size.
    public const double AvaloniaTextFontSize = 12.3;
    // Avalonia's default multiline line box is shorter than WPF's 12px dialog line box. These
    // named host corrections keep paragraph spacing and the first visible line aligned without
    // changing the shared WPF metrics.
    public const double AvaloniaTextPaddingLeft = TextPadding + 2;
    public const double AvaloniaTextPaddingTop = TextPadding + 4;
    public const double AvaloniaTextPaddingRight = 0;
    public const double AvaloniaTextLineHeight = 16;
    public const double TextMinHeight = 220;
    public const double TextPadding = 8;
    public const double RootMargin = 16;
    public const double ActionTopMargin = 12;
    public const double ButtonWidth = 84;
}
