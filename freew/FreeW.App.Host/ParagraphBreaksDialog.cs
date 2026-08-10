using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using ParagraphBreaksResult = FreeW.App.Presentation.Dialogs.ParagraphBreaksDialogResult;

namespace FreeW.App.Host;

/// <summary>
/// A two-tab Paragraph dialog matching Word's Home &gt; Paragraph dialog-launcher. The Indents and
/// Spacing tab exposes indents, space before/after, and line spacing; the Line and Page Breaks tab
/// exposes backed <see cref="ParagraphFormatting"/> pagination toggles.
/// </summary>
internal static class ParagraphBreaksDialog
{
    /// <summary>
    /// Show the Paragraph dialog seeded from <paramref name="current"/>. Returns the edited values, or
    /// null if cancelled.
    /// </summary>
    public static ParagraphBreaksResult? Prompt(Window? owner, ParagraphFormatting current)
    {
        ParagraphBreaksResult? result = null;

        var dialog = new Window
        {
            Title = "Paragraph",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        var state = ParagraphBreaksDialogPlanner.BuildInitialState(current, CultureInfo.CurrentCulture);

        var leftBox = NumberBox(state.LeftText);
        var rightBox = NumberBox(state.RightText);
        var spaceBefore = NumberBox(state.SpaceBeforeText);
        var spaceAfter = NumberBox(state.SpaceAfterText);
        var lineSpacing = NumberBox(state.LineSpacingText);
        var contextualSpacingCheck = new CheckBox
        {
            Content = "Don't add space between paragraphs of the same style",
            IsChecked = state.ContextualSpacing,
            Margin = new Thickness(0, 4, 0, 0),
        };
        AutomationProperties.SetAutomationId(leftBox, ParagraphBreaksDialogPlanner.LeftIndentAutomationId);

        var specialAmtBox = NumberBox(state.SpecialAmountText);
        var specialBox = new ComboBox { MinWidth = 120, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var item in ParagraphIndentDialogPlanner.SpecialItems)
            specialBox.Items.Add(item.Label);
        specialBox.SelectedIndex = state.SpecialIndex;
        specialBox.SelectionChanged += (_, _) =>
            specialAmtBox.IsEnabled = ParagraphBreaksDialogPlanner.IsSpecialAmountEnabled(specialBox.SelectedIndex);
        specialAmtBox.IsEnabled = state.SpecialAmountEnabled;

        var indentsPanel = new Grid { Margin = new Thickness(10) };
        indentsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        indentsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 8; i++)
            indentsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddGridRow(indentsPanel, 0, "Left indent (pt):", leftBox);
        AddGridRow(indentsPanel, 1, "Right indent (pt):", rightBox);
        AddGridRow(indentsPanel, 2, "Special:", specialBox);
        AddGridRow(indentsPanel, 3, "By (pt):", specialAmtBox);
        AddGridRow(indentsPanel, 4, "Space before (pt):", spaceBefore);
        AddGridRow(indentsPanel, 5, "Space after (pt):", spaceAfter);
        AddGridRow(indentsPanel, 6, "Line spacing (\u00d7):", lineSpacing);
        Grid.SetRow(contextualSpacingCheck, 7);
        Grid.SetColumnSpan(contextualSpacingCheck, 2);
        indentsPanel.Children.Add(contextualSpacingCheck);

        var keepWithNextCheck = new CheckBox { Content = "Keep with next", IsChecked = state.KeepWithNext, Margin = new Thickness(0, 0, 0, 6) };
        var keepLinesTogetherCheck = new CheckBox { Content = "Keep lines together", IsChecked = state.KeepLinesTogether, Margin = new Thickness(0, 0, 0, 6) };
        var widowControlCheck = new CheckBox { Content = "Widow/orphan control", IsChecked = state.WidowControl, Margin = new Thickness(0, 0, 0, 6) };
        var pageBreakBeforeCheck = new CheckBox { Content = "Page break before", IsChecked = state.PageBreakBefore, Margin = new Thickness(0, 0, 0, 6) };
        var suppressHyphensCheck = new CheckBox { Content = "Suppress auto-hyphenation", IsChecked = state.SuppressAutoHyphens, Margin = new Thickness(0, 0, 0, 6) };
        var suppressLineNumbersCheck = new CheckBox { Content = "Suppress line numbers", IsChecked = state.SuppressLineNumbers, Margin = new Thickness(0, 0, 0, 6) };

        var breaksPanel = new StackPanel { Margin = new Thickness(10) };
        breaksPanel.Children.Add(new TextBlock
        {
            Text = "Pagination",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        breaksPanel.Children.Add(keepWithNextCheck);
        breaksPanel.Children.Add(keepLinesTogetherCheck);
        breaksPanel.Children.Add(widowControlCheck);
        breaksPanel.Children.Add(pageBreakBeforeCheck);
        breaksPanel.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 8) });
        breaksPanel.Children.Add(new TextBlock
        {
            Text = "Formatting exceptions",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        breaksPanel.Children.Add(suppressHyphensCheck);
        breaksPanel.Children.Add(suppressLineNumbersCheck);

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Indents and Spacing", Content = new ScrollViewer { Content = indentsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new TabItem { Header = "Line and Page Breaks", Content = new ScrollViewer { Content = breaksPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

        void Accept()
        {
            var input = new ParagraphBreaksDialogInput(
                leftBox.Text,
                rightBox.Text,
                specialBox.SelectedIndex,
                specialAmtBox.Text,
                spaceBefore.Text,
                spaceAfter.Text,
                lineSpacing.Text,
                keepWithNextCheck.IsChecked == true,
                keepLinesTogetherCheck.IsChecked == true,
                widowControlCheck.IsChecked == true,
                pageBreakBeforeCheck.IsChecked == true,
                suppressHyphensCheck.IsChecked == true,
                suppressLineNumbersCheck.IsChecked == true,
                contextualSpacingCheck.IsChecked == true);

            if (!ParagraphBreaksDialogPlanner.TryBuildResult(
                    input,
                    CultureInfo.CurrentCulture,
                    out result,
                    out var validation))
            {
                DialogMessageHelper.ShowWarning(
                    dialog,
                    validation?.Message ?? ParagraphBreaksDialogPlanner.ValidationMessage);
                FocusFailure(validation?.Field);
                return;
            }

            dialog.DialogResult = true;
        }

        void FocusFailure(ParagraphBreaksDialogField? field)
        {
            tabs.SelectedIndex = 0;
            var target = field switch
            {
                ParagraphBreaksDialogField.Right => rightBox,
                ParagraphBreaksDialogField.SpecialAmount => specialAmtBox,
                ParagraphBreaksDialogField.SpaceBefore => spaceBefore,
                ParagraphBreaksDialogField.SpaceAfter => spaceAfter,
                ParagraphBreaksDialogField.LineSpacing => lineSpacing,
                _ => leftBox
            };
            DialogFocus.FocusAndSelect(target);
        }

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 10, 0, 0));

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(tabs);
        root.Children.Add(buttons);
        dialog.Content = root;

        DialogFocus.FocusAndSelect(leftBox);
        return dialog.ShowDialog() == true ? result : null;
    }

    private static TextBox NumberBox(string text) => new()
    {
        Text = text,
        MinWidth = 120,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private static void AddGridRow(Grid grid, int row, string label, UIElement field)
    {
        var block = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4),
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
}
