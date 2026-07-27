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
    private readonly ComboBox _footnoteFormat;
    private readonly TextBox _footnoteStart;
    private readonly ComboBox _footnoteRestart;
    private readonly ComboBox _endnoteFormat;
    private readonly TextBox _endnoteStart;
    private readonly ComboBox _endnoteRestart;
    private readonly TextBlock _status = new();

    internal FootnoteEndnoteOptionsDialog(NoteNumberingOptions footnote, NoteNumberingOptions endnote)
    {
        var state = FootnoteEndnoteOptionsDialogPlanner.BuildInitialState(footnote, endnote, CultureInfo.CurrentCulture);
        Title = FootnoteEndnoteOptionsDialogPlanner.Title;
        Width = FootnoteEndnoteOptionsDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _footnoteFormat = Combo(FootnoteEndnoteOptionsDialogPlanner.FormatItems.Select(item => item.Label), state.FootnoteFormatIndex);
        _footnoteStart = TextBox(state.FootnoteStartAtText);
        _footnoteRestart = Combo(FootnoteEndnoteOptionsDialogPlanner.FootnoteRestartItems.Select(item => item.Label), state.FootnoteRestartIndex);
        _endnoteFormat = Combo(FootnoteEndnoteOptionsDialogPlanner.FormatItems.Select(item => item.Label), state.EndnoteFormatIndex);
        _endnoteStart = TextBox(state.EndnoteStartAtText);
        _endnoteRestart = Combo(FootnoteEndnoteOptionsDialogPlanner.EndnoteRestartItems.Select(item => item.Label), state.EndnoteRestartIndex);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, Chrome, new Thickness(0, 6, 0, 0));

        var panel = new StackPanel
        {
            Margin = new Thickness(FootnoteEndnoteOptionsDialogPlanner.OuterMargin)
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
            margin: new Thickness(0, FootnoteEndnoteOptionsDialogPlanner.ActionTopMargin, 0, 0),
            style: Chrome));
        Content = panel;
        Opened += (_, _) => _footnoteFormat.Focus();
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

    internal FootnoteEndnoteOptionsDialogResult? BuildResultForTest() =>
        FootnoteEndnoteOptionsDialogPlanner.TryBuildResult(
            Input(), CultureInfo.CurrentCulture, out var result, out _) ? result : null;

    private void Accept()
    {
        if (FootnoteEndnoteOptionsDialogPlanner.TryBuildResult(
                Input(), CultureInfo.CurrentCulture, out var result, out var validation))
        {
            Close(result);
            return;
        }
        _status.Text = validation?.Message ?? FootnoteEndnoteOptionsDialogPlanner.PositiveStartAtMessage;
        var target = validation?.Field == FootnoteEndnoteOptionsDialogField.EndnoteStartAt ? _endnoteStart : _footnoteStart;
        target.Focus();
        target.SelectAll();
    }

    // The visual harness uses the same non-modal validation path as an attempted OK click without
    // replacing the captured dialog with a second warning window.
    internal void ValidateForTest() => Accept();

    private FootnoteEndnoteOptionsDialogInput Input() => new(
        _footnoteFormat.SelectedIndex, _footnoteStart.Text, _footnoteRestart.SelectedIndex,
        _endnoteFormat.SelectedIndex, _endnoteStart.Text, _endnoteRestart.SelectedIndex);

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
