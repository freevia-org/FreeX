namespace FreeP.App.Compositor;

/// <summary>
/// Shared WPF-authority layout contract for the WPF and Avalonia Custom Shows dialogs.
/// Window dimensions account for each toolkit's native non-client chrome so both expose the
/// same 626 by 402 2/3 logical comparison surface; content geometry is otherwise identical.
/// </summary>
public static class SlideShowCustomShowDialogVisualMetrics
{
    public const double WpfWindowWidth = 640;
    public const double WpfWindowHeight = 440;
    // Avalonia rounds the outer window value directly to a device pixel while WPF's
    // client-area calculation produces a 626px raster. Keep the rendered comparison
    // surface integral and identical rather than encoding the toolkit frame delta here.
    public const double AvaloniaWindowWidth = 626;
    public const double AvaloniaWindowHeight = 402.6666666666667;
    public const double MinimumWindowWidth = 560;
    public const double MinimumWindowHeight = 360;

    public const double RootInset = 14;
    public const double ShowListColumnWidth = 210;
    public const double ShowListRightGap = 10;
    public const double NameMinimumWidth = 260;
    public const double NameBottomMargin = 8;
    public const double OrderedSlidesMinimumHeight = 92;
    public const double OrderedSlidesRowHeight = 118;
    public const double LabelBottomMargin = 4;
    public const double OrderHeaderTopMargin = 2;
    public const double AvailableSlidesTopMargin = 8;
    public const double ValidationTopMargin = 4;
    public const double ValidationBottomMargin = 8;
    public const double ActionRowTopMargin = 12;
    public const double ActionSpacing = 6;
    public const double AvailableSlideVerticalMargin = 2;
    public const double AvailableSlideControlHeight = 20;
    public const double AddSlideButtonMinimumWidth = 58;
    public const double ActionButtonMinimumWidth = 82;
    public const double ActionButtonHorizontalPadding = 8;
    public const double ActionButtonVerticalPadding = 3;
}
