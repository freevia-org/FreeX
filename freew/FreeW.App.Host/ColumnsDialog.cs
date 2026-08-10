using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Columns" dialog (Layout &gt; Page Setup &gt; Columns &gt; More Columns...). Lets the user pick a
/// preset - One / Two / Three / Left / Right - or a custom number of columns, with a column spacing and a
/// "line between" toggle. Returns the chosen column settings to apply to <see cref="PageSettings"/>, or
/// null if cancelled.
///
/// <para>
/// The dialog stays WPF-only chrome: preset catalog, initial state, parsing, validation, and result
/// resolution live in the shared presentation planner.
/// </para>
/// </summary>
internal sealed class ColumnsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ColumnsDialogSession _session;
    private readonly ComboBox _presetBox;
    private readonly TextBox _countBox;
    private readonly TextBox _spacingBox;
    private readonly CheckBox _lineBetween;
    private ColumnsDialogResult? _result;

    private ColumnsDialog(Window? owner, PageSettings page)
    {
        var surface = ColumnsDialogPlanner.Surface;
        _session = new ColumnsDialogSession(page, CultureInfo.CurrentCulture);
        Owner = owner;
        Title = surface.Title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var plan = _session.InitialState;

        _countBox = NumberBox(plan.CountText);
        _spacingBox = NumberBox(plan.SpacingText);
        _lineBetween = new CheckBox { Content = surface.Field(ColumnsDialogField.LineBetween).Label, IsChecked = plan.LineBetween, Margin = new Thickness(0, 6, 0, 0) };

        _presetBox = new ComboBox { MinWidth = 140 };
        foreach (var preset in _session.Presets)
            _presetBox.Items.Add(preset.Label);
        _presetBox.SelectedIndex = plan.PresetIndex;
        _presetBox.SelectionChanged += (_, _) => ApplySelectedPreset();
        PageLayoutDialogSurfaceSemantics.Apply(this, surface);
        PageLayoutDialogSurfaceSemantics.Apply(_presetBox, surface.Field(ColumnsDialogField.Preset));
        PageLayoutDialogSurfaceSemantics.Apply(_countBox, surface.Field(ColumnsDialogField.Count));
        PageLayoutDialogSurfaceSemantics.Apply(_spacingBox, surface.Field(ColumnsDialogField.Spacing));
        PageLayoutDialogSurfaceSemantics.Apply(_lineBetween, surface.Field(ColumnsDialogField.LineBetween));

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, surface.Field(ColumnsDialogField.Preset).Label, _presetBox);
        AddRow(grid, 1, surface.Field(ColumnsDialogField.Count).Label, _countBox);
        AddRow(grid, 2, surface.Field(ColumnsDialogField.Spacing).Label, _spacingBox);

        Grid.SetRow(_lineBetween, 3);
        Grid.SetColumn(_lineBetween, 1);
        grid.Children.Add(_lineBetween);

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 4);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        DialogFocus.FocusAndSelect(_countBox);
    }

    private static TextBox NumberBox(string value) => new()
    {
        Text = value,
        MinWidth = 120
    };

    private static void AddRow(Grid grid, int row, string label, UIElement field)
    {
        var block = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 4, 0, 4);
        grid.Children.Add(field);
    }

    private void ApplySelectedPreset()
    {
        _countBox.Text = _session.CountTextForPreset(_presetBox.SelectedIndex);
    }

    private void Accept()
    {
        var acceptance = _session.PlanAcceptance(
            _presetBox.SelectedIndex,
            _countBox.Text,
            _spacingBox.Text,
            _lineBetween.IsChecked == true);
        if (!acceptance.IsAccepted)
        {
            DialogMessageHelper.ShowWarning(this, acceptance.ValidationMessage);
            return;
        }

        _result = acceptance.Result;
        Close();
    }

    /// <summary>
    /// Show the dialog seeded with the current page columns; returns the chosen settings, or null if
    /// cancelled.
    /// </summary>
    public static ColumnsDialogResult? Prompt(Window? owner, PageSettings page)
    {
        var dialog = new ColumnsDialog(owner, page);
        dialog.ShowDialog();
        return dialog._result;
    }
}
