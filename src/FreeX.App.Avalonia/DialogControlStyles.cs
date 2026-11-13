using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
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

    private static readonly IBrush TrackFill = new ImmutableSolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly IBrush ThumbFill = new ImmutableSolidColorBrush(Color.FromRgb(0xCD, 0xCD, 0xCD));
    private static readonly IBrush ThumbHoverFill = new ImmutableSolidColorBrush(Color.FromRgb(0xA6, 0xA6, 0xA6));
    private static readonly IBrush ThumbPressedFill = new ImmutableSolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
    private static readonly IBrush GlyphInk = new ImmutableSolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
    private static readonly IBrush ButtonHoverFill = new ImmutableSolidColorBrush(Color.FromRgb(0xDA, 0xDA, 0xDA));

    public static void ApplyResources(IResourceDictionary resources)
    {
        resources["ScrollBarSize"] = BarThickness;
        resources["ScrollBarBackground"] = TrackFill;
        resources["ScrollBarBackgroundPointerOver"] = TrackFill;
        resources["ScrollBarTrackFill"] = TrackFill;
        resources["ScrollBarTrackFillPointerOver"] = TrackFill;
        resources["ScrollBarTrackStroke"] = Brushes.Transparent;
        resources["ScrollBarTrackStrokePointerOver"] = Brushes.Transparent;
        resources["ScrollBarPanningThumbBackground"] = ThumbFill;
        resources["ScrollBarThumbFillPointerOver"] = ThumbHoverFill;
        resources["ScrollBarThumbFillPressed"] = ThumbPressedFill;
        resources["ScrollBarButtonBackground"] = TrackFill;
        resources["ScrollBarButtonBackgroundPointerOver"] = ButtonHoverFill;
        resources["ScrollBarButtonBackgroundPressed"] = ThumbPressedFill;
        resources["ScrollBarButtonArrowForeground"] = GlyphInk;
        resources["ScrollBarButtonArrowForegroundPointerOver"] = Brushes.Black;
        resources["ScrollBarButtonArrowForegroundPressed"] = Brushes.White;
        // The corner where a vertical and a horizontal bar meet takes the track colour too.
        resources["ScrollViewerScrollBarsSeparatorBackground"] = TrackFill;
    }

    // The WPF disabled arrow glyph.
    private static readonly IBrush DisabledGlyph = new ImmutableSolidColorBrush(Color.FromRgb(0xBF, 0xBF, 0xBF));
    // The thumb leaves one track pixel on each side, as the WPF thumb does (15 px of a 17 px bar).
    private const double ThumbInset = 1;
    private const double MinimumThumbLength = 8;

    // Marks the parts of the classic template. A style whose selector carries a class is a
    // triggered style, and a triggered style outranks every untriggered one, including Fluent's
    // own triggered theme rules for the same parts (":vertical", "[IsExpanded=true]").
    private const string ClassicPartClass = "freex-classic-scrollbar-part";

    // The Aero2 arrow glyphs: 7 x 6 chevrons (6 x 7 on a horizontal bar), drawn solid.
    private const string UpGlyph = "M 0,4 L 0,6 L 3.5,2.5 L 7,6 L 7,4 L 3.5,0.5 Z";
    private const string DownGlyph = "M 0,2.5 L 0,0.5 L 3.5,4 L 7,0.5 L 7,2.5 L 3.5,6 Z";
    private const string LeftGlyph = "M 3.18,7 L 5,7 L 1.81,3.5 L 5,0 L 3.18,0 L 0,3.5 Z";
    private const string RightGlyph = "M 1.81,7 L 0,7 L 3.18,3.5 L 0,0 L 1.81,0 L 5,3.5 Z";

    /// <summary>
    /// Restyles every scroll bar of a dialog with the classic template. Fluent's bar is a
    /// collapsed 2 px line that animates to full width (and fades its arrows in) only while it is
    /// expanded, so a capture or a bar whose owner turned auto-hide off late showed a 2–6 px thumb
    /// without arrows; the classic bar has no collapsed state and no animation.
    /// <para>
    /// Fluent sets the bar's template from its ":vertical" and ":horizontal" theme rules. Those are
    /// triggered rules, which outrank an untriggered style whatever its scope, so a plain ScrollBar
    /// selector lost to them on every bar a list or text box template creates. These styles carry
    /// the same pseudo-classes and so win as triggered, non-theme styles.
    /// </para>
    /// </summary>
    public static IEnumerable<IStyle> CreateDialogScrollBarStyles()
    {
        yield return new Style(x => x.OfType<ScrollBar>().Class(":vertical"))
        {
            Setters =
            {
                new Setter(TemplatedControl.TemplateProperty, ClassicVerticalTemplate),
                new Setter(TemplatedControl.BackgroundProperty, TrackFill),
            },
        };
        yield return new Style(x => x.OfType<ScrollBar>().Class(":horizontal"))
        {
            Setters =
            {
                new Setter(TemplatedControl.TemplateProperty, ClassicHorizontalTemplate),
                new Setter(TemplatedControl.BackgroundProperty, TrackFill),
            },
        };
    }

    private static readonly FuncControlTemplate<ScrollBar> ClassicVerticalTemplate =
        new((scrollBar, nameScope) => BuildClassicBar(scrollBar, nameScope, vertical: true));

    private static readonly FuncControlTemplate<ScrollBar> ClassicHorizontalTemplate =
        new((scrollBar, nameScope) => BuildClassicBar(scrollBar, nameScope, vertical: false));

    /// <summary>
    /// The WPF classic bar: a 17 px track, square arrow buttons at both ends and a full-width
    /// thumb, always drawn at full size. The parts keep the names <see cref="ScrollBar"/> looks
    /// up, and their look is set on the parts themselves so the platform theme's collapsed-state
    /// rules for the same names cannot shrink or hide them.
    /// </summary>
    private static Control BuildClassicBar(ScrollBar scrollBar, INameScope nameScope, bool vertical)
    {
        var lineUp = CreateLineButton("PART_LineUpButton", vertical ? UpGlyph : LeftGlyph);
        var lineDown = CreateLineButton("PART_LineDownButton", vertical ? DownGlyph : RightGlyph);
        var pageUp = CreatePageButton("PART_PageUpButton");
        var pageDown = CreatePageButton("PART_PageDownButton");

        var thumb = new Thumb
        {
            Template = new FuncControlTemplate<Thumb>((owner, _) =>
            {
                var face = new Border();
                face.Bind(Border.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = owner });
                return face;
            }),
            Margin = vertical ? new Thickness(ThumbInset, 0) : new Thickness(0, ThumbInset),
            Width = double.NaN,
            Height = double.NaN,
            MinWidth = vertical ? 0 : MinimumThumbLength,
            MinHeight = vertical ? MinimumThumbLength : 0,
            // The thumb fills the track across the bar; Fluent pins its own thumb to one edge.
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Opacity = 1,
            RenderTransform = null,
            Transitions = null,
        };
        thumb.Classes.Add(ClassicPartClass);
        thumb.Styles.Add(new Style(x => x.OfType<Thumb>().Class(ClassicPartClass))
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, ThumbFill) },
        });
        thumb.Styles.Add(new Style(x => x.OfType<Thumb>().Class(ClassicPartClass).Class(":pointerover"))
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, ThumbHoverFill) },
        });
        thumb.Styles.Add(new Style(x => x.OfType<Thumb>().Class(ClassicPartClass).Class(":pressed"))
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, ThumbPressedFill) },
        });

        var track = new Track
        {
            Name = "PART_Track",
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            // A vertical track runs bottom-up unless reversed; the bar's minimum is at the top.
            IsDirectionReversed = vertical,
            Thumb = thumb,
            DecreaseButton = pageUp,
            IncreaseButton = pageDown,
        };
        track.Bind(Track.MinimumProperty, new Binding(nameof(RangeBase.Minimum)) { Source = scrollBar });
        track.Bind(Track.MaximumProperty, new Binding(nameof(RangeBase.Maximum)) { Source = scrollBar });
        track.Bind(Track.ValueProperty, new Binding(nameof(RangeBase.Value)) { Source = scrollBar, Mode = BindingMode.TwoWay });
        track.Bind(Track.ViewportSizeProperty, new Binding(nameof(ScrollBar.ViewportSize)) { Source = scrollBar });

        nameScope.Register("PART_LineUpButton", lineUp);
        nameScope.Register("PART_LineDownButton", lineDown);
        nameScope.Register("PART_PageUpButton", pageUp);
        nameScope.Register("PART_PageDownButton", pageDown);
        nameScope.Register("PART_Track", track);

        var root = new Grid { Opacity = 1 };
        root.Bind(Panel.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = scrollBar });
        if (vertical)
        {
            root.Width = BarThickness;
            root.RowDefinitions = new RowDefinitions($"{BarThickness},*,{BarThickness}");
            Grid.SetRow(lineUp, 0);
            Grid.SetRow(track, 1);
            Grid.SetRow(lineDown, 2);
        }
        else
        {
            root.Height = BarThickness;
            root.ColumnDefinitions = new ColumnDefinitions($"{BarThickness},*,{BarThickness}");
            Grid.SetColumn(lineUp, 0);
            Grid.SetColumn(track, 1);
            Grid.SetColumn(lineDown, 2);
        }

        root.Children.Add(lineUp);
        root.Children.Add(track);
        root.Children.Add(lineDown);
        return root;
    }

    private static RepeatButton CreateLineButton(string name, string glyphData)
    {
        var button = new RepeatButton
        {
            Name = name,
            Focusable = false,
            Width = BarThickness,
            Height = BarThickness,
            MinWidth = 0,
            MinHeight = 0,
            Padding = default,
            Opacity = 1,
            IsVisible = true,
            Transitions = null,
            Template = new FuncControlTemplate<RepeatButton>((owner, _) =>
            {
                var glyph = new global::Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse(glyphData),
                    Stretch = Stretch.None,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                glyph.Bind(global::Avalonia.Controls.Shapes.Shape.FillProperty, new Binding(nameof(TemplatedControl.Foreground)) { Source = owner });
                var face = new Border { Child = glyph };
                face.Bind(Border.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = owner });
                return face;
            }),
        };
        // Triggered styles (see ClassicPartClass), so a theme rule for the bar's buttons cannot fade
        // the glyph; the state styles follow the resting one and win over it.
        button.Classes.Add(ClassicPartClass);
        button.Styles.Add(new Style(x => x.OfType<RepeatButton>().Class(ClassicPartClass))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, TrackFill),
                new Setter(TemplatedControl.ForegroundProperty, GlyphInk),
            },
        });
        button.Styles.Add(new Style(x => x.OfType<RepeatButton>().Class(ClassicPartClass).Class(":pointerover"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, ButtonHoverFill),
                new Setter(TemplatedControl.ForegroundProperty, Brushes.Black),
            },
        });
        button.Styles.Add(new Style(x => x.OfType<RepeatButton>().Class(ClassicPartClass).Class(":pressed"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, ThumbPressedFill),
                new Setter(TemplatedControl.ForegroundProperty, Brushes.White),
            },
        });
        button.Styles.Add(new Style(x => x.OfType<RepeatButton>().Class(ClassicPartClass).Class(":disabled"))
        {
            Setters = { new Setter(TemplatedControl.ForegroundProperty, DisabledGlyph) },
        });
        return button;
    }

    // The track either side of the thumb: transparent (the bar's track shows through) but hit-testable.
    private static RepeatButton CreatePageButton(string name) => new()
    {
        Name = name,
        Focusable = false,
        MinWidth = 0,
        MinHeight = 0,
        Opacity = 1,
        Transitions = null,
        Background = Brushes.Transparent,
        Template = new FuncControlTemplate<RepeatButton>((owner, _) =>
        {
            var face = new Border();
            face.Bind(Border.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = owner });
            return face;
        }),
    };
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
    // A text line is 4/3 of the font size: the 16 px line of the 12 px dialog font.
    private const double DialogTextLineHeightRatio = 4.0 / 3.0;
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

    // WPF list selection: a light blue row in a blue frame while the list has the keyboard focus,
    // a grey row otherwise.
    // Internal so hand-drawn lists (the Error Checking issue grid) share the same selection colours.
    internal static readonly IBrush SelectedRowFace = new ImmutableSolidColorBrush(Color.FromRgb(0xCC, 0xE8, 0xFF));
    internal static readonly IBrush SelectedRowFrame = new ImmutableSolidColorBrush(Color.FromRgb(0x99, 0xD1, 0xFF));
    internal static readonly IBrush InactiveRowFace = new ImmutableSolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9));
    internal static readonly IBrush InactiveRowFrame = InactiveRowFace;

    // WPF Aero2 GroupBox frame (light blue-grey) and the focused text box frame (1 px light blue).
    private static readonly Color SharedGroupBoxFrameColor = Color.Parse(global::Free.Shared.Shell.CompactDialogVisualTokens.BorderHex);
    private static readonly IBrush GroupBoxFrame = new ImmutableSolidColorBrush(Color.FromRgb(0xD5, 0xDF, 0xE5));
    private static readonly IBrush FocusedTextBoxFrame = new ImmutableSolidColorBrush(
        Color.Parse(global::Free.Shared.Shell.CompactDialogVisualTokens.FocusedInputBorderHex));
    private static readonly IBrush UnchromedTextBoxFrame = new ImmutableSolidColorBrush(
        Color.Parse(global::Free.Shared.Shell.CompactDialogVisualTokens.FieldBorderHex));

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

    // WPF dialogs whose XAML gives every push button an explicit height (OptionsDialog.xaml: 26).
    private static readonly Dictionary<string, double> ExplicitWpfButtonHeights = new(StringComparer.Ordinal)
    {
        ["OptionsDialog"] = global::Free.Shared.Shell.CompactDialogVisualTokens.ButtonHeight,
    };

    // Format Cells sets 24 px OK/Cancel and 28 px border presets in WPF; its route-helper 26 px
    // buttons are the nearer match and its layout was measured with them.
    private static readonly HashSet<string> KeptRouteButtonHeightDialogs = new(StringComparer.Ordinal)
    {
        "FormatCellsCompactDialog",
    };

    // The Go To dialog style fixes its buttons at 24 px; the WPF Go To buttons are default buttons.
    private const double GoToDialogButtonHeight = 24;
    private static readonly HashSet<string> GoToDialogButtonIds = new(StringComparer.Ordinal)
    {
        "GoToSpecialButton",
        "GoToReferenceBoxAcceptButton",
        "GoToReferenceBoxCancelButton",
    };

    // Dialogs whose WPF push buttons are default WPF buttons (no Height in the WPF dialog: ZoomDialog,
    // SubtotalDialog, ShapeGradientDialog, ManageConditionalFormatsDialog, ConditionalFormatDialog)
    // while their Avalonia route helper fixes them at 21 or 22 px. Their captioned push buttons take
    // the WPF default; the deliberate 24 and 26 px heights are outside the 21-22 band.
    private static readonly HashSet<string> WpfDefaultPushButtonDialogs = new(StringComparer.Ordinal)
    {
        "ZoomDialog",
        "SubtotalCompactDialog",
        "ShapeGradientDialog",
        "ManageConditionalFormatsDialog",
        "ConditionalFormatRuleDialog",
    };

    // Buttons whose WPF counterpart is taller than a default push button because the WPF layout
    // stretches it to its row (no explicit Height in the WPF dialog): the Find and Replace format
    // buttons on the find/replace field rows (FindReplaceDialog.xaml), the Sort options
    // button on the level-command row (SortDialog.cs), and the Sparkline range pickers beside their
    // 24 px range boxes (SparklineDialog.cs). Keyed by automation id; they keep this height.
    private static readonly Dictionary<string, double> RowStretchedWpfButtonHeights = new(StringComparer.Ordinal)
    {
        ["FindReplaceFindFormatButton"] = global::Free.Shared.Shell.CompactDialogVisualTokens.ButtonHeight,
        ["FindReplaceFindClearFormatButton"] = global::Free.Shared.Shell.CompactDialogVisualTokens.ButtonHeight,
        ["FindReplaceFindChooseFormatFromCellButton"] = global::Free.Shared.Shell.CompactDialogVisualTokens.ButtonHeight,
        ["FindReplaceReplaceFindFormatButton"] = global::Free.Shared.Shell.CompactDialogVisualTokens.ButtonHeight,
        ["FindReplaceReplaceFindClearFormatButton"] = global::Free.Shared.Shell.CompactDialogVisualTokens.ButtonHeight,
        ["FindReplaceReplaceFindChooseFormatFromCellButton"] = global::Free.Shared.Shell.CompactDialogVisualTokens.ButtonHeight,
        ["FindReplaceReplaceWithFormatButton"] = global::Free.Shared.Shell.CompactDialogVisualTokens.ButtonHeight,
        ["FindReplaceReplaceWithClearFormatButton"] = global::Free.Shared.Shell.CompactDialogVisualTokens.ButtonHeight,
        ["FindReplaceReplaceWithChooseFormatFromCellButton"] = global::Free.Shared.Shell.CompactDialogVisualTokens.ButtonHeight,
        ["SortOptionsButton"] = global::Free.Shared.Shell.CompactDialogVisualTokens.ButtonHeight,
        ["SparklineSelectDataRangeButton"] = 24,
        ["SparklineSelectLocationRangeButton"] = 24,
    };

    // Range-picker and glyph buttons size themselves to the field beside them.
    private const double MinimumNormalizedButtonWidth = 40;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, Action> DefaultButtonCueRefreshers = new();

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
        AvaloniaCompactDialogChrome.DefaultFocusedInputBorderBrush = FocusedTextBoxFrame;
        AvaloniaCompactDialogChrome.DefaultInactiveSelectedItemBackgroundBrush = InactiveRowFace;
        AvaloniaCompactDialogChrome.DefaultInactiveSelectedItemBorderBrush = InactiveRowFrame;
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

        // Buttons a dialog builds after it got its owner (pages built on demand) and default
        // buttons chosen after it opened (wizard steps) get the same height and default cue.
        Control.LoadedEvent.AddClassHandler<Button>((button, _) => NormalizeLoadedPushButtonHeight(button));
        Button.IsDefaultProperty.Changed.AddClassHandler<Button>((button, _) => OnIsDefaultChanged(button));

        // Compact single-line text boxes keep room for one text line however a helper padded them,
        // also when a helper re-pads or re-sizes a box after it was loaded.
        Control.LoadedEvent.AddClassHandler<TextBox>((textBox, _) => FitDialogTextBoxLine(textBox));
        TemplatedControl.PaddingProperty.Changed.AddClassHandler<TextBox>((textBox, _) => FitDialogTextBoxLine(textBox));
        Layoutable.HeightProperty.Changed.AddClassHandler<TextBox>((textBox, _) => FitDialogTextBoxLine(textBox));
    }

    /// <summary>
    /// WPF fits one 12 px text line (16 px) in an 18 px single-line text box: a 1 px frame and no
    /// vertical padding to spare. The shared compact chrome pads every box by 3 px top and bottom,
    /// which left an 18 px box a 10 px viewport (and a 20 or 22 px box 12 or 14), so the line was
    /// cut at the bottom. A fixed-height single-line box in a dialog gives up only as much of its
    /// vertical padding as the line needs; the line stays centred, as the chrome aligns it.
    /// </summary>
    private static void FitDialogTextBoxLine(TextBox textBox)
    {
        if (textBox.AcceptsReturn || textBox.TextWrapping != TextWrapping.NoWrap)
            return;
        var height = textBox.Height;
        if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
            return;
        if (TopLevel.GetTopLevel(textBox) is not Window { } dialog || !dialog.Classes.Contains(DialogWindowClass))
            return;

        var frame = textBox.BorderThickness;
        var padding = textBox.Padding;
        var lineHeight = Math.Ceiling(textBox.FontSize * DialogTextLineHeightRatio);
        var frameRoom = height - frame.Top - frame.Bottom;
        if (frameRoom - padding.Top - padding.Bottom >= lineHeight)
            return;

        var spare = Math.Max(0, frameRoom - lineHeight);
        var top = Math.Min(padding.Top, Math.Floor(spare / 2));
        var bottom = Math.Min(padding.Bottom, spare - top);
        textBox.Padding = new Thickness(padding.Left, top, padding.Right, bottom);
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
                    NormalizePushButtonHeight(dialog, button);
                    break;
            }
        }

        InstallWpfTabContentFocusTransfer(dialog);

        // Caption fitting needs the resolved font, so it waits for the opened window. This handler
        // is attached after the dialog's own, so it also sees the shared descendant chrome pass.
        dialog.Opened += (_, _) =>
        {
            foreach (var button in dialog.GetLogicalDescendants().OfType<Button>().ToArray())
                FitCaption(button);

            ApplyWpfGroupBoxFrames(dialog);
            InstallDefaultButtonCue(dialog);
        };
    }

    /// <summary>
    /// Brings a push button to the height its WPF counterpart has. WPF dialog buttons are default
    /// WPF buttons (one caption line, <see cref="PushButtonHeight"/>) unless the dialog's XAML
    /// gives them a height:
    /// <list type="bullet">
    ///   <item>buttons the WPF layout stretches to their row take that row height
    ///   (<see cref="RowStretchedWpfButtonHeights"/>), whatever helper chromed them;</item>
    ///   <item>route-helper buttons (a fixed height, minimum and maximum all at the shared 26 px
    ///   token, without the shared compact chrome that already applies the product height), and
    ///   the Go To buttons whose dialog style fixes 24 px, take the WPF default;</item>
    ///   <item>the 21 and 22 px buttons of <see cref="WpfDefaultPushButtonDialogs"/> take the WPF
    ///   default too;</item>
    ///   <item>an unchromed push button without a height of its own is at least the WPF default;</item>
    ///   <item>dialogs whose WPF buttons carry an explicit height get it back where the product
    ///   height replaced the shared token (<see cref="ExplicitWpfButtonHeights"/>);</item>
    ///   <item>dialogs listed in <see cref="KeptRouteButtonHeightDialogs"/>, narrow picker and
    ///   glyph buttons, and buttons without a text caption keep what they have.</item>
    /// </list>
    /// Runs when the dialog gets its owner and again for buttons loaded later; it is idempotent.
    /// </summary>
    private static void NormalizePushButtonHeight(Window dialog, Button button)
    {
        if (button.GetType() != typeof(Button) || string.IsNullOrEmpty(CaptionOf(button)))
            return;
        var dialogId = AutomationProperties.GetAutomationId(dialog) ?? string.Empty;
        var height = button.Height;
        var inWpfDefaultBand = HasLocalValue(button, Layoutable.HeightProperty)
            && height > PushButtonHeight
            && height < GoToDialogButtonHeight
            && WpfDefaultPushButtonDialogs.Contains(dialogId);
        // Narrow picker and glyph buttons size themselves to the field beside them. A glyph button
        // in a row of push buttons is the exception in those dialogs: WPF stretches it to the row.
        if (!double.IsNaN(button.Width)
            && button.Width < MinimumNormalizedButtonWidth
            && !(inWpfDefaultBand && IsInPushButtonRow(button)))
        {
            return;
        }

        var hasSharedChrome = button.Classes.Contains(AvaloniaCompactDialogChrome.CompactButtonClass);
        if (!HasLocalValue(button, Layoutable.HeightProperty))
        {
            // An unchromed push button sizes to its caption line, which the dialog font sets a
            // pixel short of WPF's 16 px line (19 px buttons); a WPF push button is 20 px.
            if (!hasSharedChrome
                && !HasLocalValue(button, Layoutable.MinHeightProperty)
                && !HasLocalValue(button, Layoutable.MaxHeightProperty)
                && IsFramedPushButton(button))
            {
                button.MinHeight = PushButtonHeight;
            }
            return;
        }

        double target;
        if (RowStretchedWpfButtonHeights.TryGetValue(
                AutomationProperties.GetAutomationId(button) ?? string.Empty,
                out var rowHeight))
        {
            if (height == rowHeight)
                return;
            target = rowHeight;
        }
        else if (ExplicitWpfButtonHeights.TryGetValue(dialogId, out var wpfHeight))
        {
            if (!hasSharedChrome || height != PushButtonHeight)
                return;
            target = wpfHeight;
        }
        else if (KeptRouteButtonHeightDialogs.Contains(dialogId))
        {
            return;
        }
        else if (height == global::Free.Shared.Shell.CompactDialogVisualTokens.ButtonHeight
            && !hasSharedChrome
            && button.MinHeight == height
            && button.MaxHeight == height)
        {
            target = PushButtonHeight;
        }
        else if (height == GoToDialogButtonHeight
            && GoToDialogButtonIds.Contains(AutomationProperties.GetAutomationId(button) ?? string.Empty))
        {
            target = PushButtonHeight;
        }
        else if (inWpfDefaultBand)
        {
            target = PushButtonHeight;
        }
        else
        {
            return;
        }

        button.Height = target;
        button.MinHeight = target;
        button.MaxHeight = target;
        if (target < height)
        {
            // One caption line plus the 1 px WPF padding has to fit the shorter frame.
            var padding = button.Padding;
            button.Padding = new Thickness(
                padding.Left,
                Math.Min(padding.Top, 1),
                padding.Right,
                Math.Min(padding.Bottom, 1));
        }
    }

    private static void NormalizeLoadedPushButtonHeight(Button button)
    {
        if (TopLevel.GetTopLevel(button) is Window { } dialog && dialog.Classes.Contains(DialogWindowClass))
            NormalizePushButtonHeight(dialog, button);
    }

    /// <summary>
    /// WPF draws the default button's blue frame while it is defaulted (Button.IsDefaulted): the
    /// dialog's keyboard focus is on the window itself or on a control that does not take Enter
    /// (KeyboardNavigation.AcceptsReturn). Every push button takes Enter, the default button
    /// itself included: a focused default button is not defaulted and draws no cue (the WPF
    /// dialogs that focus their default button on open show none). A check box, radio button or
    /// toggle does not take Enter, so the cue stays while it has the focus. A dialog whose focus
    /// is nowhere (not activated yet, or captured unfocused) keeps the cue as well, as the WPF
    /// window then holds its own focus. The default button is found again on every update, so a
    /// button made default after the dialog opened (wizard steps, rows added later) gets the cue
    /// too.
    /// </summary>
    private static void InstallDefaultButtonCue(Window dialog)
    {
        var cued = new HashSet<Button>();

        void Update()
        {
            var focused = dialog.FocusManager?.GetFocusedElement();
            var focusTakesEnter = focused is Visual focusedVisual
                && !ReferenceEquals(focusedVisual, dialog)
                && dialog.IsVisualAncestorOf(focusedVisual)
                && TakesEnter(focused);
            foreach (var button in dialog.GetLogicalDescendants().OfType<Button>())
            {
                var isDefault = button.IsDefault || button.Classes.Contains(DefaultButtonCueClass);
                if (!isDefault && !cued.Contains(button))
                    continue;
                if (cued.Add(button))
                    AddDefaultButtonCueStyles(button);
                // The resting-frame styles skip a disabled button, so a disabled default button
                // keeps the disabled frame whatever this class says.
                button.Classes.Set(DefaultedButtonClass, isDefault && !focusTakesEnter);
            }
        }

        DefaultButtonCueRefreshers.AddOrUpdate(dialog, Update);
        dialog.AddHandler(global::Avalonia.Input.InputElement.GotFocusEvent, (_, _) => Update(), handledEventsToo: true);
        dialog.AddHandler(global::Avalonia.Input.InputElement.LostFocusEvent, (_, _) => Update(), handledEventsToo: true);
        Update();
        // Dialogs move the initial focus from their own Opened handlers or a posted callback.
        Dispatcher.UIThread.Post(Update, DispatcherPriority.Input);
    }

    private static void AddDefaultButtonCueStyles(Button button)
    {
        // Appended after every style the chrome helpers added, so these decide the resting frame;
        // hover, pressed and disabled keep their own frames.
        button.Styles.Add(new Style(x => RestingButton(x.OfType<Button>()))
        {
            Setters = { new Setter(TemplatedControl.BorderBrushProperty, ButtonBorder) },
        });
        button.Styles.Add(new Style(x => RestingButton(x.OfType<Button>().Class(DefaultedButtonClass)))
        {
            Setters = { new Setter(TemplatedControl.BorderBrushProperty, DefaultButtonBorder) },
        });
    }

    /// <summary>
    /// WPF moves the keyboard focus into the newly selected tab when a tab control changes tab while
    /// the focus is inside the content of the tab it leaves (TabItem.SetFocus with its
    /// focus-on-content flag, a MoveFocus to the first stop of the new content); a focused tab
    /// header keeps the focus. Avalonia drops the focus with the content it detaches, so the dialog
    /// was left unfocused, and kept the default cue, where WPF lands on the first control of the new
    /// tab (a push button there takes Enter and clears the cue).
    /// </summary>
    private static void InstallWpfTabContentFocusTransfer(Window dialog)
    {
        // A dialog may take its initial focus before it is given its owner (and so before this
        // runs), so the focus it already holds is where the tracking starts.
        global::Avalonia.Input.IInputElement? lastFocused = dialog.FocusManager?.GetFocusedElement();
        dialog.AddHandler(
            global::Avalonia.Input.InputElement.GotFocusEvent,
            (_, _) => lastFocused = dialog.FocusManager?.GetFocusedElement() ?? lastFocused,
            handledEventsToo: true);
        dialog.AddHandler(
            SelectingItemsControl.SelectionChangedEvent,
            (_, args) =>
            {
                if (args.Source is not TabControl tabControl)
                    return;

                // The detached old content may still be reported as focused; the last focused
                // element then says where the focus was.
                var focused = dialog.FocusManager?.GetFocusedElement();
                var origin = focused is Visual focusedVisual && dialog.IsVisualAncestorOf(focusedVisual)
                    ? focused
                    : lastFocused;
                if (!IsInTabContent(origin, tabControl))
                    return;

                // After the new content has been laid out; a dialog that focuses its own target
                // on a tab change keeps it.
                Dispatcher.UIThread.Post(
                    () => FocusFirstStopOfSelectedTab(dialog, tabControl),
                    DispatcherPriority.Background);
            },
            handledEventsToo: true);
    }

    // True when the element sits in a tab's content rather than on its header.
    private static bool IsInTabContent(object? element, TabControl tabControl)
    {
        ILogical? previous = null;
        for (var node = element as ILogical; node is not null; node = node.LogicalParent)
        {
            if (node is TabItem tabItem && ReferenceEquals(tabItem.Parent, tabControl))
                return previous is not null && ReferenceEquals(previous, tabItem.Content);
            previous = node;
        }

        return false;
    }

    private static void FocusFirstStopOfSelectedTab(Window dialog, TabControl tabControl)
    {
        if (!dialog.IsVisible || tabControl.SelectedItem is not TabItem { Content: Control content })
            return;
        if (dialog.FocusManager?.GetFocusedElement() is Visual current
            && (ReferenceEquals(current, content) || content.IsVisualAncestorOf(current)))
        {
            return;
        }

        content.GetVisualDescendants()
            .OfType<Control>()
            .Prepend(content)
            .FirstOrDefault(IsAuthoredTabStop)
            ?.Focus();
    }

    // A focusable, visible, enabled tab stop the dialog built itself: the spinner and drop-down
    // buttons inside a control template are not stops of their own.
    private static bool IsAuthoredTabStop(Control control) =>
        control.Focusable
        && control.IsEffectivelyVisible
        && control.IsEffectivelyEnabled
        && global::Avalonia.Input.KeyboardNavigation.GetIsTabStop(control)
        && !(control is Button && control.TemplatedParent is not null);

    // A read-only document box (About, licence text) lets Enter through to the default button.
    private static bool TakesEnter(object focused) =>
        focused is Button and not ToggleButton
        || focused is TextBox { AcceptsReturn: true, IsReadOnly: false };

    private static void OnIsDefaultChanged(Button button)
    {
        if (TopLevel.GetTopLevel(button) is Window { } dialog
            && DefaultButtonCueRefreshers.TryGetValue(dialog, out var refresh))
        {
            refresh();
        }
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
        // A text box nothing chromed draws Fluent's 2 px accent frame on focus; WPF draws 1 px of
        // light blue.
        resources["TextControlBorderBrushFocused"] = FocusedTextBoxFrame;
        // A text box nothing chromed rests in Fluent's BaseMedium frame (#666666) and darkens under
        // the pointer; WPF frames it in the light field grey (the shared #B7BCC2 field border) and
        // in light blue under the pointer.
        resources["TextControlBorderBrush"] = UnchromedTextBoxFrame;
        resources["TextControlBorderBrushPointerOver"] = ComboBoxHoverFrame;
        resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        // Fluent's list items paint the selection from these keys (a translucent accent).
        resources["ListBoxItemBackgroundSelected"] = SelectedRowFace;
        resources["ListBoxItemBackgroundSelectedPointerOver"] = SelectedRowFace;
        resources["ListBoxItemBackgroundSelectedPressed"] = SelectedRowFace;
        resources["ListBoxItemForegroundSelected"] = DialogControlStyles.SelectionForegroundBrush;
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
            // One WPF selection highlight for lists on Fluent's item template, which paints the
            // selection on its content presenter from a translucent accent (#99C9EF on white).
            // Lists on the shared item template get the same colours from the shared list chrome
            // (the inactive pair through the product defaults set in Install).
            SelectedListRowPresenterStyle(x => x, SelectedRowFace, SelectedRowFrame),
            SelectedListRowPresenterStyle(InactiveList, InactiveRowFace, InactiveRowFrame),
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
            // while the content overflows. Every bar gets the classic template (no collapsed
            // state, no animation); with auto-hide off the scroll viewer also reserves the bar's
            // lane instead of laying the bar over the content. Templated scroll viewers (lists,
            // multi-line text boxes) take the value from their owner, so the owners carry it.
            .. ClassicScrollBarChrome.CreateDialogScrollBarStyles(),
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
            // A wrapping text box is multi-line too, whether or not it takes Enter.
            WrappingTextBoxStyle(TextWrapping.Wrap, templatePart: false),
            WrappingTextBoxStyle(TextWrapping.Wrap, templatePart: true),
            WrappingTextBoxStyle(TextWrapping.WrapWithOverflow, templatePart: false),
            WrappingTextBoxStyle(TextWrapping.WrapWithOverflow, templatePart: true),
            // A single-line WPF text box hides its vertical bar. Its text line is often a pixel or
            // two taller than a compact box's viewport, which made the bar "needed" and drew a
            // 17 px track stub at the box's right end. The visibility reaches the template's
            // scroll viewer through the box; the second rule hides the bars of a template that
            // does not pass it on.
            new Style(x => SingleLineTextBox(x))
            {
                Setters = { new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden) },
            },
            new Style(x => SingleLineTextBox(x)
                .Template()
                .OfType<ScrollViewer>()
                .Template()
                .OfType<ScrollBar>())
            {
                Setters =
                {
                    new Setter(Visual.OpacityProperty, 0d),
                    new Setter(global::Avalonia.Input.InputElement.IsHitTestVisibleProperty, false),
                },
            },
            // WPF greys a disabled text box (ink and frame) by drawing it at 0.56 opacity. The
            // editor inside an editable combo follows the combo's own disabled face instead.
            new Style(x => x.OfType<TextBox>().Class(":disabled"))
            {
                Setters = { new Setter(Visual.OpacityProperty, DisabledTextBoxOpacity) },
            },
            // Declared after the rule above so it wins for the combo editor.
            new Style(x => x.OfType<TextBox>().Name("PART_EditableTextBox").Class(":disabled"))
            {
                Setters = { new Setter(Visual.OpacityProperty, 1d) },
            },
        ];

        for (var index = 0; index < styles.Length; index++)
            dialog.Styles.Insert(index, styles[index]);
    }

    // A list that does not hold the keyboard focus shows WPF's inactive selection.
    private static Selector? InactiveList(Selector? selector) =>
        selector.Is<ListBox>().Not(x => x.Class(":focus-within")).Descendant();

    private static Style SelectedListRowPresenterStyle(Func<Selector?, Selector?> scope, IBrush face, IBrush frame) =>
        new(x => scope(x)
            .OfType<ListBoxItem>()
            .Class(":selected")
            .Template()
            .OfType<global::Avalonia.Controls.Presenters.ContentPresenter>()
            .Name("PART_ContentPresenter"))
        {
            Setters =
            {
                new Setter(global::Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, face),
                new Setter(global::Avalonia.Controls.Presenters.ContentPresenter.BorderBrushProperty, frame),
                new Setter(
                    global::Avalonia.Controls.Presenters.ContentPresenter.BorderThicknessProperty,
                    new Thickness(1)),
                new Setter(
                    global::Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty,
                    DialogControlStyles.SelectionForegroundBrush),
            },
        };

    private static Selector SingleLineTextBox(Selector? selector) =>
        selector
            .Is<TextBox>()
            .PropertyEquals(TextBox.AcceptsReturnProperty, false)
            .PropertyEquals(TextBox.TextWrappingProperty, TextWrapping.NoWrap);

    private static Style WrappingTextBoxStyle(TextWrapping wrapping, bool templatePart) =>
        new(x => templatePart
            ? x.Is<TextBox>().PropertyEquals(TextBox.TextWrappingProperty, wrapping).Template().OfType<ScrollViewer>()
            : x.Is<TextBox>().PropertyEquals(TextBox.TextWrappingProperty, wrapping))
        {
            Setters = { new Setter(ScrollViewer.AllowAutoHideProperty, false) },
        };

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
            HasLocalValue(groupBox, TemplatedControl.BorderBrushProperty) && !IsSharedGroupBoxFrame(groupBox.BorderBrush)
                ? groupBox.BorderBrush
                : GroupBoxFrame,
            HasLocalValue(groupBox, TemplatedControl.ForegroundProperty) ? groupBox.Foreground : null);
        if (!hadMargin)
            groupBox.ClearValue(Layoutable.MarginProperty);
        if (!hadPadding)
            groupBox.ClearValue(TemplatedControl.PaddingProperty);
    }

    /// <summary>
    /// The shared chrome frames a group box in the neutral control grey when its dialog names no
    /// colour; WPF frames FreeX group boxes in light blue-grey. Runs after the shared descendant
    /// pass, so it also reaches group boxes a dialog chromed itself without a colour.
    /// </summary>
    private static void ApplyWpfGroupBoxFrames(Window dialog)
    {
        foreach (var groupBox in dialog.GetLogicalDescendants().OfType<GroupBox>())
        {
            if (IsSharedGroupBoxFrame(groupBox.BorderBrush))
                groupBox.BorderBrush = GroupBoxFrame;
        }
    }

    private static bool IsSharedGroupBoxFrame(IBrush? brush) =>
        brush is ISolidColorBrush { Opacity: 1 } solid && solid.Color == SharedGroupBoxFrameColor;

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

    private static bool IsInPushButtonRow(Button button) =>
        button.Parent is Panel row
        && row.Children.OfType<Button>().Any(sibling =>
            !ReferenceEquals(sibling, button)
            && !string.IsNullOrEmpty(CaptionOf(sibling))
            && (double.IsNaN(sibling.Width) || sibling.Width >= MinimumNormalizedButtonWidth));

    // A push button keeps a frame and the push-button face; flat, link-like and swatch buttons do not.
    private static bool IsFramedPushButton(Button button)
    {
        if (HasLocalValue(button, TemplatedControl.BorderThicknessProperty) && button.BorderThickness == default)
            return false;
        if (!HasLocalValue(button, TemplatedControl.BackgroundProperty))
            return true;
        return button.Background is ISolidColorBrush { Opacity: 1 } face
            && (face.Color == Colors.White || face.Color == ButtonFaceColor);
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
