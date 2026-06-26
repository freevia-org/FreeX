using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

/// <summary>
/// Modal slide-size dialog (Wave 10B).
///
/// Lets the user set the slide width and height numerically, with a unit selector
/// (Inches / Centimeters) and a presets dropdown (Standard 4:3, Widescreen 16:9, Custom).
/// The dialog pre-fills the current presentation size on open.
///
/// Layout:
///   ┌─────────────────────────────────────────────────┐
///   │  Preset: [Standard 4:3 ▼]                       │
///   ├─────────────────────────────────────────────────┤
///   │  Unit:   ( ) Inches  (•) Centimeters             │
///   │  Width:  [___13.333___] [in/cm]                  │
///   │  Height: [___7.500____] [in/cm]                  │
///   ├─────────────────────────────────────────────────┤
///   │                          [OK]  [Cancel]           │
///   └─────────────────────────────────────────────────┘
///
/// OK calls <see cref="EditingSession.SetSlideSize"/> (undoable).
/// Cancel discards.
/// </summary>
public sealed class SlideSizeDialog : Window
{
    // ── EMU constants ──────────────────────────────────────────────────────────────

    /// <summary>EMU per inch (DrawingML). 914400 EMU = 1 inch.</summary>
    public const long EmuPerInch = 914_400L;

    /// <summary>EMU per centimetre (DrawingML). 360000 EMU = 1 cm.</summary>
    public const long EmuPerCm = 360_000L;

    // ── Presets ────────────────────────────────────────────────────────────────────

    // Standard 4:3 — 10" × 7.5" (254 mm × 190.5 mm) — same as PowerPoint default
    private static readonly long Standard43CxEmu = 9_144_000L;
    private static readonly long Standard43CyEmu = 6_858_000L;

    // Widescreen 16:9 — 13.333" × 7.5" (338.67 mm × 190.5 mm)
    private static readonly long Widescreen169CxEmu = 12_192_000L;
    private static readonly long Widescreen169CyEmu = 6_858_000L;

    public enum Preset { Standard43, Widescreen169, Custom }

    // ── State ──────────────────────────────────────────────────────────────────────

    private readonly EditingSession _editor;

    /// <summary>True = inches; false = centimetres.</summary>
    private bool _useInches = true;

    private bool _suppressPresetRefresh;  // guards against re-entrance when controls update

    // ── Controls ───────────────────────────────────────────────────────────────────

    private readonly ComboBox _presetCombo;
    private readonly RadioButton _inchesRadio;
    private readonly RadioButton _cmRadio;
    private readonly TextBox _widthBox;
    private readonly TextBox _heightBox;
    private readonly Label _widthUnitLabel;
    private readonly Label _heightUnitLabel;

