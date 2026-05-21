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
}
