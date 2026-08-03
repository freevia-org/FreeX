using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Ribbon;
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
    private readonly TextDocument _doc;
    private readonly ListBox _typeList;
    private readonly ListBox _insertAsList;
    private readonly ListBox _targetList;
    private readonly CheckBox _hyperlinkBox;
    private readonly List<CrossReferenceTargetChoice> _targets = [];
    private CrossReferenceDialogChoice? _result;

    private CrossReferenceDialog(Window? owner, TextDocument doc)
    {
        _doc = doc;
        Owner = owner;
        Title = CrossReferenceDialogPlanner.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _typeList = new ListBox { MinWidth = 150, Height = 170 };
        foreach (var type in CrossReferenceDialogPlanner.BuildTypeChoices())
            _typeList.Items.Add(type);
        _typeList.SelectedIndex = 0;

        _insertAsList = new ListBox { MinWidth = 180, Height = 170 };

        _targetList = new ListBox { MinWidth = 300, Height = 200 };
        _targetList.MouseDoubleClick += (_, _) => Accept();

        _hyperlinkBox = new CheckBox
        {
            Content = CrossReferenceDialogPlanner.HyperlinkLabel,
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

        topRow.Children.Add(LabeledColumn(CrossReferenceDialogPlanner.ReferenceTypeLabel, _typeList, column: 0));
        topRow.Children.Add(LabeledColumn(CrossReferenceDialogPlanner.InsertReferenceToLabel, _insertAsList, column: 2));

        var targetColumn = LabeledColumn(CrossReferenceDialogPlanner.TargetLabel, _targetList, column: -1);

        var actionPlans = CrossReferenceDialogPlanner.ActionButtons;
        var acceptPlan = actionPlans[0];
        var cancelPlan = actionPlans[1];
        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 80,
            rowMargin: new Thickness(0, 14, 0, 0),
            acceptContent: acceptPlan.Label,
            cancelContent: cancelPlan.Label);

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
        (_typeList.SelectedItem as CrossReferenceTypeChoice)?.Type ?? CrossRefType.Heading;

    private CrossRefInsertAs SelectedInsertAs =>
        (_insertAsList.SelectedItem as CrossReferenceInsertAsChoice)?.InsertAs ?? CrossRefInsertAs.Text;

    private void ReloadInsertOptions()
    {
        var previous = (_insertAsList.SelectedItem as CrossReferenceInsertAsChoice)?.InsertAs;
        var choices = CrossReferenceDialogPlanner.BuildInsertAsChoices(SelectedType);
        _insertAsList.Items.Clear();
        foreach (var option in choices)
            _insertAsList.Items.Add(option);
        _insertAsList.SelectedIndex = CrossReferenceDialogPlanner.PreserveInsertAsSelection(choices, previous);
    }

    private void ReloadTargets()
    {
        _targets.Clear();
        _targetList.Items.Clear();
        foreach (var target in CrossReferenceDialogPlanner.BuildTargetChoices(_doc, SelectedType))
        {
            _targets.Add(target);
            _targetList.Items.Add(target.Label);
        }
        _targetList.SelectedIndex = _targetList.Items.Count > 0 ? 0 : -1;
    }

    private void Accept()
    {
        var index = _targetList.SelectedIndex;
        if (!CrossReferenceDialogPlanner.TryCreateChoice(
                _doc,
                SelectedType,
                SelectedInsertAs,
                index,
                _hyperlinkBox.IsChecked == true,
                out var choice))
        {
            DialogMessageHelper.ShowWarning(
                this,
                CrossReferenceDialogPlanner.MissingTargetMessage,
                CrossReferenceDialogPlanner.Title);
            return;
        }
        _result = choice;
        DialogResult = true;
    }

    /// <summary>
    /// Show the Cross-reference dialog over <paramref name="doc"/>; returns the chosen reference, or null if
    /// cancelled (or nothing was selected).
    /// </summary>
    public static CrossReferenceDialogChoice? Prompt(Window? owner, TextDocument doc)
    {
        var dialog = new CrossReferenceDialog(owner, doc);
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}
