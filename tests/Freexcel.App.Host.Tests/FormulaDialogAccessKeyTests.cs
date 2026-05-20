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
            "Content = \"_OK\"",
            "Content = \"_Cancel\""
        })
            source.Should().Contain(expected);
    }

    [Fact]
    public void EvaluateFormulaDialog_ExposesKeyboardAccessKeysForActions()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "EvaluateFormulaDialog.cs"));

        foreach (var expected in new[]
        {
            "Content = \"_Previous\"",
            "Content = \"_Evaluate\"",
            "Content = \"_Close\""
        })
            source.Should().Contain(expected);
    }
}
