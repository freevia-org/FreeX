using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Avalonia;

/// <summary>
/// The floating mini toolbar the WPF host shows above the pointer-opened worksheet cell menu
/// (<c>MainWindow.WorksheetContextMiniToolbar.cs</c>): font name and size boxes, bold, italic,
/// center and fill colour. Every control runs the handler the matching ribbon control runs. The
/// popup opens with the menu and closes with it, unless the toolbar itself was used, in which case
/// it stays until the next input elsewhere.
/// </summary>
public sealed partial class MainWindow
{
    // WPF: Popup VerticalOffset = gridPosition.Y - 44.
    internal const double WorksheetContextMiniToolbarVerticalOffset = -44;

    // WPF: ComboBox Height=26; font name Width=76, font size Width=42; buttons 27x26.
    internal const double WorksheetContextMiniToolbarRowHeight = 26;
    internal const double WorksheetContextMiniToolbarFontNameWidth = 76;
    internal const double WorksheetContextMiniToolbarFontSizeWidth = 42;
    internal const double WorksheetContextMiniToolbarButtonWidth = 27;
    internal const double WorksheetContextMiniToolbarGlyphFontSize = 15;

    private const string WorksheetContextMiniToolbarAutomationId = "WorksheetContextMiniToolbar";
    private const string OfficeDefaultNarrowFontName = "Aptos Narrow";
    private const string OfficeDefaultFontSizeText = "11";

    private static readonly string[] WorksheetContextMiniToolbarFontNames =
        [OfficeDefaultNarrowFontName, "Aptos", "Calibri", "Arial"];

    private static readonly string[] WorksheetContextMiniToolbarFontSizes =
        ["8", "9", "10", "11", "12", "14", "16", "18"];

    // WPF: BorderBrush #7F7F7F on a white surface.
    private static readonly IBrush WorksheetContextMiniToolbarBorderBrush =
        new ImmutableSolidColorBrush(Color.FromRgb(127, 127, 127));

    // The system button hover and pressed fills of the WPF toolbar buttons.
    private static readonly IBrush WorksheetContextMiniToolbarHoverBrush =
        new ImmutableSolidColorBrush(Color.FromRgb(0xBE, 0xE6, 0xFD));
    private static readonly IBrush WorksheetContextMiniToolbarPressedBrush =
        new ImmutableSolidColorBrush(Color.FromRgb(0xC4, 0xE5, 0xF6));

    private Popup? _worksheetContextMiniToolbar;
    private Border? _worksheetContextMiniToolbarContent;
    private ContextMenu? _worksheetContextMiniToolbarMenu;
    private bool _preserveWorksheetContextMiniToolbarAfterContextMenuClose;
    private bool _worksheetContextMiniToolbarDismissHooked;

    /// <summary>
    /// The toolbar belongs to the plain cell menu only: a whole row or column selection gets the
    /// header menu's behaviour and no toolbar.
    /// </summary>
    internal static bool ShouldShowWorksheetContextMiniToolbar(FreeX.Core.Model.GridRange selectedRange) =>
        !FreeX.Core.Commands.SelectionRangeService.IsWholeRowSelection(selectedRange) &&
        !FreeX.Core.Commands.SelectionRangeService.IsWholeColumnSelection(selectedRange);

