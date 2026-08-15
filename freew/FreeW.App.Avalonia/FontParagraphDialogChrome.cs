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
/// FreeW-local compensation for the Fluent field templates used by the Font and Paragraph dialogs.
/// The shared compact chrome owns the checkbox template; these selectors only restore the WPF
/// authority's one-line field geometry and route-specific checkbox inset on this dialog family.
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

        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(checkBox, style, contentSpacing: 5);
        checkBox.Classes.Add(CheckBoxClass);
        checkBox.Margin = new Thickness(
            checkBox.Margin.Left + 1,
            checkBox.Margin.Top,
            checkBox.Margin.Right,
            checkBox.Margin.Bottom);
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
