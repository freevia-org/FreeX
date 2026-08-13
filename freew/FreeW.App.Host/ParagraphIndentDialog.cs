using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// A small modal dialog collecting a paragraph's left/right indents plus a first-line "special"
/// indent (None / First line / Hanging) with its amount, all in points. Returns the chosen indents,
/// or null if the user cancels.
/// </summary>
internal sealed class ParagraphIndentDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _leftBox;
    private readonly TextBox _rightBox;
    private readonly ComboBox _specialBox;
    private readonly TextBox _specialAmountBox;
    private (double Left, double Right, double FirstLine)? _result;

    private ParagraphIndentDialog(Window? owner, double leftPt, double rightPt, double firstLinePt)
    {
        var surface = ParagraphIndentDialogPlanner.CompactSurface;
        Owner = owner;
        Title = surface.Title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WpfDialogSurfaceSemantics.Apply(this, surface);

        var state = ParagraphIndentDialogPlanner.BuildInitialState(
            leftPt,
            rightPt,
            firstLinePt,
            CultureInfo.CurrentCulture);

        _leftBox = NumberBox(state.LeftText);
        _rightBox = NumberBox(state.RightText);
        _specialAmountBox = NumberBox(state.SpecialAmountText);

        _specialBox = new ComboBox { MinWidth = 120 };
        foreach (var item in ParagraphIndentDialogPlanner.SpecialItems)
            _specialBox.Items.Add(item.Label);
        _specialBox.SelectedIndex = state.SpecialIndex;
        _specialBox.SelectionChanged += (_, _) =>
            _specialAmountBox.IsEnabled = ParagraphIndentDialogPlanner.IsSpecialAmountEnabled(_specialBox.SelectedIndex);
        _specialAmountBox.IsEnabled = state.SpecialAmountEnabled;
        WpfDialogSurfaceSemantics.Apply(_leftBox, surface.Field(ParagraphIndentDialogField.Left));
        WpfDialogSurfaceSemantics.Apply(_rightBox, surface.Field(ParagraphIndentDialogField.Right));
        WpfDialogSurfaceSemantics.Apply(_specialBox, surface.Field(ParagraphIndentDialogField.Special));
        WpfDialogSurfaceSemantics.Apply(_specialAmountBox, surface.Field(ParagraphIndentDialogField.SpecialAmount));

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, surface.Field(ParagraphIndentDialogField.Left).Label, _leftBox);
        AddRow(grid, 1, surface.Field(ParagraphIndentDialogField.Right).Label, _rightBox);
        AddRow(grid, 2, surface.Field(ParagraphIndentDialogField.Special).Label, _specialBox);
        AddRow(grid, 3, surface.Field(ParagraphIndentDialogField.SpecialAmount).Label, _specialAmountBox);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 4);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        DialogFocus.FocusAndSelect(_leftBox);
    }

    private static TextBox NumberBox(string text) => new()
    {
        Text = text,
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

    private void Accept()
    {
        var input = new ParagraphIndentDialogInput(
            _leftBox.Text,
            _rightBox.Text,
            _specialBox.SelectedIndex,
            _specialAmountBox.Text);

        if (!ParagraphIndentDialogPlanner.TryBuildResult(
                input,
                CultureInfo.CurrentCulture,
                out var result,
                out var validation))
        {
            DialogMessageHelper.ShowWarning(this, validation?.Message ?? ParagraphIndentDialogPlanner.ValidationMessage);
            FocusFailure(validation?.Field);
            return;
        }

        _result = (result!.LeftPt, result.RightPt, result.FirstLinePt);
        Close();
    }

    private void FocusFailure(ParagraphIndentDialogField? field)
    {
        var target = field switch
        {
            ParagraphIndentDialogField.Right => _rightBox,
            ParagraphIndentDialogField.SpecialAmount => _specialAmountBox,
            _ => _leftBox
        };
        DialogFocus.FocusAndSelect(target);
    }

    /// <summary>
    /// Show the dialog seeded with the current indents; returns the chosen (left, right, firstLine) in
    /// points (firstLine signed per the hanging convention), or null if cancelled.
    /// </summary>
    public static (double Left, double Right, double FirstLine)? Prompt(
        Window? owner, double leftPt, double rightPt, double firstLinePt)
    {
        var dialog = new ParagraphIndentDialog(owner, leftPt, rightPt, firstLinePt);
        dialog.ShowDialog();
        return dialog._result;
    }
}
