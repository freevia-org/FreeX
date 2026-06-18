using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FreeW.App.Host;

/// <summary>
/// A small modal dialog collecting a paragraph's left/right indents plus a first-line "special"
/// indent (None / First line / Hanging) with its amount, all in points. Returns the chosen indents,
/// or null if the user cancels.
///
/// <para>
/// First-line convention (matching <see cref="FreeW.Core.Model.Indentation"/>): the returned
/// <c>FirstLine</c> is positive for a first-line indent and negative for a hanging indent; "None"
/// returns zero. The amount box holds the magnitude; the special drop-down picks the sign.
/// </para>
/// </summary>
internal sealed class ParagraphIndentDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private enum Special { None, FirstLine, Hanging }

    private readonly TextBox _leftBox;
    private readonly TextBox _rightBox;
    private readonly ComboBox _specialBox;
    private readonly TextBox _specialAmountBox;
    private (double Left, double Right, double FirstLine)? _result;

    private ParagraphIndentDialog(Window? owner, double leftPt, double rightPt, double firstLinePt)
    {
        Owner = owner;
        Title = "Paragraph";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        // Map the signed first-line indent back to a special kind + magnitude for editing.
        var special = firstLinePt > 0 ? Special.FirstLine : firstLinePt < 0 ? Special.Hanging : Special.None;
        var specialAmount = Math.Abs(firstLinePt);

        _leftBox = NumberBox(leftPt);
        _rightBox = NumberBox(rightPt);
        _specialAmountBox = NumberBox(specialAmount);

        _specialBox = new ComboBox { MinWidth = 120 };
        _specialBox.Items.Add("(none)");
        _specialBox.Items.Add("First line");
        _specialBox.Items.Add("Hanging");
        _specialBox.SelectedIndex = (int)special;
        _specialBox.SelectionChanged += (_, _) =>
            _specialAmountBox.IsEnabled = _specialBox.SelectedIndex != (int)Special.None;
        _specialAmountBox.IsEnabled = special != Special.None;

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Left (pt):", _leftBox);
        AddRow(grid, 1, "Right (pt):", _rightBox);
        AddRow(grid, 2, "Special:", _specialBox);
        AddRow(grid, 3, "By (pt):", _specialAmountBox);

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 4);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        DialogFocus.FocusAndSelect(_leftBox);
    }

    private static TextBox NumberBox(double value) => new()
    {
        Text = value.ToString("0.##", CultureInfo.CurrentCulture),
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
        if (!TryParse(_leftBox.Text, out var left) || left < 0
            || !TryParse(_rightBox.Text, out var right) || right < 0
            || !TryParse(_specialAmountBox.Text, out var amount) || amount < 0)
        {
            DialogMessageHelper.ShowWarning(this, "Enter non-negative indent values in points.");
            return;
        }

        var firstLine = _specialBox.SelectedIndex switch
        {
            (int)Special.FirstLine => amount,
            (int)Special.Hanging => -amount,
            _ => 0.0
        };

        _result = (left, right, firstLine);
        Close();
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

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
