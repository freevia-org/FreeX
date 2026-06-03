using FluentAssertions;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void UnhideSheetDialog_CreateResult_CapturesSelectedSheetName()
    {
        UnhideSheetDialog.CreateResult("  Hidden Sheet  ").Should().Be(new UnhideSheetDialogResult("Hidden Sheet"));
    }

    [Fact]
    public void UnhideSheetDialog_LabelsSheetPickerWithAccessKeyTarget()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("new Label { Content = UiText.Get(\"UnhideSheet_UnhideSheet2\"), Target = _sheetBox");
    }

    [Fact]
    public void UnhideSheetDialog_SheetListExposesAutomationName()
    {
        var source = ReadClassSource("UnhideSheetDialog.cs", "public sealed class UnhideSheetDialog", "public sealed record __NoNextUnhideSheetDialog");

        source.Should().Contain("AutomationProperties.SetName(_sheetBox, UiText.Get(\"UnhideSheet_UnhideSheet\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_sheetBox, \"UnhideSheetList\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_sheetBox, UiText.Get(\"UnhideSheet_SelectTheHiddenWorksheetToMakeVisible\"));");
    }

    [Fact]
    public void UnhideSheetDialog_UsesNonEditableSelectionList()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("private readonly ListBox _sheetBox");
        source.Should().Contain("_sheetBox.SelectedItem");
        source.Should().NotContain("_sheetBox.IsEditable = true");
        source.Should().NotContain("_sheetBox.Text");
    }

    [Fact]
    public void UnhideSheetDialogOpenedFromKeyboard_FocusesSheetList()
    {
        var source = ReadClassSource("UnhideSheetDialog.cs", "public sealed class UnhideSheetDialog", "public sealed record __NoNextUnhideSheetDialog");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_sheetBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_sheetBox);");
    }

    [Fact]
    public void UnhideSheetDialog_OkButtonTracksSelectedSheet()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new UnhideSheetDialog(["Hidden 1", "Hidden 2"]);
            var sheetBox = GetField<ListBox>(dialog, "_sheetBox");
            var okButton = GetField<Button>(dialog, "_okButton");

            okButton.IsDefault.Should().BeTrue();
            okButton.IsEnabled.Should().BeTrue();

            sheetBox.SelectedItem = null;
            okButton.IsEnabled.Should().BeFalse();

            sheetBox.SelectedItem = "Hidden 2";
            okButton.IsEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public void UnhideSheetDialog_ActionButtonsExposeAutomationMetadata()
    {
        var source = ReadClassSource("UnhideSheetDialog.cs", "public sealed class UnhideSheetDialog", "public sealed record __NoNextUnhideSheetDialog");

        source.Should().Contain("private readonly Button _okButton");
        source.Should().Contain("private readonly Button _cancelButton");
        source.Should().Contain("AutomationProperties.SetAutomationId(_okButton, \"UnhideSheetOkButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_okButton, UiText.Get(\"UnhideSheet_UnhideTheSelectedWorksheet\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_cancelButton, \"UnhideSheetCancelButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_cancelButton, UiText.Get(\"UnhideSheet_CloseTheUnhideSheetDialogWithoutChangingWorksheetVisibility\"));");
    }

    [Fact]
    public void UnhideSheetDialogSheetList_DoubleClickAcceptsSelectedSheet()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new UnhideSheetDialog(["Hidden 1", "Hidden 2"]);
            var sheetBox = GetField<ListBox>(dialog, "_sheetBox");
            dialog.Dispatcher.BeginInvoke(() =>
            {
                sheetBox.SelectedItem = "Hidden 2";
                var doubleClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent
                };
                sheetBox.RaiseEvent(doubleClick);
                doubleClick.Handled.Should().BeTrue();

                dialog.Dispatcher.BeginInvoke(() =>
                {
                    if (dialog.DialogResult is null)
                        dialog.Close();
                }, DispatcherPriority.ContextIdle);
            }, DispatcherPriority.ApplicationIdle);

            dialog.ShowDialog().Should().BeTrue();
            dialog.Result.Should().Be(new UnhideSheetDialogResult("Hidden 2"));
        });
    }

    [Fact]
    public void UnhideSheetDialogSheetList_DoubleClickWithoutSelectionDoesNotHandleMouseEvent()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new UnhideSheetDialog(["Hidden 1", "Hidden 2"]);
            var sheetBox = GetField<ListBox>(dialog, "_sheetBox");
            sheetBox.SelectedItem = null;

            var doubleClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Control.MouseDoubleClickEvent
            };

            sheetBox.RaiseEvent(doubleClick);

            doubleClick.Handled.Should().BeFalse();
            dialog.DialogResult.Should().BeNull();
        });
    }
}
