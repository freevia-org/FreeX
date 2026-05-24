using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class GoalSeekDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesAccessKeyedInputLabelsAndButtons()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "GoalSeekDialog.xaml"));
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "GoalSeekDialog.xaml.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("SetCellBox.Focus();");
        source.Should().Contain("SetCellBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(SetCellBox);");
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
                GetControl<TextBox>(dialog, targetName).Text = $" {currentText} ";
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

    private static T GetControl<T>(GoalSeekDialog dialog, string name)
        where T : class
    {
        var field = typeof(GoalSeekDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static void InvokePrivate(GoalSeekDialog dialog, string methodName, object sender)
    {
        var method = typeof(GoalSeekDialog).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(dialog, [sender, new RoutedEventArgs()]);
    }
}
