using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A small modal Sort dialog matching the subset of Word's "Sort Text" dialog FreeW exposes: a sort
/// type (Text / Number / Date), ascending vs. descending order, a case-sensitive toggle, and a
/// "My list has a header row" option that pins the first item in place. Built on the shared
/// <see cref="Free.Shared.Ribbon.Wpf.DialogWindow"/> + dialog helpers (OK/Cancel row, focus) so it
/// matches the rest of FreeW/FreeX's dialogs. Returns the chosen <see cref="SortChoice"/>, or null if
/// cancelled.
/// </summary>
internal sealed class SortDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ComboBox _typeBox;
    private readonly RadioButton _ascending;
    private readonly CheckBox _caseSensitive;
    private readonly CheckBox _hasHeaderRow;
    private SortChoice? _result;

    private SortDialog(Window? owner, string subjectLabel)
    {
        Owner = owner;
        Title = "Sort";
        Width = 300;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _typeBox = new ComboBox { MinWidth = 140 };
        _typeBox.Items.Add("Text");
        _typeBox.Items.Add("Number");
        _typeBox.Items.Add("Date");
        _typeBox.SelectedIndex = 0;

        _ascending = new RadioButton
        {
            Content = "Ascending (A → Z)",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var descending = new RadioButton { Content = "Descending (Z → A)" };

        _caseSensitive = new CheckBox { Content = "Case sensitive", Margin = new Thickness(0, 10, 0, 4) };
        _hasHeaderRow = new CheckBox { Content = "My list has a header row" };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = subjectLabel, Margin = new Thickness(0, 0, 0, 10) });

        var typeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        typeRow.Children.Add(new TextBlock
        {
            Text = "Type:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        typeRow.Children.Add(_typeBox);
        panel.Children.Add(typeRow);

        panel.Children.Add(_ascending);
        panel.Children.Add(descending);
        panel.Children.Add(_caseSensitive);
        panel.Children.Add(_hasHeaderRow);

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        panel.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 14, 0, 0)));

        Content = panel;
        Loaded += (_, _) => _typeBox.Focus();
    }

    private void Accept()
    {
        var kind = _typeBox.SelectedIndex switch
        {
            1 => SortKind.Number,
            2 => SortKind.Date,
            _ => SortKind.Text,
        };
        _result = new SortChoice(kind, _ascending.IsChecked == true, _caseSensitive.IsChecked == true, _hasHeaderRow.IsChecked == true);
        Close();
    }

    /// <summary>
    /// Show the Sort dialog. <paramref name="forTable"/> tailors the prompt text (sorting table rows by
    /// the caret's column vs. sorting selected paragraphs). Returns the chosen options, or null if
    /// cancelled.
    /// </summary>
    public static SortChoice? Prompt(Window? owner, bool forTable)
    {
        var label = forTable
            ? "Sort the table rows by the current column:"
            : "Sort the selected paragraphs:";
        var dialog = new SortDialog(owner, label);
        dialog.ShowDialog();
        return dialog._result;
    }
}

/// <summary>
/// The options captured by <see cref="SortDialog"/>: the sort <see cref="SortKind"/>, direction,
/// whether the comparison is case-sensitive, and whether the first item is a header row pinned in place.
/// </summary>
internal readonly record struct SortChoice(SortKind Kind, bool Ascending, bool CaseSensitive, bool HasHeaderRow);
