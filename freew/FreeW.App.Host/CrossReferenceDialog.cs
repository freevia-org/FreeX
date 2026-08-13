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
    private static readonly CrossReferenceDialogVisualMetrics Layout =
        CrossReferenceDialogPlanner.VisualMetrics;

    private readonly CrossReferenceDialogSession _session;
    private readonly ListBox _typeList;
    private readonly ListBox _insertAsList;
    private readonly ListBox _targetList;
    private readonly CheckBox _hyperlinkBox;
    private bool _updatingControls;
    private CrossReferenceDialogChoice? _result;

    private CrossReferenceDialog(Window? owner, TextDocument doc)
    {
        _session = new CrossReferenceDialogSession(doc);
        Owner = owner;
        Title = CrossReferenceDialogPlanner.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, CrossReferenceDialogPlanner.AutomationId);

        _typeList = new ListBox
        {
            MinWidth = Layout.TypeListMinWidth,
            Height = Layout.ChoiceListHeight,
            ItemsSource = _session.TypeChoices,
            SelectedIndex = _session.State.TypeIndex,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(
            _typeList,
            CrossReferenceDialogPlanner.TypeAutomationId);

        _insertAsList = new ListBox
        {
            MinWidth = Layout.InsertAsListMinWidth,
            Height = Layout.ChoiceListHeight
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(
            _insertAsList,
            CrossReferenceDialogPlanner.InsertAsAutomationId);

        _targetList = new ListBox
        {
            MinWidth = Layout.TargetListMinWidth,
            Height = Layout.TargetListHeight
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(
            _targetList,
            CrossReferenceDialogPlanner.TargetAutomationId);
        _targetList.MouseDoubleClick += (_, _) => Accept();

        _hyperlinkBox = new CheckBox
        {
            Content = CrossReferenceDialogPlanner.HyperlinkLabel,
            IsChecked = true,
            Margin = new Thickness(0, Layout.HyperlinkTopMargin, 0, 0)
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(
            _hyperlinkBox,
            CrossReferenceDialogPlanner.HyperlinkAutomationId);

        _typeList.SelectionChanged += (_, _) => UpdateTypeSelection();
        _insertAsList.SelectionChanged += (_, _) => UpdateInsertAsSelection();
        _targetList.SelectionChanged += (_, _) =>
        {
            if (!_updatingControls)
                _session.UpdateTarget(_targetList.SelectedIndex);
        };
        _hyperlinkBox.Checked += (_, _) => _session.UpdateHyperlink(hyperlink: true);
        _hyperlinkBox.Unchecked += (_, _) => _session.UpdateHyperlink(hyperlink: false);
        ApplySessionChoices(includeInsertAs: true);

        Content = BuildLayout();
        Loaded += (_, _) => _typeList.Focus();
    }

    private UIElement BuildLayout()
    {
        // Top row: reference type | insert reference to. Bottom: the target list spanning, then options + buttons.
        var topRow = new Grid { Margin = new Thickness(0, 0, 0, Layout.TopRowBottomMargin) };
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Layout.ColumnSpacing) });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        topRow.Children.Add(LabeledColumn(CrossReferenceDialogPlanner.ReferenceTypeLabel, _typeList, column: 0));
        topRow.Children.Add(LabeledColumn(CrossReferenceDialogPlanner.InsertReferenceToLabel, _insertAsList, column: 2));

        var targetColumn = LabeledColumn(CrossReferenceDialogPlanner.TargetLabel, _targetList, column: -1);

        var actionPlans = CrossReferenceDialogPlanner.ActionButtons;
        var acceptPlan = actionPlans[0];
        var cancelPlan = actionPlans[1];
        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: Layout.ActionButtonWidth,
            rowMargin: new Thickness(0, Layout.ActionRowTopMargin, 0, 0),
            acceptContent: acceptPlan.Label,
            cancelContent: cancelPlan.Label);

        var panel = new StackPanel { Margin = new Thickness(Layout.OuterMargin) };
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
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, Layout.LabelTopMargin, 0, Layout.LabelBottomMargin)
        });
        stack.Children.Add(control);
        if (column >= 0)
            Grid.SetColumn(stack, column);
        return stack;
    }

    private void UpdateTypeSelection()
    {
        if (_updatingControls)
            return;

        _session.UpdateType(_typeList.SelectedIndex);
        ApplySessionChoices(includeInsertAs: true);
    }

    private void UpdateInsertAsSelection()
    {
        if (_updatingControls)
            return;

        _session.UpdateInsertAs(_insertAsList.SelectedIndex);
        ApplySessionChoices(includeInsertAs: false);
    }

    private void ApplySessionChoices(bool includeInsertAs)
    {
        _updatingControls = true;
        try
        {
            if (includeInsertAs)
            {
                _insertAsList.ItemsSource = _session.InsertAsChoices;
                _insertAsList.SelectedIndex = _session.State.InsertAsIndex;
            }

            _targetList.ItemsSource = _session.TargetChoices;
            _targetList.SelectedIndex = _session.State.TargetIndex;
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void Accept()
    {
        _session.UpdateInsertAs(_insertAsList.SelectedIndex);
        _session.UpdateTarget(_targetList.SelectedIndex);
        _session.UpdateHyperlink(_hyperlinkBox.IsChecked == true);
        var acceptance = _session.PlanAcceptance();
        if (!acceptance.IsAccepted)
        {
            DialogMessageHelper.ShowWarning(
                this,
                acceptance.ValidationMessage ?? CrossReferenceDialogPlanner.MissingTargetMessage,
                CrossReferenceDialogPlanner.Title);
            return;
        }
        _result = acceptance.Result;
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
