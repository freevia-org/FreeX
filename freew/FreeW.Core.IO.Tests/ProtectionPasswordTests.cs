using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Tests for the OOXML legacy SHA-1 password hash algorithm (<see cref="ProtectionPasswordHelper"/>)
/// and the round-trip of password-protected documents (w:documentProtection with w:hash/w:salt/
/// w:cryptSpinCount attributes in word/settings.xml).
/// </summary>
public class ProtectionPasswordTests
{
    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XDocument? WriteSettingsXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/settings.xml");
        if (entry is null) return null;
        using var reader = entry.Open();
        return XDocument.Load(reader);
    }

    // ── ProtectionPasswordHelper algorithm ────────────────────────────────

    [Fact]
    public void CreateWithPassword_ProducesHashAndSalt()
    {
        var settings = ProtectionPasswordHelper.CreateWithPassword(ProtectionMode.ReadOnly, "secret");

        settings.HasPassword.Should().BeTrue();
        settings.PasswordHash.Should().NotBeNullOrEmpty();
        settings.PasswordSalt.Should().NotBeNullOrEmpty();
        settings.SpinCount.Should().Be(ProtectionPasswordHelper.DefaultSpinCount);
        settings.Mode.Should().Be(ProtectionMode.ReadOnly);
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        var settings = ProtectionPasswordHelper.CreateWithPassword(ProtectionMode.ReadOnly, "MyPassword");
        ProtectionPasswordHelper.VerifyPassword(settings, "MyPassword").Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFalse()
    {
        var settings = ProtectionPasswordHelper.CreateWithPassword(ProtectionMode.ReadOnly, "MyPassword");
        ProtectionPasswordHelper.VerifyPassword(settings, "WrongPassword").Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_EmptyPassword_ReturnsFalse_WhenHashedNonEmpty()
    {
        var settings = ProtectionPasswordHelper.CreateWithPassword(ProtectionMode.ReadOnly, "something");
        ProtectionPasswordHelper.VerifyPassword(settings, "").Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_CaseSensitive()
    {
        var settings = ProtectionPasswordHelper.CreateWithPassword(ProtectionMode.ReadOnly, "Secret");
        ProtectionPasswordHelper.VerifyPassword(settings, "secret").Should().BeFalse();
        ProtectionPasswordHelper.VerifyPassword(settings, "Secret").Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_NoHashStored_ReturnsFalse()
    {
        var settings = new ProtectionSettings(ProtectionMode.ReadOnly); // no hash
        ProtectionPasswordHelper.VerifyPassword(settings, "anything").Should().BeFalse();
    }

    [Fact]
    public void TwoHashesOfSamePassword_AreDifferent_DueToRandomSalt()
    {
        var s1 = ProtectionPasswordHelper.CreateWithPassword(ProtectionMode.ReadOnly, "pw");
        var s2 = ProtectionPasswordHelper.CreateWithPassword(ProtectionMode.ReadOnly, "pw");
        // Salts must differ (random).
        s1.PasswordSalt.Should().NotBe(s2.PasswordSalt);
        // Hashes should almost certainly differ (same password, different salt → different hash).
        s1.PasswordHash.Should().NotBe(s2.PasswordHash);
    }

    // ── Settings XML emission ─────────────────────────────────────────────

    [Fact]
    public void PasswordProtected_EmitsHashAttributes_InSettingsXml()
    {
        var doc = new TextDocument();
        doc.Protection = ProtectionPasswordHelper.CreateWithPassword(ProtectionMode.ReadOnly, "test");

        var xml = WriteSettingsXml(doc);
        xml.Should().NotBeNull();
        var W = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var protection = xml!.Root!.Element(W + "documentProtection");
        protection.Should().NotBeNull();
        protection!.Attribute(W + "enforcement")!.Value.Should().Be("1");
        protection.Attribute(W + "edit")!.Value.Should().Be("readOnly");
        protection.Attribute(W + "hash").Should().NotBeNull();
        protection.Attribute(W + "salt").Should().NotBeNull();
        protection.Attribute(W + "cryptSpinCount").Should().NotBeNull();
        protection.Attribute(W + "cryptAlgorithmSid")!.Value.Should().Be("4"); // SHA-1
    }

    [Fact]
    public void NoPassword_DoesNotEmitHashAttributes()
    {
        var doc = new TextDocument();
        doc.Protection = new ProtectionSettings(ProtectionMode.ReadOnly); // no password

        var xml = WriteSettingsXml(doc);
        var W = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var protection = xml!.Root!.Element(W + "documentProtection");
        protection.Should().NotBeNull();
        protection!.Attribute(W + "hash").Should().BeNull();
        protection.Attribute(W + "salt").Should().BeNull();
    }

    // ── Full round-trip: write then read ─────────────────────────────────

    [Fact]
    public void PasswordProtection_RoundTrips_HashSaltSpinCount()
    {
        var doc = new TextDocument();
        var originalSettings = ProtectionPasswordHelper.CreateWithPassword(ProtectionMode.CommentsOnly, "roundtrip");
        doc.Protection = originalSettings;

        var loaded = RoundTrip(doc);

        loaded.Protection.Mode.Should().Be(ProtectionMode.CommentsOnly);
        loaded.Protection.HasPassword.Should().BeTrue();
        loaded.Protection.PasswordHash.Should().Be(originalSettings.PasswordHash);
        loaded.Protection.PasswordSalt.Should().Be(originalSettings.PasswordSalt);
        loaded.Protection.SpinCount.Should().Be(originalSettings.SpinCount);
    }

    [Fact]
    public void PasswordVerification_AfterRoundTrip_StillWorks()
    {
        var doc = new TextDocument();
        doc.Protection = ProtectionPasswordHelper.CreateWithPassword(ProtectionMode.ReadOnly, "verifyMe");

        var loaded = RoundTrip(doc);

        ProtectionPasswordHelper.VerifyPassword(loaded.Protection, "verifyMe").Should().BeTrue();
        ProtectionPasswordHelper.VerifyPassword(loaded.Protection, "wrongPassword").Should().BeFalse();
    }

    [Fact]
    public void ProtectionWithoutPassword_RoundTrips_NoHash()
    {
        var doc = new TextDocument();
        doc.Protection = new ProtectionSettings(ProtectionMode.TrackChangesOnly);

        var loaded = RoundTrip(doc);

        loaded.Protection.Mode.Should().Be(ProtectionMode.TrackChangesOnly);
        loaded.Protection.HasPassword.Should().BeFalse();
        loaded.Protection.PasswordHash.Should().BeNull();
    }

    // ── Reading a hand-authored DOCX with OOXML protection attributes ─────

    [Fact]
    public void Reader_LoadsHashSaltSpinCount_FromHandAuthoredSettings()
    {
        // Simulate a Word-authored settings.xml with password protection attributes.
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            static void Add(ZipArchive z, string path, string xml)
            {
                var entry = z.CreateEntry(path);
                using var writer = new System.IO.StreamWriter(entry.Open());
                writer.Write(xml);
            }

            Add(zip, "word/document.xml",
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body><w:p/></w:body>
                </w:document>
                """);
            Add(zip, "word/settings.xml",
                """
                <w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:documentProtection w:edit="readOnly" w:enforcement="1"
                    w:cryptProviderType="rsaAES" w:cryptAlgorithmClass="hash"
                    w:cryptAlgorithmType="typeAny" w:cryptAlgorithmSid="4"
                    w:cryptSpinCount="50000"
                    w:hash="AAAAAAAAAAAAAAAAAAAAAAAAAAAA"
                    w:salt="BBBBBBBBBBBBBBBBBBBB"/>
                </w:settings>
                """);
        }
        stream.Position = 0;
        var doc = DocxReader.Read(stream);

        doc.Protection.Mode.Should().Be(ProtectionMode.ReadOnly);
        doc.Protection.HasPassword.Should().BeTrue();
        doc.Protection.PasswordHash.Should().Be("AAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        doc.Protection.PasswordSalt.Should().Be("BBBBBBBBBBBBBBBBBBBB");
        doc.Protection.SpinCount.Should().Be(50000);
    }
}