    private void ShowWorksheetContextMiniToolbar(ContextMenu menu)
    {
        CloseWorksheetContextMiniToolbar();
        EnsureWorksheetContextMiniToolbarDismissHooks();

        var toolbar = CreateWorksheetContextMiniToolbar();
        var popup = new Popup
        {
            // The grid host outlives shell refreshes; a cell border does not, and a popup closes
            // when its placement target leaves the tree.
            PlacementTarget = _sheetGridHost,
            Placement = PlacementMode.Pointer,
            VerticalOffset = WorksheetContextMiniToolbarVerticalOffset,
            IsLightDismissEnabled = false,
            Child = toolbar,
        };
        // Parented like a context-menu popup so the toolbar inherits the window's font and resources.
        ((ISetLogicalParent)popup).SetParent(this);

        // Using the toolbar dismisses the menu but keeps the toolbar, as in the WPF host.
        toolbar.AddHandler(
            InputElement.PointerPressedEvent,
            (_, _) =>
            {
                _preserveWorksheetContextMiniToolbarAfterContextMenuClose = true;
                if (menu.IsOpen)
                    menu.Close();
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        popup.Closed += (_, _) =>
        {
            ((ISetLogicalParent)popup).SetParent(null);
            if (ReferenceEquals(_worksheetContextMiniToolbar, popup))
            {
                _worksheetContextMiniToolbar = null;
                _worksheetContextMiniToolbarContent = null;
                _worksheetContextMiniToolbarMenu = null;
            }
        };
        menu.Closed += (_, _) => Dispatcher.UIThread.Post(
            () =>
            {
                if (!ReferenceEquals(_worksheetContextMiniToolbar, popup))
                    return;

                if (_preserveWorksheetContextMiniToolbarAfterContextMenuClose || toolbar.IsPointerOver)
                {
                    _preserveWorksheetContextMiniToolbarAfterContextMenuClose = false;
                    return;
                }

                CloseWorksheetContextMiniToolbar();
            },
            DispatcherPriority.Background);

        _worksheetContextMiniToolbar = popup;
        _worksheetContextMiniToolbarContent = toolbar;
        _worksheetContextMiniToolbarMenu = menu;
        popup.IsOpen = true;
    }

    /// <summary>Builds the toolbar surface with its controls wired to the ribbon handlers.</summary>
    internal Border CreateWorksheetContextMiniToolbar()
    {
        var toolbar = new Border
        {
            Background = Brushes.White,
            BorderBrush = WorksheetContextMiniToolbarBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(4),
            Child = CreateWorksheetContextMiniToolbarContent(),
        };
        AutomationProperties.SetName(toolbar, UiText.Get("WorksheetContextMiniToolbar_AutomationName"));
        AutomationProperties.SetAutomationId(toolbar, WorksheetContextMiniToolbarAutomationId);
        return toolbar;
    }

    private StackPanel CreateWorksheetContextMiniToolbarContent()
    {
        var commands = new StackPanel { Orientation = Orientation.Horizontal };
        var state = ToolbarVisualState.From(_session.SelectedRangeStartStyle);

        commands.Children.Add(CreateWorksheetContextMiniToolbarComboBox(
            WorksheetContextMiniToolbarFontNameWidth,
            string.IsNullOrWhiteSpace(state.FontName) ? OfficeDefaultNarrowFontName : state.FontName,
            WorksheetContextMiniToolbarFontNames,
            UiText.Get("MainWindow_AutomationName_Font"),
            ApplyRibbonFontName));
        commands.Children.Add(CreateWorksheetContextMiniToolbarComboBox(
            WorksheetContextMiniToolbarFontSizeWidth,
            string.IsNullOrWhiteSpace(state.FontSizeText) ? OfficeDefaultFontSizeText : state.FontSizeText,
            WorksheetContextMiniToolbarFontSizes,
            UiText.Get("MainWindow_AutomationName_FontSize"),
            ApplyRibbonFontSize));

        commands.Children.Add(CreateWorksheetContextMiniToolbarButton(
            "B",
            UiText.Get("MainWindow_TooltipTitle_Bold"),
            ToggleSelectedRangeBold,
            fontWeight: FontWeight.Bold));
        commands.Children.Add(CreateWorksheetContextMiniToolbarButton(
            "I",
            UiText.Get("MainWindow_TooltipTitle_Italic"),
            ToggleSelectedRangeItalic,
            fontStyle: FontStyle.Italic));
        commands.Children.Add(CreateWorksheetContextMiniToolbarButton(
            "≡",
            UiText.Get("MainWindow_TooltipTitle_Center"),
            () => ApplySelectedRangeHorizontalAlignment(FreeX.Core.Model.HorizontalAlignment.Center)));

        // The fill button opens the same colour palette the ribbon fill button opens.
        var fill = CreateWorksheetContextMiniToolbarButton(
            "▰",
            UiText.Get("MainWindow_AutomationName_FillColor"),
            action: null);
        fill.Flyout = CreateColorPaletteFlyout(ColorPaletteTarget.Fill, includeClearFill: true);
        commands.Children.Add(fill);

        return commands;
    }

    private static ComboBox CreateWorksheetContextMiniToolbarComboBox(
        double width,
        string text,
        IReadOnlyList<string> items,
        string automationName,
        Action<string?> apply)
    {
        var comboBox = new ComboBox
        {
            IsEditable = true,
            ItemsSource = items,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(
            comboBox,
            AvaloniaCompactDialogChrome.WindowsStyle,
            fixedHeight: false);
        comboBox.Width = width;
        comboBox.MinWidth = 0;
        comboBox.Height = WorksheetContextMiniToolbarRowHeight;
        comboBox.MinHeight = WorksheetContextMiniToolbarRowHeight;
        comboBox.MaxHeight = WorksheetContextMiniToolbarRowHeight;
        comboBox.HorizontalAlignment = HorizontalAlignment.Left;
        comboBox.Margin = new Thickness(0, 0, 3, 0);
        comboBox.Text = text;
        AutomationProperties.SetName(comboBox, automationName);

        // The seeded text is the selection's current value, so only a different value is a command.
        var lastApplied = text;
        void Commit(string? value)
        {
            var candidate = value?.Trim();
            if (string.IsNullOrEmpty(candidate) ||
                string.Equals(candidate, lastApplied, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lastApplied = candidate;
            apply(candidate);
        }

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is string selected)
                Commit(selected);
        };
        comboBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter)
                return;

            Commit(comboBox.Text);
            args.Handled = true;
        };
        comboBox.LostFocus += (_, _) => Commit(comboBox.Text);
        return comboBox;
    }

    private static Button CreateWorksheetContextMiniToolbarButton(
        string glyph,
        string automationName,
        Action? action,
        FontWeight fontWeight = FontWeight.Normal,
        FontStyle fontStyle = FontStyle.Normal)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontWeight = fontWeight,
                FontStyle = fontStyle,
                FontSize = WorksheetContextMiniToolbarGlyphFontSize,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Width = WorksheetContextMiniToolbarButtonWidth,
            MinWidth = 0,
            Height = WorksheetContextMiniToolbarRowHeight,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 2, 0),
            Background = Brushes.White,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Resources["ButtonBackgroundPointerOver"] = WorksheetContextMiniToolbarHoverBrush;
        button.Resources["ButtonBackgroundPressed"] = WorksheetContextMiniToolbarPressedBrush;
        button.Resources["ButtonBorderBrushPointerOver"] = Brushes.Transparent;
        button.Resources["ButtonBorderBrushPressed"] = Brushes.Transparent;
        if (action is not null)
            button.Click += (_, _) => action();
        AutomationProperties.SetName(button, automationName);
        return button;
    }

    // Once the menu is gone a kept toolbar is dismissed by the next press or key elsewhere in the
    // window. Presses inside the toolbar, its drop-downs and its colour palette are its own.
    private void EnsureWorksheetContextMiniToolbarDismissHooks()
    {
        if (_worksheetContextMiniToolbarDismissHooked)
            return;

        _worksheetContextMiniToolbarDismissHooked = true;
        AddHandler(
            PointerPressedEvent,
            (_, args) => DismissWorksheetContextMiniToolbarForInput(args.Source),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            KeyDownEvent,
            (_, args) => DismissWorksheetContextMiniToolbarForInput(args.Source),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void DismissWorksheetContextMiniToolbarForInput(object? source)
    {
        if (_worksheetContextMiniToolbar is null || IsWithinWorksheetContextMiniToolbar(source))
            return;

        // While the menu is open its own closing decides what happens to the toolbar.
        if (_worksheetContextMiniToolbarMenu is { IsOpen: true })
            return;

        CloseWorksheetContextMiniToolbar();
    }

    // Walks the logical chain first so a drop-down or flyout popup resolves to its owner.
    private bool IsWithinWorksheetContextMiniToolbar(object? source)
    {
        if (_worksheetContextMiniToolbarContent is not { } toolbar)
            return false;

        for (var element = source as StyledElement;
             element is not null;
             element = element.Parent ?? (element as Visual)?.GetVisualParent())
        {
            if (ReferenceEquals(element, toolbar))
                return true;
        }

        return false;
    }

    private void CloseWorksheetContextMiniToolbar()
    {
        _preserveWorksheetContextMiniToolbarAfterContextMenuClose = false;
        if (_worksheetContextMiniToolbar is not { } toolbar)
            return;

        _worksheetContextMiniToolbar = null;
        _worksheetContextMiniToolbarContent = null;
        _worksheetContextMiniToolbarMenu = null;
        toolbar.IsOpen = false;
    }
}
