using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation.Accessibility;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public sealed record AccessibilityCheckerDialogResult(AccessibilityIssue Issue);

/// <summary>
/// Excel-style Accessibility Checker task pane renderer.
/// </summary>
public sealed class AccessibilityCheckerDialog : Window
{
    private readonly AccessibilityCheckerDialogPlan _plan;
    private readonly TreeView _resultsTree = new();
    private readonly TextBlock _statusText = new();
    private readonly StackPanel _additionalInfoPanel = new();
    private readonly TextBlock _whyFixHeader = new();
    private readonly TextBlock _whyFixText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _howToFixHeader = new();
    private readonly TextBlock _howToFixText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _messageBox = new();
    private readonly Button _goToButton = new() { Width = 76, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _closeButton = new() { Width = 76 };

    public AccessibilityCheckerDialogResult? Result { get; private set; }

    public AccessibilityCheckerDialog(IReadOnlyList<AccessibilityIssue> issues)
    {
        _plan = AccessibilityCheckerDialogPlanner.Create(issues, UiText.Get);

        Title = _plan.Title;
        Width = AccessibilityCheckerDialogMetrics.Width;
        Height = AccessibilityCheckerDialogMetrics.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        ApplyAutomation(_messageBox, _plan.ResultAutomation);
        ApplyAutomation(_resultsTree, _plan.IssueListAutomation);
        ApplyAction(_goToButton, _plan.GoToAction);
        ApplyAction(_closeButton, _plan.CloseAction);

        Content = BuildContent();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
            ApplyAutomationNames();
    }

    public static string CreateMessage(IReadOnlyList<AccessibilityIssue> issues) =>
        AccessibilityCheckerDialogPlanner.CreateMessage(issues, UiText.Get);

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };

