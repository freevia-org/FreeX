using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class ManageConditionalFormatsDialog
{
    private GridView CreateRulesGridView()
    {
        var gridView = new GridView();

        gridView.Columns.Add(new GridViewColumn
        {
            Header = "#",
            Width  = 30,
            DisplayMemberBinding = new Binding("Priority")
        });
        gridView.Columns.Add(CreateRuleDescriptionColumn());
        gridView.Columns.Add(CreateFormatPreviewColumn());
        gridView.Columns.Add(CreateAppliesToColumn());
        gridView.Columns.Add(CreateStopIfTrueColumn());

        return gridView;
    }

    private static GridViewColumn CreateRuleDescriptionColumn()
    {
        var descTemplate = new DataTemplate();
        var descFactory  = new FrameworkElementFactory(typeof(TextBlock));
        descFactory.SetBinding(TextBlock.TextProperty, new Binding(".") { Converter = new RuleDescriptionConverter() });
        descFactory.SetValue(TextBlock.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        descTemplate.VisualTree = descFactory;

        return new GridViewColumn
        {
            Header = UiText.Get("ManageConditionalFormats_RuleTypeColumn"),
            Width = 200,
            CellTemplate = descTemplate
        };
    }

    private static GridViewColumn CreateFormatPreviewColumn()
    {
        var fmtTemplate = new DataTemplate();
        var previewBorderFactory = new FrameworkElementFactory(typeof(Border));
        previewBorderFactory.SetValue(Border.WidthProperty, 82.0);
        previewBorderFactory.SetValue(Border.HeightProperty, 20.0);
        previewBorderFactory.SetValue(Border.MarginProperty, new Thickness(0, 2, 0, 2));
        previewBorderFactory.SetValue(Border.BorderBrushProperty, Brushes.DarkGray);
        previewBorderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(0.5));
        previewBorderFactory.SetBinding(Border.BackgroundProperty, new Binding(".") { Converter = new PreviewBrushConverter() });

        var previewTextFactory = new FrameworkElementFactory(typeof(TextBlock));
        previewTextFactory.SetValue(TextBlock.TextProperty, UiText.Get(ManageConditionalFormatsPlanner.FormatPreviewSampleKey));
        previewTextFactory.SetValue(TextBlock.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        previewTextFactory.SetValue(TextBlock.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        previewTextFactory.SetValue(TextBlock.FontSizeProperty, 10.0);
        previewTextFactory.SetBinding(TextBlock.ForegroundProperty, new Binding(".") { Converter = new PreviewForegroundBrushConverter() });
        previewTextFactory.SetBinding(TextBlock.FontWeightProperty, new Binding(".") { Converter = new PreviewFontWeightConverter() });
        previewTextFactory.SetBinding(TextBlock.FontStyleProperty, new Binding(".") { Converter = new PreviewFontStyleConverter() });
        previewTextFactory.SetBinding(TextBlock.TextDecorationsProperty, new Binding(".") { Converter = new PreviewTextDecorationsConverter() });
        previewBorderFactory.AppendChild(previewTextFactory);

        fmtTemplate.VisualTree = previewBorderFactory;
        return new GridViewColumn
        {
            Header = UiText.Get("ManageConditionalFormats_FormatColumn"),
            Width = 95,
            CellTemplate = fmtTemplate
        };
    }

    private GridViewColumn CreateAppliesToColumn()
    {
        var appliesToTemplate = new DataTemplate();
        var appliesToPanelFactory = new FrameworkElementFactory(typeof(DockPanel));
        appliesToPanelFactory.SetValue(DockPanel.LastChildFillProperty, true);

        var rangePickerFactory = new FrameworkElementFactory(typeof(Button));
        rangePickerFactory.SetValue(ContentControl.ContentProperty, "...");
        rangePickerFactory.SetValue(FrameworkElement.WidthProperty, 24.0);
        rangePickerFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 0, 0));
        rangePickerFactory.SetValue(FrameworkElement.ToolTipProperty, UiText.Get("ManageConditionalFormats_CollapseDialogAndSelectAppliesToRange"));
        rangePickerFactory.SetValue(AutomationProperties.NameProperty, UiText.Get("ManageConditionalFormats_SelectAppliesToRange"));
        rangePickerFactory.SetValue(AutomationProperties.HelpTextProperty, UiText.Get("ManageConditionalFormats_SelectAppliesToRangeHelpText"));
        rangePickerFactory.SetValue(DockPanel.DockProperty, Dock.Right);
        rangePickerFactory.SetBinding(UIElement.IsEnabledProperty, new Binding("IsSelected")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListViewItem), 1)
        });
        rangePickerFactory.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(RangePickerButton_Click));

        var appliesToFactory = new FrameworkElementFactory(typeof(TextBox));
        appliesToFactory.SetValue(Control.PaddingProperty, new Thickness(2, 0, 2, 0));
        appliesToFactory.SetValue(Control.VerticalContentAlignmentProperty, System.Windows.VerticalAlignment.Center);
        appliesToFactory.SetBinding(UIElement.IsEnabledProperty, new Binding("IsSelected")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListViewItem), 1)
        });
        appliesToFactory.SetBinding(TextBox.TextProperty, new Binding(nameof(ConditionalFormat.AppliesTo))
        {
            Converter = new AppliesToRangeConverter(_sheet.Id),
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
        });
        appliesToFactory.AddHandler(UIElement.GotFocusEvent, new RoutedEventHandler(AppliesToTextBox_GotFocus));
        appliesToFactory.AddHandler(UIElement.LostFocusEvent, new RoutedEventHandler(AppliesToTextBox_LostFocus));

        appliesToPanelFactory.AppendChild(rangePickerFactory);
        appliesToPanelFactory.AppendChild(appliesToFactory);
        appliesToTemplate.VisualTree = appliesToPanelFactory;

        return new GridViewColumn
        {
            Header = UiText.Get("ManageConditionalFormats_AppliesToColumn"),
            Width = 170,
            CellTemplate = appliesToTemplate
        };
    }

    // Stashes the text shown when the box gains focus, so LostFocus (below) can tell "the user
    // actually retyped this reference" apart from "focus merely passed through the cell"
    // (R49-commands-cf-manage-3-2).
    private static void AppliesToTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            textBox.Tag = textBox.Text;
    }

    // The binding's ConvertBack already commits the newly-typed range into rule.AppliesTo
    // (which runs first, since this handler is attached after the binding). Typing a fresh
    // range must also drop any stale AdditionalRanges left over from a prior multi-area
    // selection, matching the range-picker path (see ManageConditionalFormatsPlanner.ApplyRuleRange).
    // But with UpdateSourceTrigger=LostFocus, WPF fires this handler on EVERY focus loss -- even
    // when the user only clicked into the cell (e.g. to inspect it, or via keyboard row
    // navigation) and back out without changing a single character. Only clear AdditionalRanges
    // when the displayed text actually changed from what it was when focus was gained; otherwise
    // a multi-area rule's non-active areas were being silently discarded on a no-op focus visit.
    private void AppliesToTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: ConditionalFormat rule } textBox)
            return;

        if (textBox.Tag as string == textBox.Text)
            return;

        // r162 remediation: the text genuinely changed, so the binding has already committed a new
        // AppliesTo into the rule. Mark the dialog dirty even when the new text does not parse --
        // the model was mutated either way, and OK must not silently drop it.
        MarkPendingChange();

        if (TryParseAppliesToText(textBox.Text, _sheet.Id, out _))
            rule.AdditionalRanges = null;
    }

    // r162 remediation. Both direct-bound editors in this ListView -- this checkbox and the
    // Applies To text box -- write straight into the ConditionalFormat through their bindings,
    // without passing through any of the dialog's own edit commands. Those commands are where
    // MarkPendingChange lives, so a visit whose ONLY edit came through a binding left
    // _hasPendingChanges false, and the r162 fix that made OK skip a no-op commit then discarded
    // the edit entirely. A binding that mutates the model is an edit and must mark the dialog dirty.
    private GridViewColumn CreateStopIfTrueColumn()
    {
        var stopIfTrueTemplate = new DataTemplate();
        var stopIfTrueFactory  = new FrameworkElementFactory(typeof(CheckBox));
        stopIfTrueFactory.SetValue(CheckBox.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        stopIfTrueFactory.SetValue(CheckBox.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        stopIfTrueFactory.SetBinding(
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(ConditionalFormat.StopIfTrue))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        stopIfTrueFactory.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(StopIfTrueCheckBox_Toggled));
        stopIfTrueFactory.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(StopIfTrueCheckBox_Toggled));
        stopIfTrueTemplate.VisualTree = stopIfTrueFactory;

        return new GridViewColumn
        {
            Header = UiText.Get("ManageConditionalFormats_StopIfTrueColumn"),
            Width = 85,
            CellTemplate = stopIfTrueTemplate
        };
    }

    // The binding has already written the new value into rule.StopIfTrue by the time this runs
    // (handlers attached after a binding fire after it commits), so there is nothing to apply here
    // -- only the dirty flag to raise.
    private void StopIfTrueCheckBox_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: ConditionalFormat })
            MarkPendingChange();
    }
}
