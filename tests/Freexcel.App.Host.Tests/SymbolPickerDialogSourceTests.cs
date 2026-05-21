using FluentAssertions;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class SymbolPickerDialogSourceTests
{
    [Fact]
    public void Dialog_ExposesKeyboardAccessKeyForCancel()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SymbolPickerDialog.cs"));

        source.Should().Contain("Content = \"_Cancel\"");
    }

    [Fact]
    public void Dialog_ExposesExcelLikeSymbolSelectionAffordances()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SymbolPickerDialog.cs"));

        source.Should().Contain("Content = \"_Font:\"");
        source.Should().Contain("Content = \"_Subset:\"");
        source.Should().Contain("Recently used symbols");
        source.Should().Contain("Character code:");
        source.Should().Contain("from: Unicode (hex)");
        source.Should().Contain("UniformGrid");
    }
}
