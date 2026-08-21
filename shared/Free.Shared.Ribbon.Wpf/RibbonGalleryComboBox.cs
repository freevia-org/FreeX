using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Keeps the compact editable <see cref="ComboBox"/> surface while owning a deterministic, rich
/// popup for gallery-style choices. The native ComboBox popup is closed immediately, so the gallery
/// never depends on a fragile ItemTemplate applied to the standard dropdown template.
/// </summary>
public sealed class RibbonGalleryComboBox : ComboBox
{
    private readonly Popup _galleryPopup;
    private readonly ListBox _galleryList;
    private readonly Button _moreButton;
    private bool _redirectingNativePopup;
    private bool _synchronizingGallerySelection;

    public RibbonGalleryComboBox()
    {
        _galleryList = new ListBox
        {
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            ItemTemplate = CreateGalleryItemTemplate(),
            ItemContainerStyle = CreateGalleryItemContainerStyle(),
        };
        _galleryList.SelectionChanged += GalleryList_SelectionChanged;

        _moreButton = new Button
        {
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0, 78, 160)),
            FontSize = 13,
            Height = 40,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(46, 0, 12, 0),
            Visibility = Visibility.Collapsed,
        };
        _moreButton.Click += MoreButton_Click;

        var popupContent = new Grid { Width = 264 };
        popupContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(600) });
        popupContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var choicesScroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _galleryList,
        };
        popupContent.Children.Add(choicesScroller);
        var footer = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(210, 210, 210)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = _moreButton,
        };
        Grid.SetRow(footer, 1);
        popupContent.Children.Add(footer);

        _galleryPopup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            Child = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(92, 92, 92)),
                BorderThickness = new Thickness(1),
                Child = popupContent,
            },
        };

        DropDownOpened += RedirectNativePopupToGallery;
        Unloaded += (_, _) => CloseGallery();
    }

    public bool IsGalleryOpen => _galleryPopup.IsOpen;

    public FrameworkElement GalleryPopupChild => (FrameworkElement)_galleryPopup.Child;

    public void SetGalleryChoices(IEnumerable<RibbonComboBoxChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);

        _synchronizingGallerySelection = true;
        try
        {
            _galleryList.Items.Clear();
            foreach (var choice in choices)
            {
                if (choice.PreviewKind == RibbonComboBoxGalleryPreviewKind.More)
                {
                    _moreButton.Content = choice.Label;
                    _moreButton.Visibility = Visibility.Visible;
                }
                else
                {
                    _galleryList.Items.Add(choice);
                }
            }
        }
        finally
        {
            _synchronizingGallerySelection = false;
        }
    }

    public void OpenGallery()
    {
        if (!IsEnabled || Items.Count == 0)
            return;

        _synchronizingGallerySelection = true;
        try
        {
            _galleryList.SelectedIndex = SelectedIndex;
        }
        finally
        {
            _synchronizingGallerySelection = false;
        }

        _galleryPopup.PlacementTarget = this;
        _galleryPopup.MinWidth = Math.Max(264, ActualWidth);
        _galleryPopup.IsOpen = true;
    }

    public void CloseGallery() => _galleryPopup.IsOpen = false;

    private void RedirectNativePopupToGallery(object? sender, EventArgs e)
    {
        if (_redirectingNativePopup)
            return;

        _redirectingNativePopup = true;
        try
        {
            IsDropDownOpen = false;
            OpenGallery();
        }
        finally
        {
            _redirectingNativePopup = false;
        }
    }

    private void GalleryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingGallerySelection || _galleryList.SelectedIndex < 0)
            return;

        var selectedIndex = _galleryList.SelectedIndex;
        CloseGallery();
        SetCurrentValue(SelectedIndexProperty, selectedIndex);
        Focus();
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        CloseGallery();
        SetCurrentValue(SelectedIndexProperty, _galleryList.Items.Count);
        Focus();
    }

    private static DataTemplate CreateGalleryItemTemplate()
    {
        var row = new FrameworkElementFactory(typeof(RibbonComboBoxGalleryRow));
        row.SetBinding(RibbonComboBoxGalleryRow.ChoiceProperty, new Binding());
        return new DataTemplate { VisualTree = row };
    }

    private static Style CreateGalleryItemContainerStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        return style;
    }
}

internal sealed class RibbonComboBoxGalleryRow : Grid
{
    public static readonly DependencyProperty ChoiceProperty = DependencyProperty.Register(
        nameof(Choice),
        typeof(RibbonComboBoxChoice),
        typeof(RibbonComboBoxGalleryRow),
        new PropertyMetadata(null, OnChoiceChanged));

    private readonly RibbonComboBoxGalleryPreview _preview = new();
    private readonly TextBlock _label = new() { FontWeight = FontWeights.SemiBold, FontSize = 14 };
    private readonly TextBlock _description = new() { FontSize = 13, Foreground = Brushes.Black };
    private readonly Border _separator = new() { Height = 1, Background = new SolidColorBrush(Color.FromRgb(210, 210, 210)), Visibility = Visibility.Collapsed };