    // ── Construction ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the dialog, pre-filling the current slide size from <paramref name="editor"/>.
    /// </summary>
    public SlideSizeDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));

        Title                 = "Slide Size";
        Width                 = 380;
        Height                = 260;
        ResizeMode            = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background            = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        // ── Controls ──────────────────────────────────────────────────────────────

        _presetCombo = new ComboBox { Margin = new Thickness(4) };
        _presetCombo.Items.Add("Standard (4:3)");
        _presetCombo.Items.Add("Widescreen (16:9)");
        _presetCombo.Items.Add("Custom");
        _presetCombo.SelectedIndex = 0;
        _presetCombo.SelectionChanged += OnPresetChanged;

        _inchesRadio = new RadioButton { Content = "Inches",      IsChecked = true, Margin = new Thickness(4, 0, 12, 0) };
        _cmRadio     = new RadioButton { Content = "Centimeters", IsChecked = false, Margin = new Thickness(4, 0, 4, 0) };
        _inchesRadio.Checked += OnUnitChanged;
        _cmRadio.Checked     += OnUnitChanged;

        _widthBox  = MakeNumericBox();
        _heightBox = MakeNumericBox();

        _widthUnitLabel  = new Label { Content = "in", Width = 30 };
        _heightUnitLabel = new Label { Content = "in", Width = 30 };

        // Pre-fill from current presentation size.
        LoadCurrentSize();

        // ── OK / Cancel ───────────────────────────────────────────────────────────

        var okBtn = new Button { Content = "OK", Width = 80, IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0) };
        okBtn.Click += (_, _) => OnOk();

        var cancelBtn = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(4, 8, 8, 8)
        };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);

        // ── Layout ────────────────────────────────────────────────────────────────

        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // preset
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // units
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // width
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // height
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // buttons

        // Row 0 — Preset
        AddLabel(grid, "Preset:", 0, 0);
        Grid.SetRow(_presetCombo, 0); Grid.SetColumn(_presetCombo, 1); Grid.SetColumnSpan(_presetCombo, 2);
        grid.Children.Add(_presetCombo);

        // Row 1 — Units
        AddLabel(grid, "Unit:", 1, 0);
        var unitPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
        unitPanel.Children.Add(_inchesRadio);
        unitPanel.Children.Add(_cmRadio);
        Grid.SetRow(unitPanel, 1); Grid.SetColumn(unitPanel, 1); Grid.SetColumnSpan(unitPanel, 2);
        grid.Children.Add(unitPanel);

        // Row 2 — Width
        AddLabel(grid, "Width:", 2, 0);
        Grid.SetRow(_widthBox, 2); Grid.SetColumn(_widthBox, 1);
        grid.Children.Add(_widthBox);
        Grid.SetRow(_widthUnitLabel, 2); Grid.SetColumn(_widthUnitLabel, 2);
        grid.Children.Add(_widthUnitLabel);

        // Row 3 — Height
        AddLabel(grid, "Height:", 3, 0);
        Grid.SetRow(_heightBox, 3); Grid.SetColumn(_heightBox, 1);
        grid.Children.Add(_heightBox);
        Grid.SetRow(_heightUnitLabel, 3); Grid.SetColumn(_heightUnitLabel, 2);
        grid.Children.Add(_heightUnitLabel);

        // Row 5 — Buttons
        Grid.SetRow(btnRow, 5); Grid.SetColumn(btnRow, 0); Grid.SetColumnSpan(btnRow, 3);
        grid.Children.Add(btnRow);

        Content = grid;
    }

    // ── Unit conversion helpers (public for unit tests) ────────────────────────────

    /// <summary>Converts EMU to inches.</summary>
    public static double EmuToInches(long emu) => emu / (double)EmuPerInch;

    /// <summary>Converts EMU to centimetres.</summary>
    public static double EmuToCm(long emu) => emu / (double)EmuPerCm;

    /// <summary>Converts inches to EMU (rounded).</summary>
    public static long InchesToEmu(double inches) => (long)Math.Round(inches * EmuPerInch);

    /// <summary>Converts centimetres to EMU (rounded).</summary>
    public static long CmToEmu(double cm) => (long)Math.Round(cm * EmuPerCm);

    // ── Preset EMU accessors (public for tests) ────────────────────────────────────

    /// <summary>Standard 4:3 slide size in EMU: 9144000 × 6858000.</summary>
    public static (long CxEmu, long CyEmu) Standard43Emu => (Standard43CxEmu, Standard43CyEmu);

    /// <summary>Widescreen 16:9 slide size in EMU: 12192000 × 6858000.</summary>
    public static (long CxEmu, long CyEmu) Widescreen169Emu => (Widescreen169CxEmu, Widescreen169CyEmu);

    // ── Preset classification (public for tests) ───────────────────────────────────

    /// <summary>
    /// Returns the <see cref="Preset"/> that matches the given EMU values, or
    /// <see cref="Preset.Custom"/> if neither standard preset matches exactly.
    /// </summary>
    public static Preset ClassifySize(long cxEmu, long cyEmu)
    {
        if (cxEmu == Standard43CxEmu   && cyEmu == Standard43CyEmu)   return Preset.Standard43;
        if (cxEmu == Widescreen169CxEmu && cyEmu == Widescreen169CyEmu) return Preset.Widescreen169;
        return Preset.Custom;
    }

    // ── Internal helpers ───────────────────────────────────────────────────────────

    private void LoadCurrentSize()
    {
        var cx = _editor.Presentation.SlideSizeCxEmu;
        var cy = _editor.Presentation.SlideSizeCyEmu;

        // Select preset combo without triggering the selection-changed handler.
        _suppressPresetRefresh = true;
        try
        {
            _presetCombo.SelectedIndex = ClassifySize(cx, cy) switch
            {
                Preset.Standard43     => 0,
                Preset.Widescreen169  => 1,
                _                     => 2
            };
        }
        finally
        {
            _suppressPresetRefresh = false;
        }

        RefreshBoxesFromEmu(cx, cy);
    }

    private void RefreshBoxesFromEmu(long cxEmu, long cyEmu)
    {
        if (_useInches)
        {
            _widthBox.Text  = EmuToInches(cxEmu).ToString("F3");
            _heightBox.Text = EmuToInches(cyEmu).ToString("F3");
            _widthUnitLabel.Content  = "in";
            _heightUnitLabel.Content = "in";
        }
        else
        {
            _widthBox.Text  = EmuToCm(cxEmu).ToString("F2");
            _heightBox.Text = EmuToCm(cyEmu).ToString("F2");
            _widthUnitLabel.Content  = "cm";
            _heightUnitLabel.Content = "cm";
        }
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetRefresh) return;

        var preset = (Preset)_presetCombo.SelectedIndex;
        if (preset == Preset.Custom) return; // let user type freely

        var (cx, cy) = preset == Preset.Widescreen169
            ? (Widescreen169CxEmu, Widescreen169CyEmu)
            : (Standard43CxEmu,   Standard43CyEmu);

        RefreshBoxesFromEmu(cx, cy);
    }

    private void OnUnitChanged(object sender, RoutedEventArgs e)
    {
        // Read current values in old unit, convert, then re-render in new unit.
        bool wasInches = _useInches;
        _useInches = _inchesRadio.IsChecked == true;

        if (wasInches == _useInches) return;

        // Parse current box values in the OLD unit.
        if (!double.TryParse(_widthBox.Text,  out double w)) w = 0;
        if (!double.TryParse(_heightBox.Text, out double h)) h = 0;

        long cxEmu = wasInches ? InchesToEmu(w) : CmToEmu(w);
        long cyEmu = wasInches ? InchesToEmu(h) : CmToEmu(h);

        RefreshBoxesFromEmu(cxEmu, cyEmu);
    }

    private void OnOk()
    {
        if (!TryParseEmu(out long cxEmu, out long cyEmu))
        {
            MessageBox.Show(
                "Please enter valid positive numbers for width and height.",
                "Invalid Size",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Minimum size guard: 0.5 inch each.
        const long minEmu = 457_200L;
        if (cxEmu < minEmu || cyEmu < minEmu)
        {
            MessageBox.Show(
                "Slide dimensions must be at least 0.5 inches (1.27 cm).",
                "Invalid Size",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _editor.SetSlideSize(cxEmu, cyEmu);
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Parses the width and height boxes into EMU values.
    /// Returns false if either value cannot be parsed or is non-positive.
    /// </summary>
    public bool TryParseEmu(out long cxEmu, out long cyEmu)
    {
        cxEmu = 0;
        cyEmu = 0;

        if (!double.TryParse(_widthBox.Text,  System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.CurrentCulture, out double w) || w <= 0)
            return false;
        if (!double.TryParse(_heightBox.Text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.CurrentCulture, out double h) || h <= 0)
            return false;

        cxEmu = _useInches ? InchesToEmu(w) : CmToEmu(w);
        cyEmu = _useInches ? InchesToEmu(h) : CmToEmu(h);
        return true;
    }

    private static TextBox MakeNumericBox() => new()
    {
        Width  = 120,
        Margin = new Thickness(4),
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private static void AddLabel(Grid grid, string text, int row, int col)
    {
        var lbl = new Label
        {
            Content             = text,
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(4, 2, 4, 2)
        };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, col);
        grid.Children.Add(lbl);
    }
}
