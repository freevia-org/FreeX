using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class SlideSizeDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly EditingSession _editor;
    private readonly ComboBox _presetCombo;
    private readonly RadioButton _inchesRadio;
    private readonly RadioButton _centimetersRadio;
    private readonly TextBox _widthBox;
    private readonly TextBox _heightBox;
    private readonly TextBlock _widthUnitLabel;
    private readonly TextBlock _heightUnitLabel;
    private readonly TextBlock _validationText;
    private SlideSizeDialogUnit _unit = SlideSizeDialogUnit.Inches;
    private bool _suppressSelectionRefresh;

    internal SlideSizeDialogResultPlan? LastResultPlan { get; private set; }
    internal SlideSizeDialogInitialState InitialState { get; }
    internal string WidthText => _widthBox.Text ?? string.Empty;
    internal string HeightText => _heightBox.Text ?? string.Empty;
    internal string ValidationText => _validationText.Text ?? string.Empty;
    internal SlideSizeDialogUnit Unit => _unit;

    public SlideSizeDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));

        Title = "Slide Size";
        Width = 365.3333333333333;
        Height = 222.66666666666666;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _presetCombo = new ComboBox
        {
            ItemsSource = new[] { "Standard (4:3)", "Widescreen (16:9)", "Custom" },
            MinWidth = 220,
        };
        _presetCombo.SelectionChanged += OnPresetChanged;

        _inchesRadio = new RadioButton
        {
            Content = "Inches",
            GroupName = "SlideSizeUnit",
            IsChecked = true,
            Margin = new Thickness(0, 0, 12, 0),
        };
        _centimetersRadio = new RadioButton
        {
            Content = "Centimeters",
            GroupName = "SlideSizeUnit",
        };
        _inchesRadio.IsCheckedChanged += OnUnitChanged;
        _centimetersRadio.IsCheckedChanged += OnUnitChanged;

        _widthBox = new TextBox { MinWidth = 150 };
        _heightBox = new TextBox { MinWidth = 150 };
        _widthUnitLabel = BuildUnitLabel();
        _heightUnitLabel = BuildUnitLabel();
        _validationText = new TextBlock();

        AvaloniaCompactDialogChrome.ApplyComboBox(_presetCombo, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(_inchesRadio, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(_centimetersRadio, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_widthBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_heightBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(
            _validationText,
            DialogChromeStyle,
            new Thickness(0, 8, 0, 0));

        InitialState = SlideSizeDialogPlanner.BuildInitialState(
            _editor.Presentation.SlideSizeCxEmu,
            _editor.Presentation.SlideSizeCyEmu,
            _unit);
        LoadInitialState(InitialState);
        Content = BuildContent();

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;

            Close(false);
            e.Handled = true;
        };
    }

    internal bool TryParseEmu(out long cxEmu, out long cyEmu)
    {
        var parse = SlideSizeDialogPlanner.TryParsePositiveSize(
            _widthBox.Text ?? string.Empty,
            _heightBox.Text ?? string.Empty,
            _unit);
        cxEmu = parse.CxEmu;
        cyEmu = parse.CyEmu;
        return parse.IsValid;
    }

    internal void SetInputForTests(string widthText, string heightText, SlideSizeDialogUnit unit)
    {
        _suppressSelectionRefresh = true;
        try
        {
            _unit = unit;
            _inchesRadio.IsChecked = unit == SlideSizeDialogUnit.Inches;
            _centimetersRadio.IsChecked = unit == SlideSizeDialogUnit.Centimeters;
            _widthBox.Text = widthText;
            _heightBox.Text = heightText;
            ApplyUnitLabels(unit == SlideSizeDialogUnit.Inches ? "in" : "cm");
        }
        finally
        {
            _suppressSelectionRefresh = false;
        }
    }

    internal bool ApplyForTests() => Apply();

    private Control BuildContent()
    {
        var grid = new Grid
        {
            Margin = new Thickness(14),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        AddLabeledRow(grid, 0, "Preset:", _presetCombo, span: 2);

        var unitRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _inchesRadio, _centimetersRadio },
        };
        AddLabeledRow(grid, 1, "Unit:", unitRow, span: 2);
        AddLabeledRow(grid, 2, "Width:", _widthBox, _widthUnitLabel);
        AddLabeledRow(grid, 3, "Height:", _heightBox, _heightUnitLabel);

        Grid.SetRow(_validationText, 4);
        Grid.SetColumnSpan(_validationText, 3);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(_validationText);

        var ok = new Button { Content = "OK" };
        ok.Click += (_, _) => Apply();
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 80, isDefault: true);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 80);

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [ok, cancel],
            new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 5);
        Grid.SetColumnSpan(buttons, 3);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(buttons);
        return grid;
    }

    private void LoadInitialState(SlideSizeDialogInitialState state)
    {
        _suppressSelectionRefresh = true;
        try
        {
            _presetCombo.SelectedIndex = ToPresetIndex(state.Preset);
            ApplyDisplay(state.Display);
        }
        finally
        {
            _suppressSelectionRefresh = false;
        }
    }

    private void OnPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionRefresh)
            return;

        var display = SlideSizeDialogPlanner.BuildPresetSelectionDisplay(
            PresetFromIndex(_presetCombo.SelectedIndex),
            _unit);
        if (display is not null)
            ApplyDisplay(display);
    }

    private void OnUnitChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressSelectionRefresh)
            return;

        var newUnit = _centimetersRadio.IsChecked == true
            ? SlideSizeDialogUnit.Centimeters
            : SlideSizeDialogUnit.Inches;
        if (newUnit == _unit)
            return;

        var display = SlideSizeDialogPlanner.BuildUnitChangeDisplay(
            _widthBox.Text ?? string.Empty,
            _heightBox.Text ?? string.Empty,
            _unit,
            newUnit);
        _unit = newUnit;
        ApplyDisplay(display);
    }

    private bool Apply()
    {
        LastResultPlan = SlideSizeDialogPlanner.BuildOkResult(
            _widthBox.Text ?? string.Empty,
            _heightBox.Text ?? string.Empty,
            _unit);
        if (!SlideSizeDialogPlanner.TryApplyResult(_editor, LastResultPlan))
        {
            var validation = LastResultPlan.Validation!;
            _validationText.Text = validation.Message;
            _validationText.IsVisible = true;
            FocusField(validation.FocusField);
            return false;
        }

        _validationText.Text = string.Empty;
        _validationText.IsVisible = false;
        if (IsVisible)
            Close(true);
        return true;
    }

    private void ApplyDisplay(SlideSizeDialogDisplayState display)
    {
        _widthBox.Text = display.WidthText;
        _heightBox.Text = display.HeightText;
        ApplyUnitLabels(display.UnitLabel);
    }

    private void ApplyUnitLabels(string label)
    {
        _widthUnitLabel.Text = label;
        _heightUnitLabel.Text = label;
    }

    private void FocusField(SlideSizeDialogField field)
    {
        var box = field switch
        {
            SlideSizeDialogField.Width => _widthBox,
            SlideSizeDialogField.Height => _heightBox,
            _ => null,
        };
        if (box is null)
            return;

        box.Focus();
        box.SelectAll();
    }

    private static TextBlock BuildUnitLabel() => new()
    {
        Width = 28,
        Margin = new Thickness(6, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static void AddLabeledRow(Grid grid, int row, string label, Control field, int span)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = BuildLabel(label);
        Grid.SetRow(text, row);
        grid.Children.Add(text);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        Grid.SetColumnSpan(field, span);
        grid.Children.Add(field);
    }

    private static void AddLabeledRow(Grid grid, int row, string label, Control field, Control suffix)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = BuildLabel(label);
        Grid.SetRow(text, row);
        grid.Children.Add(text);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
        Grid.SetRow(suffix, row);
        Grid.SetColumn(suffix, 2);
        grid.Children.Add(suffix);
    }

    private static TextBlock BuildLabel(string label) => new()
    {
        Text = label,
        Margin = new Thickness(0, 0, 10, 6),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static int ToPresetIndex(SlideSizeDialogPreset preset) => preset switch
    {
        SlideSizeDialogPreset.Widescreen169 => 1,
        SlideSizeDialogPreset.Custom => 2,
        _ => 0,
    };

    private static SlideSizeDialogPreset PresetFromIndex(int selectedIndex) => selectedIndex switch
    {
        1 => SlideSizeDialogPreset.Widescreen169,
        2 => SlideSizeDialogPreset.Custom,
        _ => SlideSizeDialogPreset.Standard43,
    };
}
