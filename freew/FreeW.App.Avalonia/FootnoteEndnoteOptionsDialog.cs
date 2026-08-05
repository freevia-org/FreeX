using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class FootnoteEndnoteOptionsDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle Chrome = AvaloniaCompactDialogChrome.WindowsStyle;
    // Avalonia's Linux text rasterizer places the same 12px dialog glyphs two pixels higher than
    // WPF's ClearType layout. Keep the shared WPF metrics intact and compensate only at this host
    // boundary so the painted authority bounds line up without changing the dialog contract.
    private const double LinuxTopInsetAdjustment = 2;
    private const double LinuxRightInsetAdjustment = -1;
    private const double LinuxActionTopAdjustment = 2;
    private readonly FootnoteEndnoteOptionsDialogSession _session;
    private readonly ComboBox _footnoteFormat;
    private readonly TextBox _footnoteStart;
    private readonly ComboBox _footnoteRestart;
    private readonly ComboBox _endnoteFormat;
    private readonly TextBox _endnoteStart;
    private readonly ComboBox _endnoteRestart;
    private readonly TextBlock _status = new();

    internal FootnoteEndnoteOptionsDialog(NoteNumberingOptions footnote, NoteNumberingOptions endnote)
    {
        _session = FootnoteEndnoteOptionsDialogPlanner.CreateSession(footnote, endnote, CultureInfo.CurrentCulture);
        var state = _session.InitialState;
        Title = FootnoteEndnoteOptionsDialogPlanner.Title;
        Width = FootnoteEndnoteOptionsDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _footnoteFormat = Combo(_session.FormatItems.Select(item => item.Label), state.FootnoteFormatIndex);
        _footnoteStart = TextBox(state.FootnoteStartAtText);
        _footnoteRestart = Combo(_session.FootnoteRestartItems.Select(item => item.Label), state.FootnoteRestartIndex);
        _endnoteFormat = Combo(_session.FormatItems.Select(item => item.Label), state.EndnoteFormatIndex);
        _endnoteStart = TextBox(state.EndnoteStartAtText);
        _endnoteRestart = Combo(_session.EndnoteRestartItems.Select(item => item.Label), state.EndnoteRestartIndex);
        _footnoteFormat.SelectionChanged += (_, _) => _session.UpdateFootnoteFormat(_footnoteFormat.SelectedIndex);
        _footnoteStart.TextChanged += (_, _) => _session.UpdateFootnoteStartAt(_footnoteStart.Text);
        _footnoteRestart.SelectionChanged += (_, _) => _session.UpdateFootnoteRestart(_footnoteRestart.SelectedIndex);
        _endnoteFormat.SelectionChanged += (_, _) => _session.UpdateEndnoteFormat(_endnoteFormat.SelectedIndex);
        _endnoteStart.TextChanged += (_, _) => _session.UpdateEndnoteStartAt(_endnoteStart.Text);
        _endnoteRestart.SelectionChanged += (_, _) => _session.UpdateEndnoteRestart(_endnoteRestart.SelectedIndex);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, Chrome, new Thickness(0, 6, 0, 0));

        var panel = new StackPanel
        {
            Margin = new Thickness(
                FootnoteEndnoteOptionsDialogPlanner.OuterMargin,
                FootnoteEndnoteOptionsDialogPlanner.OuterMargin + LinuxTopInsetAdjustment,
                FootnoteEndnoteOptionsDialogPlanner.OuterMargin + LinuxRightInsetAdjustment,
                FootnoteEndnoteOptionsDialogPlanner.OuterMargin)
        };
        AddSection(panel, FootnoteEndnoteOptionsDialogPlanner.FootnotesSectionLabel, _footnoteFormat, _footnoteStart, _footnoteRestart);
        panel.Children.Add(new Separator
        {
            Margin = new Thickness(
                0,
                FootnoteEndnoteOptionsDialogPlanner.SeparatorTopMargin,
                0,
                FootnoteEndnoteOptionsDialogPlanner.SeparatorBottomMargin)
        });
        AddSection(panel, FootnoteEndnoteOptionsDialogPlanner.EndnotesSectionLabel, _endnoteFormat, _endnoteStart, _endnoteRestart);
        panel.Children.Add(_status);
        panel.Children.Add(AvaloniaCompactDialogChrome.CreateOkCancelRow(
            Accept,
            () => Close(null),
            buttonWidth: FootnoteEndnoteOptionsDialogPlanner.ButtonWidth,
            margin: new Thickness(0, FootnoteEndnoteOptionsDialogPlanner.ActionTopMargin + LinuxActionTopAdjustment, 0, 0),
            style: Chrome));
        Content = panel;
        // WPF's RenderTargetBitmap preserves the focused border but not its text selection. Keep
        // the real recovery path selecting invalid input below, while matching the authority's
        // initial painted state for visual evidence.
        Opened += (_, _) => _footnoteStart.Focus();
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            Close(null);
            args.Handled = true;
        };
    }

    public static Task<FootnoteEndnoteOptionsDialogResult?> ShowAsync(
        Window owner,
        NoteNumberingOptions footnote,
        NoteNumberingOptions endnote) =>
        new FootnoteEndnoteOptionsDialog(footnote, endnote)
            .ShowDialog<FootnoteEndnoteOptionsDialogResult?>(owner);

    internal FootnoteEndnoteOptionsDialogResult? BuildResultForTest()
    {
        SynchronizeSession();
        return _session.PlanAcceptance().Result;
    }

    private void Accept()
    {
        SynchronizeSession();
        var acceptance = _session.PlanAcceptance();
        if (acceptance.IsAccepted)
        {
            Close(acceptance.Result);
            return;
        }
        _status.Text = acceptance.Validation?.Message ?? FootnoteEndnoteOptionsDialogPlanner.PositiveStartAtMessage;
        var target = acceptance.Validation?.Field == FootnoteEndnoteOptionsDialogField.EndnoteStartAt ? _endnoteStart : _footnoteStart;
        AvaloniaCompactDialogChrome.FocusAndSelect(target);
    }

    // The visual harness uses the same non-modal validation path as an attempted OK click without
    // replacing the captured dialog with a second warning window.
    internal void ValidateForTest() => Accept();

    private void SynchronizeSession()
    {
        _session.UpdateFootnoteFormat(_footnoteFormat.SelectedIndex);
        _session.UpdateFootnoteStartAt(_footnoteStart.Text);
        _session.UpdateFootnoteRestart(_footnoteRestart.SelectedIndex);
        _session.UpdateEndnoteFormat(_endnoteFormat.SelectedIndex);
        _session.UpdateEndnoteStartAt(_endnoteStart.Text);
        _session.UpdateEndnoteRestart(_endnoteRestart.SelectedIndex);
    }

    private static void AddSection(StackPanel panel, string heading, ComboBox format, TextBox start, ComboBox restart)
    {
        panel.Children.Add(new TextBlock
        {
            Text = heading,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, FootnoteEndnoteOptionsDialogPlanner.SectionHeaderBottomMargin)
        });
        panel.Children.Add(OptionsGrid(format, start, restart));
    }

    private static Grid OptionsGrid(ComboBox format, TextBox start, ComboBox restart)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, FootnoteEndnoteOptionsDialogPlanner.NumberFormatLabel, format);
        AddRow(grid, 1, FootnoteEndnoteOptionsDialogPlanner.StartAtLabel, start);
        AddRow(grid, 2, FootnoteEndnoteOptionsDialogPlanner.NumberingLabel, restart);
        return grid;
    }

    private static void AddRow(Grid grid, int row, string label, Control control)
    {
        var block = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(
                0,
                FootnoteEndnoteOptionsDialogPlanner.FieldVerticalMargin,
                FootnoteEndnoteOptionsDialogPlanner.LabelFieldGap,
                FootnoteEndnoteOptionsDialogPlanner.FieldVerticalMargin)
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);

        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.Margin = new Thickness(
            0,
            FootnoteEndnoteOptionsDialogPlanner.FieldVerticalMargin,
            0,
            FootnoteEndnoteOptionsDialogPlanner.FieldVerticalMargin);
        grid.Children.Add(control);
    }

    private static ComboBox Combo(IEnumerable<string> items, int selectedIndex)
    {
        var combo = new ComboBox { ItemsSource = items.ToArray(), SelectedIndex = selectedIndex };
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, Chrome);
        return combo;
    }

    private static TextBox TextBox(string text)
    {
        var box = new TextBox
        {
            Text = text,
            MinWidth = FootnoteEndnoteOptionsDialogPlanner.StartAtMinWidth
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, Chrome);
        return box;
    }
}
