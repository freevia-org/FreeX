using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>The user's chosen blank-cell display mode + hidden-data visibility from <see cref="HiddenEmptyCellSettingsDialog"/>.</summary>
public sealed record HiddenEmptyCellSettingsResult(ChartBlankDisplayMode BlankDisplayMode, bool ShowDataInHiddenRowsAndColumns);

/// <summary>
/// Excel's Select Data Source &gt; "Hidden and Empty Cell Settings" sub-dialog
/// (R92-app-chart-data-edit-5-3). Lets the user pick how blank cells inside the plotted range
/// render (Gaps / Zero / Connect data points with a line) and whether hidden worksheet rows/columns
/// still plot -- applied to the chart via
/// <see cref="FreeX.Core.Commands.ConfigureChartHiddenEmptyCellsCommand"/>, which (unlike
/// <see cref="FreeX.Core.Commands.ConfigurePivotChartOptionsCommand"/>) works for any chart, not just
/// a PivotChart. Previously <c>HiddenEmptyCellsButton_Click</c> only opened a static informational
/// MessageBox with no controls at all -- see <see cref="SelectDataSourceDialog"/>'s
/// HiddenEmptyCellsButton_Click.
/// </summary>
public sealed class HiddenEmptyCellSettingsDialog : Window
{
    private readonly RadioButton _gapsButton = new() { GroupName = "SelectDataSourceHiddenEmptyBlankDisplay", Margin = new Thickness(0, 0, 0, 4) };
    private readonly RadioButton _zeroButton = new() { GroupName = "SelectDataSourceHiddenEmptyBlankDisplay", Margin = new Thickness(0, 0, 0, 4) };
    private readonly RadioButton _connectButton = new() { GroupName = "SelectDataSourceHiddenEmptyBlankDisplay" };
    private readonly CheckBox _showHiddenDataBox = new() { Margin = new Thickness(0, 16, 0, 0) };

    public HiddenEmptyCellSettingsResult Result { get; private set; }

    public HiddenEmptyCellSettingsDialog(ChartBlankDisplayMode blankDisplayMode, bool showDataInHiddenRowsAndColumns)
    {
        Result = new HiddenEmptyCellSettingsResult(blankDisplayMode, showDataInHiddenRowsAndColumns);
        Title = UiText.Get(SelectDataSourcePlanner.HiddenEmptyCellsTitleResourceKey);
        Width = 360;
        Height = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _gapsButton.Content = UiText.Get("SelectDataSource_HiddenEmptyCells_GapsOption");
        _zeroButton.Content = UiText.Get("SelectDataSource_HiddenEmptyCells_ZeroOption");
        _connectButton.Content = UiText.Get("SelectDataSource_HiddenEmptyCells_ConnectOption");
        _showHiddenDataBox.Content = UiText.Get("SelectDataSource_HiddenEmptyCells_ShowHiddenDataOption");
        AutomationProperties.SetAutomationId(_gapsButton, "HiddenEmptyCellsGapsOption");
        AutomationProperties.SetAutomationId(_zeroButton, "HiddenEmptyCellsZeroOption");
        AutomationProperties.SetAutomationId(_connectButton, "HiddenEmptyCellsConnectOption");
        AutomationProperties.SetAutomationId(_showHiddenDataBox, "HiddenEmptyCellsShowHiddenDataCheck");

        switch (blankDisplayMode)
        {
            case ChartBlankDisplayMode.Zero:
                _zeroButton.IsChecked = true;
                break;
            case ChartBlankDisplayMode.Span:
                _connectButton.IsChecked = true;
                break;
            default:
                _gapsButton.IsChecked = true;
                break;
        }
        _showHiddenDataBox.IsChecked = showDataInHiddenRowsAndColumns;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = UiText.Get("SelectDataSource_HiddenEmptyCells_ShowEmptyCellsAsLabel"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(_gapsButton);
        stack.Children.Add(_zeroButton);
        stack.Children.Add(_connectButton);
        stack.Children.Add(_showHiddenDataBox);
        stack.Children.Add(DialogButtonRowFactory.Create(Accept, 76, new Thickness(0, 20, 0, 0)));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        var checkedButton = _zeroButton.IsChecked == true ? _zeroButton
            : _connectButton.IsChecked == true ? _connectButton
            : _gapsButton;
        checkedButton.Focus();
    }

    private void Accept()
    {
        var mode = _zeroButton.IsChecked == true
            ? ChartBlankDisplayMode.Zero
            : _connectButton.IsChecked == true
                ? ChartBlankDisplayMode.Span
                : ChartBlankDisplayMode.Gap;
        Result = new HiddenEmptyCellSettingsResult(mode, _showHiddenDataBox.IsChecked == true);
        DialogResult = true;
    }
}
