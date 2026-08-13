using System.Globalization;
using System.Windows;
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
    private static readonly ParagraphDialogVisualMetrics Layout =
        ParagraphBreaksDialogPlanner.VisualMetrics;

    /// <summary>
    /// Show the Paragraph dialog seeded from <paramref name="current"/>. Returns the edited values, or
    /// null if cancelled.
    /// </summary>
    public static ParagraphBreaksResult? Prompt(Window? owner, ParagraphFormatting current)
    {
        ParagraphBreaksResult? result = null;
        var surface = ParagraphBreaksDialogPlanner.Surface;

        var dialog = new Window
        {
            Title = surface.Title,
            Width = Layout.WindowWidth,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };
        WpfDialogSurfaceSemantics.Apply(dialog, surface);

        var state = ParagraphBreaksDialogPlanner.BuildInitialState(current, CultureInfo.CurrentCulture);

        var leftBox = NumberBox(state.LeftText);
        var rightBox = NumberBox(state.RightText);
        var spaceBefore = NumberBox(state.SpaceBeforeText);
        var spaceAfter = NumberBox(state.SpaceAfterText);
        var lineSpacing = NumberBox(state.LineSpacingText);
        var contextualSpacingCheck = new CheckBox
        {
            Content = surface.Field(ParagraphBreaksDialogField.ContextualSpacing).Label,
            IsChecked = state.ContextualSpacing,
            Margin = ToThickness(Layout.WpfContextualSpacingMargin),
        };
        WpfDialogSurfaceSemantics.Apply(leftBox, surface.Field(ParagraphBreaksDialogField.Left));
        WpfDialogSurfaceSemantics.Apply(rightBox, surface.Field(ParagraphBreaksDialogField.Right));
        WpfDialogSurfaceSemantics.Apply(spaceBefore, surface.Field(ParagraphBreaksDialogField.SpaceBefore));
        WpfDialogSurfaceSemantics.Apply(spaceAfter, surface.Field(ParagraphBreaksDialogField.SpaceAfter));
        WpfDialogSurfaceSemantics.Apply(lineSpacing, surface.Field(ParagraphBreaksDialogField.LineSpacing));
        WpfDialogSurfaceSemantics.Apply(contextualSpacingCheck, surface.Field(ParagraphBreaksDialogField.ContextualSpacing));

        var specialAmtBox = NumberBox(state.SpecialAmountText);
        var specialBox = new ComboBox { MinWidth = Layout.NumericFieldMinWidth };
        foreach (var item in ParagraphIndentDialogPlanner.SpecialItems)
            specialBox.Items.Add(item.Label);
        specialBox.SelectedIndex = state.SpecialIndex;
        specialBox.SelectionChanged += (_, _) =>
            specialAmtBox.IsEnabled = ParagraphBreaksDialogPlanner.IsSpecialAmountEnabled(specialBox.SelectedIndex);
        specialAmtBox.IsEnabled = state.SpecialAmountEnabled;
        WpfDialogSurfaceSemantics.Apply(specialBox, surface.Field(ParagraphBreaksDialogField.Special));
        WpfDialogSurfaceSemantics.Apply(specialAmtBox, surface.Field(ParagraphBreaksDialogField.SpecialAmount));

        var indentsPanel = new Grid { Margin = ToThickness(Layout.WpfTabContentMargin) };
        indentsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        indentsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 8; i++)
            indentsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddGridRow(indentsPanel, 0, surface.Field(ParagraphBreaksDialogField.Left).Label, leftBox);
        AddGridRow(indentsPanel, 1, surface.Field(ParagraphBreaksDialogField.Right).Label, rightBox);
        AddGridRow(indentsPanel, 2, surface.Field(ParagraphBreaksDialogField.Special).Label, specialBox);
        AddGridRow(indentsPanel, 3, surface.Field(ParagraphBreaksDialogField.SpecialAmount).Label, specialAmtBox);
        AddGridRow(indentsPanel, 4, surface.Field(ParagraphBreaksDialogField.SpaceBefore).Label, spaceBefore);
        AddGridRow(indentsPanel, 5, surface.Field(ParagraphBreaksDialogField.SpaceAfter).Label, spaceAfter);
        AddGridRow(indentsPanel, 6, surface.Field(ParagraphBreaksDialogField.LineSpacing).Label, lineSpacing);
        Grid.SetRow(contextualSpacingCheck, 7);
        Grid.SetColumnSpan(contextualSpacingCheck, 2);
        indentsPanel.Children.Add(contextualSpacingCheck);

        var keepWithNextCheck = new CheckBox { Content = surface.Field(ParagraphBreaksDialogField.KeepWithNext).Label, IsChecked = state.KeepWithNext, Margin = ToThickness(Layout.CheckBoxMargin) };
        var keepLinesTogetherCheck = new CheckBox { Content = surface.Field(ParagraphBreaksDialogField.KeepLinesTogether).Label, IsChecked = state.KeepLinesTogether, Margin = ToThickness(Layout.CheckBoxMargin) };
        var widowControlCheck = new CheckBox { Content = surface.Field(ParagraphBreaksDialogField.WidowControl).Label, IsChecked = state.WidowControl, Margin = ToThickness(Layout.CheckBoxMargin) };
        var pageBreakBeforeCheck = new CheckBox { Content = surface.Field(ParagraphBreaksDialogField.PageBreakBefore).Label, IsChecked = state.PageBreakBefore, Margin = ToThickness(Layout.CheckBoxMargin) };
        var suppressHyphensCheck = new CheckBox { Content = surface.Field(ParagraphBreaksDialogField.SuppressAutoHyphens).Label, IsChecked = state.SuppressAutoHyphens, Margin = ToThickness(Layout.CheckBoxMargin) };
        var suppressLineNumbersCheck = new CheckBox { Content = surface.Field(ParagraphBreaksDialogField.SuppressLineNumbers).Label, IsChecked = state.SuppressLineNumbers, Margin = ToThickness(Layout.CheckBoxMargin) };
        WpfDialogSurfaceSemantics.Apply(keepWithNextCheck, surface.Field(ParagraphBreaksDialogField.KeepWithNext));
        WpfDialogSurfaceSemantics.Apply(keepLinesTogetherCheck, surface.Field(ParagraphBreaksDialogField.KeepLinesTogether));
        WpfDialogSurfaceSemantics.Apply(widowControlCheck, surface.Field(ParagraphBreaksDialogField.WidowControl));
        WpfDialogSurfaceSemantics.Apply(pageBreakBeforeCheck, surface.Field(ParagraphBreaksDialogField.PageBreakBefore));
        WpfDialogSurfaceSemantics.Apply(suppressHyphensCheck, surface.Field(ParagraphBreaksDialogField.SuppressAutoHyphens));
        WpfDialogSurfaceSemantics.Apply(suppressLineNumbersCheck, surface.Field(ParagraphBreaksDialogField.SuppressLineNumbers));

        var breaksPanel = new StackPanel { Margin = ToThickness(Layout.WpfTabContentMargin) };
        var paginationHeading = new TextBlock
        {
            Text = surface.Field(ParagraphBreaksDialogField.PaginationSection).Label,
            FontWeight = FontWeights.SemiBold,
            Margin = ToThickness(Layout.SectionHeadingMargin)
        };
        WpfDialogSurfaceSemantics.Apply(
            paginationHeading,
            surface.Field(ParagraphBreaksDialogField.PaginationSection));
        breaksPanel.Children.Add(paginationHeading);
        breaksPanel.Children.Add(keepWithNextCheck);
        breaksPanel.Children.Add(keepLinesTogetherCheck);
        breaksPanel.Children.Add(widowControlCheck);
        breaksPanel.Children.Add(pageBreakBeforeCheck);
        breaksPanel.Children.Add(new Separator { Margin = ToThickness(Layout.SectionSeparatorMargin) });
        var formattingExceptionsHeading = new TextBlock
        {
            Text = surface.Field(ParagraphBreaksDialogField.FormattingExceptionsSection).Label,
            FontWeight = FontWeights.SemiBold,
            Margin = ToThickness(Layout.SectionHeadingMargin)
        };
        WpfDialogSurfaceSemantics.Apply(
            formattingExceptionsHeading,
            surface.Field(ParagraphBreaksDialogField.FormattingExceptionsSection));
        breaksPanel.Children.Add(formattingExceptionsHeading);
        breaksPanel.Children.Add(suppressHyphensCheck);
        breaksPanel.Children.Add(suppressLineNumbersCheck);

        var tabs = new TabControl();
        var indentsTab = new TabItem { Header = surface.Field(ParagraphBreaksDialogField.IndentsAndSpacingTab).Label, Content = new ScrollViewer { Content = indentsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        var breaksTab = new TabItem { Header = surface.Field(ParagraphBreaksDialogField.LineAndPageBreaksTab).Label, Content = new ScrollViewer { Content = breaksPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        WpfDialogSurfaceSemantics.Apply(indentsTab, surface.Field(ParagraphBreaksDialogField.IndentsAndSpacingTab));
        WpfDialogSurfaceSemantics.Apply(breaksTab, surface.Field(ParagraphBreaksDialogField.LineAndPageBreaksTab));
        tabs.Items.Add(indentsTab);
        tabs.Items.Add(breaksTab);

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

        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: Layout.ActionButtonWidth,
            rowMargin: ToThickness(Layout.WpfActionRowMargin));

        var root = new StackPanel { Margin = ToThickness(Layout.WpfRootMargin) };
        root.Children.Add(tabs);
        root.Children.Add(buttons);
        dialog.Content = root;

        DialogFocus.FocusAndSelect(leftBox);
        return dialog.ShowDialog() == true ? result : null;
    }

    private static TextBox NumberBox(string text) => new()
    {
        Text = text,
        MinWidth = Layout.NumericFieldMinWidth,
        Margin = ToThickness(Layout.FieldControlMargin),
    };

    private static void AddGridRow(Grid grid, int row, string label, UIElement field)
    {
        var block = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = ToThickness(Layout.FieldLabelMargin),
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is FrameworkElement fe)
            fe.Margin = ToThickness(Layout.FieldControlMargin);
        grid.Children.Add(field);
    }

    private static Thickness ToThickness(ParagraphDialogThickness value) =>
        new(value.Left, value.Top, value.Right, value.Bottom);
}
