using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation.Charts.Editing;
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
        Width = ChartTypeChangePlanner.DialogWidth;
        Height = ChartTypeChangePlanner.DialogHeight;
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
        panel.Height = ChartTypeChangePlanner.PickerPanelHeight;
        // The "All Charts" grid has three columns: Auto (category list) | Star (subtype gallery) | 180 (preview).
        // The shared InsertChartDialog hosts this grid inside a width-bounded TabControl, so the Star column
        // resolves against a finite width and the gallery gets a real slot. Here the grid is instead docked
        // Top in a DockPanel with LastChildFill = false, which MEASURES Top-docked children with an INFINITE
        // available width. A Star column measured at infinite width distributes "remaining" space out of an
        // unbounded total and collapses to a ZERO-WIDTH layout slot, so the gallery (col 1) is clipped to
        // width 0 and renders blank — exactly what the parity capture showed (only the Auto category list and
        // the fixed-width preview survived). Re-pin THIS grid's middle column to a fixed width so the gallery
        // no longer depends on Star resolution; the shared InsertChartDialog grid is untouched.
        // Column 0 is Auto and shares its row-0 cell with the long "Choose a subtype…" help heading, so left
        // unconstrained it inflates to ~400px (enough to lay the heading on one line) and shoves the preview
        // off the fixed-width dialog. Pin all three columns to fixed widths sized to their real content —
        //   category list 150 + 12px gap | gallery 180 + 12px gap | preview 180 —
        // so the whole "All Charts" row fits the 640px dialog with the gallery between the list and preview.
        panel.ColumnDefinitions[0].Width = new GridLength(ChartTypeChangePlanner.PickerCategoryColumnWidth);
        panel.ColumnDefinitions[1].Width = new GridLength(ChartTypeChangePlanner.PickerSubtypeColumnWidth);
        panel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        // The "All Charts" heading + help text live in row 0 of column 0; now that column 0 is a fixed 162px
        // they would wrap. Let the heading span all three columns so it reads on one line as before.
        foreach (UIElement child in panel.Children)
        {
            if (Grid.GetRow(child) == 0 && Grid.GetColumn(child) == 0)
                Grid.SetColumnSpan(child, panel.ColumnDefinitions.Count);
        }
        // The subtype gallery (Excel's "Clustered Column / Stacked Column / 100% Stacked / 3-D" row that
        // sits between the category list and the preview) is normally filled by the category list's
        // SelectionChanged handler. In the headless parity-capture render the gallery could come up empty,
        // so populate and select it directly here — mirroring the Avalonia ShowChartTypePickerAsync — and
        // give it a deterministic width plus a visible border so it reads like the Linux/Excel surface.
        PopulateSubtypeGallery(currentType);
        _subtypeGallery.Width = ChartTypeChangePlanner.PickerSubtypeWidth;
        _subtypeGallery.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
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

        var choices = ChartTypePickerPlanner.GetGalleryChoices(category.Name, WpfResourceKeyTextResolver.Instance);
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
