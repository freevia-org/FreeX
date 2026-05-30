using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

public sealed record CreateNamesFromSelectionDialogResult(
    bool UseTopRow,
    bool UseLeftColumn,
    bool UseBottomRow,
    bool UseRightColumn);

public sealed class CreateNamesFromSelectionDialog : Window
{
    private readonly CheckBox _topRow = new() { Content = UiText.Get("CreateNamesFromSelection_TopRow"), IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _leftColumn = new() { Content = UiText.Get("CreateNamesFromSelection_LeftColumn"), IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _bottomRow = new() { Content = UiText.Get("CreateNamesFromSelection_BottomRow"), Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _rightColumn = new() { Content = UiText.Get("CreateNamesFromSelection_RightColumn"), Margin = new Thickness(0, 4, 0, 0) };

    public CreateNamesFromSelectionDialogResult Result { get; private set; } =
        new(UseTopRow: true, UseLeftColumn: true, UseBottomRow: false, UseRightColumn: false);

    public bool UseTopRow => Result.UseTopRow;
    public bool UseLeftColumn => Result.UseLeftColumn;
    public bool UseBottomRow => Result.UseBottomRow;
    public bool UseRightColumn => Result.UseRightColumn;

    public CreateNamesFromSelectionDialog()
    {
        Title = UiText.Get("CreateNamesFromSelection_Title");
        Width = 280;
        Height = 230;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(16) };
        SetOptionAutomationMetadata(
            _topRow,
            "CreateNamesTopRowCheckBox",
            UiText.Get("CreateNamesFromSelection_TopRowHelpText"));
        SetOptionAutomationMetadata(
            _leftColumn,
            "CreateNamesLeftColumnCheckBox",
            UiText.Get("CreateNamesFromSelection_LeftColumnHelpText"));
        SetOptionAutomationMetadata(
            _bottomRow,
            "CreateNamesBottomRowCheckBox",
            UiText.Get("CreateNamesFromSelection_BottomRowHelpText"));
        SetOptionAutomationMetadata(
            _rightColumn,
            "CreateNamesRightColumnCheckBox",
            UiText.Get("CreateNamesFromSelection_RightColumnHelpText"));
        root.Children.Add(new TextBlock
        {
            Text = UiText.Get("CreateNamesFromSelection_IntroText"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        var group = new GroupBox { Header = UiText.Get("CreateNamesFromSelection_GroupHeader"), Margin = new Thickness(0, 0, 0, 10) };
        AutomationProperties.SetName(group, UiText.Get("CreateNamesFromSelection_GroupAutomationName"));
        AutomationProperties.SetHelpText(group, UiText.Get("CreateNamesFromSelection_GroupHelpText"));
        var options = new StackPanel { Margin = new Thickness(8, 4, 8, 8) };
        options.Children.Add(_topRow);
        options.Children.Add(_leftColumn);
        options.Children.Add(_bottomRow);
        options.Children.Add(_rightColumn);
        group.Content = options;
        root.Children.Add(group);
        root.Children.Add(new TextBlock
        {
            Text = UiText.Get("CreateNamesFromSelection_BodyText"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 0, 0, 10)
        });
        root.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 76));

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private static void SetOptionAutomationMetadata(CheckBox checkBox, string automationId, string helpText)
    {
        AutomationProperties.SetAutomationId(checkBox, automationId);
        AutomationProperties.SetHelpText(checkBox, helpText);
    }

    public static bool TryCreateResult(
        bool useTopRow,
        bool useLeftColumn,
        bool useBottomRow,
        bool useRightColumn,
        out CreateNamesFromSelectionDialogResult result,
        out string? error)
    {
        result = new CreateNamesFromSelectionDialogResult(useTopRow, useLeftColumn, useBottomRow, useRightColumn);
        if (!useTopRow && !useLeftColumn && !useBottomRow && !useRightColumn)
        {
            error = UiText.Get("CreateNamesFromSelection_NoSelectionMessage");
            return false;
        }

        error = null;
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(
            _topRow.IsChecked == true,
            _leftColumn.IsChecked == true,
            _bottomRow.IsChecked == true,
            _rightColumn.IsChecked == true,
            out var result,
            out var error))
        {
            MessageBox.Show(
                this,
                error ?? UiText.Get("CreateNamesFromSelection_NoSelectionMessage"),
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            FocusInitialKeyboardTarget();
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _topRow.Focus();
        Keyboard.Focus(_topRow);
    }
}
