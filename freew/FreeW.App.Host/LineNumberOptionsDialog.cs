using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
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
    private readonly LineNumberOptionsDialogSession _session;
    private readonly TextBox _startAtBox;
    private readonly TextBox _countByBox;
    private readonly ComboBox _modeBox;
    private LineNumberOptionsDialogResult? _result;

    private LineNumberOptionsDialog(Window? owner, int startAt, int countBy, LineNumberMode mode)
    {
        var surface = LineNumberOptionsDialogPlanner.Surface;
        _session = new LineNumberOptionsDialogSession(
            startAt,
            countBy,
            mode,
            CultureInfo.CurrentCulture);
        Owner = owner;
        Title = surface.Title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var state = _session.InitialState;

        _startAtBox = new TextBox { Text = state.StartAtText, MinWidth = 80 };
        _countByBox = new TextBox { Text = state.CountByText, MinWidth = 80 };

        _modeBox = new ComboBox { MinWidth = 140 };
        foreach (var label in _session.ModeLabels) _modeBox.Items.Add(label);
        _modeBox.SelectedIndex = state.ModeIndex;
        WpfDialogSurfaceSemantics.Apply(this, surface);
        WpfDialogSurfaceSemantics.Apply(_startAtBox, surface.Field(LineNumberOptionsDialogField.StartAt));
        WpfDialogSurfaceSemantics.Apply(_countByBox, surface.Field(LineNumberOptionsDialogField.CountBy));
        WpfDialogSurfaceSemantics.Apply(_modeBox, surface.Field(LineNumberOptionsDialogField.Numbering));

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        static void Place(Grid g, UIElement el, int row, int col)
        {
            Grid.SetRow(el, row); Grid.SetColumn(el, col); g.Children.Add(el);
        }

        Place(grid, Lbl(surface.Field(LineNumberOptionsDialogField.StartAt).Label), 0, 0); Place(grid, _startAtBox, 0, 1);
        Place(grid, Lbl(surface.Field(LineNumberOptionsDialogField.CountBy).Label), 1, 0); Place(grid, _countByBox, 1, 1);
        Place(grid, Lbl(surface.Field(LineNumberOptionsDialogField.Numbering).Label), 2, 0); Place(grid, _modeBox, 2, 1);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Place(grid, buttons, 3, 1);

        Content = grid;
        DialogFocus.FocusAndSelect(_startAtBox);
    }

    private static TextBlock Lbl(string text) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };

    private void Accept()
    {
        var input = new LineNumberOptionsDialogInput(
            _startAtBox.Text,
            _countByBox.Text,
            _modeBox.SelectedIndex);

        var acceptance = _session.PlanAcceptance(input);
        if (!acceptance.IsAccepted)
        {
            DialogMessageHelper.ShowWarning(this, acceptance.ValidationMessage);
            return;
        }

        _result = acceptance.Result;
        Close();
    }

    /// <summary>
    /// Show the Line Numbering Options dialog. Returns the user-chosen values, or null on cancel.
    /// </summary>
    public static LineNumberOptionsDialogResult? Prompt(Window? owner, int startAt, int countBy, LineNumberMode mode)
    {
        var dialog = new LineNumberOptionsDialog(owner, startAt, countBy, mode);
        dialog.ShowDialog();
        return dialog._result;
    }
}
