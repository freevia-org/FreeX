using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class NativeJsonSchemaDocumentationTests
{
    [Fact]
    public void NativeJsonSchemaReference_DocumentsCurrentHeaderAndDtoFamilies()
    {
        var path = FindWorkspaceFile("docs", "formats/native-json-schema.md");
        var doc = File.ReadAllText(path);

        doc.Should().Contain("FileFormat");
        doc.Should().Contain("FreeX.NativeJsonWorkbook");
        doc.Should().Contain("SchemaVersion");
        doc.Should().Contain("MinimumReaderVersion");
        doc.Should().Contain("current schema version is `1`");

        foreach (var section in new[]
        {
            "Workbook Root",
            "Workbook Theme",
            "Sheets",
            "Cells",
            "Style-Only Cells",
            "Data Validations",
            "Conditional Formats",
            "Charts",
            "Pictures, Text Boxes, And Drawing Shapes",
            "Sparklines",
            "Page Layout And Printing",
            "Protection",
            "Named Ranges",
            "Watched Cells",
            "Scenarios",
            "Custom Views"
        })
        {
            doc.Should().Contain($"## {section}");
        }

        foreach (var workbookThemeField in new[]
        {
            "NativeColorSchemeXml",
            "NativeFontSchemeXml",
            "NativeFormatSchemeXml",
            "NativeThemeSupplementXml",
            "AlternateColorSchemes",
            "ObjectDefaults"
        })
        {
            doc.Should().Contain(workbookThemeField);
        }
    }

    [Fact]
    public void NativeJsonSchemaReference_DocumentsMigrationPolicy()
    {
        var doc = File.ReadAllText(FindWorkspaceFile("docs", "formats/native-json-schema.md"));

        doc.Should().Contain("Legacy unversioned files");
        doc.Should().Contain("unsupported future versions");
        doc.Should().Contain("Every schema version bump must add migration tests");
        doc.Should().Contain("NativeJsonSchemaTests");
    }

    private static string FindWorkspaceFile(params string[] relativeParts) => TestWorkspaceFiles.FindWorkspaceFile(relativeParts);
}
