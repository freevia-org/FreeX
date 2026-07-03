using System.Linq;
using FreeW.App.Presentation.Backstage;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class BackstageExportFileTypePlannerTests
{
    [Fact]
    public void BuildChangeFileTypeGroup_UsesWritableCatalogFormats()
    {
        var invoked = "";

        var group = BackstageExportFileTypePlanner.BuildChangeFileTypeGroup(
            DocumentFileAdapterCatalog.CreateDefaultAdapters().SelectMany(adapter => adapter.Formats),
            extension => invoked = extension);

        group.Heading.Should().Be("Change File Type");

        var labels = group.Actions.Select(action => action.Label).ToList();
        labels.Should().ContainInOrder(
            "Word Document (*.docx)",
            "Strict Open XML Document (*.docx)",
            "Word Macro-Enabled Document (*.docm)",
            "Word Template (*.dotx)",
            "Word Macro-Enabled Template (*.dotm)",
            "Word XML Document (*.xml)",
            "Word 2003 XML Document (*.xml)",
            "Web Page, Filtered (*.htm, *.html)",
            "Web Page (*.htm, *.html)",
            "Single File Web Page (*.mht, *.mhtml)",
            "OpenDocument Text (*.odt)",
            "OpenDocument Text Template (*.ott)",
            "Rich Text Format (*.rtf)",
            "Plain Text (*.txt, *.text)",
            "Log File (*.log)",
            "Word 97-2003 Document (*.doc)",
            "Word 97-2003 Template (*.dot)");
        labels.Should().NotContain(label => label.Contains("PDF", StringComparison.OrdinalIgnoreCase));

        group.Actions.Single(action => action.Label == "Word 97-2003 Document (*.doc)")
            .Description.Should().Contain("Compatibility format");
        group.Actions.Single(action => action.Label == "OpenDocument Text (*.odt)")
            .Description.Should().Contain("Unsupported ODF constructs");

        group.Actions.Single(action => action.Label == "Plain Text (*.txt, *.text)").Invoke();

        invoked.Should().Be(".txt");
    }
}
