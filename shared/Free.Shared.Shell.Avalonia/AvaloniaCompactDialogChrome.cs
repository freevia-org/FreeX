using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

public sealed record AvaloniaCompactDialogChromeStyle(FontFamily FontFamily)
{
    public double ControlHeight { get; init; } = CompactDialogVisualTokens.ControlHeight;
    public double? TextBoxHeight { get; init; }
    public double? ComboBoxHeight { get; init; }
    public double? TabHeight { get; init; }
    public double CompactRadioButtonHeight { get; init; } = 20;
    public double ButtonHeight { get; init; } = CompactDialogVisualTokens.ButtonHeight;
    public double ButtonMinWidth { get; init; } = CompactDialogVisualTokens.ButtonMinWidth;
    public double FontSize { get; init; } = CompactDialogVisualTokens.FontSize;
    public Thickness ButtonPadding { get; init; } = new(
        CompactDialogVisualTokens.ButtonPaddingHorizontal,
        CompactDialogVisualTokens.ButtonPaddingVertical);
    public Thickness TextBoxPadding { get; init; } = new(
        CompactDialogVisualTokens.TextBoxPaddingHorizontal,
        CompactDialogVisualTokens.TextBoxPaddingVertical);
    public Thickness ComboBoxPadding { get; init; } = new(
        CompactDialogVisualTokens.ComboBoxPaddingHorizontal,
        CompactDialogVisualTokens.ComboBoxPaddingVertical);
    public Thickness TogglePadding { get; init; } = new(
        CompactDialogVisualTokens.TogglePaddingLeft,
        0,
        0,
        0);
    public Thickness LabelPadding { get; init; } = new(CompactDialogVisualTokens.LabelPadding);
    public Thickness GroupBoxMargin { get; init; } = new(
        0,
        CompactDialogVisualTokens.GroupBoxMarginVertical);
    public Thickness GroupBoxPadding { get; init; } = new(
        CompactDialogVisualTokens.GroupBoxPaddingHorizontal,
        CompactDialogVisualTokens.GroupBoxPaddingVertical);
    public Thickness ListBoxItemPadding { get; init; } = new(4, 1);
    public double ListBoxItemMinHeight { get; init; } = CompactDialogVisualTokens.ListBoxItemMinHeight;
    public double ActionSpacing { get; init; } = 8;
    public CornerRadius ButtonCornerRadius { get; init; } = new(CompactDialogVisualTokens.ButtonCornerRadius);
    public IBrush? ButtonBackgroundBrush { get; init; }
    public IBrush? ButtonHoverBackgroundBrush { get; init; }
    public IBrush? ButtonPressedBackgroundBrush { get; init; }
    public IBrush? ButtonAccentBrush { get; init; }
    public IBrush? InputBorderBrush { get; init; }
    public IBrush? DisabledInputBorderBrush { get; init; }
    public IBrush? ComboBoxBackgroundBrush { get; init; }
    public IBrush? TextBoxBackgroundBrush { get; init; }
    public IBrush? DisabledTextBoxBackgroundBrush { get; init; }
    public IBrush? TextSelectionBrush { get; init; }
    public IBrush? FocusedInputBorderBrush { get; init; }
    public IBrush? ForegroundBrush { get; init; }
    public IBrush? ButtonBorderBrush { get; init; }
    public IBrush? DefaultButtonBorderBrush { get; init; }
    public IBrush? DialogTabPaneBorderBrush { get; init; }
    public IBrush? DialogInactiveTabBorderBrush { get; init; }
    public IBrush? DialogInactiveTabBackgroundBrush { get; init; }
    public bool RemoveFocusAdorner { get; init; }
}

/// <summary>
/// Shared compact dialog chrome for Avalonia dialog controls that mirror Excel/WPF 24px metrics.
/// </summary>
public static class AvaloniaCompactDialogChrome
{
    public const string DialogWindowClass = "free-compact-dialog-window";
    public const string ClassicTabClass = "free-classic-dialog-tabs";
    public const string CompactButtonClass = "free-compact-dialog-button";
    public const string CompactComboBoxClass = "free-compact-dialog-combo";
    public const string CompactTextBoxClass = "free-compact-dialog-textbox";
    private const string ReadOnlyDocumentClass = "free-read-only-document";

    public static FontFamily WindowsUiFontFamily { get; } = new(
        "Segoe UI, Arial Narrow, Aptos Narrow, Liberation Sans Narrow, Nimbus Sans Narrow, " +
        "DejaVu Sans Condensed, Arial, Liberation Sans, Noto Sans, DejaVu Sans, Helvetica, sans-serif");

    public static AvaloniaCompactDialogChromeStyle WindowsStyle { get; } = new(WindowsUiFontFamily);
    public static IBrush NeutralButtonBorderBrush => ButtonBorderBrush;
    public static IBrush DialogSeparatorBrush => DialogTabPaneBorderBrush;

