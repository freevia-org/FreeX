using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace Free.Shared.Shell.Avalonia;

public sealed record AvaloniaCompactDialogChromeStyle(FontFamily FontFamily)
{
    public double ControlHeight { get; init; } = 24;
    public double FontSize { get; init; } = 12;
    public Thickness ButtonPadding { get; init; } = new(4, 1);
    public Thickness TextBoxPadding { get; init; } = new(4, 1);
    public Thickness ComboBoxPadding { get; init; } = new(5, 0, 4, 0);
    public Thickness ListBoxItemPadding { get; init; } = new(4, 1);
    public double ListBoxItemMinHeight { get; init; } = 24;
}

/// <summary>
/// Shared compact dialog chrome for Avalonia dialog controls that mirror Excel/WPF 24px metrics.
/// </summary>
public static class AvaloniaCompactDialogChrome
{
    private static readonly IBrush DefaultButtonBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0, 120, 215));
    private static readonly IBrush ButtonBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(112, 112, 112));
    private static readonly IBrush InputBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(130, 130, 130));
    private static readonly IBrush ValidationStatusBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x80, 0x00, 0x00));
    private static readonly IBrush DialogTabPaneBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(192, 192, 192));
    private static readonly IBrush DialogInactiveTabBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(160, 160, 160));
    private static readonly IBrush DialogInactiveTabBackgroundBrush = new ImmutableSolidColorBrush(Color.FromRgb(243, 243, 243));

    public static void ApplyButton(
        Button button,
        AvaloniaCompactDialogChromeStyle style,
        double minWidth,
        bool isDefault = false)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(style);

        button.MinWidth = minWidth;
        button.Height = style.ControlHeight;
        button.MinHeight = style.ControlHeight;
        button.MaxHeight = style.ControlHeight;
        button.Padding = style.ButtonPadding;
        button.Background = Brushes.White;
        button.BorderBrush = isDefault ? DefaultButtonBorderBrush : ButtonBorderBrush;
        button.BorderThickness = new Thickness(1);
        button.FontSize = style.FontSize;
        button.FontFamily = style.FontFamily;
        if (isDefault)
            button.IsDefault = true;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
    }

    public static void ApplyTextBox(TextBox textBox, AvaloniaCompactDialogChromeStyle style, bool fixedHeight = true)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(style);

        if (fixedHeight)
        {
            textBox.Height = style.ControlHeight;
            textBox.MinHeight = style.ControlHeight;
            textBox.MaxHeight = style.ControlHeight;
        }
        textBox.Padding = style.TextBoxPadding;
        textBox.FontSize = style.FontSize;
        textBox.FontFamily = style.FontFamily;
        textBox.BorderBrush = InputBorderBrush;
        textBox.BorderThickness = new Thickness(1);
        textBox.VerticalContentAlignment = VerticalAlignment.Center;
    }

    public static void ApplyComboBox(ComboBox comboBox, AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        ArgumentNullException.ThrowIfNull(style);

        comboBox.Height = style.ControlHeight;
        comboBox.MinHeight = style.ControlHeight;
        comboBox.MaxHeight = style.ControlHeight;
        comboBox.Padding = style.ComboBoxPadding;
        comboBox.FontSize = style.FontSize;
        comboBox.FontFamily = style.FontFamily;
        comboBox.BorderBrush = InputBorderBrush;
        comboBox.BorderThickness = new Thickness(1);
        comboBox.VerticalContentAlignment = VerticalAlignment.Center;
    }

    public static void ApplyCheckBox(CheckBox checkBox, AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(checkBox);
        ArgumentNullException.ThrowIfNull(style);

        checkBox.FontSize = style.FontSize;
        checkBox.FontFamily = style.FontFamily;
    }

    public static void ApplyRadioButton(RadioButton radioButton, AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(radioButton);
        ArgumentNullException.ThrowIfNull(style);

        radioButton.FontSize = style.FontSize;
        radioButton.FontFamily = style.FontFamily;
    }

    public static void ApplyValidationStatus(
        TextBlock status,
        AvaloniaCompactDialogChromeStyle style,
        Thickness margin = default)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(style);

        status.Foreground = ValidationStatusBrush;
        status.FontSize = 11;
        status.FontFamily = style.FontFamily;
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = margin;
        status.IsVisible = false;
    }

    public static void ApplyListBox(ListBox listBox, AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        ArgumentNullException.ThrowIfNull(style);

        listBox.FontSize = style.FontSize;
        listBox.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, style.ListBoxItemPadding),
                new Setter(Layoutable.MinHeightProperty, style.ListBoxItemMinHeight),
                new Setter(TemplatedControl.FontSizeProperty, style.FontSize),
            },
        });
    }

    /// <summary>
    /// Applies the classic Windows dialog tab treatment: bordered inactive tabs, a white selected tab,
    /// and a selected-tab body that overlaps the content pane so no gap or separator line remains.
    /// </summary>
    public static void ApplyClassicTabChrome(TabControl tabControl)
    {
        ArgumentNullException.ThrowIfNull(tabControl);

        var headerPresenterStyle = new Style(s => s
            .OfType<TabControl>()
            .Template()
            .OfType<ItemsPresenter>()
            .Name("PART_ItemsPresenter"));
        headerPresenterStyle.Setters.Add(new Setter(Layoutable.MarginProperty, new Thickness(0)));
        tabControl.Styles.Add(headerPresenterStyle);

        var contentPaneStyle = new Style(s => s
            .OfType<TabControl>()
            .Template()
            .OfType<ContentPresenter>()
            .Name("PART_SelectedContentHost"));
        contentPaneStyle.Setters.Add(new Setter(Border.BorderBrushProperty, DialogTabPaneBorderBrush));
        contentPaneStyle.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));
        contentPaneStyle.Setters.Add(new Setter(ContentPresenter.PaddingProperty, new Thickness(12)));
        contentPaneStyle.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, Brushes.White));
        tabControl.Styles.Add(contentPaneStyle);

        var tabStyle = new Style(s => s.OfType<TabItem>());
        tabStyle.Setters.Add(new Setter(TabItem.BorderBrushProperty, DialogInactiveTabBorderBrush));
        tabStyle.Setters.Add(new Setter(TabItem.BorderThicknessProperty, new Thickness(1, 1, 1, 0)));
        tabStyle.Setters.Add(new Setter(TabItem.BackgroundProperty, DialogInactiveTabBackgroundBrush));
        tabStyle.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Brushes.Black));
        tabStyle.Setters.Add(new Setter(TabItem.PaddingProperty, new Thickness(10, 4)));
        tabStyle.Setters.Add(new Setter(TabItem.MarginProperty, new Thickness(0, 0, -1, 0)));
        tabControl.Styles.Add(tabStyle);

        var selectedTabStyle = new Style(s => s.OfType<TabItem>().Class(":selected"));
        selectedTabStyle.Setters.Add(new Setter(TabItem.BackgroundProperty, Brushes.White));
        selectedTabStyle.Setters.Add(new Setter(TabItem.BorderBrushProperty, DialogTabPaneBorderBrush));
        selectedTabStyle.Setters.Add(new Setter(TabItem.BorderThicknessProperty, new Thickness(1, 1, 1, 0)));
        selectedTabStyle.Setters.Add(new Setter(TabItem.MarginProperty, new Thickness(0, 0, -1, -1)));
        selectedTabStyle.Setters.Add(new Setter(TabItem.ZIndexProperty, 1));
        tabControl.Styles.Add(selectedTabStyle);

        var classicTabTemplate = new FuncControlTemplate<TabItem>((tab, _) =>
        {
            var presenter = new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(HeaderedContentControl.Header)) { Source = tab });
            presenter.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(HeaderedContentControl.HeaderTemplate)) { Source = tab });
            presenter.Bind(ContentPresenter.PaddingProperty, new Binding(nameof(TemplatedControl.Padding)) { Source = tab });

            var root = new Border { Name = "PART_LayoutRoot" };
            root.Bind(Border.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = tab });
            root.Bind(Border.BorderBrushProperty, new Binding(nameof(TemplatedControl.BorderBrush)) { Source = tab });
            root.Bind(Border.BorderThicknessProperty, new Binding(nameof(TemplatedControl.BorderThickness)) { Source = tab });
            root.Child = presenter;
            return root;
        });
        tabStyle.Setters.Add(new Setter(TemplatedControl.TemplateProperty, classicTabTemplate));
    }

    public static StackPanel CreateActionRow(IReadOnlyList<Control> controls, Thickness margin = default)
    {
        ArgumentNullException.ThrowIfNull(controls);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = margin,
        };
        foreach (var control in controls)
        {
            row.Children.Add(control);
        }

        return row;
    }
}
