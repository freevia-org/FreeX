using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using FreeX.Core.Commands;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Review ▸ Check Accessibility — Excel-style Accessibility Checker task pane. Inspection Results
    // are grouped by severity (Errors / Warnings / Tips) and, within each section, by issue type;
    // selecting an item reveals the "Additional Information" area ("Why Fix" / "How To Fix") and
    // enables [Go To]. Structure and Additional Information text are shared with the WPF dialog via
    // FreeX.Core.Commands.AccessibilityInspectionResult / AccessibilityIssueClassification.

    private async Task ShowAccessibilityCheckerDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var issues = AccessibilityCheckerService.FindIssues(_session.Workbook);
        var sections = AccessibilityInspectionResult.Build(issues);

        if (sections.Count == 0)
        {
            await ShowAccessibilityCheckerCleanDialogAsync();
            return;
        }

        await ShowAccessibilityCheckerIssuesDialogAsync(sections);
    }

    private async Task ShowAccessibilityCheckerCleanDialogAsync()
    {
        var dialog = new Window
        {
            Title = AcText("AccessibilityChecker_Title", "Accessibility Checker"),
            Width = 360,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = true,
        };
        AutomationProperties.SetAutomationId(dialog, "AccessibilityCheckerDialog");

        var titleBlock = new TextBlock
        {
            Text = AcText("AccessibilityChecker_Title", "Accessibility Checker"),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var messageBlock = new TextBlock
        {
            Text = UiText.Get("ShellLoc_AccessibilityCheckerNoIssues"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        };
        AutomationProperties.SetName(messageBlock, UiText.Get("ShellLoc_AccessibilityCheckerResultAutomationName"));
        AutomationProperties.SetAutomationId(messageBlock, "AccessibilityCheckerResultText");
        AutomationProperties.SetHelpText(messageBlock, UiText.Get("ShellLoc_AccessibilityCheckerResultHelpText"));

        var statusBlock = new TextBlock
        {
            Text = AcText("AccessibilityChecker_StatusClean",
                "No accessibility issues found. People with disabilities should not have difficulty reading this workbook."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        };

        var okButton = new Button
        {
            Content = UiText.Get("Common_Ok"),
            MinWidth = 76,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
        };
        okButton.Click += (_, _) => dialog.Close();

        dialog.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                e.Handled = true;
                dialog.Close();
            }
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                titleBlock,
                messageBlock,
                statusBlock,
                okButton,
            },
        };

        dialog.Opened += (_, _) => okButton.Focus();

        await dialog.ShowDialog(this);
    }

    private async Task ShowAccessibilityCheckerIssuesDialogAsync(
        IReadOnlyList<AccessibilityInspectionSection> sections)
    {
        var dialog = new Window
        {
            Title = AcText("AccessibilityChecker_Title", "Accessibility Checker"),
            Width = 360,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = true,
        };
        AutomationProperties.SetAutomationId(dialog, "AccessibilityCheckerDialog");

        // ---- Inspection Results tree ----
        // Windows wraps the inspection results in a bordered box with a compact (12px) font; match
        // that here by reducing the tree font size and hosting it inside a 1px light-gray Border.
        var resultsTree = new TreeView
        {
            Margin = new Thickness(0, 0, 0, 0),
            BorderThickness = new Thickness(0),
            FontSize = 12,
        };
        // Compact node rows: Avalonia's default TreeViewItem header is taller than the Windows
        // Accessibility Checker rows. Trim the per-item padding and min-height so the tree is dense
        // like the Windows screenshot (~18px rows instead of ~30px).
        resultsTree.Styles.Add(new Style(s => s.OfType<TreeViewItem>())
        {
            Setters =
            {
                new Setter(TreeViewItem.MinHeightProperty, 18.0),
                new Setter(TreeViewItem.PaddingProperty, new Thickness(2, 0)),
            },
        });
        resultsTree.Styles.Add(new Style(s => s.OfType<TreeViewItem>()
            .Template().OfType<global::Avalonia.Controls.Presenters.ContentPresenter>().Name("PART_HeaderPresenter"))
        {
            Setters =
            {
                new Setter(Layoutable.MinHeightProperty, 18.0),
                new Setter(global::Avalonia.Controls.Presenters.ContentPresenter.MarginProperty, new Thickness(0)),
                new Setter(global::Avalonia.Controls.Presenters.ContentPresenter.PaddingProperty, new Thickness(2, 0)),
            },
        });
        AutomationProperties.SetName(resultsTree, UiText.Get("ShellLoc_AccessibilityCheckerIssueListAutomationName"));
        AutomationProperties.SetAutomationId(resultsTree, "AccessibilityCheckerIssueList");
        AutomationProperties.SetHelpText(resultsTree, UiText.Get("ShellLoc_AccessibilityCheckerIssueListHelpText"));

        var resultsBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xAB, 0xAB, 0xAB)),
            BorderThickness = new Thickness(1),
            Child = resultsTree,
        };

        TreeViewItem? firstLeaf = null;
        foreach (var section in sections)
        {
            var sectionNode = new TreeViewItem
            {
                Header = $"{SeverityHeader(section.Severity)} ({section.IssueCount})",
                FontWeight = FontWeight.SemiBold,
                IsExpanded = true,
            };

            foreach (var group in section.Groups)
            {
                var groupNode = new TreeViewItem
                {
                    Header = $"{group.Descriptor.Label} ({group.Items.Count})",
                    IsExpanded = true,
                    Tag = group,
                };

                foreach (var item in group.Items)
                {
                    var leaf = new TreeViewItem
                    {
                        Header = item.ObjectLabel,
                        Tag = item,
                    };
                    firstLeaf ??= leaf;
                    groupNode.Items.Add(leaf);
                }

                sectionNode.Items.Add(groupNode);
            }

            resultsTree.Items.Add(sectionNode);
        }

        // ---- Additional Information area ----
        var whyFixText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var howToFixText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var additionalInfoPanel = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = AcText("AccessibilityChecker_AdditionalInformation", "Additional Information"),
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6),
                },
                new TextBlock { Text = AcText("AccessibilityChecker_WhyFixHeader", "Why Fix:"), FontWeight = FontWeight.SemiBold },
                whyFixText,
                new TextBlock { Text = AcText("AccessibilityChecker_HowToFixHeader", "How To Fix:"), FontWeight = FontWeight.SemiBold },
                howToFixText,
            },
        };

        // ---- Status line ----
        var statusBlock = new TextBlock
        {
            Text = AcText("AccessibilityChecker_StatusReady", "Ready"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };

        // ---- Buttons ----
        var goToButton = new Button
        {
            Content = UiText.Get("ShellLoc_AccessibilityCheckerGoToButton"),
            MinWidth = 76,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0),
        };
        AutomationProperties.SetName(goToButton, UiText.Get("ShellLoc_AccessibilityCheckerGoToAutomationName"));
        AutomationProperties.SetAutomationId(goToButton, "AccessibilityCheckerGoToButton");
        AutomationProperties.SetHelpText(goToButton, UiText.Get("ShellLoc_AccessibilityCheckerGoToHelpText"));

        var closeButton = new Button
        {
            Content = UiText.Get("Common_Close"),
            MinWidth = 76,
            IsCancel = true,
        };
        AutomationProperties.SetName(closeButton, UiText.Get("ShellLoc_AccessibilityCheckerCloseAutomationName"));
        AutomationProperties.SetAutomationId(closeButton, "AccessibilityCheckerCloseButton");
        AutomationProperties.SetHelpText(closeButton, UiText.Get("ShellLoc_AccessibilityCheckerCloseHelpText"));

        AccessibilityIssue? selectedIssue = null;

        AccessibilityInspectionItem? SelectedItem()
        {
            if (resultsTree.SelectedItem is not TreeViewItem node)
                return null;
            return node.Tag switch
            {
                AccessibilityInspectionItem item => item,
                AccessibilityInspectionGroup group => group.Items.Count > 0 ? group.Items[0] : null,
                _ => null,
            };
        }

        AccessibilityIssueDescriptor? SelectedDescriptor()
        {
            if (resultsTree.SelectedItem is not TreeViewItem node)
                return null;
            return node.Tag switch
            {
                AccessibilityInspectionItem item => AccessibilityIssueClassification.Describe(item.Issue.Kind),
                AccessibilityInspectionGroup group => group.Descriptor,
                _ => null,
            };
        }

        void UpdateSelection()
        {
            var descriptor = SelectedDescriptor();
            var item = SelectedItem();

            goToButton.IsEnabled = item is not null;

            if (descriptor is null)
            {
                additionalInfoPanel.IsVisible = false;
                statusBlock.Text = AcText("AccessibilityChecker_StatusReady", "Ready");
                return;
            }

            additionalInfoPanel.IsVisible = true;
            whyFixText.Text = AcText(descriptor.WhyFixKey, descriptor.WhyFix);
            howToFixText.Text = AcText(descriptor.HowToFixKey, descriptor.HowToFix);
            statusBlock.Text = item is not null ? item.Description : descriptor.Label;
        }

        void GoToSelected()
        {
            if (SelectedItem() is not { } item)
                return;
            selectedIssue = item.Issue;
            dialog.Close();
        }

        resultsTree.SelectionChanged += (_, _) => UpdateSelection();
        resultsTree.DoubleTapped += (_, _) => GoToSelected();
        goToButton.Click += (_, _) => GoToSelected();
        closeButton.Click += (_, _) => dialog.Close();

        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                dialog.Close();
            }
        };

        // ---- Layout ----
        var titleBlock = new TextBlock
        {
            Text = AcText("AccessibilityChecker_Title", "Accessibility Checker"),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var resultsHeader = new TextBlock
        {
            Text = AcText("AccessibilityChecker_InspectionResults", "Inspection Results"),
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };

        var bodyGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };
        Grid.SetRow(resultsHeader, 0);
        Grid.SetRow(resultsBorder, 1);
        Grid.SetRow(additionalInfoPanel, 2);
        bodyGrid.Children.Add(resultsHeader);
        bodyGrid.Children.Add(resultsBorder);
        bodyGrid.Children.Add(additionalInfoPanel);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                goToButton,
                closeButton,
            },
        };

        var innerPanel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(titleBlock, Dock.Top);
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        DockPanel.SetDock(statusBlock, Dock.Bottom);
        innerPanel.Children.Add(titleBlock);
        innerPanel.Children.Add(buttonRow);
        innerPanel.Children.Add(statusBlock);
        innerPanel.Children.Add(bodyGrid);

        dialog.Content = new Border { Padding = new Thickness(16), Child = innerPanel };

        dialog.Opened += (_, _) =>
        {
            if (firstLeaf is not null)
                firstLeaf.IsSelected = true;
            UpdateSelection();
            resultsTree.Focus();
        };

        await dialog.ShowDialog(this);

        // Navigate after dialog closes (if Go To was used)
        if (selectedIssue is not null)
        {
            ClearSelectedDrawingObject();
            var result = _session.GoToAccessibilityIssue(selectedIssue);
            if (!result.Success)
            {
                ShowEditIssue(result.ErrorMessage ?? "Could not navigate to accessibility issue.");
                return;
            }

            if (result.SelectedRange is { } selectedRange)
                RefreshShell($"Selected {FormatRangeReference(selectedRange)} (accessibility issue)");
        }
    }

    private static string SeverityHeader(AccessibilitySeverity severity) => severity switch
    {
        AccessibilitySeverity.Error => AcText("AccessibilityChecker_SectionErrors", "Errors"),
        AccessibilitySeverity.Warning => AcText("AccessibilityChecker_SectionWarnings", "Warnings"),
        _ => AcText("AccessibilityChecker_SectionTips", "Tips"),
    };

    // Routes through the localization catalog but falls back to the supplied English default when a
    // key has not yet been added to the resx (the catalog renders missing keys as "[[Key]]").
    private static string AcText(string key, string fallback)
    {
        var value = UiText.Get(key);
        return value == "[[" + key + "]]" ? fallback : value;
    }
}
