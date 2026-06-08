using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MacOsBundleMetadataTests
{
    private static readonly string[] NativeDocumentExtensions = ["fxl"];

    private static readonly string[] ImportedDocumentExtensions =
        ["xlsx", "xlsm", "xltx", "xltm", "xls", "xlsb", "xlt", "csv", "tsv", "tab"];

    [Fact]
    public void InfoPlist_DefinesFinderDocumentExtensionSets()
    {
        var plist = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Avalonia", "Packaging", "macos", "Info.plist"));

        var documentTypes = PlistArray(plist, "CFBundleDocumentTypes")
            .Elements("dict")
            .ToList();

        documentTypes.Should().HaveCount(2);
        PlistString(documentTypes[0], "CFBundleTypeName").Should().Be("FreeX Workbook");
        PlistString(documentTypes[0], "CFBundleTypeRole").Should().Be("Editor");
        PlistString(documentTypes[0], "LSHandlerRank").Should().Be("Owner");
        PlistStringArray(documentTypes[0], "CFBundleTypeExtensions").Should().Equal(NativeDocumentExtensions);

        PlistString(documentTypes[1], "CFBundleTypeName").Should().Be("Spreadsheet Workbooks");
        PlistString(documentTypes[1], "CFBundleTypeRole").Should().Be("Viewer");
        PlistString(documentTypes[1], "LSHandlerRank").Should().Be("Alternate");
        PlistStringArray(documentTypes[1], "CFBundleTypeExtensions").Should().Equal(ImportedDocumentExtensions);
    }

    [Fact]
    public void MacOsWorkflow_VerifiesAndRecordsAllFinderDocumentExtensions()
    {
        var workflow = File.ReadAllText(WorkspaceFileLocator.Find(".github", "workflows", "macos-app.yml"));

        workflow.Should().Contain("native_document_extensions=(fxl)");
        workflow.Should().Contain("imported_document_extensions=(xlsx xlsm xltx xltm xls xlsb xlt csv tsv tab)");
        workflow.Should().Contain("assert_bundle_document_extensions \"$app/Contents/Info.plist\" 0 \"${native_document_extensions[@]}\"");
        workflow.Should().Contain("assert_bundle_document_extensions \"$app/Contents/Info.plist\" 1 \"${imported_document_extensions[@]}\"");
        workflow.Should().Contain("app_info_plist=\"$unzip_root/FreeX.app/Contents/Info.plist\"");
        workflow.Should().Contain("assert_bundle_document_extensions \"$app_info_plist\" 0 \"${native_document_extensions[@]}\"");
        workflow.Should().Contain("assert_bundle_document_extensions \"$app_info_plist\" 1 \"${imported_document_extensions[@]}\"");
        workflow.Should().Contain("test \"$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' \"$app_info_plist\")\" = \"io.github.tony-xmelon.freex\"");
        workflow.Should().Contain("test \"$(/usr/libexec/PlistBuddy -c 'Print :CFBundlePackageType' \"$app_info_plist\")\" = \"APPL\"");
        workflow.Should().Contain("test \"$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' \"$app_info_plist\")\" = \"12.0\"");
        workflow.Should().Contain("test \"$(/usr/libexec/PlistBuddy -c 'Print :NSHighResolutionCapable' \"$app_info_plist\")\" = \"true\"");
        workflow.Should().Contain("artifact_bundle_metadata_subject=unzipped_app_bundle");
        workflow.Should().Contain("bundle_executable=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' \"$app_info_plist\")");
        workflow.Should().Contain("bundle_icon=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' \"$app_info_plist\")");
        workflow.Should().Contain("bundle_identifier=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' \"$app_info_plist\")");
        workflow.Should().Contain("bundle_package_type=$(/usr/libexec/PlistBuddy -c 'Print :CFBundlePackageType' \"$app_info_plist\")");
        workflow.Should().Contain("bundle_minimum_system_version=$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' \"$app_info_plist\")");
        workflow.Should().Contain("bundle_high_resolution_capable=$(/usr/libexec/PlistBuddy -c 'Print :NSHighResolutionCapable' \"$app_info_plist\")");
        workflow.Should().Contain("artifact_document_extensions_subject=unzipped_app_bundle");
        workflow.Should().Contain("native_document_type=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleDocumentTypes:0:CFBundleTypeName' \"$app_info_plist\")");
        workflow.Should().Contain("native_document_extensions=$(IFS=';'; echo \"${native_document_extensions[*]}\")");
        workflow.Should().Contain("imported_document_type=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleDocumentTypes:1:CFBundleTypeName' \"$app_info_plist\")");
        workflow.Should().Contain("imported_document_extensions=$(IFS=';'; echo \"${imported_document_extensions[*]}\")");
    }

    private static XElement PlistArray(XDocument plist, string key)
    {
        var value = PlistValue(plist, key);
        value.Should().NotBeNull();
        value!.Name.LocalName.Should().Be("array");
        return value;
    }

    private static string? PlistString(XElement dict, string key) =>
        PlistValue(dict, key)?.Name.LocalName == "string"
            ? PlistValue(dict, key)!.Value
            : null;

    private static IReadOnlyList<string> PlistStringArray(XElement dict, string key)
    {
        var value = PlistValue(dict, key);
        value.Should().NotBeNull();
        value!.Name.LocalName.Should().Be("array");
        return value.Elements("string").Select(element => element.Value).ToList();
    }

    private static XElement? PlistValue(XDocument plist, string key)
    {
        var dict = plist.Root?.Element("dict");
        return dict is null ? null : PlistValue(dict, key);
    }

    private static XElement? PlistValue(XElement dict, string key)
    {
        var elements = dict.Elements().ToList();
        for (var index = 0; index < elements.Count - 1; index++)
        {
            if (elements[index].Name.LocalName == "key" &&
                elements[index].Value == key)
            {
                return elements[index + 1];
            }
        }

        return null;
    }
}
