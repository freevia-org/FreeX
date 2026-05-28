using System.IO;
using System.Windows.Controls;
using FluentAssertions;
using Freexcel.App.Host;
using Xunit;

namespace Freexcel.App.Host.Tests;

public sealed class DialogFocusTests
{
    [Fact]
    public void FocusAndSelect_SelectsAllText()
    {
        StaTestRunner.Run(() =>
        {
            var textBox = new TextBox { Text = "Sheet1!A1:C10" };

            DialogFocus.FocusAndSelect(textBox);

            textBox.SelectionStart.Should().Be(0);
            textBox.SelectionLength.Should().Be(textBox.Text.Length);
        });
    }

    [Fact]
    public void FocusAndSelect_PreservesKeyboardFocusCall()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "DialogFocus.cs"));

        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }
}
