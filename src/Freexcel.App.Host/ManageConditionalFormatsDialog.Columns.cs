using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
            Header = "Rule (Type)",
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
        previewTextFactory.SetValue(TextBlock.TextProperty, "AaBbCcYyZz");
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
            Header = "Format",
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
        rangePickerFactory.SetValue(FrameworkElement.ToolTipProperty, "Collapse dialog and select Applies To range");
        rangePickerFactory.SetValue(AutomationProperties.NameProperty, "Select Applies To range");
        rangePickerFactory.SetValue(AutomationProperties.HelpTextProperty, "Collapse dialog and select a worksheet range for this conditional format rule.");
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

        appliesToPanelFactory.AppendChild(rangePickerFactory);
        appliesToPanelFactory.AppendChild(appliesToFactory);
        appliesToTemplate.VisualTree = appliesToPanelFactory;

        return new GridViewColumn
        {
            Header = "Applies To",
            Width = 170,
            CellTemplate = appliesToTemplate
        };
    }

    private static GridViewColumn CreateStopIfTrueColumn()
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
        stopIfTrueTemplate.VisualTree = stopIfTrueFactory;

        return new GridViewColumn
        {
            Header = "Stop If True",
            Width = 85,
            CellTemplate = stopIfTrueTemplate
        };
    }
}
