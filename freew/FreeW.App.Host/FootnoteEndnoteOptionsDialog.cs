using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Footnote and Endnote" options dialog (References &gt; Footnotes group launcher).
/// Exposes the document-level numbering properties stored in <c>w:footnotePr</c> /
/// <c>w:endnotePr</c> in word/settings.xml: number format, start-at value and restart mode.
///
/// <para>
/// The dialog has two mirrored sections — Footnotes and Endnotes — matching Word's layout.
/// On OK it returns a <see cref="Result"/> the caller applies to the document's
/// <see cref="TextDocument.FootnoteNumbering"/> and <see cref="TextDocument.EndnoteNumbering"/>
/// properties; on Cancel it returns null and nothing is changed.
/// </para>
/// </summary>
internal sealed class FootnoteEndnoteOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    /// <summary>The settings the dialog produces.</summary>
    internal sealed record Result(
        NoteNumberFormat FootnoteFormat,
        int FootnoteStartAt,
        NoteNumberRestart FootnoteRestart,
        NoteNumberFormat EndnoteFormat,
        int EndnoteStartAt,
        NoteNumberRestart EndnoteRestart);

    private readonly ComboBox _footnoteFormatBox;
    private readonly TextBox _footnoteStartBox;
    private readonly ComboBox _footnoteRestartBox;
    private readonly ComboBox _endnoteFormatBox;
    private readonly TextBox _endnoteStartBox;
    private readonly ComboBox _endnoteRestartBox;
    private Result? _result;

    private static readonly (string Label, NoteNumberFormat Value)[] FormatItems =
    [
        ("1, 2, 3, …",   NoteNumberFormat.Decimal),
        ("i, ii, iii, …", NoteNumberFormat.LowerRoman),
        ("I, II, III, …", NoteNumberFormat.UpperRoman),
        ("a, b, c, …",   NoteNumberFormat.LowerLetter),
        ("A, B, C, …",   NoteNumberFormat.UpperLetter),
        ("*, †, ‡, …",   NoteNumberFormat.Chicago),
    ];

    private static readonly (string Label, NoteNumberRestart Value)[] FootnoteRestartItems =
    [
        ("Continuous",        NoteNumberRestart.Continuous),
        ("Restart each section", NoteNumberRestart.EachSection),
        ("Restart each page", NoteNumberRestart.EachPage),
    ];

    private static readonly (string Label, NoteNumberRestart Value)[] EndnoteRestartItems =
    [
        ("Continuous",           NoteNumberRestart.Continuous),
        ("Restart each section", NoteNumberRestart.EachSection),
    ];

    private FootnoteEndnoteOptionsDialog(Window? owner, NoteNumberingOptions footnote, NoteNumberingOptions endnote)
    {
        Owner = owner;
        Title = "Footnote and Endnote";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _footnoteFormatBox = FormatCombo(footnote.NumberFormat, FormatItems);
        _footnoteStartBox = StartBox(footnote.StartAt);
        _footnoteRestartBox = RestartCombo(footnote.NumberRestart, FootnoteRestartItems);
        _endnoteFormatBox = FormatCombo(endnote.NumberFormat, FormatItems);
        _endnoteStartBox = StartBox(endnote.StartAt);
        _endnoteRestartBox = RestartCombo(endnote.NumberRestart, EndnoteRestartItems);

        var outerStack = new StackPanel { Margin = new Thickness(14) };

        outerStack.Children.Add(SectionHeader("Footnotes"));
        outerStack.Children.Add(OptionsGrid(_footnoteFormatBox, _footnoteStartBox, _footnoteRestartBox));

        outerStack.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 8) });

        outerStack.Children.Add(SectionHeader("Endnotes"));
        outerStack.Children.Add(OptionsGrid(_endnoteFormatBox, _endnoteStartBox, _endnoteRestartBox));

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 14, 0, 0));
        outerStack.Children.Add(buttons);

        Content = outerStack;
        DialogFocus.FocusAndSelect(_footnoteStartBox);
    }

    private static TextBlock SectionHeader(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };

    private static Grid OptionsGrid(ComboBox formatBox, TextBox startBox, ComboBox restartBox)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Number format:", formatBox);
        AddRow(grid, 1, "Start at:",      startBox);
        AddRow(grid, 2, "Numbering:",     restartBox);

        return grid;
    }

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

    private static ComboBox FormatCombo(NoteNumberFormat current, (string Label, NoteNumberFormat Value)[] items)
    {
        var box = new ComboBox();
        foreach (var (label, _) in items)
            box.Items.Add(label);
        box.SelectedIndex = System.Array.FindIndex(items, i => i.Value == current);
        if (box.SelectedIndex < 0) box.SelectedIndex = 0;
        return box;
    }

    private static ComboBox RestartCombo(NoteNumberRestart current, (string Label, NoteNumberRestart Value)[] items)
    {
        var box = new ComboBox();
        foreach (var (label, _) in items)
            box.Items.Add(label);
        box.SelectedIndex = System.Array.FindIndex(items, i => i.Value == current);
        if (box.SelectedIndex < 0) box.SelectedIndex = 0;
        return box;
    }

    private static TextBox StartBox(int current) =>
        new() { Text = current.ToString(System.Globalization.CultureInfo.CurrentCulture), MinWidth = 60 };

    private void Accept()
    {
        if (!int.TryParse(_footnoteStartBox.Text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.CurrentCulture, out var fnStart) || fnStart < 1
         || !int.TryParse(_endnoteStartBox.Text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.CurrentCulture, out var enStart) || enStart < 1)
        {
            DialogMessageHelper.ShowWarning(this, "Enter a positive integer for the start-at values.");
            return;
        }

        _result = new Result(
            FormatItems[System.Math.Max(0, _footnoteFormatBox.SelectedIndex)].Value,
            fnStart,
            FootnoteRestartItems[System.Math.Max(0, _footnoteRestartBox.SelectedIndex)].Value,
            FormatItems[System.Math.Max(0, _endnoteFormatBox.SelectedIndex)].Value,
            enStart,
            EndnoteRestartItems[System.Math.Max(0, _endnoteRestartBox.SelectedIndex)].Value);
        Close();
    }

    /// <summary>
    /// Show the dialog seeded with the document's current footnote/endnote numbering settings;
    /// returns the chosen settings, or null if cancelled.
    /// </summary>
    public static Result? Prompt(Window? owner, NoteNumberingOptions footnote, NoteNumberingOptions endnote)
    {
        var dialog = new FootnoteEndnoteOptionsDialog(owner, footnote, endnote);
        dialog.ShowDialog();
        return dialog._result;
    }
}
