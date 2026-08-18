using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class ColorPickerDialog : Window
{
    private bool _updatingText;
    private bool _updatingSlider;
    private readonly CellColor? _currentColor;
    private readonly WorkbookTheme? _theme;
    private CellColor? _customSpectrumBaseColor;
    private Button? _initialFocusButton;
    private Button? _selectedSwatchButton;

    /// <summary>
    /// R142-services-theme-colors-1: which theme slot/tint the currently-selected color came from,
    /// if it was picked from a Theme Colors swatch and hasn't since been overridden by a manual
    /// hex/RGB/luminosity edit (which -- like real Excel -- breaks the theme link). See
    /// <see cref="SelectColor"/>.
    /// </summary>
    private WorkbookThemeColorReference? _selectedThemeColor;

    public ColorPickerDialog(
        CellColor? initialColor = null,
        bool allowNoColor = false,
        string? noColorButtonText = null,
        WorkbookTheme? theme = null)
    {
        InitializeComponent();

        AllowNoColor = allowNoColor;
        _currentColor = initialColor;
        _theme = theme;
        SelectedColor = initialColor;
        NoColorButton.Visibility = allowNoColor ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(noColorButtonText))
            NoColorButton.Content = noColorButtonText;
        BuildPaletteButtons();

        SetPreview(CurrentForegroundPreview, CurrentBackgroundPreview, CurrentBackgroundText, _currentColor);
        SetPreview(NewForegroundPreview, NewBackgroundPreview, NewBackgroundText, initialColor);
        if (initialColor is { } color)
        {
            _customSpectrumBaseColor = color;
            SetCustomColorText(color);
        }
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public CellColor? SelectedColor { get; private set; }

    /// <summary>
    /// R142-services-theme-colors-1: the theme slot/tint <see cref="SelectedColor"/> came from, if
    /// the user's final choice was a Theme Colors swatch untouched by any subsequent manual
    /// hex/RGB/luminosity edit. Null for a Standard/Custom Spectrum swatch, a manually-entered
    /// color, or "No Fill". Callers that want a color applied to still track the workbook theme
    /// (e.g. the ribbon Font/Fill Color commands) should attach this alongside
    /// <see cref="SelectedColor"/> instead of only the resolved flat RGB.
    /// </summary>
    public WorkbookThemeColorReference? SelectedThemeColor { get; private set; }

    public bool AllowNoColor { get; }

    private void CustomColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingText || !ColorInputParser.TryParseColorText(CustomColorTextBox.Text, out var color))
            return;

        SelectColor(color);
    }

    private void CustomRgbTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingText
            || !ColorInputParser.TryParseRgbComponents(
                CustomRedTextBox.Text,
                CustomGreenTextBox.Text,
                CustomBlueTextBox.Text,
                out var color))
        {
            return;
        }

        SelectColor(color);
    }

    private void CustomLuminositySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSlider || _customSpectrumBaseColor is not { } baseColor)
            return;

        var factor = CustomLuminositySlider.Value / 100d;
        SelectColor(CellColorPalettePlanner.ScaleColor(baseColor, factor), updateSpectrumBase: false);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ColorInputParser.TryParseColorText(CustomColorTextBox.Text, out var color))
        {
            ShowInvalidCustomColorWarning(UiText.Get("ColorPicker_InvalidColorMessage"), CustomColorTextBox);
            return;
        }

        if (!TryParseCustomRgbFields(out _, out var invalidRgbInput))
        {
            ShowInvalidCustomColorWarning("Enter RGB values from 0 to 255.", invalidRgbInput);
            return;
        }

        SelectedColor = color;
        // Finalize the theme link SelectColor has been tracking as the user made their choice (the
        // color just re-parsed above from CustomColorTextBox.Text is the same one SelectColor kept
        // that textbox in sync with -- see its remarks).
        SelectedThemeColor = _selectedThemeColor;
        SetPreview(NewForegroundPreview, NewBackgroundPreview, NewBackgroundText, color);
        DialogResult = true;
    }

    private void ShowInvalidCustomColorWarning(string message, TextBox target)
    {
        ColorTabs.SelectedItem = CustomTab;
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
    }

    private void NoColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowNoColor)
            return;

        SelectedColor = null;
        SelectedThemeColor = null;
        DialogResult = true;
    }

    private void BuildPaletteButtons()
    {
        var themeColumns = CellColorPalettePlanner.BuildThemePalette(_theme);
        for (var row = 0; row < themeColumns[0].Shades.Count; row++)
        {
            foreach (var column in themeColumns)
                ThemeColorsPanel.Children.Add(CreateSwatchButton(column.Shades[row], column.Name));
        }

        foreach (var swatch in CellColorPalettePlanner.BuildStandardSwatches())
            StandardColorsPanel.Children.Add(CreateSwatchButton(swatch, UiText.Get("ColorPicker_StandardColorGroup")));

        foreach (var swatch in CellColorPalettePlanner.BuildCustomSpectrumSwatches())
            CustomSpectrumPanel.Children.Add(CreateSwatchButton(swatch, UiText.Get("ColorPicker_CustomSpectrumColorGroup")));
    }

    private Button CreateSwatchButton(CellColorSwatch swatch, string? groupName = null)
    {
        var button = new Button
        {
            Width = 30,
            Height = 24,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
            Background = ToBrush(swatch.Color),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            ToolTip = groupName is null ? swatch.Hex : UiText.Format("ColorPicker_GroupSwatchToolTip", groupName, swatch.Hex),
            // R142-services-theme-colors-1: the whole swatch (not just its resolved Color) so
            // SwatchButton_Click can recover which theme slot/tint a Theme Colors swatch came from.
            Tag = swatch
        };
        AutomationProperties.SetName(button, CreateSwatchAutomationName(swatch, groupName));
        AutomationProperties.SetHelpText(button, UiText.Get("ColorPicker_SwatchHelpText"));
        AutomationProperties.SetItemStatus(button, NotSelectedItemStatus);
        button.Click += SwatchButton_Click;
        _initialFocusButton ??= button;
        // A color can appear in more than one palette (for example standard red also appears in
        // the custom spectrum). Keep the first visible match selected during construction instead
        // of moving selection to the last duplicate, which can live on a different tab.
        if (_selectedSwatchButton is null && SelectedColor == swatch.Color)
            MarkSelectedSwatch(button);
        return button;
    }

    private static string CreateSwatchAutomationName(CellColorSwatch swatch, string? groupName) =>
        groupName is null
            ? UiText.Format("ColorPicker_ColorSwatchAutomationName", swatch.Hex)
            : UiText.Format("ColorPicker_GroupSwatchAutomationName", groupName, swatch.Hex);

    private void FocusInitialKeyboardTarget()
    {
        _initialFocusButton?.Focus();
        if (_initialFocusButton is not null)
            Keyboard.Focus(_initialFocusButton);
    }

    private void SwatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CellColorSwatch swatch } button)
        {
            MarkSelectedSwatch(button);
            SelectColor(
                swatch.Color,
                updateSpectrumBase: ReferenceEquals(((Button)sender).Parent, CustomSpectrumPanel),
                updateSwatchSelection: false,
                themeColor: swatch.ThemeColor);
        }
    }

    private const string SelectedItemStatus = "Selected";
    private const string NotSelectedItemStatus = "Not selected";

    private void MarkSelectedSwatch(Button button)
    {
        if (_selectedSwatchButton is not null)
            SetSwatchSelectionState(_selectedSwatchButton, isSelected: false);

        SetSwatchSelectionState(button, isSelected: true);
        _selectedSwatchButton = button;
    }

    private void ClearSelectedSwatch()
    {
        if (_selectedSwatchButton is null)
            return;

        SetSwatchSelectionState(_selectedSwatchButton, isSelected: false);
        _selectedSwatchButton = null;
    }

    /// <summary>
    /// Updates a swatch button's visual selection border and exposes the same selection
    /// state through UI Automation (AutomationProperties.ItemStatus, plus a property-changed
    /// notification), so screen readers can tell which color is currently selected. Mirrors
    /// the ItemStatus selection convention used for gallery-style selection UI in the
    /// Avalonia shell (see MainWindow.cs/MainWindow.Charts.cs AutomationProperties.SetItemStatus).
    /// </summary>
    private static void SetSwatchSelectionState(Button button, bool isSelected)
    {
        button.BorderBrush = isSelected ? Brushes.Black : Brushes.Gray;
        button.BorderThickness = new Thickness(isSelected ? 2 : 1);

        var status = isSelected ? SelectedItemStatus : NotSelectedItemStatus;
        var previousStatus = AutomationProperties.GetItemStatus(button);
        if (string.Equals(previousStatus, status, StringComparison.Ordinal))
            return;

        AutomationProperties.SetItemStatus(button, status);

        if (!button.IsLoaded)
            return;

        try
        {
            var peer = UIElementAutomationPeer.FromElement(button) ??
                       UIElementAutomationPeer.CreatePeerForElement(button);
            peer?.RaisePropertyChangedEvent(AutomationElementIdentifiers.ItemStatusProperty, previousStatus, status);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void UpdateSwatchSelection(CellColor color)
    {
        var matchingButton = FindSwatchButton(color);

        if (matchingButton is null)
        {
            ClearSelectedSwatch();
            return;
        }

        MarkSelectedSwatch(matchingButton);
    }

    private Button? FindSwatchButton(CellColor color)
    {
        foreach (var child in ThemeColorsPanel.Children)
        {
            if (child is Button button && button.Tag is CellColorSwatch swatch && swatch.Color == color)
                return button;
        }

        foreach (var child in StandardColorsPanel.Children)
        {
            if (child is Button button && button.Tag is CellColorSwatch swatch && swatch.Color == color)
                return button;
        }

        foreach (var child in CustomSpectrumPanel.Children)
        {
            if (child is Button button && button.Tag is CellColorSwatch swatch && swatch.Color == color)
                return button;
        }

        return null;
    }

    /// <summary>
    /// Records the dialog's current color choice. <paramref name="themeColor"/> defaults to null,
    /// so every non-swatch caller (custom hex/RGB text edits, the luminosity slider) implicitly
    /// clears any previously-tracked theme link -- matching Excel: editing the custom color breaks
    /// the connection to the Theme Colors swatch that seeded it, even if the edit happens to land
    /// back on the exact same RGB. Only <see cref="SwatchButton_Click"/> passes a non-null value,
    /// and only for an actual Theme Colors swatch (R142-services-theme-colors-1).
    /// </summary>
    private void SelectColor(
        CellColor color,
        bool updateSpectrumBase = true,
        bool updateSwatchSelection = true,
        WorkbookThemeColorReference? themeColor = null)
    {
        SelectedColor = color;
        _selectedThemeColor = themeColor;
        if (updateSwatchSelection)
            UpdateSwatchSelection(color);

        if (updateSpectrumBase)
        {
            _customSpectrumBaseColor = color;
            _updatingSlider = true;
            CustomLuminositySlider.Value = 100;
            _updatingSlider = false;
        }

        SetPreview(NewForegroundPreview, NewBackgroundPreview, NewBackgroundText, color);
        SetCustomColorText(color);
    }

    private void SetCustomColorText(CellColor color)
    {
        _updatingText = true;
        CustomColorTextBox.Text = ColorInputParser.FormatHexColor(color);
        CustomRedTextBox.Text = color.R.ToString();
        CustomGreenTextBox.Text = color.G.ToString();
        CustomBlueTextBox.Text = color.B.ToString();
        _updatingText = false;
    }

    private bool TryParseCustomRgbFields(out CellColor color, out TextBox invalidInput)
    {
        if (!TryParseRgbByte(CustomRedTextBox.Text, out var red))
        {
            color = default;
            invalidInput = CustomRedTextBox;
            return false;
        }

        if (!TryParseRgbByte(CustomGreenTextBox.Text, out var green))
        {
            color = default;
            invalidInput = CustomGreenTextBox;
            return false;
        }

        if (!TryParseRgbByte(CustomBlueTextBox.Text, out var blue))
        {
            color = default;
            invalidInput = CustomBlueTextBox;
            return false;
        }

        color = new CellColor(red, green, blue);
        invalidInput = CustomRedTextBox;
        return true;
    }

    private static bool TryParseRgbByte(string text, out byte value) =>
        byte.TryParse(text.Trim(), out value);

    private static void SetPreview(TextBlock foregroundPreview, Border backgroundPreview, TextBlock backgroundText, CellColor? color)
    {
        if (color is not { } selected)
        {
            foregroundPreview.Foreground = SystemColors.GrayTextBrush;
            backgroundPreview.Background = Brushes.Transparent;
            backgroundText.Foreground = SystemColors.ControlTextBrush;
            return;
        }

        foregroundPreview.Foreground = ToBrush(selected);
        backgroundPreview.Background = ToBrush(selected);
        backgroundText.Foreground = GetReadableBrush(selected);
    }

    private static SolidColorBrush ToBrush(CellColor color) =>
        new(Color.FromRgb(color.R, color.G, color.B));

    private static Brush GetReadableBrush(CellColor color)
    {
        return CellColorPalettePlanner.NeedsDarkForeground(color) ? Brushes.Black : Brushes.White;
    }
}