        var title = new TextBlock
        {
            Text = _plan.Title,
            FontSize = AccessibilityCheckerDialogMetrics.TitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        _goToButton.Click += (_, _) => GoToSelectedIssue();
        var buttons = DialogButtonRowFactory.Create(_goToButton, _closeButton, new Thickness(0, 12, 0, 0));
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _statusText.TextWrapping = TextWrapping.Wrap;
        _statusText.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(_statusText, Dock.Bottom);
        root.Children.Add(_statusText);

        if (_plan.State == AccessibilityCheckerDialogState.Clean)
        {
            _messageBox.Text = _plan.CleanMessage;
            _messageBox.IsReadOnly = true;
            _messageBox.AcceptsReturn = true;
            _messageBox.TextWrapping = TextWrapping.Wrap;
            _messageBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _messageBox.BorderThickness = new Thickness(0);
            _messageBox.Background = Brushes.Transparent;
            root.Children.Add(_messageBox);

            _goToButton.Visibility = Visibility.Collapsed;
            _statusText.Text = _plan.StatusText;
            return root;
        }

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var resultsHeader = new TextBlock
        {
            Text = _plan.InspectionResultsHeader,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(resultsHeader, 0);
        body.Children.Add(resultsHeader);

        PopulateTree();
        _resultsTree.BorderThickness = new Thickness(1);
        _resultsTree.SelectedItemChanged += (_, _) => OnSelectionChanged();
        _resultsTree.MouseDoubleClick += ResultsTree_MouseDoubleClick;
        Grid.SetRow(_resultsTree, 1);
        body.Children.Add(_resultsTree);

        var additionalInfo = BuildAdditionalInformation();
        Grid.SetRow(additionalInfo, 2);
        body.Children.Add(additionalInfo);

        root.Children.Add(body);

        OnSelectionChanged();
        return root;
    }

    private UIElement BuildAdditionalInformation()
    {
        _additionalInfoPanel.Margin = new Thickness(0, 12, 0, 0);

        _additionalInfoPanel.Children.Add(new TextBlock
        {
            Text = _plan.AdditionalInformationHeader,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });

        _whyFixHeader.Text = _plan.WhyFixHeader;
        _whyFixHeader.FontWeight = FontWeights.SemiBold;
        _additionalInfoPanel.Children.Add(_whyFixHeader);
        _additionalInfoPanel.Children.Add(_whyFixText);

        _howToFixHeader.Text = _plan.HowToFixHeader;
        _howToFixHeader.FontWeight = FontWeights.SemiBold;
        _additionalInfoPanel.Children.Add(_howToFixHeader);
        _additionalInfoPanel.Children.Add(_howToFixText);

        return _additionalInfoPanel;
    }

    private void PopulateTree()
    {
        TreeViewItem? initialNode = null;
        foreach (var node in _plan.TreeNodes)
            _resultsTree.Items.Add(CreateTreeNode(node, ref initialNode));

        if (initialNode is not null)
            initialNode.IsSelected = true;
    }

    private static TreeViewItem CreateTreeNode(
        AccessibilityCheckerTreeNodePlan plan,
        ref TreeViewItem? initialNode)
    {
        var node = new TreeViewItem
        {
            Header = plan.Header,
            FontWeight = plan.Kind == AccessibilityCheckerTreeNodeKind.Section
                ? FontWeights.SemiBold
                : FontWeights.Normal,
            IsExpanded = plan.IsExpanded,
            Tag = plan.Item is not null ? plan.Item : plan.Group,
        };

        if (plan.IsInitialSelection)
            initialNode = node;

        foreach (var child in plan.Children)
            node.Items.Add(CreateTreeNode(child, ref initialNode));

        return node;
    }

    private AccessibilityCheckerItemPlan? SelectedItem()
    {
        if (_resultsTree.SelectedItem is not TreeViewItem node)
            return null;
        return node.Tag switch
        {
            AccessibilityCheckerItemPlan item => item,
            AccessibilityCheckerGroupPlan group => group.Items.Count > 0 ? group.Items[0] : null,
            _ => null,
        };
    }

    private AccessibilityCheckerGroupPlan? SelectedGroup()
    {
        if (_resultsTree.SelectedItem is not TreeViewItem node)
            return null;
        return node.Tag as AccessibilityCheckerGroupPlan;
    }

    private void OnSelectionChanged()
    {
        var selection = AccessibilityCheckerDialogPlanner.CreateSelection(SelectedItem(), SelectedGroup(), _plan);

        _goToButton.IsEnabled = selection.CanNavigate;

        if (!selection.HasAdditionalInformation)
        {
            _additionalInfoPanel.Visibility = Visibility.Collapsed;
            _statusText.Text = selection.StatusText;
            return;
        }

        _additionalInfoPanel.Visibility = Visibility.Visible;
        _whyFixText.Text = selection.WhyFix;
        _howToFixText.Text = selection.HowToFix;
        _statusText.Text = selection.StatusText;
    }

    private void GoToSelectedIssue()
    {
        if (SelectedItem() is not { } item)
            return;

        Result = new AccessibilityCheckerDialogResult(item.Issue);
        DialogResult = true;
    }

    private void ResultsTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedItem() is null)
            return;

        GoToSelectedIssue();
        e.Handled = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        if (_plan.State == AccessibilityCheckerDialogState.Issues)
        {
            _resultsTree.Focus();
            Keyboard.Focus(_resultsTree);
            return;
        }

        _messageBox.Focus();
        Keyboard.Focus(_messageBox);
    }

    private static void ApplyAction(Button button, AccessibilityCheckerActionSpec action)
    {
        button.Content = action.Text;
        button.IsDefault = action.IsDefault;
        button.IsCancel = action.IsCancel;
        ApplyAutomation(button, action.Automation);
    }

    private static void ApplyAutomation(DependencyObject target, AccessibilityCheckerAutomationSpec automation)
    {
        AutomationProperties.SetName(target, automation.Name);
        AutomationProperties.SetAutomationId(target, automation.AutomationId);
        AutomationProperties.SetHelpText(target, automation.HelpText);
    }

    /// <summary>
    /// Screen-reader names for this dialog's controls. Ported from the abandoned
    /// codex/dialog-parity-loop branch, whose paths predate the Freexcel -> FreeX rename.
    /// </summary>
    private void ApplyAutomationNames()
    {
        AutomationProperties.SetName(_messageBox, "Accessibility checker summary");
    }
}
