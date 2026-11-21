using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class CompatibilityModeRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordUri = "http://schemas.microsoft.com/office/word";

    [Theory]
    [InlineData("11", 11, true)]
    [InlineData("12", 12, true)]
    [InlineData("14", 14, true)]
    [InlineData("15", 15, false)]
    public void CompatibilityModeSetting_IsReadAndSavedByteStably(string value, int expectedMode, bool legacy)
    {
        var source = BuildPackage(new XElement(W + "compat",
            new XElement(W + "useFELayout"),
            CompatibilitySetting("compatibilityMode", value),
            CompatibilitySetting("overrideTableStyleFontSizeAndJustification", "1")));

        var document = Read(source);

        document.CompatibilityMode.Should().Be(expectedMode);
        document.UsesLegacyCompatibilityLayout.Should().Be(legacy);
        document.CompatibilityFlags.Should().Equal("useFELayout");

        var firstSave = Write(document);
        var firstSettings = ReadSettings(firstSave);

        firstSettings.Element(W + "compat")!.Elements().Select(element => element.Name.LocalName)
            .Should().Equal("useFELayout", "compatSetting", "compatSetting");
        Read(firstSave).CompatibilityMode.Should().Be(expectedMode);
        XNode.DeepEquals(firstSettings, ReadSettings(Write(Read(firstSave)))).Should().BeTrue();
    }

    [Fact]
    public void CompatWithoutCompatibilityMode_IsWord2007()
    {
        var document = Read(BuildPackage(new XElement(W + "compat")));

        document.CompatibilityMode.Should().Be(12);
        document.UsesLegacyCompatibilityLayout.Should().BeTrue();
        Read(Write(document)).CompatibilityMode.Should().Be(12);
    }

    [Fact]
    public void SettingsWithoutCompat_IsWord2007()
    {
        var document = Read(BuildPackage(new XElement(W + "doNotTrackMoves")));

        document.CompatibilityMode.Should().Be(12);
        document.UsesLegacyCompatibilityLayout.Should().BeTrue();
    }

    [Fact]
    public void PackageWithoutSettingsPart_IsWord2007AndResavesWithoutOne()
    {
        var bytes = Write(new TextDocument());

        HasEntry(bytes, "word/settings.xml").Should().BeFalse();
        var reopened = Read(bytes);
        reopened.CompatibilityMode.Should().Be(12);
        HasEntry(Write(reopened), "word/settings.xml").Should().BeFalse();
    }

    [Fact]
    public void DisabledCompatFlag_IsNotReportedAsEnabled()
    {
        var document = Read(BuildPackage(new XElement(W + "compat",
            new XElement(W + "useFELayout", new XAttribute(W + "val", "0")),
            new XElement(W + "doNotExpandShiftReturn"))));

        document.CompatibilityFlags.Should().Equal("doNotExpandShiftReturn");
    }

    [Theory]
    [InlineData(12)]
    [InlineData(14)]
    public void AuthoredLegacyMode_RoundTripsThroughAFreshSettingsPart(int mode)
    {
        var document = new TextDocument { CompatibilityMode = mode };
        document.CompatibilityFlags.Add("doNotExpandShiftReturn");

        var firstSave = Write(document);
        var reopened = Read(firstSave);

        reopened.CompatibilityMode.Should().Be(mode);
        reopened.CompatibilityFlags.Should().Equal("doNotExpandShiftReturn");
        XNode.DeepEquals(ReadSettings(firstSave), ReadSettings(Write(reopened))).Should().BeTrue();
    }

    [Fact]
    public void AuthoredCurrentModeWithFlags_StaysCurrentAfterReopen()
    {
        var document = new TextDocument();
        document.CompatibilityFlags.Add("doNotExpandShiftReturn");

        Read(Write(document)).CompatibilityMode.Should().Be(TextDocument.CurrentCompatibilityMode);
    }

    private static XElement CompatibilitySetting(string name, string value) =>
        new(W + "compatSetting",
            new XAttribute(W + "name", name),
            new XAttribute(W + "uri", WordUri),
            new XAttribute(W + "val", value));

    private static byte[] BuildPackage(params XElement[] settingsChildren)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddXml(archive, "word/document.xml",
                new XDocument(new XElement(W + "document",
                    new XElement(W + "body", new XElement(W + "p")))));
            AddXml(archive, "word/settings.xml",
                new XDocument(new XElement(W + "settings",
                    new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                    settingsChildren)));
        }

        return stream.ToArray();
    }

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return DocxReader.Read(stream);
    }

    private static XElement ReadSettings(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = archive.GetEntry("word/settings.xml")!.Open();
        return XDocument.Load(entry).Root!;
    }

    private static bool HasEntry(byte[] bytes, string entryName)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.GetEntry(entryName) is not null;
    }

    private static void AddXml(ZipArchive archive, string path, XDocument document)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        document.Save(writer);
    }
}
