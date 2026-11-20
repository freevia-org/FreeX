using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Services;

namespace FreeX.App.Host;

public sealed class ZoomDialog : Window
{
    private readonly TextBox _zoomBox = new();
    private readonly RadioButton _customZoomButton = new() { Content = UiText.Get("Zoom_Custom"), GroupName = "Zoom", IsChecked = true };
    private readonly RadioButton _fitSelectionButton = new() { Content = UiText.Get("Zoom_FitSelection"), GroupName = "Zoom" };
    private readonly List<RadioButton> _presetButtons = [];

    public ZoomDialogSelection Result { get; private set; }

    public ZoomDialog(int currentZoomPercent)
    {
        Result = new ZoomDialogSelection(currentZoomPercent);
        Title = UiText.Get("Zoom_Zoom");
        Width = ZoomDialogVisualMetrics.WindowWidth;
        Height = ZoomDialogVisualMetrics.WindowHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _zoomBox.Text = currentZoomPercent.ToString(CultureInfo.InvariantCulture);
        AutomationProperties.SetName(_zoomBox, UiText.Get("Zoom_CustomZoomPercent"));
        AutomationProperties.SetAutomationId(_zoomBox, ZoomDialogVisualMetrics.CustomPercentBoxAutomationId);
        AutomationProperties.SetHelpText(_zoomBox, UiText.Get("Zoom_EnterAWholeZoomPercentageFrom10To400"));
        _zoomBox.GotKeyboardFocus += (_, _) => _customZoomButton.IsChecked = true;
        Content = CreateZoomContent(currentZoomPercent);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        RadioButton? checkedPreset = null;
        foreach (var button in _presetButtons)
        {
            if (button.IsChecked != true)
                continue;

            checkedPreset = button;
            break;
        }

        if (checkedPreset is not null)
        {
            checkedPreset.Focus();
            Keyboard.Focus(checkedPreset);
        }
        else
        {
            _customZoomButton.Focus();
            DialogFocus.FocusAndSelect(_zoomBox);
        }
    }

    public static bool TryCreateResult(string? input, out ZoomDialogSelection result, out string? error)
    {
        result = new ZoomDialogSelection(100);
        error = null;

        if (!ZoomDialogPlanner.TryCreateResult(input, out var selection, out var validationError))
        {
            error = ResolveValidationError(validationError);
            return false;
        }

        result = selection;
        return true;
    }

    public static ZoomDialogSelection CreateFitSelectionResult(int currentZoomPercent) =>
        ZoomDialogPlanner.CreateFitSelectionResult(currentZoomPercent);

    private static string ResolveValidationError(ZoomDialogValidationError? validationError) =>
        validationError is null
            ? UiText.Get(ZoomDialogPlanner.ValidationFallbackResourceKey)
            : UiText.Get(validationError.ResourceKey);

    private void Accept()
    {
        if (_fitSelectionButton.IsChecked == true)
        {
            Result = CreateFitSelectionResult(Result.ZoomPercent);
            DialogResult = true;
            return;
        }

        var selectedPreset = GetSelectedPresetText();
        var input = selectedPreset ?? _zoomBox.Text;
        if (!TryCreateResult(input, out var result, out var error))
        {
            DialogMessageHelper.ShowWarning(
                this,
                error ?? UiText.Get(ZoomDialogPlanner.ValidationFallbackResourceKey),
                Title);
            _customZoomButton.IsChecked = true;
            DialogFocus.FocusAndSelect(_zoomBox);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private RadioButton? FindCheckedPresetButton()
    {
        foreach (var button in _presetButtons)
        {
            if (button.IsChecked == true)
            {
                return button;
            }
        }

        return null;
    }

    private string? GetSelectedPresetText()
    {
        var selectedPreset = FindCheckedPresetButton();
        return selectedPreset?.Tag?.ToString();
    }

    private UIElement CreateZoomContent(int currentZoomPercent)
    {
        var stack = new StackPanel { Margin = new Thickness(ZoomDialogVisualMetrics.OuterPadding) };
        var group = new GroupBox
        {
            Header = UiText.Get("Zoom_Magnification"),
            Padding = new Thickness(ZoomDialogVisualMetrics.MagnificationGroupPadding),
            Margin = new Thickness(0, 0, 0, ZoomDialogVisualMetrics.MagnificationGroupBottomMargin)
        };
        var choices = new Grid();
        choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ZoomDialogVisualMetrics.PresetColumnWidth) });
        choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var presets = new StackPanel();
        foreach (var preset in ZoomDialogPlanner.Presets)
        {
            var button = new RadioButton
            {
                Content = ZoomDialogPlanner.FormatPresetLabel(preset),
                GroupName = "Zoom",
                Tag = preset,
                IsChecked = preset == currentZoomPercent,
                Margin = new Thickness(0, 0, 0, ZoomDialogVisualMetrics.PresetItemBottomMargin)
            };
            _presetButtons.Add(button);
            presets.Children.Add(button);
        }

        choices.Children.Add(presets);
        var customChoices = new StackPanel();
        _customZoomButton.IsChecked = !ZoomDialogPlanner.IsPreset(currentZoomPercent);
        _fitSelectionButton.Margin = new Thickness(0, 0, 0, ZoomDialogVisualMetrics.FitSelectionBottomMargin);
        customChoices.Children.Add(_fitSelectionButton);
        var customRow = new StackPanel { Orientation = Orientation.Horizontal };
        customRow.Children.Add(_customZoomButton);
        _zoomBox.Width = ZoomDialogVisualMetrics.CustomPercentBoxWidth;
        _zoomBox.Height = ZoomDialogVisualMetrics.CustomPercentBoxHeight;
        customRow.Children.Add(_zoomBox);
        customRow.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
        customChoices.Children.Add(customRow);
        Grid.SetColumn(customChoices, 1);
        choices.Children.Add(customChoices);
        group.Content = choices;
        stack.Children.Add(group);
        stack.Children.Add(DialogButtonRowFactory.Create(Accept, ZoomDialogVisualMetrics.ActionButtonWidth));
        return stack;
    }
}