    public RibbonComboBoxGalleryRow()
    {
        Height = 60;
        MinWidth = 252;
        Margin = new Thickness(3, 0, 3, 0);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _preview.HorizontalAlignment = HorizontalAlignment.Center;
        _preview.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_preview);
        SetColumn(_preview, 0);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_label);
        text.Children.Add(_description);
        Children.Add(text);
        SetColumn(text, 1);

        _separator.HorizontalAlignment = HorizontalAlignment.Stretch;
        _separator.VerticalAlignment = VerticalAlignment.Top;
        _separator.Margin = new Thickness(0, 0, 0, 0);
        Children.Add(_separator);
        SetColumnSpan(_separator, 2);
    }

    public RibbonComboBoxChoice? Choice
    {
        get => (RibbonComboBoxChoice?)GetValue(ChoiceProperty);
        set => SetValue(ChoiceProperty, value);
    }

    private static void OnChoiceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) =>
        ((RibbonComboBoxGalleryRow)dependencyObject).ApplyChoice((RibbonComboBoxChoice?)e.NewValue);

    private void ApplyChoice(RibbonComboBoxChoice? choice)
    {
        _label.Text = choice?.Label ?? string.Empty;
        _description.Text = choice?.Description ?? string.Empty;
        _description.Visibility = string.IsNullOrWhiteSpace(choice?.Description) ? Visibility.Collapsed : Visibility.Visible;
        _preview.PreviewKind = choice?.PreviewKind ?? RibbonComboBoxGalleryPreviewKind.None;
        _preview.InvalidateVisual();
        _separator.Visibility = choice?.PreviewKind == RibbonComboBoxGalleryPreviewKind.More ? Visibility.Visible : Visibility.Collapsed;
        Height = choice?.PreviewKind == RibbonComboBoxGalleryPreviewKind.More ? 38 : 60;
        _preview.Visibility = choice?.PreviewKind == RibbonComboBoxGalleryPreviewKind.More ? Visibility.Collapsed : Visibility.Visible;
    }
}

internal sealed class RibbonComboBoxGalleryPreview : FrameworkElement
{
    public RibbonComboBoxGalleryPreviewKind PreviewKind { get; set; }

    public RibbonComboBoxGalleryPreview()
    {
        Width = 48;
        Height = 44;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var brush = Brushes.Black;
        var pen = new Pen(brush, 1.25);
        pen.Freeze();

        switch (PreviewKind)
        {
            case RibbonComboBoxGalleryPreviewKind.General:
                DrawText(context, "123", 20, new Point(7, 16));
                context.DrawEllipse(null, pen, new Point(13, 12), 9, 9);
                context.DrawLine(pen, new Point(13, 12), new Point(13, 6));
                break;
            case RibbonComboBoxGalleryPreviewKind.Number:
                DrawText(context, "12", 27, new Point(5, 7));
                break;
            case RibbonComboBoxGalleryPreviewKind.Currency:
                DrawText(context, "$", 28, new Point(8, 6));
                DrawCoin(context, pen, new Point(35, 29));
                break;
            case RibbonComboBoxGalleryPreviewKind.Accounting:
                context.DrawRectangle(null, pen, new Rect(7, 7, 20, 29));
                context.DrawLine(pen, new Point(11, 14), new Point(23, 14));
                context.DrawLine(pen, new Point(11, 20), new Point(15, 20));
                context.DrawLine(pen, new Point(18, 20), new Point(23, 20));
                DrawCoin(context, pen, new Point(35, 29));
                break;
            case RibbonComboBoxGalleryPreviewKind.ShortDate:
            case RibbonComboBoxGalleryPreviewKind.LongDate:
                context.DrawRectangle(null, pen, new Rect(7, 8, 31, 29));
                context.DrawLine(pen, new Point(7, 16), new Point(38, 16));
                context.DrawLine(pen, new Point(14, 5), new Point(14, 12));
                context.DrawLine(pen, new Point(31, 5), new Point(31, 12));
                context.DrawEllipse(brush, null, new Point(22, 25), 1.5, 1.5);
                break;
            case RibbonComboBoxGalleryPreviewKind.Time:
                context.DrawEllipse(null, pen, new Point(22, 22), 16, 16);
                context.DrawLine(pen, new Point(22, 22), new Point(22, 11));
                context.DrawLine(pen, new Point(22, 22), new Point(30, 22));
                break;
            case RibbonComboBoxGalleryPreviewKind.Percentage:
                DrawText(context, "%", 30, new Point(7, 5));
                break;
            case RibbonComboBoxGalleryPreviewKind.Fraction:
                DrawText(context, "½", 30, new Point(10, 5));
                break;
            case RibbonComboBoxGalleryPreviewKind.Scientific:
                DrawText(context, "10²", 25, new Point(3, 8));
                break;
            case RibbonComboBoxGalleryPreviewKind.Text:
                DrawText(context, "abc", 20, new Point(3, 13));
                break;
        }
    }

    private static void DrawCoin(DrawingContext context, Pen pen, Point center)
    {
        context.DrawEllipse(null, pen, center, 8, 4);
        context.DrawLine(pen, new Point(center.X - 8, center.Y), new Point(center.X - 8, center.Y + 7));
        context.DrawLine(pen, new Point(center.X + 8, center.Y), new Point(center.X + 8, center.Y + 7));
        context.DrawEllipse(null, pen, new Point(center.X, center.Y + 7), 8, 4);
    }

    private void DrawText(DrawingContext context, string text, double size, Point origin)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(formatted, origin);
    }
}