    private static readonly IBrush ButtonBorderBrush = new ImmutableSolidColorBrush(
        Color.Parse(CompactDialogVisualTokens.BorderHex));
    private static readonly IBrush ButtonAccentBrush = new ImmutableSolidColorBrush(Color.FromRgb(15, 109, 140));
    private static readonly IBrush ButtonHoverBackgroundBrush = new ImmutableSolidColorBrush(Color.FromRgb(230, 246, 250));
    private static readonly IBrush ButtonPressedBackgroundBrush = new ImmutableSolidColorBrush(Color.FromRgb(204, 234, 242));
    // Match the shared WPF DialogFieldBorder authority (#B7BCC2), rather than
    // Fluent's darker neutral border or the legacy Windows #ABADB3 shade.
    private static readonly IBrush InputBorderBrush = new ImmutableSolidColorBrush(
        Color.Parse(CompactDialogVisualTokens.FieldBorderHex));
    private static readonly IBrush DisabledInputBorderBrush = new ImmutableSolidColorBrush(
        Color.Parse(CompactDialogVisualTokens.DisabledFieldBorderHex));
    private static readonly IBrush ComboBoxBackgroundBrush = new ImmutableSolidColorBrush(Color.FromRgb(240, 240, 240));
    private static readonly IBrush TextSelectionBrush = new ImmutableSolidColorBrush(Color.FromRgb(0, 120, 215));
    private static readonly IBrush SelectedItemBackgroundBrush = new ImmutableSolidColorBrush(Color.FromRgb(204, 232, 255));
    private static readonly IBrush SelectedItemBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(153, 209, 255));
    private static readonly IBrush DialogForegroundBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f));
    private static readonly IBrush GroupBoxBorderBrush = ButtonBorderBrush;
    private static readonly IBrush ValidationStatusBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x80, 0x00, 0x00));
    private static readonly IBrush DisabledButtonForegroundBrush = new ImmutableSolidColorBrush(
        Color.Parse(CompactDialogVisualTokens.DisabledForegroundHex));
    private static readonly IBrush DisabledButtonBorderBrush = new ImmutableSolidColorBrush(
        Color.Parse(CompactDialogVisualTokens.DisabledBorderHex));
    private static readonly IBrush DialogTabPaneBorderBrush = new ImmutableSolidColorBrush(
        Color.Parse(DialogTabChromeMetrics.PaneBorderHex));
    private static readonly IBrush DialogInactiveTabBorderBrush = new ImmutableSolidColorBrush(
        Color.Parse(DialogTabChromeMetrics.InactiveTabBorderHex));
    private static readonly IBrush DialogInactiveTabBackgroundBrush = new ImmutableSolidColorBrush(
        Color.Parse(DialogTabChromeMetrics.InactiveTabBackgroundHex));
    private static readonly IBrush DialogSelectedTabBackgroundBrush = new ImmutableSolidColorBrush(
        Color.Parse(DialogTabChromeMetrics.SelectedTabBackgroundHex));
    private static readonly IBrush ReadOnlyDocumentFocusedBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(86, 157, 229));

    private static IBrush ThemeBrush(string resourceKey, IBrush fallback)
    {
        var app = Application.Current;
        return app is not null
            && app.TryGetResource(resourceKey, ThemeVariant.Default, out var value)
            && value is IBrush brush
                ? brush
                : fallback;
    }

    private static IBrush ThemeTextBrush(AvaloniaCompactDialogChromeStyle style) =>
        style.ForegroundBrush ?? ThemeBrush("ThemeNeutralTextBrush", DialogForegroundBrush);

    private static IBrush ThemeWhiteBrush() =>
        ThemeBrush("ThemeNeutralWhiteBrush", Brushes.White);

    private static IBrush ThemeAccentBrush(AvaloniaCompactDialogChromeStyle style) =>
        style.ButtonAccentBrush ?? ThemeBrush("ThemeAccentBrush", ButtonAccentBrush);

    /// <summary>
    /// Gives a code-built Avalonia dialog the same inherited surface and compact control metrics as the
    /// WPF <c>DialogWindow</c>. Descendants are normalized after the visual tree is created so every route
    /// uses the product chrome, including controls that do not call the individual helper methods.
    /// </summary>
    public static void ApplyWindow(
        Window window,
        AvaloniaCompactDialogChromeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        style ??= WindowsStyle;

        if (window.Classes.Contains(DialogWindowClass))
            return;

        window.Classes.Add(DialogWindowClass);
        window.Background = ThemeWhiteBrush();
        window.Foreground = ThemeTextBrush(style);
        window.FontFamily = style.FontFamily;
        window.FontSize = style.FontSize;
        // WPF's shared DialogWindow explicitly requests ClearType. Avalonia's closest rendering
        // contract is subpixel antialiasing; forcing grayscale here collapses the glyph palette
        // and inflates text-edge deltas across every paired dialog.
        TextOptions.SetTextRenderingMode(window, TextRenderingMode.SubpixelAntialias);
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.ShowInTaskbar = false;
        window.Opened += (_, _) => ApplyDescendantChrome(window, style);
    }

    public static void ApplyDescendantChrome(
        Window window,
        AvaloniaCompactDialogChromeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        style ??= WindowsStyle;

        foreach (var control in window.GetVisualDescendants().OfType<Control>())
        {
            if (control is TextBlock textBlock)
            {
                // Match WPF implicit dialog styles: supply defaults to inherited labels, but do not
                // overwrite local typography or color choices used by hierarchy, hints, and links.
                if (!textBlock.IsSet(TextBlock.FontFamilyProperty))
                    textBlock.FontFamily = style.FontFamily;
                if (!textBlock.IsSet(TextBlock.FontSizeProperty))
                    textBlock.FontSize = style.FontSize;
                if (!textBlock.IsSet(TextBlock.ForegroundProperty))
                    textBlock.Foreground = ThemeTextBrush(style);
            }

            switch (control)
            {
                case TextBox textBox:
                {
                    var explicitFamily = textBox.FontFamily;
                    var isEditableComboTextBox = textBox.Name == "PART_EditableTextBox";
                    var hasExplicitHeight = !isEditableComboTextBox
                        && (textBox.IsSet(Layoutable.HeightProperty)
                            || textBox.IsSet(Layoutable.MinHeightProperty)
                            || textBox.IsSet(Layoutable.MaxHeightProperty));
                    var isMultiline = !isEditableComboTextBox && (textBox.AcceptsReturn
                        || textBox.MinHeight > style.ControlHeight
                        || (!double.IsNaN(textBox.Height) && textBox.Height > style.ControlHeight));
                    ApplyTextBox(textBox, style, fixedHeight: !hasExplicitHeight && !isMultiline);
                    if (explicitFamily != FontFamily.Default && explicitFamily != window.FontFamily)
                        textBox.FontFamily = explicitFamily;
                    break;
                }
                case ComboBox comboBox:
                {
                    var hasExplicitHeight = comboBox.IsSet(Layoutable.HeightProperty)
                        || comboBox.IsSet(Layoutable.MinHeightProperty)
                        || comboBox.IsSet(Layoutable.MaxHeightProperty);
                    ApplyComboBox(comboBox, style, fixedHeight: !hasExplicitHeight);
                    break;
                }
                case CheckBox checkBox:
                    ApplyCheckBox(checkBox, style);
                    break;
                case RadioButton radioButton:
                    ApplyRadioButton(radioButton, style);
                    break;
                case ListBox listBox:
                    ApplyListBox(listBox, style);
                    break;
                case GroupBox groupBox:
                    ApplyGroupBox(groupBox, style);
                    break;
                case Label label:
                    ApplyLabel(label, style);
                    break;
                case TabControl tabControl:
                    ApplyClassicTabChrome(tabControl, style);
                    break;
                case Button button:
                    ApplyButton(
                        button,
                        style,
                        button.IsSet(Layoutable.MinWidthProperty) ? button.MinWidth : style.ButtonMinWidth,
                        button.IsDefault);
                    break;
            }
        }
    }

    public static void ApplyButton(
        Button button,
        AvaloniaCompactDialogChromeStyle style,
        double minWidth,
        bool isDefault = false)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(style);

        button.MinWidth = minWidth;
        button.Height = style.ButtonHeight;
        button.MinHeight = style.ButtonHeight;
        button.MaxHeight = style.ButtonHeight;
        button.Padding = style.ButtonPadding;
        button.CornerRadius = style.ButtonCornerRadius;
        button.BorderThickness = new Thickness(CompactDialogVisualTokens.BorderThickness);
        button.FontSize = style.FontSize;
        button.FontFamily = style.FontFamily;
        if (isDefault)
            button.IsDefault = true;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        if (button.Content is string content)
            AvaloniaDialogButtonContent.Apply(button, content);
        if (button.Classes.Contains(CompactButtonClass))
            return;

        var restingBackground = style.ButtonBackgroundBrush ?? ThemeWhiteBrush();
        var accentBrush = ThemeAccentBrush(style);
        var restingBorder = isDefault
            ? style.DefaultButtonBorderBrush ?? accentBrush
            : style.ButtonBorderBrush ?? ButtonBorderBrush;
        button.Styles.Add(new Style(selector => selector.OfType<Button>())
        {
            Setters =
            {
                new Setter(Button.ForegroundProperty, ThemeTextBrush(style)),
                new Setter(Button.BackgroundProperty, restingBackground),
                new Setter(Button.BorderBrushProperty, restingBorder),
            },
        });
        button.Classes.Add(CompactButtonClass);
        button.Styles.Add(new Style(selector => selector.OfType<Button>().Class(":pointerover"))
        {
            Setters =
            {
                new Setter(Button.BackgroundProperty, style.ButtonHoverBackgroundBrush
                    ?? ThemeBrush("ThemeAccentSoftBrush", ButtonHoverBackgroundBrush)),
                new Setter(Button.BorderBrushProperty, accentBrush),
            },
        });
        button.Styles.Add(new Style(selector => selector.OfType<Button>().Class(":pressed"))
        {
            Setters =
            {
                new Setter(Button.BackgroundProperty, style.ButtonPressedBackgroundBrush
                    ?? ThemeBrush("ThemeAccentPressedBrush", ButtonPressedBackgroundBrush)),
                new Setter(Button.BorderBrushProperty, accentBrush),
            },
        });
        button.Styles.Add(new Style(selector => selector.OfType<Button>().Class(":disabled"))
        {
            Setters =
            {
                new Setter(Button.ForegroundProperty, DisabledButtonForegroundBrush),
                new Setter(Button.BackgroundProperty,
                    ThemeBrush("ThemeNeutralSheetSurfaceBrush", new ImmutableSolidColorBrush(Color.FromRgb(0xf3, 0xf3, 0xf3)))),
                new Setter(Button.BorderBrushProperty, DisabledButtonBorderBrush),
            },
        });
    }

    public static void ApplyTextBox(TextBox textBox, AvaloniaCompactDialogChromeStyle style, bool fixedHeight = true)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(style);

        if (fixedHeight)
        {
            var height = style.TextBoxHeight ?? style.ControlHeight;
            textBox.Height = height;
            textBox.MinHeight = height;
            textBox.MaxHeight = height;
        }
        textBox.Padding = style.TextBoxPadding;
        textBox.CornerRadius = new CornerRadius(0);
        textBox.FontSize = style.FontSize;
        textBox.FontFamily = style.FontFamily;
        var inputBorder = style.InputBorderBrush ?? InputBorderBrush;
        var disabledInputBorder = style.DisabledInputBorderBrush ?? DisabledInputBorderBrush;
        var textBoxBackground = style.TextBoxBackgroundBrush ?? ThemeWhiteBrush();
        textBox.Foreground = ThemeTextBrush(style);
        textBox.Background = textBoxBackground;
        textBox.BorderBrush = inputBorder;
        textBox.BorderThickness = new Thickness(CompactDialogVisualTokens.BorderThickness);
        textBox.SelectionBrush = style.TextSelectionBrush ?? TextSelectionBrush;
        if (style.TextSelectionBrush is not null)
            textBox.SelectionForegroundBrush = Brushes.Black;
        textBox.VerticalContentAlignment = VerticalAlignment.Center;
        var focusedBorder = style.FocusedInputBorderBrush ?? ThemeAccentBrush(style);
        if (style.RemoveFocusAdorner)
            textBox.FocusAdorner = null;

        // Fluent owns the realized field border in PART_BorderElement. Keep the WPF
        // field authority in this shared adapter so individual applications do not
        // patch toolkit internals or duplicate focus/disabled event policy.
        if (textBox.Classes.Contains(CompactTextBoxClass))
        {
            QueueRenderedTextBoxChrome(textBox, style, fixedHeight);
            return;
        }

        textBox.Classes.Add(CompactTextBoxClass);
        textBox.Styles.Add(new Style(selector => selector.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TextBox.ForegroundProperty, ThemeTextBrush(style)),
                new Setter(TextBox.BackgroundProperty, textBoxBackground),
                new Setter(TextBox.BorderBrushProperty, inputBorder),
                new Setter(TextBox.BorderThicknessProperty, new Thickness(CompactDialogVisualTokens.BorderThickness)),
            },
        });
        textBox.Styles.Add(new Style(selector => selector.OfType<TextBox>().Class(":focus"))
        {
            Setters =
            {
                new Setter(TextBox.BorderBrushProperty, focusedBorder),
                new Setter(TextBox.BorderThicknessProperty, new Thickness(CompactDialogVisualTokens.BorderThickness)),
            },
        });
        textBox.Styles.Add(new Style(selector => selector.OfType<TextBox>().Class(":pointerover"))
        {
            Setters = { new Setter(TextBox.BorderBrushProperty, focusedBorder) },
        });
        if (style.DisabledTextBoxBackgroundBrush is not null)
        {
            textBox.Styles.Add(new Style(selector => selector.OfType<TextBox>().Class(":disabled"))
            {
                Setters =
                {
                    new Setter(TextBox.BackgroundProperty, style.DisabledTextBoxBackgroundBrush),
                    new Setter(TextBox.BorderBrushProperty, disabledInputBorder),
                },
            });
            // Avalonia's Fluent disabled template supplies its own surface after the class
            // style is applied. Keep WPF-authority palettes visible for controls that are
            // already disabled when the dialog chrome is installed.
            if (!textBox.IsEnabled)
            {
                textBox.Opacity = 1;
                textBox.Background = style.DisabledTextBoxBackgroundBrush;
                textBox.Foreground = new ImmutableSolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
            }
        }
        var borderStyle = new Style(selector => selector
            .OfType<TextBox>()
            .Template()
            .OfType<Border>()
            .Name("PART_BorderElement"));
        borderStyle.Setters.Add(new Setter(Border.BorderBrushProperty, inputBorder));
        borderStyle.Setters.Add(new Setter(
            Border.BorderThicknessProperty,
            new Thickness(CompactDialogVisualTokens.BorderThickness)));
        borderStyle.Setters.Add(new Setter(Border.BackgroundProperty, textBoxBackground));
        if (fixedHeight)
        {
            borderStyle.Setters.Add(new Setter(Layoutable.MinHeightProperty, 0d));
            borderStyle.Setters.Add(new Setter(
                Layoutable.HeightProperty,
                style.TextBoxHeight ?? style.ControlHeight));
        }
        textBox.Styles.Add(borderStyle);
        textBox.Styles.Add(new Style(selector => selector
            .OfType<TextBox>()
            .Class(":focus")
            .Template()
            .OfType<Border>()
            .Name("PART_BorderElement"))
        {
            Setters =
            {
                new Setter(Border.BorderBrushProperty, focusedBorder),
                new Setter(
                    Border.BorderThicknessProperty,
                    new Thickness(CompactDialogVisualTokens.BorderThickness)),
            },
        });
        textBox.Styles.Add(new Style(selector => selector
            .OfType<TextBox>()
            .Class(":pointerover")
            .Template()
            .OfType<Border>()
            .Name("PART_BorderElement"))
        {
            Setters = { new Setter(Border.BorderBrushProperty, focusedBorder) },
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
                new Setter(Border.BorderBrushProperty, disabledInputBorder),
                new Setter(
                    Border.BorderThicknessProperty,
                    new Thickness(CompactDialogVisualTokens.BorderThickness)),
                new Setter(
                    Border.BackgroundProperty,
                    style.DisabledTextBoxBackgroundBrush ?? textBoxBackground),
            },
        });

        textBox.AttachedToVisualTree += (_, _) =>
            QueueRenderedTextBoxChrome(textBox, style, fixedHeight);
        textBox.GotFocus += (_, _) =>
            QueueRenderedTextBoxChrome(textBox, style, fixedHeight);
        textBox.LostFocus += (_, _) =>
            QueueRenderedTextBoxChrome(textBox, style, fixedHeight);
        textBox.PropertyChanged += (_, args) =>
        {
            if (args.Property == InputElement.IsEnabledProperty)
                QueueRenderedTextBoxChrome(textBox, style, fixedHeight);
        };
        QueueRenderedTextBoxChrome(textBox, style, fixedHeight);
    }

    private static void QueueRenderedTextBoxChrome(
        TextBox textBox,
        AvaloniaCompactDialogChromeStyle style,
        bool fixedHeight) =>
        Dispatcher.UIThread.Post(
            () => RefreshRenderedTextBoxChrome(textBox, style, fixedHeight),
            DispatcherPriority.Render);

    private static void RefreshRenderedTextBoxChrome(
        TextBox textBox,
        AvaloniaCompactDialogChromeStyle style,
        bool fixedHeight)
    {
        textBox.ApplyTemplate();
        var normalBorder = style.InputBorderBrush ?? InputBorderBrush;
        var border = !textBox.IsEnabled
            ? style.DisabledInputBorderBrush ?? DisabledInputBorderBrush
            : textBox.IsFocused
                ? style.FocusedInputBorderBrush ?? ThemeAccentBrush(style)
                : normalBorder;
        var background = !textBox.IsEnabled && style.DisabledTextBoxBackgroundBrush is not null
            ? style.DisabledTextBoxBackgroundBrush
            : style.TextBoxBackgroundBrush ?? ThemeWhiteBrush();
        textBox.BorderBrush = border;
        foreach (var borderElement in textBox.GetVisualDescendants()
                     .OfType<Border>()
                     .Where(candidate => candidate.Name == "PART_BorderElement"))
        {
            if (fixedHeight)
            {
                borderElement.MinHeight = 0;
                borderElement.Height = style.TextBoxHeight ?? style.ControlHeight;
            }
            borderElement.BorderBrush = border;
            borderElement.BorderThickness = new Thickness(CompactDialogVisualTokens.BorderThickness);
            borderElement.Background = background;
        }
    }

    /// <summary>
    /// Applies Avalonia-host template compensation for the shared WPF read-only document padding.
    /// The resulting Avalonia <see cref="Thickness"/> is host-specific, not a shared WPF metric:
    /// Avalonia's TextBox template contributes a four-pixel leading content inset for this surface family.
    /// </summary>
    public static void ApplyAvaloniaReadOnlyDocumentTemplatePadding(
        TextBox textBox,
        double sharedPadding,
        double rightMargin = 1)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        textBox.Padding = new Thickness(
            sharedPadding + 4,
            sharedPadding,
            sharedPadding,
            sharedPadding);
        textBox.Margin = new Thickness(2, 0, rightMargin, 0);
        // WPF's multiline read-only document host keeps its content pinned to the
        // top edge. The shared compact chrome defaults single-line inputs to center
        // alignment, so restore the document template's vertical behavior here.
        textBox.VerticalContentAlignment = VerticalAlignment.Top;
        textBox.HorizontalContentAlignment = HorizontalAlignment.Left;
        if (textBox.Classes.Contains(ReadOnlyDocumentClass))
        {
            textBox.SetValue(ScrollViewer.AllowAutoHideProperty, false);
            return;
        }

        textBox.Classes.Add(ReadOnlyDocumentClass);
        // WPF reserves an 18-pixel vertical scrollbar lane inside a read-only
        // document box. Fluent's compact scrollbar is narrower, which widens the
        // text viewport and changes wrapping across long notices.
        textBox.Styles.Add(new Style(selector => selector
            .OfType<ScrollBar>()
            .Class(":vertical"))
        {
            Setters =
            {
                new Setter(Layoutable.WidthProperty, 18d),
                new Setter(Layoutable.MinWidthProperty, 18d),
                new Setter(Layoutable.MaxWidthProperty, 18d),
            },
        });
        textBox.Styles.Add(new Style(selector => selector
            .OfType<TextBox>()
            .Class(":focus"))
        {
            Setters =
            {
                new Setter(TextBox.BorderBrushProperty, ReadOnlyDocumentFocusedBorderBrush),
                new Setter(TextBox.BorderThicknessProperty, new Thickness(CompactDialogVisualTokens.BorderThickness)),
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
                new Setter(Border.BorderBrushProperty, ReadOnlyDocumentFocusedBorderBrush),
                new Setter(Border.BorderThicknessProperty, new Thickness(CompactDialogVisualTokens.BorderThickness)),
                new Setter(Border.BackgroundProperty, Brushes.White),
            },
        });
        textBox.GotFocus += (_, _) => textBox.BorderBrush = ReadOnlyDocumentFocusedBorderBrush;
        textBox.LostFocus += (_, _) => textBox.BorderBrush = InputBorderBrush;
        textBox.SetValue(ScrollViewer.AllowAutoHideProperty, false);
    }

    /// <summary>Matches the WPF Legal Notices button's neutral default-state border.</summary>
    public static void ApplyLegalNoticesDefaultButtonChrome(Button button)
    {
        ApplyNeutralDefaultButtonChrome(button);
    }

    /// <summary>Matches the WPF neutral resting border for a default action button.</summary>
    public static void ApplyNeutralDefaultButtonChrome(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);

        button.BorderBrush = ButtonBorderBrush;
        button.BorderThickness = new Thickness(CompactDialogVisualTokens.BorderThickness);
    }

    /// <summary>
    /// Applies the Avalonia-host intro arrangement compensation for the shared WPF dialog margin.
    /// Avalonia's dialog template needs three additional pixels before a following tab body;
    /// the extra space belongs to this host template, not to shared dialog geometry.
    /// </summary>
    public static void ApplyAvaloniaDocumentIntroTemplateCompensation(
        TextBlock intro,
        double sharedBottomMargin)
    {
        ArgumentNullException.ThrowIfNull(intro);

        intro.Margin = new Thickness(
            intro.Margin.Left,
            intro.Margin.Top,
            intro.Margin.Right,
            sharedBottomMargin + 3);
    }

    public static void FocusAndSelect(TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        textBox.Focus();
        textBox.SelectionStart = 0;
        textBox.SelectionEnd = textBox.Text?.Length ?? 0;
    }

    public static void ApplyComboBox(
        ComboBox comboBox,
        AvaloniaCompactDialogChromeStyle style,
        bool fixedHeight = true)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        ArgumentNullException.ThrowIfNull(style);

        if (fixedHeight)
        {
            var height = style.ComboBoxHeight ?? style.ControlHeight;
            comboBox.Height = height;
            comboBox.MinHeight = height;
            comboBox.MaxHeight = height;
        }
        comboBox.Padding = style.ComboBoxPadding;
        comboBox.CornerRadius = new CornerRadius(0);
        comboBox.FontSize = style.FontSize;
        comboBox.FontFamily = style.FontFamily;
        var foreground = ThemeTextBrush(style);
        comboBox.Foreground = foreground;
        comboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        // Fluent's editable ComboBox template hosts the text presenter separately from
        // the arrow. Stretch the content slot so an editable field remains a full-width
        // WPF-style input instead of collapsing to the text's desired width.
        comboBox.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var comboBackground = style.ComboBoxBackgroundBrush ?? ComboBoxBackgroundBrush;
        comboBox.Background = comboBackground;
        comboBox.BorderBrush = style.InputBorderBrush ?? InputBorderBrush;
        comboBox.BorderThickness = new Thickness(CompactDialogVisualTokens.BorderThickness);
        comboBox.VerticalContentAlignment = VerticalAlignment.Center;
        if (comboBox.IsEditable)
        {
            comboBox.IsTabStop = false;
            KeyboardNavigation.SetTabNavigation(comboBox, KeyboardNavigationMode.Local);
        }
        if (comboBox.Classes.Contains(CompactComboBoxClass))
            return;

        comboBox.Classes.Add(CompactComboBoxClass);
        var focusedBorder = style.FocusedInputBorderBrush ?? ThemeAccentBrush(style);
        comboBox.Styles.Add(new Style(selector => selector.OfType<ComboBox>().Class(":pointerover"))
        {
            Setters = { new Setter(ComboBox.BorderBrushProperty, focusedBorder) },
        });
        comboBox.Styles.Add(new Style(selector => selector.OfType<ComboBox>().Class(":focus-visible"))
        {
            Setters = { new Setter(ComboBox.BorderBrushProperty, focusedBorder) },
        });
        comboBox.Styles.Add(new Style(selector => selector.OfType<ComboBox>().Class(":disabled"))
        {
            Setters =
            {
                new Setter(ComboBox.ForegroundProperty, DisabledButtonForegroundBrush),
                new Setter(ComboBox.BorderBrushProperty, DisabledButtonBorderBrush),
            },
        });

        // Own the compact template instead of repairing private Fluent-theme parts after attach.
        // The required Avalonia ComboBox parts retain native selection, editable text, popup,
        // keyboard, and automation behavior while their geometry is now stable across theme updates.
        comboBox.Template = CreateCompactComboBoxTemplate(style, comboBackground, foreground);
    }

    public static void ApplyWpfDisabledComboSurface(ComboBox comboBox)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        // The shared template binds both the field surface and selection presenter directly to
        // ComboBox.Background, so callers only need to update the public control property.
        comboBox.InvalidateVisual();
    }

    private static FuncControlTemplate<ComboBox> CreateCompactComboBoxTemplate(
        AvaloniaCompactDialogChromeStyle style,
        IBrush backgroundBrush,
        IBrush foregroundBrush) => new((comboBox, nameScope) =>
    {
        var field = new Border
        {
            Name = "Background",
            Background = backgroundBrush,
        };
        field.Bind(Border.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = comboBox });
        field.Bind(Border.BorderBrushProperty, new Binding(nameof(TemplatedControl.BorderBrush)) { Source = comboBox });
        field.Bind(Border.BorderThicknessProperty, new Binding(nameof(TemplatedControl.BorderThickness)) { Source = comboBox });
        field.Bind(Border.CornerRadiusProperty, new Binding(nameof(TemplatedControl.CornerRadius)) { Source = comboBox });

        var selection = new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            Background = backgroundBrush,
            Foreground = foregroundBrush,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        selection.Bind(ContentPresenter.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = comboBox });
        selection.Bind(ContentPresenter.ForegroundProperty, new Binding(nameof(TemplatedControl.Foreground)) { Source = comboBox });
        selection.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ComboBox.SelectionBoxItem)) { Source = comboBox });
        selection.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ComboBox.SelectionBoxItemTemplate)) { Source = comboBox });
        selection.Bind(ContentPresenter.PaddingProperty, new Binding(nameof(TemplatedControl.Padding)) { Source = comboBox });
        selection.Bind(ContentPresenter.HorizontalContentAlignmentProperty, new Binding(nameof(ContentControl.HorizontalContentAlignment)) { Source = comboBox });
        selection.Bind(ContentPresenter.VerticalContentAlignmentProperty, new Binding(nameof(ContentControl.VerticalContentAlignment)) { Source = comboBox });
        selection.Bind(Visual.IsVisibleProperty, new Binding(nameof(ComboBox.IsEditable))
        {
            Source = comboBox,
            Converter = BoolConverters.Not,
        });

        var editableText = new TextBox
        {
            Name = "PART_EditableTextBox",
            Padding = new Thickness(
                style.ComboBoxPadding.Left,
                style.ComboBoxPadding.Top,
                20,
                style.ComboBoxPadding.Bottom),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = foregroundBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        editableText.Bind(TextBox.ForegroundProperty, new Binding(nameof(TemplatedControl.Foreground)) { Source = comboBox });
        editableText.Bind(TextBox.TextProperty, new Binding(nameof(ComboBox.Text))
        {
            Source = comboBox,
            Mode = BindingMode.TwoWay,
        });
        editableText.Bind(TextBox.PlaceholderTextProperty, new Binding(nameof(ComboBox.PlaceholderText)) { Source = comboBox });
        editableText.Bind(Visual.IsVisibleProperty, new Binding(nameof(ComboBox.IsEditable)) { Source = comboBox });

        var placeholder = new TextBlock
        {
            Background = backgroundBrush,
            Foreground = foregroundBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        placeholder.Bind(TextBlock.TextProperty, new Binding(nameof(ComboBox.PlaceholderText)) { Source = comboBox });
        placeholder.Bind(TextBlock.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = comboBox });
        placeholder.Bind(TextBlock.ForegroundProperty, new Binding(nameof(ComboBox.PlaceholderForeground)) { Source = comboBox });
        placeholder.Bind(TextBlock.PaddingProperty, new Binding(nameof(TemplatedControl.Padding)) { Source = comboBox });
        placeholder.Bind(Visual.IsVisibleProperty, new MultiBinding
        {
            Converter = BoolConverters.And,
            Bindings =
            {
                new Binding(nameof(ComboBox.SelectionBoxItem))
                {
                    Source = comboBox,
                    Converter = ObjectConverters.IsNull,
                },
                new Binding(nameof(ComboBox.IsEditable))
                {
                    Source = comboBox,
                    Converter = BoolConverters.Not,
                },
            },
        });

        var glyph = new global::Avalonia.Controls.PathIcon
        {
            Name = "DropDownGlyph",
            Data = Geometry.Parse("M 0 0 L 4 4 L 8 0 L 8 1 L 4 5 L 0 1 Z"),
            Foreground = foregroundBrush,
            Width = 8,
            Height = 5,
            Margin = new Thickness(0, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        glyph.Bind(global::Avalonia.Controls.PathIcon.ForegroundProperty, new Binding(nameof(TemplatedControl.Foreground)) { Source = comboBox });

        var items = new ItemsPresenter { Name = "PART_ItemsPresenter" };
        items.Bind(ItemsPresenter.ItemsPanelProperty, new Binding(nameof(ItemsControl.ItemsPanel)) { Source = comboBox });
        var popupScroll = new ScrollViewer
        {
            Content = items,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsScrollChainingEnabled = false,
        };
        var popupSurface = new Border
        {
            Name = "PopupBorder",
            Background = ThemeWhiteBrush(),
            BorderBrush = style.InputBorderBrush ?? InputBorderBrush,
            BorderThickness = new Thickness(CompactDialogVisualTokens.BorderThickness),
            Child = popupScroll,
        };
        var popup = new Popup
        {
            Name = "PART_Popup",
            Child = popupSurface,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            PlacementTarget = field,
            InheritsTransform = true,
            IsLightDismissEnabled = true,
        };
        popup.Bind(Popup.IsOpenProperty, new Binding(nameof(ComboBox.IsDropDownOpen))
        {
            Source = comboBox,
            Mode = BindingMode.TwoWay,
        });
        popup.Bind(Layoutable.MinWidthProperty, new Binding("Bounds.Width") { Source = comboBox });
        popup.Bind(Layoutable.MaxHeightProperty, new Binding(nameof(ComboBox.MaxDropDownHeight)) { Source = comboBox });

        nameScope.Register("PART_EditableTextBox", editableText);
        nameScope.Register("PART_ItemsPresenter", items);
        nameScope.Register("PART_Popup", popup);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,20") };
        Grid.SetColumnSpan(field, 2);
        Grid.SetColumn(placeholder, 0);
        Grid.SetColumnSpan(editableText, 2);
        Grid.SetColumn(selection, 0);
        Grid.SetColumn(glyph, 1);
        Grid.SetColumnSpan(popup, 2);
        grid.Children.Add(field);
        grid.Children.Add(placeholder);
        grid.Children.Add(selection);
        grid.Children.Add(editableText);
        grid.Children.Add(glyph);
        grid.Children.Add(popup);
        return grid;
    });

    public static double CalculateReadOnlyDocumentInset(double viewportHeight, double documentHeight)
    {
        if (!double.IsFinite(viewportHeight) ||
            !double.IsFinite(documentHeight) ||
            viewportHeight <= 0 ||
            documentHeight <= 0)
        {
            return 0;
        }

        return Math.Max(0, Math.Floor((viewportHeight - documentHeight) / 2));
    }

    public static bool RequiresReadOnlyDocumentOverflowLineHeight(
        double viewportHeight,
        int lineCount,
        double overflowLineHeight,
        double verticalPadding)
    {
        return double.IsFinite(viewportHeight) &&
               double.IsFinite(overflowLineHeight) &&
               double.IsFinite(verticalPadding) &&
               viewportHeight > 0 &&
               lineCount > 0 &&
               overflowLineHeight > 0 &&
               verticalPadding >= 0 &&
               (lineCount * overflowLineHeight) + verticalPadding > viewportHeight;
    }

    public static void ApplyCheckBox(CheckBox checkBox, AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(checkBox);
        ArgumentNullException.ThrowIfNull(style);

        checkBox.FontSize = style.FontSize;
        checkBox.FontFamily = style.FontFamily;
        checkBox.Foreground = ThemeTextBrush(style);
        checkBox.Padding = style.TogglePadding;
        checkBox.VerticalContentAlignment = VerticalAlignment.Center;
    }

    public static void ApplyCompactCheckBox(
        CheckBox checkBox,
        AvaloniaCompactDialogChromeStyle style,
        double contentSpacing = 4)
    {
        ApplyCheckBox(checkBox, style);
        checkBox.Height = 18;
        checkBox.MinHeight = 18;
        checkBox.Padding = new Thickness(0);
        checkBox.Template = CreateCompactCheckBoxTemplate(style, contentSpacing);
    }

    public static FuncControlTemplate<CheckBox> CreateCompactCheckBoxTemplate(
        AvaloniaCompactDialogChromeStyle style,
        double contentSpacing = 4)
    {
        ArgumentNullException.ThrowIfNull(style);

        var foreground = ThemeTextBrush(style);
        var white = ThemeWhiteBrush();
        var border = new ImmutableSolidColorBrush(Color.Parse(CompactDialogVisualTokens.ToggleBorderHex));
        var disabledBackground = new ImmutableSolidColorBrush(
            Color.Parse(CompactDialogVisualTokens.ToggleDisabledBackgroundHex));
        var disabledBorder = new ImmutableSolidColorBrush(
            Color.Parse(CompactDialogVisualTokens.ToggleDisabledBorderHex));
        var disabledMark = new ImmutableSolidColorBrush(
            Color.Parse(CompactDialogVisualTokens.ToggleDisabledMarkHex));

        return new FuncControlTemplate<CheckBox>((control, _) =>
        {
            var checkMark = new global::Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M 2 6 L 5 9 L 12 2"),
                Stroke = foreground,
                StrokeThickness = 1.4,
                Width = CompactDialogVisualTokens.CheckBoxCheckMarkWidth,
                Height = CompactDialogVisualTokens.CheckBoxCheckMarkHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            checkMark.Bind(
                Visual.IsVisibleProperty,
                new Binding(nameof(ToggleButton.IsChecked))
                {
                    Source = control,
                });

            var indeterminateMark = new Border
            {
                Width = CompactDialogVisualTokens.CheckBoxIndeterminateMarkWidth,
                Height = CompactDialogVisualTokens.CheckBoxIndeterminateMarkHeight,
                Background = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            indeterminateMark.Bind(
                Visual.IsVisibleProperty,
                new Binding(nameof(ToggleButton.IsChecked))
                {
                    Source = control,
                    Converter = ObjectConverters.IsNull,
                });

            var indicator = new Border
            {
                // WPF's native compact checkbox paints a 14 x 13 device-pixel frame at 96 DPI.
                // Keep the separately configured interaction row height while sharing that authority
                // geometry across FreeX, FreeW, and FreeP dialog renderers.
                Width = CompactDialogVisualTokens.CheckBoxIndicatorWidth,
                Height = CompactDialogVisualTokens.CheckBoxIndicatorHeight,
                Background = white,
                BorderBrush = border,
                BorderThickness = new Thickness(CompactDialogVisualTokens.BorderThickness),
                Child = new Panel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { checkMark, indeterminateMark },
                },
            };

            void UpdateEnabledChrome()
            {
                indicator.Background = control.IsEnabled ? white : disabledBackground;
                indicator.BorderBrush = control.IsEnabled ? border : disabledBorder;
                checkMark.Stroke = control.IsEnabled ? foreground : disabledMark;
                indeterminateMark.Background = control.IsEnabled ? foreground : disabledMark;
            }

            UpdateEnabledChrome();
            control.PropertyChanged += (_, args) =>
            {
                if (args.Property == InputElement.IsEnabledProperty)
                    UpdateEnabledChrome();
            };

            var content = new ContentPresenter
            {
                FontFamily = style.FontFamily,
                FontSize = style.FontSize,
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = foreground,
                RecognizesAccessKey = true,
            };
            content.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = control });
            content.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ContentControl.ContentTemplate)) { Source = control });

            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = contentSpacing,
                Children = { indicator, content },
            };
        });
    }

    public static void ApplyCompactRadioButton(RadioButton radioButton, AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(radioButton);
        ArgumentNullException.ThrowIfNull(style);

        ApplyRadioButton(radioButton, style);
        var foreground = ThemeTextBrush(style);
        radioButton.Height = style.CompactRadioButtonHeight;
        radioButton.MinHeight = style.CompactRadioButtonHeight;
        radioButton.MaxHeight = style.CompactRadioButtonHeight;
        radioButton.Padding = new Thickness(0);
        radioButton.Foreground = foreground;
        radioButton.Template = CreateCompactRadioButtonTemplate(style);
    }

    public static FuncControlTemplate<RadioButton> CreateCompactRadioButtonTemplate(
        AvaloniaCompactDialogChromeStyle style,
        double contentSpacing = 4)
    {
        ArgumentNullException.ThrowIfNull(style);

        var foreground = ThemeTextBrush(style);
        var white = ThemeWhiteBrush();
        var border = new ImmutableSolidColorBrush(Color.Parse(CompactDialogVisualTokens.ToggleBorderHex));
        var disabledBackground = new ImmutableSolidColorBrush(
            Color.Parse(CompactDialogVisualTokens.ToggleDisabledBackgroundHex));
        var disabledBorder = new ImmutableSolidColorBrush(
            Color.Parse(CompactDialogVisualTokens.ToggleDisabledBorderHex));
        var disabledMark = new ImmutableSolidColorBrush(
            Color.Parse(CompactDialogVisualTokens.ToggleDisabledMarkHex));

        return new FuncControlTemplate<RadioButton>((control, _) =>
        {
            var dot = new Ellipse
            {
                Width = CompactDialogVisualTokens.RadioButtonDotSize,
                Height = CompactDialogVisualTokens.RadioButtonDotSize,
                Fill = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            dot.Bind(
                Visual.IsVisibleProperty,
                new Binding(nameof(ToggleButton.IsChecked))
                {
                    Source = control,
                });

            var indicator = new Border
            {
                Width = CompactDialogVisualTokens.RadioButtonIndicatorSize,
                Height = CompactDialogVisualTokens.RadioButtonIndicatorSize,
                Background = white,
                BorderBrush = border,
                BorderThickness = new Thickness(CompactDialogVisualTokens.BorderThickness),
                CornerRadius = new CornerRadius(CompactDialogVisualTokens.RadioButtonIndicatorSize / 2),
                Child = dot,
            };

            void UpdateEnabledChrome()
            {
                indicator.Background = control.IsEnabled ? white : disabledBackground;
                indicator.BorderBrush = control.IsEnabled ? border : disabledBorder;
                dot.Fill = control.IsEnabled ? foreground : disabledMark;
            }

            UpdateEnabledChrome();
            control.PropertyChanged += (_, args) =>
            {
                if (args.Property == InputElement.IsEnabledProperty)
                    UpdateEnabledChrome();
            };

            var content = new ContentPresenter
            {
                VerticalContentAlignment = VerticalAlignment.Center,
                RecognizesAccessKey = true,
            };
            content.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = control });
            content.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ContentControl.ContentTemplate)) { Source = control });

            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = contentSpacing,
                Children = { indicator, content },
            };
        });
    }

    /// <summary>Applies the unframed, full-width expander chrome used by compact WPF dialogs.</summary>
    public static void ApplyWpfExpander(
        Expander expander,
        AvaloniaCompactDialogChromeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(expander);
        style ??= WindowsStyle;
        var foreground = ThemeTextBrush(style);
        var white = ThemeWhiteBrush();

        expander.FontFamily = style.FontFamily;
        expander.FontSize = style.FontSize;
        expander.Foreground = foreground;
        expander.HorizontalAlignment = HorizontalAlignment.Stretch;
        expander.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        expander.Padding = new Thickness(0);
        expander.Background = Brushes.Transparent;
        expander.BorderBrush = Brushes.Transparent;
        expander.BorderThickness = new Thickness(0);
        expander.Template = new FuncControlTemplate<Expander>((control, _) =>
        {
            var arrow = new global::Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M 3 5 L 6 2 L 9 5"),
                Stroke = foreground,
                StrokeThickness = 1,
                Width = 12,
                Height = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var indicator = new Border
            {
                Width = 18,
                Height = 18,
                Background = white,
                BorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(112, 112, 112)),
                BorderThickness = new Thickness(CompactDialogVisualTokens.BorderThickness),
                CornerRadius = new CornerRadius(9),
                Child = arrow,
            };
            var header = new ContentPresenter
            {
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            header.Bind(ContentPresenter.ContentProperty, new Binding(nameof(HeaderedContentControl.Header)) { Source = control });
            header.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(HeaderedContentControl.HeaderTemplate)) { Source = control });

            var toggle = new ToggleButton
            {
                Height = 20,
                MinHeight = 20,
                MaxHeight = 20,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { indicator, header },
                },
            };
            toggle.Template = new FuncControlTemplate<ToggleButton>((button, _) =>
            {
                var presenter = new ContentPresenter
                {
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = button });
                presenter.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ContentControl.ContentTemplate)) { Source = button });
                return presenter;
            });

            var content = new ContentPresenter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsVisible = control.IsExpanded,
            };
            content.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = control });
            content.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ContentControl.ContentTemplate)) { Source = control });

            void UpdateExpandedState()
            {
                content.IsVisible = control.IsExpanded;
                toggle.IsChecked = control.IsExpanded;
                arrow.Data = Geometry.Parse(control.IsExpanded ? "M 3 6 L 6 3 L 9 6" : "M 3 3 L 6 6 L 9 3");
            }

            control.PropertyChanged += (_, args) =>
            {
                if (args.Property == Expander.IsExpandedProperty)
                    UpdateExpandedState();
            };
            toggle.Click += (_, _) => control.IsExpanded = !control.IsExpanded;
            UpdateExpandedState();

            return new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Children = { toggle, content },
            };
        });
    }

    public static void ApplyGroupBox(
        GroupBox groupBox,
        AvaloniaCompactDialogChromeStyle? style = null,
        IBrush? borderBrush = null)
    {
        ArgumentNullException.ThrowIfNull(groupBox);
        style ??= WindowsStyle;
        var accent = ThemeAccentBrush(style);

        groupBox.FontFamily = style.FontFamily;
        groupBox.FontSize = style.FontSize;
        groupBox.Foreground = accent;
        groupBox.BorderBrush = borderBrush ?? GroupBoxBorderBrush;
        groupBox.BorderThickness = new Thickness(CompactDialogVisualTokens.BorderThickness);
        if (!groupBox.IsSet(Layoutable.MarginProperty))
            groupBox.Margin = style.GroupBoxMargin;
        if (!groupBox.IsSet(TemplatedControl.PaddingProperty))
            groupBox.Padding = style.GroupBoxPadding;
        groupBox.HeaderTemplate = new FuncDataTemplate<object>((header, _) => new TextBlock
        {
            Text = header?.ToString() ?? string.Empty,
            FontFamily = style.FontFamily,
            FontSize = style.FontSize,
            Foreground = accent,
            TextWrapping = TextWrapping.NoWrap,
        });
    }

    public static void ApplyLabel(Label label, AvaloniaCompactDialogChromeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        style ??= WindowsStyle;

        if (!label.IsSet(TemplatedControl.FontFamilyProperty))
            label.FontFamily = style.FontFamily;
        if (!label.IsSet(TemplatedControl.FontSizeProperty))
            label.FontSize = style.FontSize;
        if (!label.IsSet(TemplatedControl.ForegroundProperty))
            label.Foreground = ThemeTextBrush(style);
        if (!label.IsSet(TemplatedControl.PaddingProperty))
            label.Padding = style.LabelPadding;
    }

    public static void ApplyRadioButton(RadioButton radioButton, AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(radioButton);
        ArgumentNullException.ThrowIfNull(style);

        radioButton.FontSize = style.FontSize;
        radioButton.FontFamily = style.FontFamily;
        radioButton.Foreground = ThemeTextBrush(style);
        radioButton.Padding = style.TogglePadding;
        radioButton.VerticalContentAlignment = VerticalAlignment.Center;
    }

    public static void ApplyValidationStatus(
        TextBlock status,
        AvaloniaCompactDialogChromeStyle style,
        Thickness margin = default)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(style);

        status.Foreground = ThemeBrush("ThemeNeutralDangerBrush", ValidationStatusBrush);
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
        listBox.Background = ThemeWhiteBrush();
        listBox.Foreground = ThemeTextBrush(style);
        listBox.BorderBrush = style.InputBorderBrush ?? InputBorderBrush;
        listBox.BorderThickness = new Thickness(CompactDialogVisualTokens.BorderThickness);
        var itemTemplate = new FuncControlTemplate<ListBoxItem>((item, _) =>
        {
            var presenter = new ContentPresenter
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = item });
            presenter.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ContentControl.ContentTemplate)) { Source = item });
            presenter.Bind(ContentPresenter.PaddingProperty, new Binding(nameof(TemplatedControl.Padding)) { Source = item });

            var border = new Border();
            border.Bind(Border.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = item });
            border.Bind(Border.BorderBrushProperty, new Binding(nameof(TemplatedControl.BorderBrush)) { Source = item });
            border.Bind(Border.BorderThicknessProperty, new Binding(nameof(TemplatedControl.BorderThickness)) { Source = item });
            border.Child = presenter;
            return border;
        });
        listBox.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, style.ListBoxItemPadding),
                new Setter(Layoutable.MinHeightProperty, style.ListBoxItemMinHeight),
                new Setter(TemplatedControl.FontSizeProperty, style.FontSize),
                new Setter(TemplatedControl.TemplateProperty, itemTemplate),
            },
        });
        listBox.Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, SelectedItemBackgroundBrush),
                new Setter(TemplatedControl.BorderBrushProperty, SelectedItemBorderBrush),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(CompactDialogVisualTokens.BorderThickness)),
            },
        });
    }

    /// <summary>
    /// Applies the classic Windows dialog tab treatment: bordered inactive tabs, a white selected tab,
    /// and a selected-tab body that overlaps the content pane so no gap or separator line remains.
    /// </summary>
    public static void ApplyClassicTabChrome(
        TabControl tabControl,
        AvaloniaCompactDialogChromeStyle? style = null,
        Thickness? contentPaneMargin = null)
    {
        ArgumentNullException.ThrowIfNull(tabControl);
        style ??= WindowsStyle;

        if (tabControl.Classes.Contains(ClassicTabClass))
            return;
        tabControl.Classes.Add(ClassicTabClass);

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
        var tabPaneBorder = style.DialogTabPaneBorderBrush ?? DialogTabPaneBorderBrush;
        contentPaneStyle.Setters.Add(new Setter(Border.BorderBrushProperty, tabPaneBorder));
        contentPaneStyle.Setters.Add(new Setter(
            Border.BorderThicknessProperty,
            new Thickness(DialogTabChromeMetrics.PaneBorderThickness)));
        // Avalonia's platform TabControl template reserves an 11px body inset. The
        // WPF dialog pane is flush with the surrounding content, so cancel that
        // template inset while retaining the shared one-pixel pane frame.
        contentPaneStyle.Setters.Add(new Setter(
            Layoutable.MarginProperty,
            contentPaneMargin ?? new Thickness(0)));
        contentPaneStyle.Setters.Add(new Setter(ContentPresenter.PaddingProperty, new Thickness(0)));
        contentPaneStyle.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, DialogSelectedTabBackgroundBrush));
        tabControl.Styles.Add(contentPaneStyle);

        var authorityPaneMargin = contentPaneMargin ?? new Thickness(0);
        // The Fluent template can retain its own 12px presenter inset after the selector style
        // runs. Apply the shared authority margin to the realized presenter as well, including
        // the default zero-margin contract used by ordinary tabbed dialogs.
        void ApplyAuthorityPaneMargin()
        {
            tabControl.ApplyTemplate();
            var selectedPane = tabControl.GetVisualDescendants()
                .OfType<ContentPresenter>()
                .FirstOrDefault(presenter => presenter.Name == "PART_SelectedContentHost");
            if (selectedPane is not null)
            {
                // Fluent's template contributes a 12px horizontal inset outside the
                // presenter. A raw negative margin participates in its measure pass and
                // can collapse the pane, so consume that compensation at the template
                // boundary while keeping the selected content host stretched.
                selectedPane.Margin = new Thickness(
                    authorityPaneMargin.Left < 0 ? 0 : authorityPaneMargin.Left,
                    authorityPaneMargin.Top,
                    authorityPaneMargin.Right < 0 ? 0 : authorityPaneMargin.Right,
                    authorityPaneMargin.Bottom);
                selectedPane.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
        }

        tabControl.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(ApplyAuthorityPaneMargin, DispatcherPriority.Render);
        Dispatcher.UIThread.Post(ApplyAuthorityPaneMargin, DispatcherPriority.Render);

        var tabStyle = new Style(s => s.OfType<TabItem>());
        tabStyle.Setters.Add(new Setter(TabItem.BorderBrushProperty, style.DialogInactiveTabBorderBrush ?? DialogInactiveTabBorderBrush));
        tabStyle.Setters.Add(new Setter(
            TabItem.BorderThicknessProperty,
            new Thickness(
                DialogTabChromeMetrics.PaneBorderThickness,
                DialogTabChromeMetrics.PaneBorderThickness,
                DialogTabChromeMetrics.PaneBorderThickness,
                0)));
        tabStyle.Setters.Add(new Setter(TabItem.BackgroundProperty, style.DialogInactiveTabBackgroundBrush ?? DialogInactiveTabBackgroundBrush));
        tabStyle.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Brushes.Black));
        tabStyle.Setters.Add(new Setter(TemplatedControl.FontFamilyProperty, style.FontFamily));
        tabStyle.Setters.Add(new Setter(TemplatedControl.FontSizeProperty, style.FontSize));
        var tabHeight = style.TabHeight ?? style.ControlHeight;
        tabStyle.Setters.Add(new Setter(Layoutable.MinHeightProperty, tabHeight));
        if (style.TabHeight is { } explicitTabHeight)
        {
            tabStyle.Setters.Add(new Setter(Layoutable.HeightProperty, explicitTabHeight));
            tabStyle.Setters.Add(new Setter(Layoutable.MaxHeightProperty, explicitTabHeight));
        }
        tabStyle.Setters.Add(new Setter(TabItem.PaddingProperty, new Thickness(6, 2)));
        tabStyle.Setters.Add(new Setter(
            TabItem.MarginProperty,
            new Thickness(0, 0, -DialogTabChromeMetrics.AdjacentTabOverlap, 0)));
        tabControl.Styles.Add(tabStyle);

        var selectedTabStyle = new Style(s => s.OfType<TabItem>().Class(":selected"));
        selectedTabStyle.Setters.Add(new Setter(TabItem.BackgroundProperty, DialogSelectedTabBackgroundBrush));
        selectedTabStyle.Setters.Add(new Setter(TabItem.BorderBrushProperty, tabPaneBorder));
        selectedTabStyle.Setters.Add(new Setter(
            TabItem.BorderThicknessProperty,
            new Thickness(
                DialogTabChromeMetrics.PaneBorderThickness,
                DialogTabChromeMetrics.PaneBorderThickness,
                DialogTabChromeMetrics.PaneBorderThickness,
                0)));
        selectedTabStyle.Setters.Add(new Setter(
            TabItem.MarginProperty,
            new Thickness(
                0,
                0,
                -DialogTabChromeMetrics.AdjacentTabOverlap,
                -DialogTabChromeMetrics.SelectedTabContentOverlap)));
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
            presenter.Bind(ContentPresenter.ForegroundProperty, new Binding(nameof(TemplatedControl.Foreground)) { Source = tab });
            presenter.Bind(ContentPresenter.FontFamilyProperty, new Binding(nameof(TemplatedControl.FontFamily)) { Source = tab });
            presenter.Bind(ContentPresenter.FontSizeProperty, new Binding(nameof(TemplatedControl.FontSize)) { Source = tab });
            presenter.Styles.Add(new Style(s => s.OfType<AccessText>())
            {
                Setters =
                {
                    new Setter(TextBlock.ForegroundProperty, new Binding(nameof(ContentPresenter.Foreground)) { Source = presenter }),
                    new Setter(TextBlock.FontFamilyProperty, new Binding(nameof(ContentPresenter.FontFamily)) { Source = presenter }),
                    new Setter(TextBlock.FontSizeProperty, new Binding(nameof(ContentPresenter.FontSize)) { Source = presenter }),
                },
            });

            var root = new Border { Name = "PART_LayoutRoot" };
            root.Bind(Border.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = tab });
            root.Bind(Border.BorderBrushProperty, new Binding(nameof(TemplatedControl.BorderBrush)) { Source = tab });
            root.Bind(Border.BorderThicknessProperty, new Binding(nameof(TemplatedControl.BorderThickness)) { Source = tab });
            root.Child = presenter;
            return root;
        });
        tabStyle.Setters.Add(new Setter(TemplatedControl.TemplateProperty, classicTabTemplate));
    }

    public static StackPanel CreateActionRow(
        IReadOnlyList<Control> controls,
        Thickness margin = default,
        AvaloniaCompactDialogChromeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(controls);
        style ??= WindowsStyle;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = style.ActionSpacing,
            Margin = margin,
        };
        foreach (var control in controls)
        {
            row.Children.Add(control);
        }

        return row;
    }

    public static Button CreateActionButton(
        string content,
        Action action,
        double minWidth,
        bool isDefault = false,
        bool isCancel = false,
        AvaloniaCompactDialogChromeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(action);
        style ??= WindowsStyle;

        var button = new Button
        {
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        ApplyButton(button, style, minWidth, isDefault);
        AvaloniaDialogButtonContent.Apply(button, content);
        button.Click += (_, _) => action();
        return button;
    }

    public static StackPanel CreateOkCancelRow(
        Action accept,
        Action cancel,
        double buttonWidth,
        Thickness margin = default,
        AvaloniaCompactDialogChromeStyle? style = null)
    {
        return AvaloniaDialogButtonRowFactory.CreateOkCancel(
            accept,
            cancel,
            buttonWidth,
            margin,
            style);
    }
}
