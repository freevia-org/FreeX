using System.Linq;
using FreeW.App.Host.Backstage;
using FreeW.Core.IO;

namespace FreeW.App.Host.Tests;

public sealed class BackstageSaveAsFileTypePlannerTests
{
    [Fact]
    public void Build_UsesWritableCatalogFormatsWithoutReadOnlyPlaceholders()
    {
        var invoked = "";

        var groups = BackstageSaveAsFileTypePlanner.Build(
            DocumentFileAdapterCatalog.CreateDefaultAdapters().SelectMany(adapter => adapter.Formats),
            extension => invoked = extension);

        groups.Select(group => group.Heading).Should().Equal("Word Documents", "Web Pages", "Other Formats");

        var labels = groups.SelectMany(group => group.Actions).Select(action => action.Label).ToList();
        labels.Should().ContainInOrder(
            "Word Document (*.docx)",
            "Word Macro-Enabled Document (*.docm)",
            "Word Template (*.dotx)",
            "Word Macro-Enabled Template (*.dotm)",
            "Word XML Document (*.xml)",
            "Web Page (*.htm, *.html)",
            "Single File Web Page (*.mht, *.mhtml)",
            "Rich Text Format (*.rtf)",
            "Plain Text (*.txt, *.text)",
            "Log File (*.log)");
        labels.Should().NotContain(label => label.Contains("PDF", StringComparison.OrdinalIgnoreCase));
        labels.Should().NotContain(label => label.Contains("97-2003", StringComparison.OrdinalIgnoreCase));

        groups.Single(group => group.Heading == "Web Pages").Actions
            .Single(action => action.Label == "Web Page (*.htm, *.html)")
            .Invoke();

        invoked.Should().Be(".htm");
    }
}
