using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FreeWMacOsInstallerDocumentAssociationTests
{
    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void GeneratedFreeWInstallerPreservesDocumentAssociationsAndRequestedBundleIdentity()
    {
        using var temporary = new TestTemporaryDirectory("freew-macos-installer-");
        var plist = GenerateInstaller(temporary.Path, "FreeW");
        plist.DocumentType.Should().BeNull(
            "the optional DTD must be omitted rather than saved with an empty internal subset that macOS rejects");
        var dictionary = plist.Root!.Element("dict")!;

        Value(dictionary, "CFBundleExecutable").Value.Should().Be("FreeW");
        Value(dictionary, "CFBundleIdentifier").Value.Should().Be("io.github.tony-xmelon.freew");
        Value(dictionary, "CFBundleIconFile").Value.Should().Be("FreeW.icns");
        Value(dictionary, "CFBundleShortVersionString").Value.Should().Be("4.5.6");
        Value(dictionary, "CFBundleVersion").Value.Should().Be("4.5.6");

        var types = Value(dictionary, "CFBundleDocumentTypes").Elements("dict").ToList();
        types.Should().HaveCount(5);
        foreach (var type in types)
        {
            Value(type, "CFBundleTypeRole").Value.Should().Be("Editor");
            Value(type, "LSHandlerRank").Value.Should().Be("Alternate");
        }

        AssertAssociations(types[0], ["docx", "docm", "doc"],
            ["org.openxmlformats.wordprocessingml.document",
             "org.openxmlformats.wordprocessingml.document.macroenabled", "com.microsoft.word.doc"]);
        AssertAssociations(types[1], ["dotx", "dotm", "dot"],
            ["org.openxmlformats.wordprocessingml.template",
             "org.openxmlformats.wordprocessingml.template.macroenabled", "com.microsoft.word.dot"]);
        AssertAssociations(types[2], ["rtf"], ["public.rtf"]);
        AssertAssociations(types[3], ["odt", "ott"],
            ["org.oasis-open.opendocument.text", "org.oasis-open.opendocument.text-template"]);
        AssertAssociations(types[4], ["txt"], ["public.plain-text"]);

        var extensions = types.SelectMany(type => Value(type, "CFBundleTypeExtensions").Elements("string"))
            .Select(extension => extension.Value);
        extensions.Should().NotContain(["pdf", "xml", "html", "htm"]);
    }

    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void OtherGeneratedAppInstallersDoNotAcquireFreeWDocumentAssociations()
    {
        using var temporary = new TestTemporaryDirectory("other-macos-installer-");
        foreach (var app in new[] { "FreeX", "FreeP" })
        {
            var plist = GenerateInstaller(Path.Combine(temporary.Path, app), app);
            var dictionary = plist.Root!.Element("dict")!;
            Value(dictionary, "CFBundleExecutable").Value.Should().Be(app);
            Value(dictionary, "CFBundleIdentifier").Value.Should().Be($"io.github.tony-xmelon.{app.ToLowerInvariant()}");
            dictionary.Elements("key").Select(key => key.Value).Should().NotContain("CFBundleDocumentTypes");
        }
    }

    private static XDocument GenerateInstaller(string directory, string app)
    {
        var payload = Path.Combine(directory, "payload");
        var input = Path.Combine(directory, "input");
        var output = Path.Combine(directory, "output");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(payload, app), "synthetic app payload");
        ZipFile.CreateFromDirectory(payload, Path.Combine(input, $"{app}-v4.5.6-osx-arm64.zip"));

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            Path.Combine("packaging", "New-AppInstallers.ps1"),
            directory,
            $"-Apps {app} -Platform macos -Version 4.5.6 -Runtime osx-arm64 -InputRoot \"{input}\" -OutputDir \"{output}\"");
        result.ExitCode.Should().Be(0, result.CombinedOutput);

        using var archive = ZipFile.OpenRead(Path.Combine(output, $"{app}-v4.5.6-osx-arm64-apps.zip"));
        var plistEntry = archive.Entries.Single(entry =>
            entry.FullName.Replace('\\', '/').EndsWith($"{app}.app/Contents/Info.plist", StringComparison.Ordinal));
        var packagedPlistPath = Path.Combine(directory, "packaged-Info.plist");
        plistEntry.ExtractToFile(packagedPlistPath);
        if (OperatingSystem.IsMacOS())
        {
            var validation = TestProcessRunner.Run("/usr/bin/plutil", $"-lint \"{packagedPlistPath}\"", directory);
            validation.ExitCode.Should().Be(0, validation.CombinedOutput);
        }
        return XDocument.Load(packagedPlistPath);
    }

    private static void AssertAssociations(XElement type, string[] extensions, string[] contentTypes)
    {
        Value(type, "CFBundleTypeExtensions").Elements("string").Select(element => element.Value)
            .Should().Equal(extensions);
        Value(type, "LSItemContentTypes").Elements("string").Select(element => element.Value)
            .Should().Equal(contentTypes);
    }

    private static XElement Value(XElement dictionary, string key) =>
        dictionary.Elements("key").Single(element => element.Value == key).ElementsAfterSelf().First();
}
