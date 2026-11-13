using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Shared WPF-authority metrics for the Zoom target dialogs (single target, summary, cover image).</summary>
public static class ZoomDialogVisualMetrics
{
    /// <summary>
    /// WPF's standard dialog button width. Avalonia previously used 80 with no documented compensation, so the
    /// WPF reference value now applies to both hosts.
    /// </summary>
    public const double ButtonMinWidth = 75;
}

/// <summary>Shared WPF-authority metrics for the Motion Path editor dialog rows.</summary>
public static class MotionPathEditorDialogVisualMetrics
{
    /// <summary>Segment-kind combo width. Avalonia previously used 78 with no documented compensation.</summary>
    public const double KindWidth = 76;

    /// <summary>The WPF <c>Brushes.LightGray</c> value used for the bottom rule of each row.</summary>
    public static readonly SrgbColor RowBorderColor = new(0xD3, 0xD3, 0xD3);
}

/// <summary>Shared metrics for the Header and Footer dialog.</summary>
public static class HeaderFooterDialogVisualMetrics
{
    /// <summary>WPF gap under the Footer check box.</summary>
    public const double WpfFooterCheckBottomMargin = 4;

    /// <summary>
    /// Avalonia leaves the Footer check box un-margined: that dialog is a fixed 346 x 254 window whose outer
    /// geometry is tuned to the WPF content-sized raster (see the constructor comment) and its compact check
    /// boxes are a fixed 18 high, so changing a row gap shifts that tuned layout. Kept as a separate named
    /// per-host value until a visual capture shows the gap can be unified.
    /// </summary>
    public const double AvaloniaFooterCheckBottomMargin = 0;
}
