using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Avalonia;

/// <summary>
/// Avalonia projection of the shared Insert &gt; Shapes gallery (<see cref="PresetShapeCatalog"/>):
/// Word's groups, each entry a thumbnail drawn from the shared preset outline engine.
/// </summary>
internal sealed class ShapeGalleryDialog : FreeWDialogWindow
{
    private const double PxPerPoint = 96.0 / 72.0;

    private ShapeGalleryDialog()
    {
        Title = PresetShapeCatalog.DialogTitle(UiText.Get);
        Width = 420;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        Button? first = null;
        foreach (var group in PresetShapeCatalog.Groups)
        {
            panel.Children.Add(new TextBlock
            {
                Text = group.Title.Resolve(UiText.Get),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 6, 0, 0),
            });

            var items = new WrapPanel();
            foreach (var item in group.Items)
            {
                var captured = item;
                var name = item.Name.Resolve(UiText.Get);
                var button = new Button
                {
                    Width = 40,
                    Height = 40,
                    Margin = new Thickness(1),
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Content = BuildThumbnail(item),
                };
                ToolTip.SetTip(button, name);
                AutomationProperties.SetAutomationId(button, $"ShapeGalleryItem_{item.Id}");
                AutomationProperties.SetName(button, name);
                button.Click += (_, _) => Close(captured.Id);
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
        Opened += (_, _) => first?.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            Close(null);
            e.Handled = true;
        };
    }

    private static Control BuildThumbnail(PresetShapeCatalogItem item)
    {
        var (widthPt, heightPt) = item.IsLine
            ? (PresetShapeCatalog.ThumbnailSizePt, PresetShapeCatalog.ThumbnailSizePt * 0.7)
            : PresetShapeCatalog.ThumbnailBox(item.Preset);
        var (width, height) = (widthPt * PxPerPoint, heightPt * PxPerPoint);
        var path = new global::Avalonia.Controls.Shapes.Path
        {
            Width = width,
            Height = height,
            Stretch = Stretch.None,
            StrokeThickness = 1.25,
            Stroke = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4)),
            Fill = item.IsLine ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(0x38, 0x44, 0x72, 0xC4)),
        };
        if (PresetShapeCatalog.BuildThumbnail(item) is { } outline)
            path.Data = CustomShapePathAvaloniaAdapter.Build(outline, new Rect(0, 0, width, height), isFilled: true);
        return path;
    }

    public static Task<string?> ShowAsync(Window owner) =>
        new ShapeGalleryDialog().ShowDialog<string?>(owner);
}
