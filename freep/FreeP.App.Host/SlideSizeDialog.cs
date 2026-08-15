using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

/// <summary>
/// Modal slide-size dialog. WPF owns the controls and localization shell; shared
/// presentation policy lives in <see cref="SlideSizeDialogPlanner"/>.
/// </summary>
public sealed partial class SlideSizeDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly SlideSizeDialogSession _session;
    private bool _suppressPresetRefresh;

    private readonly ComboBox _presetCombo;
    private readonly RadioButton _inchesRadio;
    private readonly RadioButton _cmRadio;
    private readonly TextBox _widthBox;
    private readonly TextBox _heightBox;
    private readonly Label _widthUnitLabel;
    private readonly Label _heightUnitLabel;

    internal SlideSizeDialogResultPlan? LastResultPlan => _session.LastResultPlan;
    internal SlideSizeDialogInitialState InitialState => _session.InitialState;
    internal string WidthText => _widthBox.Text;
    internal string HeightText => _heightBox.Text;
    internal string ValidationText => LastResultPlan?.Validation?.Message ?? string.Empty;

    public SlideSizeDialog(EditingSession editor)
    {
        _session = new SlideSizeDialogSession(editor);
        var surface = _session.Surface;

        Title = surface.Title;
        AutomationProperties.SetName(this, surface.Schema.AccessibleName);
        AutomationProperties.SetAutomationId(this, surface.Schema.AutomationId);
        Width = 380;
        Height = 260;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _presetCombo = new ComboBox
        {
            ItemsSource = surface.PresetNames,
            Margin = new Thickness(4),
        };
        _presetCombo.SelectedIndex = 0;
        _presetCombo.SelectionChanged += OnPresetChanged;

        _inchesRadio = new RadioButton
        {
            Content = surface.UnitLabel(SlideSizeDialogUnit.Inches),
            IsChecked = true,
            Margin = new Thickness(4, 0, 12, 0)
        };
        _cmRadio = new RadioButton
        {
            Content = surface.UnitLabel(SlideSizeDialogUnit.Centimeters),
            IsChecked = false,
            Margin = new Thickness(4, 0, 4, 0)
        };
        _inchesRadio.Checked += OnUnitChanged;
        _cmRadio.Checked += OnUnitChanged;

        _widthBox = MakeNumericBox();
        _heightBox = MakeNumericBox();

        _widthUnitLabel = new Label { Content = _session.State.Display.UnitLabel, Width = 30 };
        _heightUnitLabel = new Label { Content = _session.State.Display.UnitLabel, Width = 30 };

        PresentationDialogControlAdapter.ApplySemantic(_presetCombo, surface.Field(SlideSizeDialogSurfaceField.Preset));
        PresentationDialogControlAdapter.ApplySemantic(_inchesRadio, surface.Field(SlideSizeDialogSurfaceField.Unit), ".Inches");
        PresentationDialogControlAdapter.ApplySemantic(_cmRadio, surface.Field(SlideSizeDialogSurfaceField.Unit), ".Centimeters");
        PresentationDialogControlAdapter.ApplySemantic(_widthBox, surface.Field(SlideSizeDialogSurfaceField.Width));
        PresentationDialogControlAdapter.ApplySemantic(_heightBox, surface.Field(SlideSizeDialogSurfaceField.Height));

        LoadInitialState();

        var btnRow = DialogButtonRowFactory.Create(
            OnOk,
            buttonWidth: 80,
            rowMargin: new Thickness(4, 8, 8, 8),
            acceptContent: surface.Action(SlideSizeDialogAction.Accept).Label,
            cancelContent: surface.Action(SlideSizeDialogAction.Cancel).Label);

        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabel(grid, surface.Field(SlideSizeDialogSurfaceField.Preset).Label, 0, 0);
        Grid.SetRow(_presetCombo, 0);
        Grid.SetColumn(_presetCombo, 1);
        Grid.SetColumnSpan(_presetCombo, 2);
        grid.Children.Add(_presetCombo);

        AddLabel(grid, surface.Field(SlideSizeDialogSurfaceField.Unit).Label, 1, 0);
        var unitPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
        unitPanel.Children.Add(_inchesRadio);
        unitPanel.Children.Add(_cmRadio);
        Grid.SetRow(unitPanel, 1);
        Grid.SetColumn(unitPanel, 1);
        Grid.SetColumnSpan(unitPanel, 2);
        grid.Children.Add(unitPanel);

        AddLabel(grid, surface.Field(SlideSizeDialogSurfaceField.Width).Label, 2, 0);
        Grid.SetRow(_widthBox, 2);
        Grid.SetColumn(_widthBox, 1);
        grid.Children.Add(_widthBox);
        Grid.SetRow(_widthUnitLabel, 2);
        Grid.SetColumn(_widthUnitLabel, 2);
        grid.Children.Add(_widthUnitLabel);

        AddLabel(grid, surface.Field(SlideSizeDialogSurfaceField.Height).Label, 3, 0);
        Grid.SetRow(_heightBox, 3);
        Grid.SetColumn(_heightBox, 1);
        grid.Children.Add(_heightBox);
        Grid.SetRow(_heightUnitLabel, 3);
        Grid.SetColumn(_heightUnitLabel, 2);
        grid.Children.Add(_heightUnitLabel);

        Grid.SetRow(btnRow, 5);
        Grid.SetColumn(btnRow, 0);
        Grid.SetColumnSpan(btnRow, 3);
        grid.Children.Add(btnRow);

        Content = grid;
    }

    public bool TryParseEmu(out long cxEmu, out long cyEmu)
    {
        var parse = _session.TryParse(_widthBox.Text, _heightBox.Text);

        cxEmu = parse.CxEmu;
        cyEmu = parse.CyEmu;
        return parse.IsValid;
    }

    private void LoadInitialState()
    {
        _suppressPresetRefresh = true;
        try
        {
            _presetCombo.SelectedIndex = _session.State.PresetIndex;
        }
        finally
        {
            _suppressPresetRefresh = false;
        }

        ApplyDisplay(_session.State.Display);
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetRefresh)
        {
            return;
        }

        var display = _session.SelectPreset(_presetCombo.SelectedIndex);
        if (display is not null)
        {
            ApplyDisplay(display);
        }
    }

    private void OnUnitChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressPresetRefresh)
            return;

        var newUnit = _inchesRadio.IsChecked == true
            ? SlideSizeDialogUnit.Inches
            : SlideSizeDialogUnit.Centimeters;

        if (_session.State.Unit == newUnit)
        {
            return;
        }

        var state = _session.ChangeUnit(
            _widthBox.Text,
            _heightBox.Text,
            newUnit);
        ApplyDisplay(state.Display);
    }

    private void OnOk()
        => Apply(showValidationDialog: true);

    private bool Apply(bool showValidationDialog)
    {
        if (!_session.TryCommit(_widthBox.Text, _heightBox.Text))
        {
            var validation = LastResultPlan!.Validation!;
            if (showValidationDialog)
                DialogMessageHelper.ShowWarning(this, validation.Message, validation.Caption);
            FocusField(validation.FocusField);
            return false;
        }

        if (IsLoaded)
        {
            DialogResult = true;
            Close();
        }
        return true;
    }

    private void ApplyDisplay(SlideSizeDialogDisplayState display)
    {
        _widthBox.Text = display.WidthText;
        _heightBox.Text = display.HeightText;
        _widthUnitLabel.Content = display.UnitLabel;
        _heightUnitLabel.Content = display.UnitLabel;
    }

    private void FocusField(SlideSizeDialogField field)
    {
        var box = field switch
        {
            SlideSizeDialogField.Width => _widthBox,
            SlideSizeDialogField.Height => _heightBox,
            _ => null
        };

        if (box is not null)
            DialogFocus.FocusAndSelect(box);
    }

    private static TextBox MakeNumericBox() => new()
    {
        Width = 120,
        Margin = new Thickness(4),
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private static void AddLabel(Grid grid, string text, int row, int col)
    {
        var lbl = new Label
        {
            Content = text,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(4, 2, 4, 2)
        };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, col);
        grid.Children.Add(lbl);
    }

}
