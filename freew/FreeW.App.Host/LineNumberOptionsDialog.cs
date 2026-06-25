using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// "Line Numbering Options" dialog: Start At, Count By, and Restart mode (Continuous / Each Page).
/// Mirrors Word's Layout &gt; Page Setup &gt; Line Numbers &gt; Line Numbering Options, but surfaced as a
/// dedicated lightweight dialog so it doesn't require navigating through Page Setup &gt; Layout.
/// Returns null when the user cancels, or a <see cref="Result"/> record on OK.
/// </summary>
internal sealed class LineNumberOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _startAtBox;
    private readonly TextBox _countByBox;
    private readonly ComboBox _modeBox;
    private Result? _result;

    private static readonly string[] ModeLabels = ["Continuous", "Restart Each Page"];

    private LineNumberOptionsDialog(Window? owner, int startAt, int countBy, LineNumberMode mode)
    {
        Owner = owner;
        Title = "Line Numbering Options";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _startAtBox = new TextBox { Text = startAt.ToString(CultureInfo.CurrentCulture), MinWidth = 80 };
        _countByBox = new TextBox { Text = countBy.ToString(CultureInfo.CurrentCulture), MinWidth = 80 };

        _modeBox = new ComboBox { MinWidth = 140 };
        foreach (var label in ModeLabels) _modeBox.Items.Add(label);
        _modeBox.SelectedIndex = mode == LineNumberMode.RestartEachPage ? 1 : 0;

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        static void Place(Grid g, UIElement el, int row, int col)
        {
            Grid.SetRow(el, row); Grid.SetColumn(el, col); g.Children.Add(el);
        }

        Place(grid, Lbl("Start at:"),    0, 0); Place(grid, _startAtBox, 0, 1);
        Place(grid, Lbl("Count by:"),    1, 0); Place(grid, _countByBox, 1, 1);
        Place(grid, Lbl("Numbering:"),   2, 0); Place(grid, _modeBox, 2, 1);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Place(grid, buttons, 3, 1);

        Content = grid;
        DialogFocus.FocusAndSelect(_startAtBox);
    }

    private static TextBlock Lbl(string text) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };

    private void Accept()
    {
        if (!int.TryParse(_startAtBox.Text, out var startAt) || startAt < 1)
        {
            DialogMessageHelper.ShowWarning(this, "Start At must be a whole number of 1 or greater.");
            return;
        }
        if (!int.TryParse(_countByBox.Text, out var countBy) || countBy < 1)
        {
            DialogMessageHelper.ShowWarning(this, "Count By must be a whole number of 1 or greater.");
            return;
        }
        var mode = _modeBox.SelectedIndex == 1
            ? LineNumberMode.RestartEachPage
            : LineNumberMode.Continuous;
        _result = new Result(startAt, countBy, mode);
        Close();
    }

    /// <summary>Return value of the dialog when the user clicks OK.</summary>
    internal sealed record Result(int StartAt, int CountBy, LineNumberMode Mode);

    /// <summary>
    /// Show the Line Numbering Options dialog. Returns the user-chosen values, or null on cancel.
    /// </summary>
    public static Result? Prompt(Window? owner, int startAt, int countBy, LineNumberMode mode)
    {
        var dialog = new LineNumberOptionsDialog(owner, startAt, countBy, mode);
        dialog.ShowDialog();
        return dialog._result;
    }
}
