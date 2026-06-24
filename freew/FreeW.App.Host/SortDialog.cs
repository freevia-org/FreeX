using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A modal Sort dialog matching Word's "Sort Text" / "Sort" dialog. Supports up to three sort keys
/// (Sort by + Then by × 2), with per-key sort type (Text / Number / Date) and direction
/// (Ascending / Descending), plus global case-sensitive and header-row toggles. Built on the shared
/// <see cref="Free.Shared.Ribbon.Wpf.DialogWindow"/> + dialog helpers so it matches the rest of
/// FreeW/FreeX's dialogs. Returns the chosen <see cref="SortChoice"/>, or null if cancelled.
/// </summary>
internal sealed class SortDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    // Per-key controls: type box and ascending radio.
    private readonly ComboBox   _type1;
    private readonly RadioButton _asc1;
    private readonly ComboBox   _type2;
    private readonly RadioButton _asc2;
    private readonly CheckBox   _useKey2;
    private readonly ComboBox   _type3;
    private readonly RadioButton _asc3;
    private readonly CheckBox   _useKey3;
    private readonly CheckBox   _caseSensitive;
    private readonly CheckBox   _hasHeaderRow;
    private SortChoice? _result;

    private SortDialog(Window? owner, string subjectLabel)
    {
        Owner = owner;
        Title = "Sort";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _type1 = TypeCombo();
        _asc1  = AscRadio();
        var desc1 = DescRadio();

        _useKey2 = new CheckBox { Content = "Then by", Margin = new Thickness(0, 8, 0, 4) };
        _type2 = TypeCombo();
        _asc2  = AscRadio();
        var desc2 = DescRadio();
        SetKeyEnabled(_type2, _asc2, desc2, enabled: false);

        _useKey3 = new CheckBox { Content = "Then by (2nd)", Margin = new Thickness(0, 8, 0, 4) };
        _type3 = TypeCombo();
        _asc3  = AscRadio();
        var desc3 = DescRadio();
        SetKeyEnabled(_type3, _asc3, desc3, enabled: false);

        _caseSensitive = new CheckBox { Content = "Case sensitive",        Margin = new Thickness(0, 10, 0, 4) };
        _hasHeaderRow  = new CheckBox { Content = "My list has a header row", Margin = new Thickness(0, 0, 0, 0) };

        _useKey2.Checked   += (_, _) => SetKeyEnabled(_type2, _asc2, desc2, enabled: true);
        _useKey2.Unchecked += (_, _) => SetKeyEnabled(_type2, _asc2, desc2, enabled: false);
        _useKey3.Checked   += (_, _) => SetKeyEnabled(_type3, _asc3, desc3, enabled: true);
        _useKey3.Unchecked += (_, _) => SetKeyEnabled(_type3, _asc3, desc3, enabled: false);

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = subjectLabel, Margin = new Thickness(0, 0, 0, 10) });

        // Key 1: Sort by
        panel.Children.Add(new TextBlock { Text = "Sort by", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(KeyRow(_type1));
        panel.Children.Add(_asc1);
        panel.Children.Add(desc1);

        // Key 2: Then by (optional)
        panel.Children.Add(_useKey2);
        panel.Children.Add(KeyRow(_type2));
        panel.Children.Add(_asc2);
        panel.Children.Add(desc2);

        // Key 3: Then by (2nd) (optional)
        panel.Children.Add(_useKey3);
        panel.Children.Add(KeyRow(_type3));
        panel.Children.Add(_asc3);
        panel.Children.Add(desc3);

        panel.Children.Add(_caseSensitive);
        panel.Children.Add(_hasHeaderRow);

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        panel.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 14, 0, 0)));

        Content = panel;
        Loaded += (_, _) => _type1.Focus();
    }

    private static ComboBox TypeCombo()
    {
        var box = new ComboBox { MinWidth = 120, Margin = new Thickness(0, 0, 0, 4) };
        box.Items.Add("Text");
        box.Items.Add("Number");
        box.Items.Add("Date");
        box.SelectedIndex = 0;
        return box;
    }

    private static RadioButton AscRadio() =>
        new() { Content = "Ascending",  IsChecked = true, Margin = new Thickness(4, 0, 8, 4) };

    private static RadioButton DescRadio() =>
        new() { Content = "Descending", Margin = new Thickness(4, 0, 0, 4) };

    private static StackPanel KeyRow(ComboBox typeBox)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(new TextBlock
        {
            Text = "Type:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        row.Children.Add(typeBox);
        return row;
    }

    private static void SetKeyEnabled(ComboBox typeBox, RadioButton asc, RadioButton desc, bool enabled)
    {
        typeBox.IsEnabled = enabled;
        asc.IsEnabled     = enabled;
        desc.IsEnabled    = enabled;
    }

    private static SortKind KindOf(ComboBox typeBox) => typeBox.SelectedIndex switch
    {
        1 => SortKind.Number,
        2 => SortKind.Date,
        _ => SortKind.Text,
    };

    private void Accept()
    {
        var key1 = new SortKey(KindOf(_type1), _asc1.IsChecked == true);
        SortKey? key2 = _useKey2.IsChecked == true
            ? new SortKey(KindOf(_type2), _asc2.IsChecked == true)
            : null;
        SortKey? key3 = _useKey3.IsChecked == true
            ? new SortKey(KindOf(_type3), _asc3.IsChecked == true)
            : null;

        _result = new SortChoice(
            key1,
            key2,
            key3,
            _caseSensitive.IsChecked == true,
            _hasHeaderRow.IsChecked == true);
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
/// A single sort key: the data type (<see cref="SortKind"/>) and whether it sorts ascending or descending.
/// </summary>
internal readonly record struct SortKey(SortKind Kind, bool Ascending);

/// <summary>
/// The options captured by <see cref="SortDialog"/>: up to three sort keys, a case-sensitive toggle, and
/// a header-row flag. <see cref="Key1"/> is always present; <see cref="Key2"/> and <see cref="Key3"/> are
/// null when the corresponding "Then by" checkbox was unchecked.
/// </summary>
internal readonly record struct SortChoice(
    SortKey Key1,
    SortKey? Key2,
    SortKey? Key3,
    bool CaseSensitive,
    bool HasHeaderRow)
{
    /// <summary>The primary sort type (shortcut for <c>Key1.Kind</c>).</summary>
    public SortKind Kind      => Key1.Kind;
    /// <summary>The primary sort direction (shortcut for <c>Key1.Ascending</c>).</summary>
    public bool     Ascending => Key1.Ascending;
}
