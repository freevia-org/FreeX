using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class NativeJsonSchemaDocumentationTests
{
    [Fact]
    public void NativeJsonSchemaReference_DocumentsCurrentHeaderAndDtoFamilies()
    {
        var doc = TestWorkspaceFiles.ReadWorkspaceText("docs", "formats/native-json-schema.md");

        doc.Should().Contain("FileFormat");
        doc.Should().Contain("FreeX.NativeJsonWorkbook");
        doc.Should().Contain("SchemaVersion");
        doc.Should().Contain("MinimumReaderVersion");
        // Derive the version from the code rather than hardcoding it. This assertion previously pinned
        // the literal `1` and silently rotted when NativeJsonAdapter bumped CurrentSchemaVersion to 2:
        // the doc and the adapter agreed with each other, and only this test disagreed with both. A
        // documentation contract that must be hand-edited on every bump is a contract that will be
        // wrong again at the next one -- so read the constant and assert the doc states THAT.
        doc.Should().Contain($"current schema version is `{ReadCurrentSchemaVersion()}`");

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
        var doc = TestWorkspaceFiles.ReadWorkspaceText("docs", "formats/native-json-schema.md");

        doc.Should().Contain("Legacy unversioned files");
        doc.Should().Contain("unsupported future versions");
        doc.Should().Contain("Every schema version bump must add migration tests");
        doc.Should().Contain("NativeJsonSchemaTests");
    }

    /// <summary>
    /// Reads <c>NativeJsonAdapter.CurrentSchemaVersion</c> straight out of the source so the
    /// documentation contract tracks the code automatically instead of needing a hand edit per bump.
    /// </summary>
    private static int ReadCurrentSchemaVersion()
    {
        var adapter = TestWorkspaceFiles.ReadCoreIoSource("NativeJsonAdapter.cs");
        var match = Regex.Match(adapter, @"CurrentSchemaVersion\s*=\s*(\d+)\s*;");

        match.Success.Should().BeTrue(
            "NativeJsonAdapter must declare CurrentSchemaVersion as a literal this contract can read; " +
            "if that declaration is refactored, update this reader rather than pinning a literal here");

        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
