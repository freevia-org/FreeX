using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Shared WPF-authority visual contract for the WPF and Avalonia Selection panes.
/// Hosts retain native control construction and input wiring.
/// </summary>
public static class PresentationSelectionPaneVisualMetrics
{
    public const double PaneWidth = 320;
    public const double PaneBorderThickness = 1;
    public const double HeadingFontSize = 15;
    public const double ContentSideMargin = 12;
    public const double HeadingTopMargin = 12;
    public const double HeadingBottomMargin = 4;
    public const double MessageBottomMargin = 8;
    public const double SelectHorizontalPadding = 8;
    public const double SelectVerticalPadding = 5;
    public const double ItemVerticalMargin = 1;
    public const double SelectRightMargin = 4;
    public const double NestingIndent = 16;
    public const double RenameMinimumWidth = 170;
    public const double FieldHorizontalPadding = 4;
    public const double FieldVerticalPadding = 3;
    public const double RenameRightMargin = 4;
    public const double VisibilityMinimumWidth = 50;
    public const double VisibilityHorizontalPadding = 5;
    public const double VisibilityVerticalPadding = 3;
    public const double VisibilityRightMargin = 8;
    public const double MoveButtonWidth = 22;
    public const double MoveButtonRightMargin = 2;

    public static readonly SrgbColor PaneBackgroundColor = SrgbColor.White;
    public static readonly SrgbColor PaneBorderColor = new(0xC0, 0xC0, 0xC0);
    public static readonly SrgbColor MessageColor = new(0x55, 0x55, 0x55);
}
