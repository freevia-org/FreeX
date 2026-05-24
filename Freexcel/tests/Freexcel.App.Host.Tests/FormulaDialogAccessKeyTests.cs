using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class FormulaDialogAccessKeyTests
{
    [Fact]
    public void CreateNamesFromSelectionDialog_ExposesKeyboardAccessKeys()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CreateNamesFromSelectionDialog.cs"));

        foreach (var expected in new[]
        {
            "Content = \"_Top row\"",
            "Content = \"_Left column\"",
            "Content = \"_Bottom row\"",
            "Content = \"_Right column\"",
            "DialogButtonRowFactory.Create"
        })
            source.Should().Contain(expected);

        source.Should().Contain("Header = \"Create names from\"");
        source.Should().Contain("Excel creates named ranges");
    }

    [Fact]
    public void CreateNamesFromSelectionDialogOpenedFromKeyboard_FocusesTopRowChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CreateNamesFromSelectionDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_topRow.Focus();");
        source.Should().Contain("Keyboard.Focus(_topRow);");
    }

    [Fact]
    public void EvaluateFormulaDialog_ExposesKeyboardAccessKeysForActions()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "EvaluateFormulaDialog.cs"));

        foreach (var expected in new[]
        {
            "Content = \"_Help on this formula\"",
            "Content = \"_Evaluate\"",
            "Content = \"Step _In\"",
            "Content = \"Step _Out\"",
            "Content = \"_Restart\"",
            "Content = \"_Close\""
        })
            source.Should().Contain(expected);

        source.Should().NotContain("Content = \"_Previous\"");
    }
}
