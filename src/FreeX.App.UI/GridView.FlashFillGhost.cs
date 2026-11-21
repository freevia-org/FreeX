using System.Globalization;
using System.Windows;
using System.Windows.Media;

using FreeX.App.Presentation.Editing;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private static readonly Brush FlashFillGhostBrush = CreateFlashFillGhostBrush();

    /// <summary>
    /// Excel's "Automatically Flash Fill" preview: the values Flash Fill would write, drawn as grey
    /// ghost text over the (still empty) cells until the suggestion is accepted or dismissed.
    /// </summary>
    public static readonly DependencyProperty FlashFillGhostCellsProperty =
        DependencyProperty.Register(nameof(FlashFillGhostCells), typeof(IReadOnlyList<FlashFillGhostCell>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<FlashFillGhostCell>? FlashFillGhostCells
    {
        get => (IReadOnlyList<FlashFillGhostCell>?)GetValue(FlashFillGhostCellsProperty);
        set => SetValue(FlashFillGhostCellsProperty, value);
    }

    private static Brush CreateFlashFillGhostBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
        brush.Freeze();
        return brush;
    }

    private void RenderFlashFillGhosts(DrawingContext dc)
    {
        if (FlashFillGhostCells is not { Count: > 0 } ghosts || Viewport is null)
            return;

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var fontSize = ToDisplayFontSize(DefaultCellFontSizePoints);
        foreach (var ghost in ghosts)
        {
            var address = new CellAddress(ActiveSheetId, ghost.Row, ghost.Col);
            var rect = CalculateVisibleSelectionRect(
                Viewport,
                new GridRange(address, address),
                ActualRowHeaderWidth,
                EffectiveColHeaderHeight);
            if (rect is not { } cellRect || cellRect.Width <= 4 || cellRect.Height <= 4)
                continue;

            var text = new FormattedText(
                ghost.Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface,
                fontSize,
                FlashFillGhostBrush,
                pixelsPerDip);

            dc.PushClip(new RectangleGeometry(cellRect));
            dc.DrawText(text, new Point(
                cellRect.Left + 2,
                Math.Max(cellRect.Top, cellRect.Bottom - text.Height - 1)));
            dc.Pop();
        }
    }
}
