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
    private readonly ComboBox _presetBox;
    private readonly TextBox _countBox;
    private readonly TextBox _spacingBox;
    private readonly CheckBox _lineBetween;
    private readonly double _contentWidthPt;
    private ColumnsDialogResult? _result;

    private ColumnsDialog(Window? owner, PageSettings page)
    {
        Owner = owner;
        Title = "Columns";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var plan = ColumnsDialogPlanner.BuildInitialState(page, CultureInfo.CurrentCulture);
        _contentWidthPt = plan.ContentWidthPt;

        _countBox = NumberBox(plan.CountText);
        _spacingBox = NumberBox(plan.SpacingText);
        _lineBetween = new CheckBox { Content = "Line between", IsChecked = plan.LineBetween, Margin = new Thickness(0, 6, 0, 0) };

        _presetBox = new ComboBox { MinWidth = 140 };
        foreach (var preset in ColumnsDialogPlanner.Presets)
            _presetBox.Items.Add(preset.Label);
        _presetBox.SelectedIndex = plan.PresetIndex;
        _presetBox.SelectionChanged += (_, _) => ApplySelectedPreset();

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Presets:", _presetBox);
        AddRow(grid, 1, "Number of columns:", _countBox);
        AddRow(grid, 2, "Spacing (pt):", _spacingBox);

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
        var count = ColumnsDialogPlanner.ColumnCountForPreset(_presetBox.SelectedIndex);
        _countBox.Text = count.ToString(CultureInfo.CurrentCulture);
    }

    private void Accept()
    {
        var input = new ColumnsDialogInput(
            _presetBox.SelectedIndex,
            _countBox.Text,
            _spacingBox.Text,
            _lineBetween.IsChecked == true,
            _contentWidthPt);

        if (!ColumnsDialogPlanner.TryBuildResult(input, CultureInfo.CurrentCulture, out var result, out var errorMessage))
        {
            DialogMessageHelper.ShowWarning(this, errorMessage ?? ColumnsDialogPlanner.ValidationMessage);
            return;
        }

        _result = result;
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
