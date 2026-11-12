using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using FreeX.App.Presentation.Accessibility;
using FreeX.Core.Commands;
using Free.Shared.Shell.Avalonia;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private async Task ShowAccessibilityCheckerDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var issues = AccessibilityCheckerService.FindIssues(_session.Workbook);
        var plan = AccessibilityCheckerDialogPlanner.Create(issues, UiText.Get);

        if (plan.State == AccessibilityCheckerDialogState.Clean)
        {
            await ShowAccessibilityCheckerCleanDialogAsync(plan);
            return;
        }

        await ShowAccessibilityCheckerIssuesDialogAsync(plan);
    }

    private async Task ShowAccessibilityCheckerCleanDialogAsync(AccessibilityCheckerDialogPlan plan)
    {
        var dialog = new Window
        {
            Title = plan.Title,
            // WPF AccessibilityCheckerDialog: Width/Height are *outer* sizes of a resizable window;
            // Avalonia sizes the client area, so take the Windows caption/frame off.
            Width = AccessibilityCheckerDialogMetrics.Width - WpfResizableFrameWidth,
            Height = AccessibilityCheckerDialogMetrics.Height - WpfResizableFrameHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = true,
        };
        AvaloniaCompactDialogChrome.ApplyWindow(dialog);
        AutomationProperties.SetAutomationId(dialog, "AccessibilityCheckerDialog");

        var titleBlock = new TextBlock
        {
            Text = plan.Title,
            FontSize = AccessibilityCheckerDialogMetrics.TitleFontSize,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var messageBlock = new TextBox
        {
            Text = plan.CleanMessage,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
        };
        messageBlock.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        ApplyAutomation(messageBlock, plan.ResultAutomation);

        var statusBlock = new TextBlock
        {
            Text = plan.StatusText,
            TextWrapping = TextWrapping.Wrap,
            // WPF: _statusText.Margin = (0, 8, 0, 0), docked above the 12px-offset button row.
            Margin = new Thickness(0, 8, 0, 0),
        };

        var closeButton = new Button
        {
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        ApplyAction(closeButton, plan.CloseAction);
        AvaloniaCompactDialogChrome.ApplyButton(
            closeButton,
            AvaloniaCompactDialogChrome.WindowsStyle,
            AccessibilityCheckerDialogMetrics.ActionButtonWidth,
            isDefault: true);
        closeButton.Click += (_, _) => dialog.Close();

        dialog.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                e.Handled = true;
                dialog.Close();
            }
        };

        var root = new DockPanel { Margin = new Thickness(AccessibilityCheckerDialogMetrics.ContentMargin) };
        DockPanel.SetDock(titleBlock, Dock.Top);
        DockPanel.SetDock(closeButton, Dock.Bottom);
        DockPanel.SetDock(statusBlock, Dock.Bottom);
        root.Children.Add(titleBlock);
        root.Children.Add(closeButton);
        root.Children.Add(statusBlock);
        root.Children.Add(messageBlock);
        dialog.Content = root;

        dialog.Opened += (_, _) => messageBlock.Focus();

        await dialog.ShowDialog(this);
    }

    private async Task ShowAccessibilityCheckerIssuesDialogAsync(AccessibilityCheckerDialogPlan plan)
    {
        var dialog = new Window
        {
            Title = plan.Title,
            // WPF AccessibilityCheckerDialog: Width/Height are *outer* sizes of a resizable window;
            // Avalonia sizes the client area, so take the Windows caption/frame off.
            Width = AccessibilityCheckerDialogMetrics.Width - WpfResizableFrameWidth,
            Height = AccessibilityCheckerDialogMetrics.Height - WpfResizableFrameHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = true,
        };
        AvaloniaCompactDialogChrome.ApplyWindow(dialog);
        AutomationProperties.SetAutomationId(dialog, "AccessibilityCheckerDialog");

        var resultsTree = new TreeView
        {
            Margin = new Thickness(0),
            BorderThickness = new Thickness(0),
            FontSize = AccessibilityCheckerDialogMetrics.BodyFontSize,
            Background = Brushes.White,
        };
        resultsTree.Styles.Add(new Style(s => s.OfType<TreeViewItem>())
        {
            Setters =
            {
                new Setter(TreeViewItem.MinHeightProperty, 20.0),
                new Setter(TreeViewItem.PaddingProperty, new Thickness(2, 0)),
            },
        });
        resultsTree.Styles.Add(new Style(s => s.OfType<TreeViewItem>()
            .Template().OfType<global::Avalonia.Controls.Presenters.ContentPresenter>().Name("PART_HeaderPresenter"))
        {
            Setters =
            {
                new Setter(Layoutable.MinHeightProperty, 20.0),
                new Setter(global::Avalonia.Controls.Presenters.ContentPresenter.MarginProperty, new Thickness(0)),
                new Setter(global::Avalonia.Controls.Presenters.ContentPresenter.PaddingProperty, new Thickness(2, 0)),
            },
        });
        resultsTree.Styles.Add(new Style(s => s.OfType<TreeViewItem>().Class(":selected"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xE6, 0xF0, 0xFA))),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
            },
        });
        resultsTree.Styles.Add(new Style(s => s.OfType<TreeViewItem>()
            .Class(":selected")
            .Template().OfType<Border>().Name("PART_LayoutRoot"))
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xE6, 0xF0, 0xFA))),
                new Setter(Border.BorderBrushProperty, Brushes.Transparent),
                new Setter(Border.BorderThicknessProperty, new Thickness(0)),
            },
        });
        ApplyAutomation(resultsTree, plan.IssueListAutomation);

        // WPF: the results tree owns the star row between the "Inspection Results" header and the
        // additional-information block, so it takes whatever height the window leaves.
        var resultsBorder = new Border
        {
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xAB, 0xAB, 0xAB)),
            BorderThickness = new Thickness(1),
            Child = resultsTree,
        };

        TreeViewItem? firstLeaf = null;
        foreach (var node in plan.TreeNodes)
            resultsTree.Items.Add(CreateAccessibilityCheckerTreeNode(node, ref firstLeaf));

        var whyFixText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var howToFixText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var additionalInfoPanel = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = plan.AdditionalInformationHeader,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6),
                },
                new TextBlock { Text = plan.WhyFixHeader, FontWeight = FontWeight.SemiBold },
                whyFixText,
                new TextBlock { Text = plan.HowToFixHeader, FontWeight = FontWeight.SemiBold },
                howToFixText,
            },
        };

        var statusBlock = new TextBlock
        {
            Text = plan.StatusText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var goToButton = new Button
        {
        };
        ApplyAction(goToButton, plan.GoToAction);

        var closeButton = new Button();
        ApplyAction(closeButton, plan.CloseAction);
        AvaloniaCompactDialogChrome.ApplyButton(
            goToButton,
            AvaloniaCompactDialogChrome.WindowsStyle,
            AccessibilityCheckerDialogMetrics.ActionButtonWidth,
            isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(
            closeButton,
            AvaloniaCompactDialogChrome.WindowsStyle,
            AccessibilityCheckerDialogMetrics.ActionButtonWidth);

        AccessibilityIssue? selectedIssue = null;

        AccessibilityCheckerItemPlan? SelectedItem()
        {
            if (resultsTree.SelectedItem is not TreeViewItem node)
                return null;
            return node.Tag switch
            {
                AccessibilityCheckerItemPlan item => item,
                AccessibilityCheckerGroupPlan group => group.Items.Count > 0 ? group.Items[0] : null,
                _ => null,
            };
        }

        AccessibilityCheckerGroupPlan? SelectedGroup()
        {
            if (resultsTree.SelectedItem is not TreeViewItem node)
                return null;
            return node.Tag as AccessibilityCheckerGroupPlan;
        }

        void UpdateSelection()
        {
            var selection = AccessibilityCheckerDialogPlanner.CreateSelection(SelectedItem(), SelectedGroup(), plan);

            goToButton.IsEnabled = selection.CanNavigate;

            if (!selection.HasAdditionalInformation)
            {
                additionalInfoPanel.IsVisible = false;
                statusBlock.Text = selection.StatusText;
                return;
            }

            additionalInfoPanel.IsVisible = true;
            whyFixText.Text = selection.WhyFix;
            howToFixText.Text = selection.HowToFix;
            statusBlock.Text = selection.StatusText;
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

        var titleBlock = new TextBlock
        {
            Text = plan.Title,
            FontSize = AccessibilityCheckerDialogMetrics.TitleFontSize,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var resultsHeader = new TextBlock
        {
            Text = plan.InspectionResultsHeader,
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
            // WPF: DialogButtonRowFactory.Create(goTo, close, (0, 12, 0, 0)) -- 8px apart, no rule.
            Spacing = 8,
            Children =
            {
                goToButton,
                closeButton,
            },
        };

        var buttonBar = new Border
        {
            Padding = new Thickness(0, 12, 0, 0),
            Child = buttonRow,
        };

        var innerPanel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(titleBlock, Dock.Top);
        DockPanel.SetDock(buttonBar, Dock.Bottom);
        DockPanel.SetDock(statusBlock, Dock.Bottom);
        innerPanel.Children.Add(titleBlock);
        innerPanel.Children.Add(buttonBar);
        innerPanel.Children.Add(statusBlock);
        innerPanel.Children.Add(bodyGrid);

        dialog.Content = new Border
        {
            Padding = new Thickness(AccessibilityCheckerDialogMetrics.ContentMargin),
            Child = innerPanel,
        };

        dialog.Opened += (_, _) =>
        {
            if (firstLeaf is not null)
                firstLeaf.IsSelected = true;
            UpdateSelection();
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => (firstLeaf ?? (Control)resultsTree).Focus(), global::Avalonia.Threading.DispatcherPriority.Background);
        };

        await dialog.ShowDialog(this);

        if (selectedIssue is not null)
        {
            ClearSelectedDrawingObject();
            var target = AccessibilityCheckerDialogPlanner.GetNavigationTarget(selectedIssue);
            var result = _session.GoToCell(target);
            if (!result.Success)
            {
                ShowEditIssue(result.ErrorMessage ?? UiText.Get("AccessibilityChecker_NavigateFailed"));
                return;
            }

            if (result.SelectedRange is { } selectedRange)
                RefreshShell(UiText.Format(
                    "MainLoc_SelectedX",
                    FormatRangeReference(selectedRange)));
        }
    }

    private static TreeViewItem CreateAccessibilityCheckerTreeNode(
        AccessibilityCheckerTreeNodePlan plan,
        ref TreeViewItem? initialNode)
    {
        var node = new TreeViewItem
        {
            Template = AccessibilityCheckerTreeItemTemplate,
            Header = plan.Header,
            FontWeight = plan.Kind == AccessibilityCheckerTreeNodeKind.Section
                ? FontWeight.SemiBold
                : FontWeight.Normal,
            IsExpanded = plan.IsExpanded,
            Tag = plan.Item is not null ? plan.Item : plan.Group,
        };

        if (plan.IsInitialSelection)
            initialNode = node;

        foreach (var child in plan.Children)
            node.Items.Add(CreateAccessibilityCheckerTreeNode(child, ref initialNode));

        return node;
    }

    // WPF TreeView rows: a small chevron (hidden but still reserving its slot on leaves) before the
    // header, each level indented about 12px. The Fluent item template uses a 36px chevron slot and
    // 16px levels, and its chevron is a Button that the shared dialog chrome later widens to the
    // command-button minimum, which pushed group captions far to the right of their leaves. This
    // template draws the chevron as a plain shape so the row geometry stays the WPF one.
    private const double AccessibilityCheckerTreeIndent = 12;

    private static readonly FuncControlTemplate<TreeViewItem> AccessibilityCheckerTreeItemTemplate =
        new((item, scope) =>
        {
            var chevronPath = new global::Avalonia.Controls.Shapes.Path
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                StrokeThickness = 1,
                Width = 8,
                Height = 8,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
            chevronPath.Bind(
                global::Avalonia.Controls.Shapes.Path.DataProperty,
                new Binding(nameof(TreeViewItem.IsExpanded))
                {
                    Source = item,
                    Converter = new FuncValueConverter<bool, Geometry>(expanded =>
                        Geometry.Parse(expanded ? "M 0,2 L 4,6 L 8,2" : "M 2,0 L 6,4 L 2,8")),
                });
            var chevron = new Border
            {
                Width = 12,
                Height = 12,
                Margin = new Thickness(5, 0, 2, 0),
                Background = Brushes.Transparent,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                Child = chevronPath,
            };
            chevronPath.Bind(
                Visual.IsVisibleProperty,
                new Binding(nameof(ItemsControl.ItemCount))
                {
                    Source = item,
                    Converter = new FuncValueConverter<int, bool>(count => count > 0),
                });
            chevron.PointerPressed += (_, args) =>
            {
                if (item.ItemCount == 0)
                    return;
                item.IsExpanded = !item.IsExpanded;
                args.Handled = true;
            };

            var headerPresenter = new ContentPresenter
            {
                Name = "PART_HeaderPresenter",
                Padding = new Thickness(2, 0),
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            };
            headerPresenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(HeaderedItemsControl.Header)) { Source = item });
            scope.Register("PART_HeaderPresenter", headerPresenter);

            var header = new Grid
            {
                Name = "PART_Header",
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children = { chevron, headerPresenter },
            };
            Grid.SetColumn(headerPresenter, 1);
            header.Bind(
                Layoutable.MarginProperty,
                new Binding(nameof(TreeViewItem.Level))
                {
                    Source = item,
                    Converter = new FuncValueConverter<int, Thickness>(level =>
                        new Thickness(level * AccessibilityCheckerTreeIndent, 0, 0, 0)),
                });
            scope.Register("PART_Header", header);

            var layoutRoot = new Border
            {
                Name = "PART_LayoutRoot",
                MinHeight = 20,
                Child = header,
            };
            scope.Register("PART_LayoutRoot", layoutRoot);

            var itemsPresenter = new ItemsPresenter { Name = "PART_ItemsPresenter" };
            itemsPresenter.Bind(
                Visual.IsVisibleProperty,
                new Binding(nameof(TreeViewItem.IsExpanded)) { Source = item });
            scope.Register("PART_ItemsPresenter", itemsPresenter);

            return new StackPanel { Children = { layoutRoot, itemsPresenter } };
        });

    private static void ApplyAction(Button button, AccessibilityCheckerActionSpec action)
    {
        button.Content = action.Text;
        button.IsDefault = action.IsDefault;
        button.IsCancel = action.IsCancel;
        ApplyAutomation(button, action.Automation);
    }

    private static void ApplyAutomation(StyledElement target, AccessibilityCheckerAutomationSpec automation)
    {
        AutomationProperties.SetName(target, automation.Name);
        AutomationProperties.SetAutomationId(target, automation.AutomationId);
        AutomationProperties.SetHelpText(target, automation.HelpText);
    }
}
