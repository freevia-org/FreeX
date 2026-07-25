using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

/// <summary>Small host adapter for the shared Selection Pane projection.</summary>
internal sealed class SelectionPane : Border
{
    private EditingSession _editor;
    private readonly StackPanel _items = new() { Orientation = Orientation.Vertical };
    private readonly TextBlock _message = new();

    public SelectionPane(EditingSession editor)
    {
        _editor = editor;
        Width = 320;
        IsVisible = false;
        Background = Brushes.White;
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
        BorderThickness = new Thickness(1, 0, 0, 0);

        var heading = new TextBlock
        {
            Text = "Selection Pane",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _message.Margin = new Thickness(12, 0, 12, 8);
        _message.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

        var header = new StackPanel { Children = { heading, _message } };
        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _items,
        });
        Child = panel;
    }

    public void SetEditor(EditingSession editor)
    {
        _editor = editor;
        Refresh();
    }

    public PresentationSelectionPanePlan Refresh()
    {
        var plan = PresentationSelectionPanePlanner.Build(
            _editor.CurrentSlide,
            _editor.CurrentSlideIndex,
            _editor.SelectedShapeIds);
        _message.Text = plan.HasSlide
            ? $"Slide {plan.SlideIndex + 1} ({plan.Items.Count} objects)"
            : PresentationSelectionPanePlanner.EmptyMessage;
        _items.Children.Clear();
        foreach (var item in plan.Items)
            _items.Children.Add(BuildItem(item));
        return plan;
    }

    private Control BuildItem(PresentationSelectionPaneItemPlan item)
    {
        var select = new Button
        {
            Content = $"{item.SelectionIndex + 1}. {item.ShapeName}",
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 5),
            Margin = new Thickness(8, 1, 4, 1),
        };
        ToolTip.SetTip(select, $"Select {item.ShapeTypeLabel}");
        select.Click += (_, _) => _editor.Select(item.ShapeId);

        var visibility = new Button
        {
            Content = item.IsHidden ? "Show" : "Hide",
            MinWidth = 50,
            Padding = new Thickness(5, 3),
            Margin = new Thickness(0, 1, 8, 1),
        };
        ToolTip.SetTip(visibility, item.IsHidden ? "Show object" : "Hide object");
        visibility.Click += (_, _) => _editor.ToggleShapeHidden(item.ShapeId);

        var row = new DockPanel();
        DockPanel.SetDock(visibility, Dock.Right);
        row.Children.Add(visibility);
        row.Children.Add(select);
        return row;
    }
}
