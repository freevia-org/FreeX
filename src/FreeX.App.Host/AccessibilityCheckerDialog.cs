using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record AccessibilityCheckerDialogResult(AccessibilityIssue Issue);

/// <summary>
/// Excel-style Accessibility Checker task pane. Inspection Results are grouped by severity
/// (Errors / Warnings / Tips) and, within each section, by issue type; selecting an item reveals an
/// "Additional Information" area with "Why Fix" and "How To Fix" guidance and enables [Go To].
/// </summary>
public sealed class AccessibilityCheckerDialog : Window
{
    private readonly IReadOnlyList<AccessibilityInspectionSection> _sections;
    private readonly TreeView _resultsTree = new();
    private readonly TextBlock _statusText = new();
    private readonly StackPanel _additionalInfoPanel = new();
    private readonly TextBlock _whyFixHeader = new();
    private readonly TextBlock _whyFixText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _howToFixHeader = new();
    private readonly TextBlock _howToFixText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _messageBox = new();
    private readonly Button _goToButton = new() { Content = UiText.Get("AccessibilityChecker_GoToButton"), Width = 76, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _closeButton = new() { Content = UiText.Get("AccessibilityChecker_CloseButton"), Width = 76, IsCancel = true };

    public AccessibilityCheckerDialogResult? Result { get; private set; }

    public AccessibilityCheckerDialog(IReadOnlyList<AccessibilityIssue> issues)
    {
        _sections = AccessibilityInspectionResult.Build(issues);

        Title = Text("AccessibilityChecker_Title", "Accessibility Checker");
        Width = 360;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        AutomationProperties.SetName(_messageBox, UiText.Get("AccessibilityChecker_ResultAutomationName"));
        AutomationProperties.SetAutomationId(_messageBox, "AccessibilityCheckerResultText");
        AutomationProperties.SetHelpText(_messageBox, UiText.Get("AccessibilityChecker_ResultHelpText"));
        AutomationProperties.SetName(_resultsTree, UiText.Get("AccessibilityChecker_IssueListAutomationName"));
        AutomationProperties.SetAutomationId(_resultsTree, "AccessibilityCheckerIssueList");
        AutomationProperties.SetHelpText(_resultsTree, UiText.Get("AccessibilityChecker_IssueListHelpText"));
        AutomationProperties.SetName(_goToButton, UiText.Get("AccessibilityChecker_GoToAutomationName"));
        AutomationProperties.SetAutomationId(_goToButton, "AccessibilityCheckerGoToButton");
        AutomationProperties.SetHelpText(_goToButton, UiText.Get("AccessibilityChecker_GoToHelpText"));
        AutomationProperties.SetName(_closeButton, UiText.Get("AccessibilityChecker_CloseAutomationName"));
        AutomationProperties.SetAutomationId(_closeButton, "AccessibilityCheckerCloseButton");
        AutomationProperties.SetHelpText(_closeButton, UiText.Get("AccessibilityChecker_CloseHelpText"));

        Content = BuildContent();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static string CreateMessage(IReadOnlyList<AccessibilityIssue> issues) =>
        issues.Count == 0
            ? UiText.Get("AccessibilityChecker_NoIssuesMessage")
            : AccessibilityIssueFormatter.Format(issues);

    public static CellAddress GetNavigationTarget(AccessibilityIssue issue)
    {
        var location = issue.Location.Trim();
        var firstLocation = location.Split(':', 2)[0];
        return CellAddress.TryParse(firstLocation, issue.SheetId, out var address)
            ? address
            : new CellAddress(issue.SheetId, 1, 1);
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };

        // Title — task-pane heading.
        var title = new TextBlock
        {
            Text = Text("AccessibilityChecker_Title", "Accessibility Checker"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        // Button row — anchored to the bottom.
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        _goToButton.Click += (_, _) => GoToSelectedIssue();
        buttons.Children.Add(_goToButton);
        buttons.Children.Add(_closeButton);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        // Status line — just above the buttons.
        _statusText.TextWrapping = TextWrapping.Wrap;
        _statusText.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(_statusText, Dock.Bottom);
        root.Children.Add(_statusText);

        if (_sections.Count == 0)
        {
            // Clean workbook — keep the read-only message box used by automation/tests.
            _messageBox.Text = CreateMessage(System.Array.Empty<AccessibilityIssue>());
            _messageBox.IsReadOnly = true;
            _messageBox.AcceptsReturn = true;
            _messageBox.TextWrapping = TextWrapping.Wrap;
            _messageBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _messageBox.BorderThickness = new Thickness(0);
            _messageBox.Background = Brushes.Transparent;
            root.Children.Add(_messageBox);

            _goToButton.Visibility = Visibility.Collapsed;
            _statusText.Text = Text("AccessibilityChecker_StatusClean",
                "No accessibility issues found. People with disabilities should not have difficulty reading this workbook.");
            return root;
        }

        // Inspection Results heading + tree, with the Additional Information area below it.
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var resultsHeader = new TextBlock
        {
            Text = Text("AccessibilityChecker_InspectionResults", "Inspection Results"),
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

        SelectFirstIssue();
        OnSelectionChanged();
        return root;
    }

    private UIElement BuildAdditionalInformation()
    {
        _additionalInfoPanel.Margin = new Thickness(0, 12, 0, 0);

        _additionalInfoPanel.Children.Add(new TextBlock
        {
            Text = Text("AccessibilityChecker_AdditionalInformation", "Additional Information"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });

        _whyFixHeader.Text = Text("AccessibilityChecker_WhyFixHeader", "Why Fix:");
        _whyFixHeader.FontWeight = FontWeights.SemiBold;
        _additionalInfoPanel.Children.Add(_whyFixHeader);
        _additionalInfoPanel.Children.Add(_whyFixText);

        _howToFixHeader.Text = Text("AccessibilityChecker_HowToFixHeader", "How To Fix:");
        _howToFixHeader.FontWeight = FontWeights.SemiBold;
        _additionalInfoPanel.Children.Add(_howToFixHeader);
        _additionalInfoPanel.Children.Add(_howToFixText);

        return _additionalInfoPanel;
    }

    private void PopulateTree()
    {
        foreach (var section in _sections)
        {
            var sectionNode = new TreeViewItem
            {
                Header = $"{SeverityHeader(section.Severity)} ({section.IssueCount})",
                FontWeight = FontWeights.SemiBold,
                IsExpanded = true,
            };

            foreach (var group in section.Groups)
            {
                var groupNode = new TreeViewItem
                {
                    Header = $"{group.Descriptor.Label} ({group.Items.Count})",
                    FontWeight = FontWeights.Normal,
                    IsExpanded = true,
                    Tag = group,
                };

                foreach (var item in group.Items)
                {
                    groupNode.Items.Add(new TreeViewItem
                    {
                        Header = item.ObjectLabel,
                        FontWeight = FontWeights.Normal,
                        Tag = item,
                    });
                }

                sectionNode.Items.Add(groupNode);
            }

            _resultsTree.Items.Add(sectionNode);
        }
    }

    private string SeverityHeader(AccessibilitySeverity severity) => severity switch
    {
        AccessibilitySeverity.Error => Text("AccessibilityChecker_SectionErrors", "Errors"),
        AccessibilitySeverity.Warning => Text("AccessibilityChecker_SectionWarnings", "Warnings"),
        _ => Text("AccessibilityChecker_SectionTips", "Tips"),
    };

    private void SelectFirstIssue()
    {
        foreach (TreeViewItem section in _resultsTree.Items)
        {
            foreach (TreeViewItem group in section.Items)
            {
                if (group.Items.Count > 0 && group.Items[0] is TreeViewItem leaf)
                {
                    leaf.IsSelected = true;
                    return;
                }
            }
        }
    }

    private AccessibilityInspectionItem? SelectedItem()
    {
        if (_resultsTree.SelectedItem is not TreeViewItem node)
            return null;
        return node.Tag switch
        {
            AccessibilityInspectionItem item => item,
            AccessibilityInspectionGroup group => group.Items.Count > 0 ? group.Items[0] : null,
            _ => null,
        };
    }

    private AccessibilityIssueDescriptor? SelectedDescriptor()
    {
        if (_resultsTree.SelectedItem is not TreeViewItem node)
            return null;
        return node.Tag switch
        {
            AccessibilityInspectionItem item => AccessibilityIssueClassification.Describe(item.Issue.Kind),
            AccessibilityInspectionGroup group => group.Descriptor,
            _ => null,
        };
    }

    private void OnSelectionChanged()
    {
        var descriptor = SelectedDescriptor();
        var item = SelectedItem();

        _goToButton.IsEnabled = item is not null;

        if (descriptor is null)
        {
            _additionalInfoPanel.Visibility = Visibility.Collapsed;
            _statusText.Text = Text("AccessibilityChecker_StatusReady", "Ready");
            return;
        }

        _additionalInfoPanel.Visibility = Visibility.Visible;
        _whyFixText.Text = Text(descriptor.WhyFixKey, descriptor.WhyFix);
        _howToFixText.Text = Text(descriptor.HowToFixKey, descriptor.HowToFix);
        _statusText.Text = item is not null ? item.Description : descriptor.Label;
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
        if (_sections.Count > 0)
        {
            _resultsTree.Focus();
            Keyboard.Focus(_resultsTree);
            return;
        }

        _messageBox.Focus();
        Keyboard.Focus(_messageBox);
    }

    // Routes through the localization catalog but falls back to the supplied English default when a
    // key has not yet been added to the resx (the catalog renders missing keys as "[[Key]]").
    private static string Text(string key, string fallback)
    {
        var value = UiText.Get(key);
        return value == "[[" + key + "]]" ? fallback : value;
    }
}
