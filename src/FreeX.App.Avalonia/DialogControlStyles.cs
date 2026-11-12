using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;

namespace FreeX.App.Avalonia;

/// <summary>
/// Registers low-priority application styles for code-built dialogs. Shared shell owns control
/// metrics, templates, and selectors; FreeX supplies only its product-theme brushes and the one
/// Options-page disabled-opacity exception.
/// </summary>
internal static class DialogControlStyles
{
    private static readonly IBrush BorderBrush =
        new ImmutableSolidColorBrush(Color.FromRgb(0xAB, 0xAB, 0xAB));

    private static readonly IBrush SelectionBrush =
        new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0x0F, 0x6D, 0x8C));

    internal static readonly IBrush SelectionForegroundBrush =
        AvaloniaThemeResourceResolver.Find<IBrush>(ProductThemeResourceProfiles.FreeX.Brush("Text"))
        ?? new ImmutableSolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));

    private static readonly IBrush TextSelectionBrush =
        new ImmutableSolidColorBrush(Color.FromRgb(173, 214, 255));

    public static IEnumerable<IStyle> Build()
    {
        var options = new AvaloniaCompactDialogFallbackStyleOptions(
            AvaloniaCompactDialogChrome.WindowsStyle,
            BorderBrush,
            SelectionBrush,
            SelectionForegroundBrush,
            TextSelectionBrush);

        foreach (var style in AvaloniaCompactDialogFallbackStyles.Create(options))
            yield return style;

        yield return new Style(x => x
            .OfType<CheckBox>()
            .Class("free-options-ease-checkbox")
            .Class(":disabled"))
        {
            // WPF keeps disabled labels on this Options page at full contrast while changing
            // the glyph fill, border, and mark through shared compact-toggle chrome.
            Setters = { new Setter(Visual.OpacityProperty, 1d) },
        };
    }
}

/// <summary>
/// WPF-shaped defaults for every FreeX dialog window, installed once at startup. FreeX dialogs are a
/// mix of windows on the shared compact chrome, windows chromed by a route helper and raw windows,
/// so the native WPF look (push-button colours, 12 px text, selection colours, group-box frame) is
/// supplied here at the window boundary instead of dialog by dialog:
/// <list type="bullet">
///   <item>product defaults on the shared chrome, for every control a dialog chromes itself;</item>
///   <item>resources and lowest-priority styles scoped to the dialog window, for controls nothing
///   chromes, so the main window, ribbon, backstage and menus never see them;</item>
///   <item>one pass over the dialog's own controls for values only a local assignment can correct.</item>
/// </list>
/// Explicit values a dialog sets on its controls always win.
/// </summary>
internal static class DialogWindowDefaults
{
    private const string DialogWindowClass = "freex-dialog-window-defaults";
    private const double DialogFontSize = 12;
    // WPF list rows at the 12 px dialog font: a 16 px text line plus the 1 px item padding.
    private const double DialogListRowMinHeight = 18;
    // WPF DialogReferencePicker and the native button template keep 1 px between frame and caption.
    private const double MinimumCaptionSidePadding = 1;
    private const double GrownCaptionSidePadding = 4;

