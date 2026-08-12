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
            Tag = swatch.Color
        };
        AutomationProperties.SetName(button, CreateSwatchAutomationName(swatch, groupName));
        AutomationProperties.SetHelpText(button, UiText.Get("ColorPicker_SwatchHelpText"));
        AutomationProperties.SetItemStatus(button, NotSelectedItemStatus);
        button.Click += SwatchButton_Click;
        _initialFocusButton ??= button;
        if (SelectedColor == swatch.Color)
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
        if (sender is Button { Tag: CellColor color } button)
        {
            MarkSelectedSwatch(button);
            SelectColor(
                color,
                updateSpectrumBase: ReferenceEquals(((Button)sender).Parent, CustomSpectrumPanel),
                updateSwatchSelection: false);
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
            if (child is Button button && button.Tag is CellColor swatchColor && swatchColor == color)
                return button;
        }

        foreach (var child in StandardColorsPanel.Children)
        {
            if (child is Button button && button.Tag is CellColor swatchColor && swatchColor == color)
                return button;
        }

        foreach (var child in CustomSpectrumPanel.Children)
        {
            if (child is Button button && button.Tag is CellColor swatchColor && swatchColor == color)
                return button;
        }

        return null;
    }

    private void SelectColor(CellColor color, bool updateSpectrumBase = true, bool updateSwatchSelection = true)
    {
        SelectedColor = color;
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
