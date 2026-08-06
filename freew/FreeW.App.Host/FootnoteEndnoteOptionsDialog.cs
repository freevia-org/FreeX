using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Footnote and Endnote" options dialog (References &gt; Footnotes group launcher).
/// Exposes the document-level numbering properties stored in <c>w:footnotePr</c> /
/// <c>w:endnotePr</c> in word/settings.xml: number format, start-at value and restart mode.
/// </summary>
internal sealed class FootnoteEndnoteOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly FootnoteEndnoteOptionsDialogSession _session;
    private readonly ComboBox _footnoteFormatBox;
    private readonly TextBox _footnoteStartBox;
    private readonly ComboBox _footnoteRestartBox;
    private readonly ComboBox _endnoteFormatBox;
    private readonly TextBox _endnoteStartBox;
    private readonly ComboBox _endnoteRestartBox;
    private FootnoteEndnoteOptionsDialogResult? _result;

    internal FootnoteEndnoteOptionsDialog(Window? owner, NoteNumberingOptions footnote, NoteNumberingOptions endnote)
    {
        Owner = owner;
        var surface = FootnoteEndnoteOptionsDialogPlanner.Surface;
        Title = surface.Title;
        Width = surface.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _session = FootnoteEndnoteOptionsDialogPlanner.CreateSession(
            footnote,
            endnote,
            CultureInfo.CurrentCulture);
        var state = _session.InitialState;
        var controls = surface.Sections.ToDictionary(section => section.Kind, CreateControls);
        (_footnoteFormatBox, _footnoteStartBox, _footnoteRestartBox) = controls[FootnoteEndnoteNoteKind.Footnote];
        (_endnoteFormatBox, _endnoteStartBox, _endnoteRestartBox) = controls[FootnoteEndnoteNoteKind.Endnote];

        var outerStack = new StackPanel
        {
            Margin = new Thickness(surface.OuterMargin)
        };
        foreach (var section in surface.Sections)
        {
            if (section.Kind != FootnoteEndnoteNoteKind.Footnote)
            {
                outerStack.Children.Add(new Separator
                {
                    Margin = new Thickness(0, surface.SeparatorTopMargin, 0, surface.SeparatorBottomMargin)
                });
            }
            outerStack.Children.Add(SectionHeader(section, surface));
            outerStack.Children.Add(OptionsGrid(section, controls[section.Kind], surface));
        }

        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: surface.ButtonWidth,
            rowMargin: new Thickness(0, surface.ActionTopMargin, 0, 0));
        outerStack.Children.Add(buttons);

        Content = outerStack;
        DialogFocus.FocusAndSelect(_footnoteStartBox);

        (ComboBox Format, TextBox StartAt, ComboBox Restart) CreateControls(
            FootnoteEndnoteSectionSpec section)
        {
            var format = ChoiceCombo(_session.FormatItems, state.FormatIndex(section.Kind));
            var startAt = StartBox(state.StartAtText(section.Kind), section.Field(FootnoteEndnoteFieldKind.StartAt).MinWidth);
            var restart = ChoiceCombo(_session.RestartItems(section.Kind), state.RestartIndex(section.Kind));
            AutomationProperties.SetAutomationId(format, section.Field(FootnoteEndnoteFieldKind.NumberFormat).AutomationId);
            AutomationProperties.SetAutomationId(startAt, section.Field(FootnoteEndnoteFieldKind.StartAt).AutomationId);
            AutomationProperties.SetAutomationId(restart, section.Field(FootnoteEndnoteFieldKind.Numbering).AutomationId);
            format.SelectionChanged += (_, _) => _session.UpdateIndex(section.Kind, FootnoteEndnoteFieldKind.NumberFormat, format.SelectedIndex);
            startAt.TextChanged += (_, _) => _session.UpdateStartAt(section.Kind, startAt.Text);
            restart.SelectionChanged += (_, _) => _session.UpdateIndex(section.Kind, FootnoteEndnoteFieldKind.Numbering, restart.SelectedIndex);
            return (format, startAt, restart);
        }
    }

    private static TextBlock SectionHeader(
        FootnoteEndnoteSectionSpec section,
        FootnoteEndnoteSurfaceSpec surface)
    {
        var header = new TextBlock
        {
            Text = section.Label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, surface.SectionHeaderBottomMargin)
        };
        AutomationProperties.SetAutomationId(header, section.AutomationId);
        return header;
    }

    private static Grid OptionsGrid(
        FootnoteEndnoteSectionSpec section,
        (ComboBox Format, TextBox StartAt, ComboBox Restart) controls,
        FootnoteEndnoteSurfaceSpec surface)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var fields = new UIElement[] { controls.Format, controls.StartAt, controls.Restart };
        for (var row = 0; row < section.Fields.Count; row++)
            AddRow(grid, row, section.Fields[row].Label, fields[row], surface);

        return grid;
    }

    private static void AddRow(
        Grid grid,
        int row,
        string label,
        UIElement field,
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

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(
                0,
                surface.FieldVerticalMargin,
                0,
                surface.FieldVerticalMargin);
        grid.Children.Add(field);
    }

    private static ComboBox ChoiceCombo<TValue>(
        IReadOnlyList<FootnoteEndnoteOptionsChoice<TValue>> items,
        int selectedIndex)
    {
        var box = new ComboBox();
        foreach (var item in items)
            box.Items.Add(item.Label);
        box.SelectedIndex = selectedIndex;
        return box;
    }

    private static TextBox StartBox(string text, double minWidth) =>
        new() { Text = text, MinWidth = minWidth };

    private void Accept()
    {
        SynchronizeSession();
        var acceptance = _session.PlanAcceptance();
        if (!acceptance.IsAccepted)
        {
            DialogMessageHelper.ShowWarning(
                this,
                acceptance.Validation?.Message ?? FootnoteEndnoteOptionsDialogPlanner.PositiveStartAtMessage);
            FocusFailure(acceptance.Validation?.Field);
            return;
        }

        _result = acceptance.Result;
        Close();
    }

    // The visual harness uses this non-modal seam to exercise the same planner and focus policy as an
    // attempted OK click without opening a second warning window during a static capture.
    internal void ValidateForTest()
    {
        SynchronizeSession();
        var acceptance = _session.PlanAcceptance();
        FocusFailure(acceptance.Validation?.Field);
    }

    private void SynchronizeSession()
    {
        Synchronize(FootnoteEndnoteNoteKind.Footnote, _footnoteFormatBox, _footnoteStartBox, _footnoteRestartBox);
        Synchronize(FootnoteEndnoteNoteKind.Endnote, _endnoteFormatBox, _endnoteStartBox, _endnoteRestartBox);
    }

    private void Synchronize(FootnoteEndnoteNoteKind note, ComboBox format, TextBox startAt, ComboBox restart)
    {
        _session.UpdateIndex(note, FootnoteEndnoteFieldKind.NumberFormat, format.SelectedIndex);
        _session.UpdateStartAt(note, startAt.Text);
        _session.UpdateIndex(note, FootnoteEndnoteFieldKind.Numbering, restart.SelectedIndex);
    }

    private void FocusFailure(FootnoteEndnoteOptionsDialogField? field)
    {
        var target = field == FootnoteEndnoteOptionsDialogField.EndnoteStartAt
            ? _endnoteStartBox
            : _footnoteStartBox;
        DialogFocus.FocusAndSelect(target);
    }

    /// <summary>
    /// Show the dialog seeded with the document's current footnote/endnote numbering settings;
    /// returns the chosen settings, or null if cancelled.
    /// </summary>
    public static FootnoteEndnoteOptionsDialogResult? Prompt(
        Window? owner,
        NoteNumberingOptions footnote,
        NoteNumberingOptions endnote)
    {
        var dialog = new FootnoteEndnoteOptionsDialog(owner, footnote, endnote);
        dialog.ShowDialog();
        return dialog._result;
    }
}
