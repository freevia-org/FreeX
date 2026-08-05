using System.Globalization;
using System.Windows;
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
        Title = FootnoteEndnoteOptionsDialogPlanner.Title;
        Width = FootnoteEndnoteOptionsDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var state = FootnoteEndnoteOptionsDialogPlanner.BuildInitialState(
            footnote,
            endnote,
            CultureInfo.CurrentCulture);

        _footnoteFormatBox = ChoiceCombo(
            FootnoteEndnoteOptionsDialogPlanner.FormatItems,
            state.FootnoteFormatIndex);
        _footnoteStartBox = StartBox(state.FootnoteStartAtText);
        _footnoteRestartBox = ChoiceCombo(
            FootnoteEndnoteOptionsDialogPlanner.FootnoteRestartItems,
            state.FootnoteRestartIndex);
        _endnoteFormatBox = ChoiceCombo(
            FootnoteEndnoteOptionsDialogPlanner.FormatItems,
            state.EndnoteFormatIndex);
        _endnoteStartBox = StartBox(state.EndnoteStartAtText);
        _endnoteRestartBox = ChoiceCombo(
            FootnoteEndnoteOptionsDialogPlanner.EndnoteRestartItems,
            state.EndnoteRestartIndex);

        var outerStack = new StackPanel
        {
            Margin = new Thickness(FootnoteEndnoteOptionsDialogPlanner.OuterMargin)
        };

        outerStack.Children.Add(SectionHeader(FootnoteEndnoteOptionsDialogPlanner.FootnotesSectionLabel));
        outerStack.Children.Add(OptionsGrid(_footnoteFormatBox, _footnoteStartBox, _footnoteRestartBox));

        outerStack.Children.Add(new Separator
        {
            Margin = new Thickness(
                0,
                FootnoteEndnoteOptionsDialogPlanner.SeparatorTopMargin,
                0,
                FootnoteEndnoteOptionsDialogPlanner.SeparatorBottomMargin)
        });

        outerStack.Children.Add(SectionHeader(FootnoteEndnoteOptionsDialogPlanner.EndnotesSectionLabel));
        outerStack.Children.Add(OptionsGrid(_endnoteFormatBox, _endnoteStartBox, _endnoteRestartBox));

        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: FootnoteEndnoteOptionsDialogPlanner.ButtonWidth,
            rowMargin: new Thickness(0, FootnoteEndnoteOptionsDialogPlanner.ActionTopMargin, 0, 0));
        outerStack.Children.Add(buttons);

        Content = outerStack;
        DialogFocus.FocusAndSelect(_footnoteStartBox);
    }

    private static TextBlock SectionHeader(string text) =>
        new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, FootnoteEndnoteOptionsDialogPlanner.SectionHeaderBottomMargin)
        };

    private static Grid OptionsGrid(ComboBox formatBox, TextBox startBox, ComboBox restartBox)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, FootnoteEndnoteOptionsDialogPlanner.NumberFormatLabel, formatBox);
        AddRow(grid, 1, FootnoteEndnoteOptionsDialogPlanner.StartAtLabel, startBox);
        AddRow(grid, 2, FootnoteEndnoteOptionsDialogPlanner.NumberingLabel, restartBox);

        return grid;
    }

    private static void AddRow(Grid grid, int row, string label, UIElement field)
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

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 4, 0, 4);
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

    private static TextBox StartBox(string text) =>
        new() { Text = text, MinWidth = FootnoteEndnoteOptionsDialogPlanner.StartAtMinWidth };

    private void Accept()
    {
        if (!TryBuildInput(out var result, out var validation))
        {
            DialogMessageHelper.ShowWarning(
                this,
                validation?.Message ?? FootnoteEndnoteOptionsDialogPlanner.PositiveStartAtMessage);
            FocusFailure(validation?.Field);
            return;
        }

        _result = result;
        Close();
    }

    // The visual harness uses this non-modal seam to exercise the same planner and focus policy as an
    // attempted OK click without opening a second warning window during a static capture.
    internal void ValidateForTest()
    {
        _ = TryBuildInput(out _, out var validation);
        FocusFailure(validation?.Field);
    }

    private bool TryBuildInput(
        out FootnoteEndnoteOptionsDialogResult? result,
        out FootnoteEndnoteOptionsValidation? validation)
    {
        var input = new FootnoteEndnoteOptionsDialogInput(
            _footnoteFormatBox.SelectedIndex,
            _footnoteStartBox.Text,
            _footnoteRestartBox.SelectedIndex,
            _endnoteFormatBox.SelectedIndex,
            _endnoteStartBox.Text,
            _endnoteRestartBox.SelectedIndex);

        return FootnoteEndnoteOptionsDialogPlanner.TryBuildResult(
            input,
            CultureInfo.CurrentCulture,
            out result,
            out validation);
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
