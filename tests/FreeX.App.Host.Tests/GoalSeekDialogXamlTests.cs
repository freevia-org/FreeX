using System.IO;
using System.Windows.Controls;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class GoalSeekDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesAccessKeyedInputLabelsAndButtons()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("GoalSeekDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        AssertLabelTargets(document, presentation, "_Set cell:", "SetCellBox");
        AssertLabelTargets(document, presentation, "_To value:", "ToValueBox");
        AssertLabelTargets(document, presentation, "_By changing cell:", "ChangingCellBox");

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_OK", "_Cancel"]);

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("AutomationProperties.Name")?.Value)
            .Should()
            .Contain(["Select set cell reference", "Select changing cell reference"]);

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("ToolTip")?.Value)
            .Should()
            .Contain(["Collapse dialog and select set cell reference", "Collapse dialog and select changing cell reference"]);

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("CommandParameter")?.Value)
            .Should()
            .Contain(["SetCellBox", "ChangingCellBox"]);

        static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
        {
            var label = document
                .Descendants(presentation + "Label")
                .Single(element => element.Attribute("Content")?.Value == content);

            label.Attribute("Target")?.Value.Should().Be($"{{Binding ElementName={target}}}");
        }
    }

    [Fact]
    public void Dialog_InputFieldsExposeAutomationMetadata()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("GoalSeekDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        AssertTextBoxAutomation(
            document,
            presentation,
            xaml,
            "SetCellBox",
            "GoalSeekSetCellBox",
            UiText.Get("GoalSeek_SetCellAutomationName"),
            UiText.Get("GoalSeek_SetCellHelpText"));
        AssertTextBoxAutomation(
            document,
            presentation,
            xaml,
            "ToValueBox",
            "GoalSeekToValueBox",
            UiText.Get("GoalSeek_ToValueAutomationName"),
            UiText.Get("GoalSeek_ToValueHelpText"));
        AssertTextBoxAutomation(
            document,
            presentation,
            xaml,
            "ChangingCellBox",
            "GoalSeekChangingCellBox",
            UiText.Get("GoalSeek_ByChangingCellAutomationName"),
            UiText.Get("GoalSeek_ByChangingCellHelpText"));

        static void AssertTextBoxAutomation(
            XDocument document,
            XNamespace presentation,
            XNamespace xaml,
            string textBoxName,
            string automationId,
            string name,
            string helpText)
        {
            var textBox = document
                .Descendants(presentation + "TextBox")
                .Single(element => element.Attribute(xaml + "Name")?.Value == textBoxName);

            textBox.Attribute("AutomationProperties.AutomationId")?.Value.Should().Be(automationId);
            textBox.Attribute("AutomationProperties.Name")?.Value.Should().Be(name);
            textBox.Attribute("AutomationProperties.HelpText")?.Value.Should().Be(helpText);
        }
    }

    [Fact]
    public void CreateRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        GoalSeekDialog.CreateRangeSelectionRequest(GoalSeekRangeSelectionTarget.ChangingCell, " $B$2 ")
            .Should()
            .Be(new GoalSeekRangeSelectionRequest(
                GoalSeekRangeSelectionTarget.ChangingCell,
                "$B$2",
                CollapseDialog: true));
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesSetCellBox()
    {
        var source = DialogSourceTestSupport.ReadHostSources("GoalSeekDialog.xaml.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(SetCellBox);");
    }

    [Fact]
    public void InvalidInputMessage_RefocusesAndSelectsOffendingField()
    {
        var source = DialogSourceTestSupport.ReadHostSources("GoalSeekDialog.xaml.cs");

        source.Should().Contain("GoalSeekStatusDialogPlanner.DescribeValidationError(");
        source.Should().Contain("validation.Message.Resolve(UiText.Get, UiText.Format)");
        source.Should().Contain("FocusInvalidInput(validation.FocusTarget);");
        source.Should().Contain("private void FocusInvalidInput(GoalSeekValidationFocusTarget focusTarget)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void RangePickerButtons_RefocusSelectedInputWithKeyboardFocus()
    {
        var source = DialogSourceTestSupport.ReadHostSources("GoalSeekDialog.xaml.cs");
        var handlerSource = source[
            source.IndexOf("private void RangePickerButton_Click", StringComparison.Ordinal)..
            source.IndexOf("public static GoalSeekRangeSelectionRequest", StringComparison.Ordinal)];

        handlerSource.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void ApplyRangeSelection_UpdatesRequestedCellBox()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var dialog = new GoalSeekDialog(sheetId, null);
            dialog.Show();
            try
            {
                dialog.ApplyRangeSelection(
                    GoalSeekRangeSelectionTarget.SetCell,
                    new CellAddress(sheetId, 3, 2));
                dialog.ApplyRangeSelection(
                    GoalSeekRangeSelectionTarget.ChangingCell,
                    new CellAddress(sheetId, 7, 4));

                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "SetCellBox").Text.Should().Be("B3");
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ChangingCellBox").Text.Should().Be("D7");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ApplyInputValues_SeedsAllRequestFields()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var dialog = new GoalSeekDialog(sheetId, null);
            dialog.Show();
            try
            {
                dialog.ApplyInputValues(
                    new CellAddress(sheetId, 2, 3),
                    "5000",
                    new CellAddress(sheetId, 2, 5));

                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "SetCellBox").Text.Should().Be("C2");
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ToValueBox").Text.Should().Be("5000");
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ChangingCellBox").Text.Should().Be("E2");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void WpfParityCapture_SeedsSameGoalSeekRequestAsAvalonia()
    {
        var wpfSource = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");
        var avaloniaSource = File.ReadAllText(WorkspaceFileLocator.Find("tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.ParityCapture.cs"));

        wpfSource.Should().Contain("string.Equals(targetSurfaceId, \"dialog.GoalSeek\", StringComparison.Ordinal)");
        wpfSource.Should().Contain("CreateGoalSeekParityDialog(sheet.Id)");
        wpfSource.Should().Contain("new CellAddress(sheetId, 2, 3)");
        wpfSource.Should().Contain("dialog.ApplyInputValues(setCell, \"5000\", changingCell);");
        wpfSource.Should().Contain("new CellAddress(sheetId, 2, 5)");

        avaloniaSource.Should().Contain("initialSetCellText: \"C2\"");
        avaloniaSource.Should().Contain("initialTargetValueText: \"5000\"");
        avaloniaSource.Should().Contain("initialChangingCellText: \"E2\"");
    }

    [Fact]
    public void MainWindow_WiresGoalSeekRangePickerToCurrentSelection()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");

        source.Should().Contain("new GoalSeekDialog(");
        source.Should().Contain("request => ApplyGoalSeekRangeSelection(dlg, request)");
        source.Should().Contain("private void ApplyGoalSeekRangeSelection(");
        source.Should().Contain("GoalSeekRangeSelectionRequest request");
        source.Should().Contain("BeginDialogRangeSelection(");
        source.Should().Contain("request.CollapseDialog");
        source.Should().Contain("selectedRange => dialog.ApplyRangeSelection(request.Target, selectedRange.Start)");
    }

    [Theory]
    [InlineData("SetCellBox", GoalSeekRangeSelectionTarget.SetCell, "$A$1")]
    [InlineData("ChangingCellBox", GoalSeekRangeSelectionTarget.ChangingCell, "$B$2")]
    public void RangePickerButtons_RaiseRangeSelectionRequest(
        string targetName,
        GoalSeekRangeSelectionTarget expectedTarget,
        string currentText)
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<GoalSeekRangeSelectionRequest>();
            var sheetId = SheetId.New();
            var dialog = new GoalSeekDialog(sheetId, null, requests.Add);
            dialog.Show();
            try
            {
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, targetName).Text = $" {currentText} ";
                var button = new Button { CommandParameter = targetName };

                InvokePrivate(dialog, "RangePickerButton_Click", button);

                requests.Should().Equal(new GoalSeekRangeSelectionRequest(
                    expectedTarget,
                    currentText,
                    CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static void InvokePrivate(GoalSeekDialog dialog, string methodName, object sender)
        => DialogSourceTestSupport.InvokePrivateHandler(dialog, methodName, sender);
}