    private static readonly Color ButtonFaceColor = Color.FromRgb(0xDD, 0xDD, 0xDD);
    private static readonly IBrush ButtonFace = new ImmutableSolidColorBrush(ButtonFaceColor);
    private static readonly IBrush ButtonBorder = new ImmutableSolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
    private static readonly IBrush DefaultButtonBorder = new ImmutableSolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7));
    private static readonly IBrush ButtonHoverFace = new ImmutableSolidColorBrush(Color.FromRgb(0xBE, 0xE6, 0xFD));
    private static readonly IBrush ButtonHoverBorder = new ImmutableSolidColorBrush(Color.FromRgb(0x3C, 0x7F, 0xB1));
    private static readonly IBrush ButtonPressedFace = new ImmutableSolidColorBrush(Color.FromRgb(0xC4, 0xE5, 0xF6));
    private static readonly IBrush ButtonPressedBorder = new ImmutableSolidColorBrush(Color.FromRgb(0x2C, 0x62, 0x8B));
    private static readonly IBrush ButtonDisabledFace = new ImmutableSolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4));
    private static readonly IBrush ButtonDisabledBorder = new ImmutableSolidColorBrush(Color.FromRgb(0xAD, 0xB2, 0xB5));
    private static readonly IBrush ButtonDisabledInk = new ImmutableSolidColorBrush(Color.FromRgb(0x83, 0x83, 0x83));
    // The WPF system highlight (#0078D7) at the text box's 0.4 selection opacity over a white field.
    private static readonly IBrush TextSelection = new ImmutableSolidColorBrush(Color.FromRgb(0x99, 0xC9, 0xEF));

    private static bool _installed;

    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;

        AvaloniaCompactDialogChrome.DefaultButtonPalette = new AvaloniaCompactDialogButtonPalette(
            ButtonFace,
            ButtonBorder,
            DefaultButtonBorder,
            ButtonHoverFace,
            ButtonHoverBorder,
            ButtonPressedFace,
            ButtonPressedBorder,
            ButtonDisabledFace,
            ButtonDisabledBorder,
            ButtonDisabledInk)
        {
            CornerRadius = new CornerRadius(0),
        };
        AvaloniaCompactDialogChrome.DefaultTextSelectionBrush = TextSelection;
        AvaloniaCompactDialogChrome.DefaultTextSelectionForegroundBrush = DialogControlStyles.SelectionForegroundBrush;
        AvaloniaCompactDialogChrome.DefaultListRowMinHeight = DialogListRowMinHeight;

        // Every dialog is shown owned (ShowDialog / Show with an owner), and the owner is assigned
        // after the dialog has been built, so this is the one point that sees all of them.
        Window.OwnerProperty.Changed.AddClassHandler<Window>(OnOwnerChanged);
    }

    private static void OnOwnerChanged(Window dialog, AvaloniaPropertyChangedEventArgs _)
    {
        if (dialog.Owner is null || dialog is MainWindow || IsOutOfScope(dialog))
            return;
        if (dialog.Classes.Contains(DialogWindowClass))
            return;
        dialog.Classes.Add(DialogWindowClass);

        ApplyScopedResources(dialog);
        InsertScopedStyles(dialog);
        if (!HasLocalValue(dialog, TemplatedControl.FontSizeProperty))
            dialog.FontSize = DialogFontSize;

        foreach (var control in dialog.GetLogicalDescendants().OfType<Control>().ToArray())
        {
            switch (control)
            {
                case GroupBox groupBox:
                    ApplyGroupBoxFrame(groupBox);
                    break;
                case Button button:
                    ApplyPushButtonFace(button);
                    break;
            }
        }

        // Caption fitting needs the resolved font, so it waits for the opened window. This handler
        // is attached after the dialog's own, so it also sees the shared descendant chrome pass.
        dialog.Opened += (_, _) =>
        {
            foreach (var button in dialog.GetLogicalDescendants().OfType<Button>().ToArray())
                FitCaption(button);
        };
    }

    // The backstage panes own their chrome, and print surfaces belong to the print work stream.
    private static bool IsOutOfScope(Window dialog)
    {
        var automationId = AutomationProperties.GetAutomationId(dialog) ?? string.Empty;
        return automationId.StartsWith("Backstage", StringComparison.Ordinal)
            || automationId.Contains("Print", StringComparison.Ordinal)
            || dialog.GetType().Name.Contains("Print", StringComparison.Ordinal);
    }

    /// <summary>
    /// Fluent resolves these keys from the nearest resource host, so setting them on the dialog
    /// window restyles the unchromed controls inside it (and the hover/pressed/disabled states of
    /// every push button, which Fluent's template paints from these keys whatever the button's own
    /// brushes are) without reaching any other window.
    /// </summary>
    private static void ApplyScopedResources(Window dialog)
    {
        var resources = dialog.Resources;
        resources["ControlContentThemeFontSize"] = DialogFontSize;
        resources["ButtonBackground"] = ButtonFace;
        resources["ButtonBackgroundPointerOver"] = ButtonHoverFace;
        resources["ButtonBackgroundPressed"] = ButtonPressedFace;
        resources["ButtonBackgroundDisabled"] = ButtonDisabledFace;
        resources["ButtonBorderBrush"] = ButtonBorder;
        resources["ButtonBorderBrushPointerOver"] = ButtonHoverBorder;
        resources["ButtonBorderBrushPressed"] = ButtonPressedBorder;
        resources["ButtonBorderBrushDisabled"] = ButtonDisabledBorder;
        resources["ButtonForegroundPointerOver"] = DialogControlStyles.SelectionForegroundBrush;
        resources["ButtonForegroundPressed"] = DialogControlStyles.SelectionForegroundBrush;
        resources["ButtonForegroundDisabled"] = ButtonDisabledInk;
    }

    /// <summary>
    /// Inserted ahead of the dialog's own window styles: they outrank the application-level
    /// fallback styles but lose to anything the dialog or a control declares itself.
    /// </summary>
    private static void InsertScopedStyles(Window dialog)
    {
        IStyle[] styles =
        [
            new Style(x => x.OfType<TextBox>())
            {
                Setters =
                {
                    new Setter(TextBox.SelectionBrushProperty, TextSelection),
                    new Setter(TextBox.SelectionForegroundBrushProperty, DialogControlStyles.SelectionForegroundBrush),
                },
            },
            // WPF greys only the glyph of a disabled check box or radio button; the shared compact
            // templates already do that, so the label keeps its ink.
            new Style(x => x.OfType<CheckBox>().Class(":disabled"))
            {
                Setters = { new Setter(Visual.OpacityProperty, 1d) },
            },
            new Style(x => x.OfType<RadioButton>().Class(":disabled"))
            {
                Setters = { new Setter(Visual.OpacityProperty, 1d) },
            },
            new Style(x => x.OfType<ListBoxItem>())
            {
                Setters = { new Setter(Layoutable.MinHeightProperty, DialogListRowMinHeight) },
            },
            // A WPF list box scrolls a row wider than the list horizontally (ScrollViewer default
            // Auto); Fluent disables horizontal scrolling, so long rows were cut off instead.
            new Style(x => x.OfType<ListBox>())
            {
                Setters =
                {
                    new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto),
                },
            },
        ];

        for (var index = 0; index < styles.Length; index++)
            dialog.Styles.Insert(index, styles[index]);
    }

    /// <summary>
    /// A group box no chrome helper touched still carries the theme's large caption and dark frame.
    /// Gives it the shared 12 px caption, caption ink and light frame, keeping any brush the dialog
    /// assigned and leaving its margin and padding (the dialog's layout) alone.
    /// </summary>
    private static void ApplyGroupBoxFrame(GroupBox groupBox)
    {
        if (HasLocalValue(groupBox, HeaderedContentControl.HeaderTemplateProperty))
            return;

        var hadMargin = HasLocalValue(groupBox, Layoutable.MarginProperty);
        var hadPadding = HasLocalValue(groupBox, TemplatedControl.PaddingProperty);
        AvaloniaCompactDialogChrome.ApplyGroupBox(
            groupBox,
            AvaloniaCompactDialogChrome.WindowsStyle,
            HasLocalValue(groupBox, TemplatedControl.BorderBrushProperty) ? groupBox.BorderBrush : null,
            HasLocalValue(groupBox, TemplatedControl.ForegroundProperty) ? groupBox.Foreground : null);
        if (!hadMargin)
            groupBox.ClearValue(Layoutable.MarginProperty);
        if (!hadPadding)
            groupBox.ClearValue(TemplatedControl.PaddingProperty);
    }

    /// <summary>
    /// Route helpers that predate the shared chrome paint push buttons with a local white (or
    /// already grey) face and their own frame colour. A captioned, framed push button with such a
    /// face gets the one WPF look; swatches, glyph buttons, flat buttons and toggles are left alone.
    /// </summary>
    private static void ApplyPushButtonFace(Button button)
    {
        if (button.GetType() != typeof(Button) || string.IsNullOrEmpty(CaptionOf(button)))
            return;

        if (!HasLocalValue(button, TemplatedControl.BackgroundProperty))
        {
            // Unchromed: the scoped resources supply the face; only the default-button frame is missing.
            if (button.IsDefault
                && !HasLocalValue(button, TemplatedControl.BorderBrushProperty)
                && !button.Classes.Contains(AvaloniaCompactDialogChrome.CompactButtonClass))
            {
                button.BorderBrush = DefaultButtonBorder;
            }

            return;
        }

        if (button.Background is not ISolidColorBrush { Opacity: 1 } face
            || (face.Color != Colors.White && face.Color != ButtonFaceColor))
        {
            return;
        }

        if (HasLocalValue(button, TemplatedControl.BorderThicknessProperty)
            && button.BorderThickness == default)
        {
            return;
        }

        button.Background = ButtonFace;
        button.BorderBrush = button.IsDefault ? DefaultButtonBorder : ButtonBorder;
        button.BorderThickness = new Thickness(1);
        button.CornerRadius = new CornerRadius(0);
    }

    /// <summary>
    /// A fixed-width button must not clip its own caption. WPF buttons keep 1 px of side padding, so
    /// first let the padding give way; only when the caption is wider than the button itself does
    /// the fixed width become a minimum. Buttons whose caption already fits are not touched.
    /// </summary>
    private static void FitCaption(Button button)
    {
        if (button.GetType() != typeof(Button))
            return;
        var caption = CaptionOf(button);
        var width = button.Width;
        if (string.IsNullOrEmpty(caption) || double.IsNaN(width) || width <= 0)
            return;

        var captionBlock = button.Content as TextBlock;
        var typeface = captionBlock is not null
            ? new Typeface(captionBlock.FontFamily, captionBlock.FontStyle, captionBlock.FontWeight)
            : new Typeface(button.FontFamily, button.FontStyle, button.FontWeight);
        var fontSize = captionBlock?.FontSize ?? button.FontSize;
        if (!(fontSize > 0))
            return;

        // The access-key marker is not drawn.
        var measured = new FormattedText(
            caption.Replace("_", string.Empty, StringComparison.Ordinal),
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black);
        var captionWidth = Math.Ceiling(measured.WidthIncludingTrailingWhitespace);
        var frame = button.BorderThickness.Left + button.BorderThickness.Right;
        var padding = button.Padding;
        if (captionWidth + frame + padding.Left + padding.Right <= width)
            return;

        var side = Math.Floor((width - frame - captionWidth) / 2);
        if (side >= MinimumCaptionSidePadding)
        {
            button.Padding = new Thickness(
                Math.Min(padding.Left, side),
                padding.Top,
                Math.Min(padding.Right, side),
                padding.Bottom);
            return;
        }

        button.Padding = new Thickness(
            Math.Min(padding.Left, GrownCaptionSidePadding),
            padding.Top,
            Math.Min(padding.Right, GrownCaptionSidePadding),
            padding.Bottom);
        if (button.MinWidth < width)
            button.MinWidth = width;
        if (button.MaxWidth < captionWidth + frame + (2 * GrownCaptionSidePadding))
            button.ClearValue(Layoutable.MaxWidthProperty);
        button.ClearValue(Layoutable.WidthProperty);
    }

    private static string? CaptionOf(Button button) => button.Content switch
    {
        string text => text,
        TextBlock textBlock => textBlock.Text,
        _ => null,
    };

    // IsSet is also true for a value a theme or style supplies; only a local assignment is the
    // dialog's own decision.
    private static bool HasLocalValue(AvaloniaObject target, AvaloniaProperty property) =>
        target.IsSet(property)
        && global::Avalonia.Diagnostics.AvaloniaObjectExtensions.GetDiagnostic(target, property)
            is { Priority: BindingPriority.LocalValue };
}
