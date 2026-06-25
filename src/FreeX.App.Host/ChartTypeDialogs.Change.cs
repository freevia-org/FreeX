using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record ChangeChartTypeDialogResult(ChartType ChartType);

public sealed class ChangeChartTypeDialog : Window
{
    private readonly ListBox _categoryList = new();
    private readonly ListBox _subtypeGallery = new();

    public ChartType SelectedChartType { get; private set; }
    public ChangeChartTypeDialogResult Result { get; private set; }

    public ChangeChartTypeDialog(ChartType currentType)
    {
        SelectedChartType = currentType;
        Result = CreateResult(currentType);
        Title = UiText.Get("ChangeChartType_Title");
        Width = 640;
        Height = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = false };
        var heading = new TextBlock
        {
            Text = UiText.Get("ChartTypePicker_ChooseChartTypeHeading"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        var panel = InsertChartDialog.CreateAllChartsPanel(_categoryList, _subtypeGallery, currentType);
        panel.Height = 290;
        // The subtype gallery (Excel's "Clustered Column / Stacked Column / 100% Stacked / 3-D" row that
        // sits between the category list and the preview) is normally filled by the category list's
        // SelectionChanged handler. In the headless parity-capture render the gallery could come up empty,
        // so populate and select it directly here — mirroring the Avalonia ShowChartTypePickerAsync — and
        // give it a deterministic width plus a visible border so it reads like the Linux/Excel surface.
        PopulateSubtypeGallery(currentType);
        _subtypeGallery.Width = 180;
        _subtypeGallery.BorderBrush = SystemColors.ControlDarkBrush;
        _subtypeGallery.BorderThickness = new Thickness(1);
        _subtypeGallery.MouseDoubleClick += SubtypeGallery_MouseDoubleClick;
        DockPanel.SetDock(panel, Dock.Top);
        root.Children.Add(panel);
        var buttons = CreateButtonRow();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void PopulateSubtypeGallery(ChartType currentType)
    {
        if (_categoryList.SelectedItem is not ChartTypePickerCategory category)
        {
            if (_categoryList.Items.Count == 0)
                return;
            category = (ChartTypePickerCategory)_categoryList.Items[0]!;
            _categoryList.SelectedItem = category;
        }

        var choices = ChartTypePickerPlanner.GetGalleryChoices(category.Name);
        _subtypeGallery.ItemsSource = choices;
        var selected = choices.FirstOrDefault(c => c.Type == currentType)
            ?? (choices.Count > 0 ? choices[0] : null);
        _subtypeGallery.SelectedItem = selected;
    }

    public static ChangeChartTypeDialogResult CreateResult(ChartType chartType) => new(chartType);

    private StackPanel CreateButtonRow() => InsertChartDialog.CreateButtonRow(AcceptSelectedChartType);

    private void AcceptSelectedChartType()
    {
        if (_subtypeGallery.SelectedItem is ChartTypeGalleryChoice option)
            SelectedChartType = option.Type;
        Result = CreateResult(SelectedChartType);
        DialogResult = true;
    }

    private void SubtypeGallery_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        AcceptSelectedChartType();
        e.Handled = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _subtypeGallery.Focus();
        Keyboard.Focus(_subtypeGallery);
    }
}
