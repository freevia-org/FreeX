using FluentAssertions;

namespace Freexcel.Core.IO.Tests;

public sealed class NativeJsonSchemaDocumentationTests
{
    [Fact]
    public void NativeJsonSchemaReference_DocumentsCurrentHeaderAndDtoFamilies()
    {
        var path = FindWorkspaceFile("docs", "NATIVE_JSON_SCHEMA.md");
        var doc = File.ReadAllText(path);

        doc.Should().Contain("FileFormat");
        doc.Should().Contain("Freexcel.NativeJsonWorkbook");
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
    }

    [Fact]
    public void NativeJsonSchemaReference_DocumentsMigrationPolicy()
    {
        var doc = File.ReadAllText(FindWorkspaceFile("docs", "NATIVE_JSON_SCHEMA.md"));

        doc.Should().Contain("Legacy unversioned files");
        doc.Should().Contain("unsupported future versions");
        doc.Should().Contain("Every schema version bump must add migration tests");
        doc.Should().Contain("NativeJsonSchemaTests");
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        foreach (var root in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(root);
            while (directory is not null)
            {
                var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
