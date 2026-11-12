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
using Avalonia.Threading;
using Avalonia.VisualTree;
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
/// WPF's classic system scroll bar: 17 px thick, a #F0F0F0 track, a square #CDCDCD thumb (#A6A6A6
/// hover, #606060 pressed) and #606060 arrow glyphs on buttons that share the track colour. Fluent
/// draws a 12 px bar with a rounded translucent thumb; these keys restyle every bar that resolves
/// them from the given scope (one worksheet bar, or a whole dialog window).
/// </summary>
internal static class ClassicScrollBarChrome
{
    public const double BarThickness = 17;

    private static readonly IBrush Track = new ImmutableSolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly IBrush Thumb = new ImmutableSolidColorBrush(Color.FromRgb(0xCD, 0xCD, 0xCD));
    private static readonly IBrush ThumbHover = new ImmutableSolidColorBrush(Color.FromRgb(0xA6, 0xA6, 0xA6));
    private static readonly IBrush ThumbPressed = new ImmutableSolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
    private static readonly IBrush Glyph = new ImmutableSolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
    private static readonly IBrush ButtonHover = new ImmutableSolidColorBrush(Color.FromRgb(0xDA, 0xDA, 0xDA));

    public static void ApplyResources(IResourceDictionary resources)
    {
        resources["ScrollBarSize"] = BarThickness;
        resources["ScrollBarBackground"] = Track;
        resources["ScrollBarBackgroundPointerOver"] = Track;
        resources["ScrollBarTrackFill"] = Track;
        resources["ScrollBarTrackFillPointerOver"] = Track;
        resources["ScrollBarTrackStroke"] = Brushes.Transparent;
        resources["ScrollBarTrackStrokePointerOver"] = Brushes.Transparent;
        resources["ScrollBarPanningThumbBackground"] = Thumb;
        resources["ScrollBarThumbFillPointerOver"] = ThumbHover;
        resources["ScrollBarThumbFillPressed"] = ThumbPressed;
        resources["ScrollBarButtonBackground"] = Track;
        resources["ScrollBarButtonBackgroundPointerOver"] = ButtonHover;
        resources["ScrollBarButtonBackgroundPressed"] = ThumbPressed;
        resources["ScrollBarButtonArrowForeground"] = Glyph;
        resources["ScrollBarButtonArrowForegroundPointerOver"] = Brushes.Black;
        resources["ScrollBarButtonArrowForegroundPressed"] = Brushes.White;
        // The corner where a vertical and a horizontal bar meet takes the track colour too.
        resources["ScrollViewerScrollBarsSeparatorBackground"] = Track;
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

    // WPF's standard push button: a 16 px caption line at the 12 px dialog font, 1 px padding and a
    // 1 px frame. Replaces the shared compact token (26) wherever a dialog's chrome style keeps it.
    private const double PushButtonHeight = 20;
    private static readonly Thickness UnchromedButtonPadding = new(8, 1, 8, 1);

    // WPF Aero2 ComboBox: a #F0F0F0 to #E5E5E5 face in a 1 px #ACACAC frame, a white editable
    // field, #ECF4FC to #DCECFC with a #7EB4EA frame under the pointer, and a #F0F0F0 face in a
    // #D9D9D9 frame with GrayText ink when disabled; 22 px at the default control height.
    private const double ComboBoxHeight = 22;
    private static readonly IBrush ComboBoxFace = VerticalGradient(0xF0, 0xF0, 0xF0, 0xE5, 0xE5, 0xE5);
    private static readonly IBrush ComboBoxFrame = new ImmutableSolidColorBrush(Color.FromRgb(0xAC, 0xAC, 0xAC));
    private static readonly IBrush ComboBoxHoverFace = VerticalGradient(0xEC, 0xF4, 0xFC, 0xDC, 0xEC, 0xFC);
    private static readonly IBrush ComboBoxHoverFrame = new ImmutableSolidColorBrush(Color.FromRgb(0x7E, 0xB4, 0xEA));
    private static readonly IBrush ComboBoxDisabledFace = new ImmutableSolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly IBrush ComboBoxDisabledFrame = new ImmutableSolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9));
    private static readonly IBrush GrayText = new ImmutableSolidColorBrush(Color.FromRgb(0x6D, 0x6D, 0x6D));

    // WPF Aero2 TabItem: unselected headers carry the combo face gradient in a #ACACAC frame, the
    // selected header is white, joined to a #ACACAC pane and raised 2 px above and beside its slot.
    private const double SelectedTabRise = 2;

    // WPF Aero2 greys a disabled text box by drawing the whole box at 0.56 opacity.
    private const double DisabledTextBoxOpacity = 0.56;

    // A default button shows the default cue only while the keyboard focus is on a control that
    // does not take Enter itself (WPF Button.IsDefaulted).
    private const string DefaultedButtonClass = "freex-dialog-defaulted";

    /// <summary>
    /// Marks the WPF default button of a dialog that routes Enter itself and so leaves
    /// <see cref="Button.IsDefault"/> unset; it gets the same focus-driven default cue.
    /// </summary>
    internal const string DefaultButtonCueClass = "freex-dialog-default-button";

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
            Height = PushButtonHeight,
        };
        AvaloniaCompactDialogChrome.DefaultTextSelectionBrush = TextSelection;
        AvaloniaCompactDialogChrome.DefaultTextSelectionForegroundBrush = DialogControlStyles.SelectionForegroundBrush;
        AvaloniaCompactDialogChrome.DefaultListRowMinHeight = DialogListRowMinHeight;
        AvaloniaCompactDialogChrome.CompactEditableComboOwnsEditor = true;
        AvaloniaCompactDialogChrome.DefaultComboBoxPalette = new AvaloniaCompactDialogComboBoxPalette(
            ComboBoxFace,
            Brushes.White,
            ComboBoxFrame,
            ComboBoxHoverFace,
            ComboBoxHoverFrame,
            ComboBoxDisabledFace,
            ComboBoxDisabledFrame,
            GrayText)
        {
            Height = ComboBoxHeight,
        };
        AvaloniaCompactDialogChrome.DefaultTabPalette = new AvaloniaCompactDialogTabPalette(
            ComboBoxFace,
            ComboBoxFrame,
            ComboBoxFrame)
        {
            SelectedTabRise = SelectedTabRise,
        };

        // Every dialog is shown owned (ShowDialog / Show with an owner), and the owner is assigned
        // after the dialog has been built, so this is the one point that sees all of them.
        Window.OwnerProperty.Changed.AddClassHandler<Window>(OnOwnerChanged);

        // Combo boxes no chrome helper touched (raw dialogs, and rows a dialog adds after it opened,
        // such as the per-level combos of a sort list) still carry Fluent's white face and large
        // chevron. Each one gets the shared compact combo once it is loaded inside a dialog.
        Control.LoadedEvent.AddClassHandler<ComboBox>((comboBox, _) => QueueDialogComboBoxChrome(comboBox));
    }

    private static IBrush VerticalGradient(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2) =>
        new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new global::Avalonia.Media.GradientStop(Color.FromRgb(r1, g1, b1), 0),
                new global::Avalonia.Media.GradientStop(Color.FromRgb(r2, g2, b2), 1),
            },
        };

    // Posted so that a dialog's own chrome pass (which runs when the window opens) goes first.
    private static void QueueDialogComboBoxChrome(ComboBox comboBox) =>
        Dispatcher.UIThread.Post(() => ApplyDialogComboBoxChrome(comboBox), DispatcherPriority.Background);

    private static void ApplyDialogComboBoxChrome(ComboBox comboBox)
    {
        if (comboBox.Classes.Contains(AvaloniaCompactDialogChrome.CompactComboBoxClass))
            return;
        if (TopLevel.GetTopLevel(comboBox) is not Window { } dialog || !dialog.Classes.Contains(DialogWindowClass))
            return;

        // Keep what the dialog decided about the combo's own layout and type; the shared chrome
        // supplies only the frame, face, template and (when the dialog fixed none) the height.
        var hadExplicitHeight = HasLocalValue(comboBox, Layoutable.HeightProperty)
            || HasLocalValue(comboBox, Layoutable.MinHeightProperty)
            || HasLocalValue(comboBox, Layoutable.MaxHeightProperty);
        var alignment = comboBox.HorizontalAlignment;
        AvaloniaProperty[] keptProperties =
        [
            TemplatedControl.PaddingProperty,
            TemplatedControl.FontSizeProperty,
            TemplatedControl.FontFamilyProperty,
        ];
        var kept = keptProperties
            .Where(property => HasLocalValue(comboBox, property))
            .Select(property => (Property: property, Value: comboBox.GetValue(property)))
            .ToArray();

        AvaloniaCompactDialogChrome.ApplyComboBox(
            comboBox,
            AvaloniaCompactDialogChrome.WindowsStyle,
            fixedHeight: !hadExplicitHeight);

        comboBox.HorizontalAlignment = alignment;
        foreach (var (property, value) in kept)
            comboBox.SetValue(property, value);
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
            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
            foreach (var button in buttons)
                FitCaption(button);

            InstallDefaultButtonCue(dialog, buttons);
        };
    }

    /// <summary>
    /// WPF draws the default button's blue frame only while it is defaulted, that is while the
    /// keyboard focus is inside the dialog on a control that does not take Enter itself (no push
    /// button, toggle or multi-line text box, the default button included). With the focus on a
    /// button, or nowhere, every button keeps the neutral frame.
    /// </summary>
    private static void InstallDefaultButtonCue(Window dialog, IReadOnlyList<Button> buttons)
    {
        var defaultButtons = buttons
            .Where(button => button.IsDefault || button.Classes.Contains(DefaultButtonCueClass))
            .ToArray();
        if (defaultButtons.Length == 0)
            return;

        foreach (var button in defaultButtons)
        {
            // Appended after every style the chrome helpers added, so these decide the resting
            // frame; hover, pressed and disabled keep their own frames.
            button.Styles.Add(new Style(x => RestingButton(x.OfType<Button>()))
            {
                Setters = { new Setter(TemplatedControl.BorderBrushProperty, ButtonBorder) },
            });
            button.Styles.Add(new Style(x => RestingButton(x.OfType<Button>().Class(DefaultedButtonClass)))
            {
                Setters = { new Setter(TemplatedControl.BorderBrushProperty, DefaultButtonBorder) },
            });
        }

        void Update()
        {
            var focused = dialog.FocusManager?.GetFocusedElement();
            var defaulted = focused is Visual focusedVisual
                && (ReferenceEquals(focusedVisual, dialog) || dialog.IsVisualAncestorOf(focusedVisual))
                && focused is not Button
                && focused is not TextBox { AcceptsReturn: true };
            foreach (var button in defaultButtons)
                button.Classes.Set(DefaultedButtonClass, defaulted && button.IsEffectivelyEnabled);
        }

        dialog.AddHandler(global::Avalonia.Input.InputElement.GotFocusEvent, (_, _) => Update(), handledEventsToo: true);
        dialog.AddHandler(global::Avalonia.Input.InputElement.LostFocusEvent, (_, _) => Update(), handledEventsToo: true);
        Update();
        // Dialogs move the initial focus from their own Opened handlers or a posted callback.
        Dispatcher.UIThread.Post(Update, DispatcherPriority.Input);
    }

    private static Selector RestingButton(Selector selector) =>
        selector
            .Not(x => x.Class(":pointerover"))
            .Not(x => x.Class(":pressed"))
            .Not(x => x.Class(":disabled"));

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
        // Fluent's 8,5,8,6 button padding makes an unchromed 12 px push button 29-30 px tall.
        resources["ButtonPadding"] = UnchromedButtonPadding;
        // The disabled-text-box opacity (InsertScopedStyles) does the greying, as in WPF, so the
        // Fluent disabled ink and frame stay those of an enabled box underneath it.
        resources["TextControlForegroundDisabled"] = DialogControlStyles.SelectionForegroundBrush;
        resources["TextControlBorderBrushDisabled"] = new ImmutableSolidColorBrush(Color.FromRgb(0xAB, 0xAD, 0xB3));
        ClassicScrollBarChrome.ApplyResources(resources);
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
            // WPF scroll bars are classic, always-expanded bars beside the content, shown only
            // while the content overflows. Fluent's auto-hiding bars collapse to a 2 px line laid
            // over the content (over group-box frames, or as a stray line under a list) and only
            // expand under the pointer; with auto-hide off they stay expanded and the scroll
            // viewer reserves their lane. Templated scroll viewers (lists, multi-line text boxes)
            // take the value from their owner, so the owners carry it. A single-line text box keeps
            // the auto-hiding bar: a box shorter than its text line would otherwise show a full bar.
            new Style(x => x.OfType<ScrollViewer>().Not(y => y.Name("PART_ScrollViewer")))
            {
                Setters = { new Setter(ScrollViewer.AllowAutoHideProperty, false) },
            },
            new Style(x => x.Is<ItemsControl>())
            {
                Setters = { new Setter(ScrollViewer.AllowAutoHideProperty, false) },
            },
            new Style(x => x.Is<ItemsControl>().Template().OfType<ScrollViewer>())
            {
                Setters = { new Setter(ScrollViewer.AllowAutoHideProperty, false) },
            },
            new Style(x => x.Is<TextBox>().PropertyEquals(TextBox.AcceptsReturnProperty, true))
            {
                Setters = { new Setter(ScrollViewer.AllowAutoHideProperty, false) },
            },
            new Style(x => x.Is<TextBox>()
                .PropertyEquals(TextBox.AcceptsReturnProperty, true)
                .Template()
                .OfType<ScrollViewer>())
            {
                Setters = { new Setter(ScrollViewer.AllowAutoHideProperty, false) },
            },
            // WPF greys a disabled text box (ink and frame) by drawing it at 0.56 opacity. The
            // editor inside an editable combo follows the combo's own disabled face instead.
            new Style(x => x.Is<TextBox>()
                .Class(":disabled")
                .Not(y => y.Name("PART_EditableTextBox")))
            {
                Setters = { new Setter(Visual.OpacityProperty, DisabledTextBoxOpacity) },
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

        // Unchromed: the scoped resources supply the face and frame, and the default-button frame
        // follows the keyboard focus (InstallDefaultButtonCue).
        if (!HasLocalValue(button, TemplatedControl.BackgroundProperty))
            return;

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
        // The scoped ButtonBorderBrush resource supplies the neutral frame; a local frame here would
        // outrank the focus-driven default cue.
        button.ClearValue(TemplatedControl.BorderBrushProperty);
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
