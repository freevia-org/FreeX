using System.Windows;
using System.Windows.Media;

namespace Freexcel.App.Host;

/// <summary>
/// Minimal UIElement wrapper that hosts an arbitrary <see cref="DrawingVisual"/>
/// inside a <see cref="System.Windows.Documents.FixedPage"/>.
/// </summary>
internal sealed class VisualHost : UIElement
{
    public Visual? Visual { get; init; }
    public IReadOnlyList<PdfTextOverlay> TextOverlays { get; init; } = [];

    protected override int VisualChildrenCount => Visual != null ? 1 : 0;

    protected override Visual GetVisualChild(int index)
    {
        if (index != 0 || Visual == null)
            throw new ArgumentOutOfRangeException(nameof(index));
        return Visual;
    }

    protected override void OnRender(DrawingContext drawingContext) { }
}
