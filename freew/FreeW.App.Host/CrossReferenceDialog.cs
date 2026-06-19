using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's References &gt; Cross-reference dialog. Lets the user pick a <see cref="CrossRefType"/>
/// (heading, bookmark, figure, table, footnote, endnote, numbered item), an "Insert reference to"
/// (<see cref="CrossRefInsertAs"/> — text / page number / heading number / above-below / paragraph
/// number), toggle "Insert as hyperlink", and choose a target from the document's targets of that type.
/// Returns the chosen <see cref="Result"/>, or null when cancelled or there is nothing to reference.
///
/// <para>
/// The target enumeration and field-building live in the pure, testable
/// <see cref="CrossReferences"/> in the model project; this dialog only gathers the choices and the
/// view (<see cref="DocumentView.InsertCrossReference"/>) writes the REF/PAGEREF/NOTEREF field.
/// </para>
/// </summary>
internal sealed class CrossReferenceDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    /// <summary>The choices the dialog produces; applied by <see cref="DocumentView.InsertCrossReference"/>.</summary>
    internal sealed record Result(CrossRefType Type, CrossRefTarget Target, CrossRefInsertAs InsertAs, bool Hyperlink);

    // The reference types offered, in Word's order. Only those with at least one target in the document are
    // still listed (with an empty target list) so the user sees why nothing inserts.
    private static readonly CrossRefType[] Types =
    [
        CrossRefType.Heading, CrossRefType.Bookmark, CrossRefType.Figure, CrossRefType.Table,
        CrossRefType.Footnote, CrossRefType.Endnote, CrossRefType.NumberedItem
    ];

    private readonly TextDocument _doc;
    private readonly ListBox _typeList;
    private readonly ListBox _insertAsList;
    private readonly ListBox _targetList;
    private readonly CheckBox _hyperlinkBox;
    private readonly List<CrossRefTarget> _targets = [];
    private Result? _result;

    private CrossReferenceDialog(Window? owner, TextDocument doc)
    {
        _doc = doc;
        Owner = owner;
        Title = "Cross-reference";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _typeList = new ListBox { MinWidth = 150, Height = 170 };
        foreach (var type in Types)
            _typeList.Items.Add(new TypeItem(type));
        _typeList.SelectedIndex = 0;

        _insertAsList = new ListBox { MinWidth = 180, Height = 170 };

        _targetList = new ListBox { MinWidth = 300, Height = 200 };
        _targetList.MouseDoubleClick += (_, _) => Accept();

        _hyperlinkBox = new CheckBox
        {
            Content = "Insert as hyperlink",
            IsChecked = true,
            Margin = new Thickness(0, 10, 0, 0)
        };

        _typeList.SelectionChanged += (_, _) => { ReloadInsertOptions(); ReloadTargets(); };
        _insertAsList.SelectionChanged += (_, _) => ReloadTargets();
        ReloadInsertOptions();
        ReloadTargets();

        Content = BuildLayout();
        Loaded += (_, _) => _typeList.Focus();
    }

    private UIElement BuildLayout()
    {
        // Top row: reference type | insert reference to. Bottom: the target list spanning, then options + buttons.
        var topRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        topRow.Children.Add(LabeledColumn("Reference type:", _typeList, column: 0));
        topRow.Children.Add(LabeledColumn("Insert reference to:", _insertAsList, column: 2));

        var targetColumn = LabeledColumn("For which item:", _targetList, column: -1);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 80, rowMargin: new Thickness(0, 14, 0, 0));

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(topRow);
        panel.Children.Add(_hyperlinkBox);
        panel.Children.Add(targetColumn);
        panel.Children.Add(buttons);
        return panel;
    }

    // A labelled column (a heading TextBlock above the control). When column >= 0 the column is placed in a
    // grid cell; otherwise it is returned as a free StackPanel for stacking.
    private static StackPanel LabeledColumn(string label, UIElement control, int column)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
        stack.Children.Add(control);
        if (column >= 0)
            Grid.SetColumn(stack, column);
        return stack;
    }

    private CrossRefType SelectedType =>
        (_typeList.SelectedItem as TypeItem)?.Type ?? CrossRefType.Heading;

    private CrossRefInsertAs SelectedInsertAs =>
        (_insertAsList.SelectedItem as InsertAsItem)?.Value ?? CrossRefInsertAs.Text;

    private void ReloadInsertOptions()
    {
        var previous = (_insertAsList.SelectedItem as InsertAsItem)?.Value;
        _insertAsList.Items.Clear();
        foreach (var option in CrossReferences.InsertOptions(SelectedType))
            _insertAsList.Items.Add(new InsertAsItem(option));
        // Keep the previously-chosen aspect when still offered, else default to the first (text).
        var keep = 0;
        for (var i = 0; i < _insertAsList.Items.Count; i++)
        {
            if (_insertAsList.Items[i] is InsertAsItem item && item.Value == previous)
            {
                keep = i;
                break;
            }
        }
        _insertAsList.SelectedIndex = _insertAsList.Items.Count > 0 ? keep : -1;
    }

    private void ReloadTargets()
    {
        _targets.Clear();
        _targetList.Items.Clear();
        foreach (var target in CrossReferences.Targets(_doc, SelectedType))
        {
            _targets.Add(target);
            _targetList.Items.Add(target.Display);
        }
        _targetList.SelectedIndex = _targetList.Items.Count > 0 ? 0 : -1;
    }

    private void Accept()
    {
        var index = _targetList.SelectedIndex;
        if (index < 0 || index >= _targets.Count)
        {
            DialogMessageHelper.ShowWarning(this, "Select an item to reference.", "Cross-reference");
            return;
        }
        _result = new Result(SelectedType, _targets[index], SelectedInsertAs, _hyperlinkBox.IsChecked == true);
        DialogResult = true;
    }

    /// <summary>
    /// Show the Cross-reference dialog over <paramref name="doc"/>; returns the chosen reference, or null if
    /// cancelled (or nothing was selected).
    /// </summary>
    public static Result? Prompt(Window? owner, TextDocument doc)
    {
        var dialog = new CrossReferenceDialog(owner, doc);
        return dialog.ShowDialog() == true ? dialog._result : null;
    }

    // Wraps a CrossRefType so the list shows Word's friendly label (e.g. "Numbered item").
    private sealed record TypeItem(CrossRefType Type)
    {
        public override string ToString() => Type switch
        {
            CrossRefType.NumberedItem => "Numbered item",
            _ => Type.ToString()
        };
    }

    // Wraps a CrossRefInsertAs so the list shows Word's friendly label.
    private sealed record InsertAsItem(CrossRefInsertAs Value)
    {
        public override string ToString() => Value switch
        {
            CrossRefInsertAs.Text => "Text",
            CrossRefInsertAs.PageNumber => "Page number",
            CrossRefInsertAs.HeadingNumber => "Heading number",
            CrossRefInsertAs.AboveBelow => "Above/below",
            CrossRefInsertAs.ParagraphNumber => "Paragraph number",
            _ => Value.ToString()
        };
    }
}
