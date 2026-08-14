using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.DefinedNames;

namespace FreeX.App.Host;

public sealed class CreateNamesFromSelectionDialog : Window
{
    private readonly CheckBox _topRow;
    private readonly CheckBox _leftColumn;
    private readonly CheckBox _bottomRow;
    private readonly CheckBox _rightColumn;

    public CreateNamesFromSelectionOptions Result { get; private set; }

    public bool UseTopRow => Result.UseTopRow;
    public bool UseLeftColumn => Result.UseLeftColumn;
    public bool UseBottomRow => Result.UseBottomRow;
    public bool UseRightColumn => Result.UseRightColumn;

    /// <summary>
    /// Seeds the four checkboxes from <paramref name="detectedOptions"/>, which the caller obtains from
    /// <see cref="CreateNamesFromSelectionPlanner.DetectOptions"/> for the current selection, so the dialog
    /// opens with the same edges Excel pre-checks (and identically to the Avalonia renderer).
    /// </summary>
    public CreateNamesFromSelectionDialog(CreateNamesFromSelectionOptions detectedOptions)
    {
        Result = detectedOptions;
        _topRow = new CheckBox { Content = UiText.Get("CreateNamesFromSelection_TopRow"), IsChecked = detectedOptions.UseTopRow, Margin = new Thickness(0, 4, 0, 0) };
        _leftColumn = new CheckBox { Content = UiText.Get("CreateNamesFromSelection_LeftColumn"), IsChecked = detectedOptions.UseLeftColumn, Margin = new Thickness(0, 4, 0, 0) };
        _bottomRow = new CheckBox { Content = UiText.Get("CreateNamesFromSelection_BottomRow"), IsChecked = detectedOptions.UseBottomRow, Margin = new Thickness(0, 4, 0, 0) };
        _rightColumn = new CheckBox { Content = UiText.Get("CreateNamesFromSelection_RightColumn"), IsChecked = detectedOptions.UseRightColumn, Margin = new Thickness(0, 4, 0, 0) };

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
        out CreateNamesFromSelectionOptions result,
        out string? error)
    {
        if (CreateNamesFromSelectionPlanner.TryCreateOptions(
                useTopRow,
                useLeftColumn,
                useBottomRow,
                useRightColumn,
                out result,
                out var inputError))
        {
            error = null;
            return true;
        }

        error = ToErrorMessage(inputError);
        return false;
    }

    private static string? ToErrorMessage(CreateNamesFromSelectionInputError inputError) =>
        inputError == CreateNamesFromSelectionInputError.NoSelectedEdge
            ? UiText.Get("CreateNamesFromSelection_NoSelectionMessage")
            : null;

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
            DialogMessageHelper.ShowWarning(
                this,
                error ?? UiText.Get("CreateNamesFromSelection_NoSelectionMessage"),
                Title);
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
