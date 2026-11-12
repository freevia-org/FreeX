using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host;

/// <summary>
/// Insert &gt; Shapes gallery: the shared <see cref="PresetShapeCatalog"/> shown the way Word groups it
/// (Lines, Rectangles, Basic Shapes, Block Arrows, Equation Shapes, Flowchart, Stars and Banners,
/// Callouts), each entry a thumbnail drawn from the same outline engine that renders the shape.
/// Returns the chosen catalog item id, or null when dismissed.
/// </summary>
internal sealed class ShapeGalleryDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private const double PxPerPoint = 96.0 / 72.0;
    private string? _result;

    private ShapeGalleryDialog(Window? owner)
    {
        Owner = owner;
        Title = PresetShapeCatalog.DialogTitle(UiText.Get);
        Width = 420;
        Height = 560;
        MinWidth = 320;
        MinHeight = 320;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, "ShapeGalleryDialog");

        var panel = new StackPanel { Margin = new Thickness(12) };
        Button? first = null;
        foreach (var group in PresetShapeCatalog.Groups)
        {
            var title = group.Title.Resolve(UiText.Get);
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 10, 0, 4),
            });

            var items = new WrapPanel();
            AutomationProperties.SetAutomationId(items, $"ShapeGalleryGroup_{group.Id}");
            AutomationProperties.SetName(items, title);
            foreach (var item in group.Items)
            {
                var button = BuildItemButton(item);
                first ??= button;
                items.Children.Add(button);
            }

            panel.Children.Add(items);
        }

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel,
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            Close();
        };
        Loaded += (_, _) => first?.Focus();
    }

    private Button BuildItemButton(PresetShapeCatalogItem item)
    {
        var name = item.Name.Resolve(UiText.Get);
        var button = new Button
        {
            Width = 40,
            Height = 40,
            Margin = new Thickness(1),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            ToolTip = name,
            Content = BuildThumbnail(item),
        };
        AutomationProperties.SetAutomationId(button, $"ShapeGalleryItem_{item.Id}");
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) =>
        {
            _result = item.Id;
            Close();
        };
        return button;
    }

    /// <summary>Draws a catalog entry's outline at thumbnail size. Shared with the gallery tests.</summary>
    internal static FrameworkElement BuildThumbnail(PresetShapeCatalogItem item)
    {
        var (widthPt, heightPt) = item.IsLine
            ? (PresetShapeCatalog.ThumbnailSizePt, PresetShapeCatalog.ThumbnailSizePt * 0.7)
            : PresetShapeCatalog.ThumbnailBox(item.Preset);
        var (width, height) = (widthPt * PxPerPoint, heightPt * PxPerPoint);
        var path = new System.Windows.Shapes.Path
        {
            Width = width,
            Height = height,
            Stretch = Stretch.None,
            StrokeThickness = 1.25,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = item.IsLine
                ? Brushes.Transparent
                : new SolidColorBrush(Color.FromArgb(0x38, 0x44, 0x72, 0xC4)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        };
        if (PresetShapeCatalog.BuildThumbnail(item) is { } outline)
            path.Data = CustomShapePathWpfAdapter.Build(outline, 0, 0, width, height);
        path.SetBinding(
            System.Windows.Shapes.Shape.StrokeProperty,
            new Binding(nameof(Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        return path;
    }

    /// <summary>Shows the gallery and returns the chosen catalog item id, or null if dismissed.</summary>
    public static string? Prompt(Window? owner)
    {
        var dialog = new ShapeGalleryDialog(owner);
        dialog.ShowDialog();
        return dialog._result;
    }
}
