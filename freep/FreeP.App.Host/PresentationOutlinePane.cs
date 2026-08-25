using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

/// <summary>Native WPF realization of the shared, read-only outline projection.</summary>
internal sealed class PresentationOutlinePane : Border
{
    private readonly PresentationWorkareaSession _workarea;
    private readonly ListBox _list;
    private bool _realizing;

    public PresentationOutlinePane(PresentationWorkareaSession workarea)
    {
        _workarea = workarea ?? throw new ArgumentNullException(nameof(workarea));
        _list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 6, 6, 6),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        _list.SelectionChanged += OnSelectionChanged;
        Child = _list;
        RefreshProjection();
    }

    public void RefreshProjection()
    {
        var plan = PresentationOutlineViewPlanner.Build(_workarea.Presentation);
        _realizing = true;
        try
        {
            _list.Items.Clear();
            foreach (var slide in plan)
                _list.Items.Add(BuildSlide(slide));
            SyncNativeSelection(scrollActiveIntoView: false);
        }
        finally
        {
            _realizing = false;
        }
    }

    public void SyncNativeSelection(bool scrollActiveIntoView = true)
    {
        var activeIndex = _workarea.SlidePaneSession.Selection.ActiveSlideIndex;
        _realizing = true;
        try
        {
            foreach (var item in _list.Items.OfType<ListBoxItem>())
                item.IsSelected = item.Tag is int slideIndex && slideIndex == activeIndex;
        }
        finally
        {
            _realizing = false;
        }

        if (scrollActiveIntoView && _list.SelectedItem is { } active)
            _list.ScrollIntoView(active);
    }

    private static ListBoxItem BuildSlide(PresentationOutlineSlidePlan slide)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = slide.SlideLabel,
            FontSize = 10,
            Foreground = Brushes.DimGray,
        });
        panel.Children.Add(new TextBlock
        {
            Text = slide.Title,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var paragraph in slide.Body)
        {
            panel.Children.Add(new TextBlock
            {
                Text = paragraph.Text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10 + paragraph.Level * 12, 2, 0, 0),
            });
        }

        var item = new ListBoxItem { Tag = slide.SlideIndex, Content = panel };
        AutomationProperties.SetName(item, $"{slide.SlideLabel}: {slide.Title}");
        return item;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_realizing || _list.SelectedItem is not ListBoxItem { Tag: int slideIndex })
            return;

        _workarea.ApplySlidePaneNativeSelection([slideIndex], slideIndex);
    }
}
