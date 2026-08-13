using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed partial class FootnoteEndnoteOptionsDialog : FreeWDialogWindow
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
        var surface = FootnoteEndnoteOptionsDialogPlanner.Surface;
        Title = surface.Title;
        Width = surface.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var controls = surface.Sections.ToDictionary(section => section.Kind, CreateControls);
        (_footnoteFormat, _footnoteStart, _footnoteRestart) = controls[FootnoteEndnoteNoteKind.Footnote];
        (_endnoteFormat, _endnoteStart, _endnoteRestart) = controls[FootnoteEndnoteNoteKind.Endnote];
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, Chrome, new Thickness(0, 6, 0, 0));

        var panel = new StackPanel
        {
            Margin = new Thickness(
                surface.OuterMargin,
                surface.OuterMargin + LinuxTopInsetAdjustment,
                surface.OuterMargin + LinuxRightInsetAdjustment,
                surface.OuterMargin)
        };
        foreach (var section in surface.Sections)
        {
            if (section.Kind != FootnoteEndnoteNoteKind.Footnote)
            {
                panel.Children.Add(new Separator
                {
                    Margin = new Thickness(0, surface.SeparatorTopMargin, 0, surface.SeparatorBottomMargin)
                });
            }
            AddSection(panel, section, controls[section.Kind], surface);
        }
        panel.Children.Add(_status);
        panel.Children.Add(AvaloniaCompactDialogChrome.CreateOkCancelRow(
            Accept,
            () => Close(null),
            buttonWidth: surface.ButtonWidth,
            margin: new Thickness(0, surface.ActionTopMargin + LinuxActionTopAdjustment, 0, 0),
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

        (ComboBox Format, TextBox StartAt, ComboBox Restart) CreateControls(
            FootnoteEndnoteSectionSpec section)
        {
            var format = Combo(_session.FormatItems.Select(item => item.Label), state.FormatIndex(section.Kind));
            var startAt = TextBox(state.StartAtText(section.Kind), section.Field(FootnoteEndnoteFieldKind.StartAt).MinWidth);
            var restart = Combo(_session.RestartItems(section.Kind).Select(item => item.Label), state.RestartIndex(section.Kind));
            AutomationProperties.SetAutomationId(format, section.Field(FootnoteEndnoteFieldKind.NumberFormat).AutomationId);
            AutomationProperties.SetAutomationId(startAt, section.Field(FootnoteEndnoteFieldKind.StartAt).AutomationId);
            AutomationProperties.SetAutomationId(restart, section.Field(FootnoteEndnoteFieldKind.Numbering).AutomationId);
            format.SelectionChanged += (_, _) => _session.UpdateIndex(section.Kind, FootnoteEndnoteFieldKind.NumberFormat, format.SelectedIndex);
            startAt.TextChanged += (_, _) => _session.UpdateStartAt(section.Kind, startAt.Text);
            restart.SelectionChanged += (_, _) => _session.UpdateIndex(section.Kind, FootnoteEndnoteFieldKind.Numbering, restart.SelectedIndex);
            return (format, startAt, restart);
        }
    }

    public static Task<FootnoteEndnoteOptionsDialogResult?> ShowAsync(
        Window owner,
        NoteNumberingOptions footnote,
        NoteNumberingOptions endnote) =>
        new FootnoteEndnoteOptionsDialog(footnote, endnote)
            .ShowDialog<FootnoteEndnoteOptionsDialogResult?>(owner);

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

    private void SynchronizeSession()
    {
        Synchronize(FootnoteEndnoteNoteKind.Footnote, _footnoteFormat, _footnoteStart, _footnoteRestart);
        Synchronize(FootnoteEndnoteNoteKind.Endnote, _endnoteFormat, _endnoteStart, _endnoteRestart);
    }

    private void Synchronize(FootnoteEndnoteNoteKind note, ComboBox format, TextBox startAt, ComboBox restart)
    {
        _session.UpdateIndex(note, FootnoteEndnoteFieldKind.NumberFormat, format.SelectedIndex);
        _session.UpdateStartAt(note, startAt.Text);
        _session.UpdateIndex(note, FootnoteEndnoteFieldKind.Numbering, restart.SelectedIndex);
    }

    private static void AddSection(
        StackPanel panel,
        FootnoteEndnoteSectionSpec section,
        (ComboBox Format, TextBox StartAt, ComboBox Restart) controls,
        FootnoteEndnoteSurfaceSpec surface)
    {
        var header = new TextBlock
        {
            Text = section.Label,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, surface.SectionHeaderBottomMargin)
        };
        AutomationProperties.SetAutomationId(header, section.AutomationId);
        panel.Children.Add(header);
        var grid = AvaloniaLabeledFormRow.CreateCompactGrid(3);

        var fields = new Control[] { controls.Format, controls.StartAt, controls.Restart };
        for (var row = 0; row < section.Fields.Count; row++)
            AddRow(grid, row, section.Fields[row].Label, fields[row], surface);
        panel.Children.Add(grid);
    }

    private static void AddRow(
        Grid grid,
        int row,
        string label,
        Control control,
        FootnoteEndnoteSurfaceSpec surface)
    {
        var block = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(
                0,
                surface.FieldVerticalMargin,
                surface.LabelFieldGap,
                surface.FieldVerticalMargin)
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);

        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.Margin = new Thickness(
            0,
            surface.FieldVerticalMargin,
            0,
            surface.FieldVerticalMargin);
        grid.Children.Add(control);
    }

    private static ComboBox Combo(IEnumerable<string> items, int selectedIndex)
    {
        var combo = new ComboBox { ItemsSource = items.ToArray(), SelectedIndex = selectedIndex };
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, Chrome);
        return combo;
    }

    private static TextBox TextBox(string text, double minWidth)
    {
        var box = new TextBox
        {
            Text = text,
            MinWidth = minWidth
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, Chrome);
        return box;
    }
}
