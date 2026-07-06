using System.Linq;
using Free.Shared.IO;
using FreeW.App.Presentation.Backstage;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class BackstageSaveAsFileTypePlannerTests
{
    [Fact]
    public void BuildInlinePlan_DefaultsUntitledDocumentToWordDocument()
    {
        var plan = BackstageSaveAsFileTypePlanner.BuildInlinePlan(
            DocumentFileAdapterCatalog.CreateDefaultAdapters().SelectMany(adapter => adapter.Formats),
            displayName: "Untitled",
            currentPath: null);

        plan.SuggestedFileName.Should().Be("Untitled.docx");
        plan.SelectedExtension.Should().Be(".docx");
        plan.FileTypes.Select(type => type.Label).Should().ContainInOrder(
            "Word Document (*.docx)",
            "Strict Open XML Document (*.docx)",
            "Word Macro-Enabled Document (*.docm)",
            "OpenDocument Text (*.odt)",
            "Rich Text Format (*.rtf)",
            "Plain Text (*.txt, *.text)");
        plan.FileTypes.Select(type => type.Label)
            .Should()
            .NotContain(label => label.Contains("PDF", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildInlinePlan_PreservesCurrentWritableDocumentType()
    {
        var plan = BackstageSaveAsFileTypePlanner.BuildInlinePlan(
            DocumentFileAdapterCatalog.CreateDefaultAdapters().SelectMany(adapter => adapter.Formats),
            displayName: "Quarterly Draft",
            currentPath: @"C:\Docs\Quarterly Draft.rtf");

        plan.SuggestedFileName.Should().Be("Quarterly Draft.rtf");
        plan.SelectedExtension.Should().Be(".rtf");
    }

    [Fact]
    public void Build_UsesWritableCatalogFormatsWithoutReadOnlyPlaceholders()
    {
        var invoked = ("", 0);
        var formats = DocumentFileAdapterCatalog.CreateDefaultAdapters().SelectMany(adapter => adapter.Formats).ToArray();

        var groups = BackstageSaveAsFileTypePlanner.Build(
            formats,
            (extension, filterIndex) => invoked = (extension, filterIndex));

        groups.Select(group => group.Heading).Should().Equal(
            "Word Documents",
            "Web Pages",
            "Other Formats",
            "Compatibility Formats");

        var labels = groups.SelectMany(group => group.Actions).Select(action => action.Label).ToList();
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

        groups.Single(group => group.Heading == "Word Documents").Actions
            .Single(action => action.Label == "Word Document (*.docx)")
            .Description.Should().Contain("drops macro parts").And.Contain("VBA project bytes are not written");
        groups.Single(group => group.Heading == "Word Documents").Actions
            .Single(action => action.Label == "Word Macro-Enabled Document (*.docm)")
            .Description.Should().Contain("does not inspect or execute macros").And.Contain("drops macro parts");
        groups.Single(group => group.Heading == "Word Documents").Actions
            .Single(action => action.Label == "Word Template (*.dotx)")
            .Description.Should().Contain("Opening it creates a new unsaved document").And.Contain("drops macro parts");
        groups.Single(group => group.Heading == "Word Documents").Actions
            .Single(action => action.Label == "Word Macro-Enabled Template (*.dotm)")
            .Description.Should().Contain("Opening it creates a new unsaved document").And.Contain("preserves existing VBA project bytes");
        groups.Single(group => group.Heading == "Compatibility Formats").Actions
            .Single(action => action.Label == "Word 97-2003 Document (*.doc)")
            .Description.Should().Contain("Compatibility format");
        groups.Single(group => group.Heading == "Other Formats").Actions
            .Single(action => action.Label == "Plain Text (*.txt, *.text)")
            .Description.Should().Contain("Formatting, images, tables, and document structure are not preserved");

        groups.Single(group => group.Heading == "Web Pages").Actions
            .Single(action => action.Label == "Web Page (*.htm, *.html)")
            .Invoke();

        invoked.Should().Be((".htm", SaveFilterIndex(formats, "Web Page", ".htm")));
    }

    [Theory]
    [InlineData("Strict Open XML Document (*.docx)", "Strict Open XML Document", ".docx")]
    [InlineData("Word 2003 XML Document (*.xml)", "Word 2003 XML Document", ".xml")]
    [InlineData("Web Page, Filtered (*.htm, *.html)", "Web Page, Filtered", ".htm")]
    [InlineData("Web Page (*.htm, *.html)", "Web Page", ".htm")]
    public void Build_PreservesDuplicateExtensionFormatIdentity(string label, string formatName, string extension)
    {
        var invoked = ("", 0);
        var formats = DocumentFileAdapterCatalog.CreateDefaultAdapters().SelectMany(adapter => adapter.Formats).ToArray();

        var groups = BackstageSaveAsFileTypePlanner.Build(
            formats,
            (selectedExtension, filterIndex) => invoked = (selectedExtension, filterIndex));

        groups.SelectMany(group => group.Actions).Single(action => action.Label == label).Invoke();

        invoked.Should().Be((extension, SaveFilterIndex(formats, formatName, extension)));
    }

    private static int SaveFilterIndex(IEnumerable<FileFormatDescriptor> formats, string formatName, string extension) =>
        formats
            .Where(format => format.CanSave)
            .Select((format, index) => new { Format = format, Index = index + 1 })
            .Single(row =>
                row.Format.FormatName == formatName &&
                string.Equals(
                    DocumentFileFormatResolver.NormalizeExtension(row.Format.Extension),
                    extension,
                    StringComparison.OrdinalIgnoreCase))
            .Index;
}
