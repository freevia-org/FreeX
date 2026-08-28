using System.Reflection;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Options;

namespace FreeW.App.Host.Tests;

public sealed class StartupBlankDocumentTests
{
    [StaFact]
    public void FirstLaunch_StartsWithTheSameBlankDocumentAsNew()
    {
        var window = new MainWindow(new FreeWOptions());
        try
        {
            var editor = (DocumentView)typeof(MainWindow)
                .GetField("_editor", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!;

            editor.Model.PlainText.Should().BeEmpty();
            editor.Model.Blocks.OfType<Paragraph>().Should().ContainSingle()
                .Which.PlainText.Should().BeEmpty();
            editor.FloatingObjectsCanvas.Should().NotBeNull();
            editor.FloatingObjectsCanvas!.Background.Should().BeNull(
                "the empty floating-object overlay must not intercept clicks intended for the document editor");
        }
        finally
        {
            window.Close();
        }
    }
}
