using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia;

/// <summary>
/// FreeW-local compensation for the Fluent templates used by the Font and Paragraph dialogs.
/// The shared compact chrome owns the common contract; these selectors only restore the WPF
/// authority's one-line field and checkbox geometry on this dialog family.
/// </summary>
internal static class FontParagraphDialogChrome
{
    private const string TextBoxClass = "freew-font-paragraph-textbox";
    private const string CheckBoxClass = "freew-font-paragraph-checkbox";
    private static readonly IBrush WpfDisabledInputBorderBrush =
        new ImmutableSolidColorBrush(Color.FromRgb(0xD0, 0xD1, 0xD4));

    public static void ApplyTextBox(TextBox textBox, AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(style);

        AvaloniaCompactDialogChrome.ApplyTextBox(textBox, style);
        textBox.Foreground = style.ForegroundBrush ?? Brushes.Black;
        if (textBox.Classes.Contains(TextBoxClass))
            return;

        textBox.Classes.Add(TextBoxClass);
        var height = style.TextBoxHeight ?? style.ControlHeight;
        var borderBrush = style.InputBorderBrush ?? new ImmutableSolidColorBrush(Color.FromRgb(0xAB, 0xAD, 0xB3));
        var disabledBorderBrush = WpfDisabledInputBorderBrush;
        var background = style.TextBoxBackgroundBrush ?? Brushes.White;
        textBox.BorderBrush = textBox.IsEnabled ? borderBrush : disabledBorderBrush;
        textBox.Styles.Add(new Style(selector => selector.OfType<Border>().Name("PART_BorderElement"))
        {
            Setters =
            {
                new Setter(Layoutable.MinHeightProperty, 0d),
                new Setter(Layoutable.HeightProperty, height),
                new Setter(Border.BorderBrushProperty, borderBrush),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
                new Setter(Border.BackgroundProperty, background),
            },
        });
        textBox.Styles.Add(new Style(selector => selector
            .OfType<TextBox>()
            .Class(":focus")
            .Template()
            .OfType<Border>()
            .Name("PART_BorderElement"))
        {
            Setters =
            {
                new Setter(Border.BorderBrushProperty, style.FocusedInputBorderBrush ?? borderBrush),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
            },
        });
        textBox.Styles.Add(new Style(selector => selector
            .OfType<TextBox>()
            .Class(":disabled")
            .Template()
            .OfType<Border>()
            .Name("PART_BorderElement"))
        {
            Setters =
            {
                new Setter(Border.BorderBrushProperty, disabledBorderBrush),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
            },
        });

        void RefreshRenderedChrome()
        {
            textBox.ApplyTemplate();
            var brush = !textBox.IsEnabled
                ? disabledBorderBrush
                : textBox.IsFocused
                ? style.FocusedInputBorderBrush ?? borderBrush
                : borderBrush;
            textBox.BorderBrush = brush;
            foreach (var border in textBox.GetVisualDescendants().OfType<Border>().Where(border => border.Name == "PART_BorderElement"))
            {
                border.MinHeight = 0;
                border.Height = height;
                border.BorderBrush = brush;
                border.BorderThickness = new Thickness(1);
                border.Background = background;
            }
        }

        void QueueRenderedChrome() =>
            Dispatcher.UIThread.Post(RefreshRenderedChrome, DispatcherPriority.Render);

        textBox.AttachedToVisualTree += (_, _) => QueueRenderedChrome();
        textBox.GotFocus += (_, _) => QueueRenderedChrome();
        textBox.LostFocus += (_, _) => QueueRenderedChrome();
        textBox.PropertyChanged += (_, args) =>
        {
            if (args.Property == InputElement.IsEnabledProperty)
                QueueRenderedChrome();
        };
        QueueRenderedChrome();
    }

    public static void ApplyCheckBox(CheckBox checkBox, AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(checkBox);
        ArgumentNullException.ThrowIfNull(style);

        if (checkBox.Classes.Contains(CheckBoxClass))
            return;

        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(checkBox, style);
        checkBox.Classes.Add(CheckBoxClass);
        checkBox.Margin = new Thickness(
            checkBox.Margin.Left + 1,
            checkBox.Margin.Top,
            checkBox.Margin.Right,
            checkBox.Margin.Bottom);
        checkBox.Template = new FuncControlTemplate<CheckBox>((control, _) =>
        {
            var checkMark = new global::Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M 2 6 L 5 9 L 12 2"),
                Stroke = style.ForegroundBrush ?? Brushes.Black,
                StrokeThickness = 1.4,
                Width = 12,
                Height = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            checkMark.Bind(
                Visual.IsVisibleProperty,
                new Binding(nameof(ToggleButton.IsChecked))
                {
                    Source = control,
                });

            var indicator = new Border
            {
                Width = 14,
                // WPF's default checkbox paints a 14px wide, 13px high device-pixel frame.
                // Keep the control's 18px hit row while matching the authority's painted glyph.
                Height = 13,
                Background = Brushes.White,
                BorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(112, 112, 112)),
                BorderThickness = new Thickness(1),
                Child = checkMark,
            };
            var content = new ContentPresenter
            {
                FontFamily = style.FontFamily,
                FontSize = style.FontSize,
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = style.ForegroundBrush ?? Brushes.Black,
            };
            content.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = control });
            content.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ContentControl.ContentTemplate)) { Source = control });

            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 5,
                Children = { indicator, content },
            };
        });
    }

    public static void ApplyComboBox(
        ComboBox comboBox,
        AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        ArgumentNullException.ThrowIfNull(style);

        AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, style);
    }
}
