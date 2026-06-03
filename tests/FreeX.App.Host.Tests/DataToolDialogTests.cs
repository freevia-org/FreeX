using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class DataToolDialogTests
{
    [Fact]
    public void DialogReferencePicker_RaisesSelectionRequestAndMarksCollapseAffordance()
    {
        StaTestRunner.Run(() =>
        {
            var box = new TextBox { Text = "A1:C10" };
            DialogReferencePickerRequest? captured = null;

            var request = DialogReferencePicker.RequestSelection(
                box,
                "Select table range",
                next => captured = next);

            request.Target.Should().BeSameAs(box);
            request.AutomationName.Should().Be("Select table range");
            request.CurrentText.Should().Be("A1:C10");
            captured.Should().Be(request);

            var button = DialogReferencePicker.CreateButton(box, "Select table range");
            button.ToolTip.Should().Be(UiText.Get("DialogReferencePicker_ToolTip"));

            var pickerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DialogReferencePicker.cs"));
            pickerSource.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
        });
    }

    [Fact]
    public void DataValidationNoSelectionWarning_UsesOwnedMessage()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataFilterCommands.cs"));

        source.Should().Contain("UiText.Get(\"MainWindowMessage_SelectRangeFirst\")");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_DataValidationTitle\")");
        source.Should().NotContain("MessageBox.Show(\"Select a range first.\", \"Data Validation\")");
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static char GetAccessKey(string label)
    {
        var index = label.IndexOf('_', StringComparison.Ordinal);
        return char.ToUpperInvariant(label[index + 1]);
    }
}
