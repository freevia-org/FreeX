using System.Security.Cryptography;
using System.Text;
using Free.Shared.Opc;
using NPOI.POIFS.Crypt;
using NPOI.POIFS.FileSystem;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// "Encrypt with Password" (ECMA-376 / [MS-OFFCRYPTO]) for the WordprocessingML package family:
/// Agile read/write, Standard read, and the failure modes a reader must turn into its own errors.
/// </summary>
public sealed class EncryptedDocumentPackageTests
{
    private const string Password = "s3cret-Ω-пароль";
    private const string WordFixturePassword = "Pässw0rd-Ω";
    private static readonly byte[] CompoundFileSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public static TheoryData<string> PackageFamily => new() { ".docx", ".docm", ".dotx", ".dotm", "strict" };

    [Theory]
    [MemberData(nameof(PackageFamily))]
    public void Save_WithPassword_WritesAnEncryptedCompoundFile_ThatRoundTrips(string variant)
    {
        var adapter = Adapter(variant);
        var document = Document("Confidential body text");
        document.EncryptionPassword = Password;

        var bytes = Save(adapter, document);

        bytes.AsSpan(0, 8).SequenceEqual(CompoundFileSignature).Should().BeTrue();
        Encoding.ASCII.GetString(bytes).Should().NotContain("word/document.xml")
            .And.NotContain("Confidential");
        Encoding.Unicode.GetString(bytes).Should().NotContain("Confidential");
        adapter.IsPasswordProtected(new MemoryStream(bytes)).Should().BeTrue();

        var loaded = adapter.Load(new MemoryStream(bytes), Password);

        PlainText(loaded).Should().Contain("Confidential body text");
        loaded.EncryptionPassword.Should().Be(Password);
    }

    [Fact]
    public void Save_WithoutPassword_StaysAPlainPackage()
    {
        var adapter = new DocxFileAdapter();
        var bytes = Save(adapter, Document("plain"));

        bytes.AsSpan(0, 2).SequenceEqual("PK"u8).Should().BeTrue();
        adapter.IsPasswordProtected(new MemoryStream(bytes)).Should().BeFalse();
        adapter.Load(new MemoryStream(bytes)).EncryptionPassword.Should().BeNull();
    }

    [Fact]
    public void Save_TwiceWithTheSamePassword_UsesFreshSaltsAndKeys()
    {
        var document = Document("same");
        document.EncryptionPassword = Password;
        var adapter = new DocxFileAdapter();

        var first = ReadStreams(Save(adapter, document));
        var second = ReadStreams(Save(adapter, document));

        first.EncryptionInfo.Should().NotEqual(second.EncryptionInfo);
        first.EncryptedPackage.AsSpan(8, 32).SequenceEqual(second.EncryptedPackage.AsSpan(8, 32)).Should().BeFalse();
    }

    [Fact]
    public void Save_WritesOfficeDefaultAgileParameters()
    {
        var document = Document("params");
        document.EncryptionPassword = Password;

        var info = ReadStreams(Save(new DocxFileAdapter(), document)).EncryptionInfo;

        info.AsSpan(0, 8).ToArray().Should().Equal(4, 0, 4, 0, 0x40, 0, 0, 0);
        var xml = Encoding.UTF8.GetString(info, 8, info.Length - 8);
        xml.Should().Contain("spinCount=\"100000\"")
            .And.Contain("keyBits=\"256\"")
            .And.Contain("hashAlgorithm=\"SHA512\"")
            .And.Contain("cipherChaining=\"ChainingModeCBC\"")
            .And.NotContain(Password);
    }

    [Fact]
    public void Load_WithoutPassword_ReportsPasswordProtection_NotALegacyDocGuess()
    {
        var document = Document("needs password");
        document.EncryptionPassword = Password;
        var bytes = Save(new DocxFileAdapter(), document);

        Action viaAdapter = () => new DocxFileAdapter().Load(new MemoryStream(bytes));
        Action viaReader = () => DocxReader.Read(new MemoryStream(bytes));

        viaAdapter.Should().Throw<DocumentPasswordRequiredException>().WithMessage("*password-protected*");
        viaReader.Should().Throw<DocumentPasswordRequiredException>();
    }

    [Fact]
    public void Load_LegacyCompoundFileWithoutEncryptionStreams_IsNotReportedAsPasswordProtected()
    {
        var fileSystem = new POIFSFileSystem();
        fileSystem.CreateDocument(new MemoryStream(new byte[64]), "WordDocument");
        using var output = new MemoryStream();
        fileSystem.WriteFileSystem(output);
        fileSystem.Close();

        new DocxFileAdapter().IsPasswordProtected(new MemoryStream(output.ToArray())).Should().BeFalse();
        Action act = () => DocxReader.Read(new MemoryStream(output.ToArray()));
        act.Should().Throw<InvalidDataException>().WithMessage("*legacy Word 97-2003*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData("s3cret-Ω-пароль ")]
    [InlineData("S3CRET-Ω-ПАРОЛЬ")]
    public void Load_WithWrongPassword_FailsAsInvalidPassword(string wrongPassword)
    {
        var document = Document("guarded");
        document.EncryptionPassword = Password;
        var bytes = Save(new DocxFileAdapter(), document);

        Action act = () => new DocxFileAdapter().Load(new MemoryStream(bytes), wrongPassword);

        act.Should().Throw<OoxmlPackageEncryptionException>()
            .Where(ex => ex.Failure == OoxmlPackageEncryptionFailure.InvalidPassword);
    }

    [Fact]
    public void Decrypt_WordAuthoredAgileFile_WithItsPassword()
    {
        var bytes = WordFixture();
        var adapter = new DocxFileAdapter();

        adapter.IsPasswordProtected(new MemoryStream(bytes)).Should().BeTrue();
        var loaded = adapter.Load(new MemoryStream(bytes), WordFixturePassword);

        PlainText(loaded).Should().Contain("Word encrypted fixture: Agile AES. Zażółć gęślą jaźń.");
        loaded.EncryptionPassword.Should().Be(WordFixturePassword);
    }

    [Fact]
    public void Decrypt_WordAuthoredAgileFile_RejectsAWrongPassword()
    {
        Action act = () => new DocxFileAdapter().Load(new MemoryStream(WordFixture()), "Passw0rd");

        act.Should().Throw<OoxmlPackageEncryptionException>()
            .Where(ex => ex.Failure == OoxmlPackageEncryptionFailure.InvalidPassword);
    }

    public static TheoryData<int, string> AgileVariants => new()
    {
        { 128, "SHA1" }, { 256, "SHA1" }, { 128, "SHA256" }, { 192, "SHA384" }, { 256, "SHA512" },
    };

    [Theory]
    [MemberData(nameof(AgileVariants))]
    public void Agile_RoundTripsEveryKeySizeAndHash_AcrossSegmentBoundaries(int keyBits, string hashName)
    {
        var hash = new HashAlgorithmName(hashName);
        foreach (var length in new[] { 0, 1, 15, 16, 17, 4095, 4096, 4097, 8192, 10_000 })
        {
            var package = RandomNumberGenerator.GetBytes(length);

            var streams = OoxmlPackageEncryption.Encrypt(package, Password, keyBits, hash, spinCount: 1000);
            var decrypted = OoxmlPackageEncryption.Decrypt(streams.EncryptionInfo, streams.EncryptedPackage, Password);

            decrypted.Should().Equal(package, "length {0}", length);
            Action wrong = () => OoxmlPackageEncryption.Decrypt(streams.EncryptionInfo, streams.EncryptedPackage, "nope");
            wrong.Should().Throw<OoxmlPackageEncryptionException>()
                .Where(ex => ex.Failure == OoxmlPackageEncryptionFailure.InvalidPassword);
        }
    }

    [Fact]
    public void Agile_KeyDerivation_MatchesAnIndependentComputationOfTheSpecification()
    {
        // Recompute the password verifier from the written header with nothing but the framework
        // primitives and the algorithm text of [MS-OFFCRYPTO] 2.3.4.11/2.3.4.13.
        var streams = OoxmlPackageEncryption.Encrypt([1, 2, 3], Password, 256, HashAlgorithmName.SHA512, spinCount: 5000);
        var xml = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(streams.EncryptionInfo, 8, streams.EncryptionInfo.Length - 8));
        var key = xml.Descendants().Single(element => element.Name.LocalName == "encryptedKey");
        var salt = Convert.FromBase64String(key.Attribute("saltValue")!.Value);

        var hash = SHA512.HashData([.. salt, .. Encoding.Unicode.GetBytes(Password)]);
        for (var iteration = 0; iteration < 5000; iteration++)
            hash = SHA512.HashData([.. BitConverter.GetBytes(iteration), .. hash]);

        byte[] Decrypt(string attribute, byte[] blockKey)
        {
            using var aes = Aes.Create();
            aes.Key = SHA512.HashData([.. hash, .. blockKey])[..32];
            return aes.DecryptCbc(Convert.FromBase64String(key.Attribute(attribute)!.Value), salt, PaddingMode.None);
        }

        var verifierInput = Decrypt("encryptedVerifierHashInput", [0xFE, 0xA7, 0xD2, 0x76, 0x3B, 0x4B, 0x9E, 0x79]);
        var verifierHash = Decrypt("encryptedVerifierHashValue", [0xD7, 0xAA, 0x0F, 0x6D, 0x30, 0x61, 0x34, 0x4E]);

        SHA512.HashData(verifierInput).Should().Equal(verifierHash);
    }

    [Fact]
    public void Agile_TamperedCiphertext_FailsTheIntegrityCheck()
    {
        var streams = OoxmlPackageEncryption.Encrypt(RandomNumberGenerator.GetBytes(9000), Password, 256, HashAlgorithmName.SHA512, 1000);
        foreach (var index in new[] { 0, 7, 8, 100, 4104, streams.EncryptedPackage.Length - 1 })
        {
            var tampered = (byte[])streams.EncryptedPackage.Clone();
            tampered[index] ^= 0x01;

            Action act = () => OoxmlPackageEncryption.Decrypt(streams.EncryptionInfo, tampered, Password);

            act.Should().Throw<OoxmlPackageEncryptionException>()
                .Where(ex => ex.Failure == OoxmlPackageEncryptionFailure.IntegrityCheckFailed, "byte {0}", index);
        }
    }

    [Fact]
    public void Agile_TamperedHmacValue_FailsTheIntegrityCheck()
    {
        var streams = OoxmlPackageEncryption.Encrypt(RandomNumberGenerator.GetBytes(500), Password, 256, HashAlgorithmName.SHA512, 1000);
        var xml = Encoding.UTF8.GetString(streams.EncryptionInfo, 8, streams.EncryptionInfo.Length - 8);
        const string marker = "encryptedHmacValue=\"";
        var start = xml.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = xml.IndexOf('"', start);
        var value = Convert.FromBase64String(xml[start..end]);
        value[3] ^= 0x80;
        var tamperedXml = xml[..start] + Convert.ToBase64String(value) + xml[end..];
        byte[] tamperedInfo = [.. streams.EncryptionInfo.AsSpan(0, 8), .. Encoding.UTF8.GetBytes(tamperedXml)];

        Action act = () => OoxmlPackageEncryption.Decrypt(tamperedInfo, streams.EncryptedPackage, Password);

        act.Should().Throw<OoxmlPackageEncryptionException>()
            .Where(ex => ex.Failure == OoxmlPackageEncryptionFailure.IntegrityCheckFailed);
    }

    [Fact]
    public void Agile_TruncatedOrMalformedStreams_FailWithTheReadersOwnError()
    {
        var streams = OoxmlPackageEncryption.Encrypt(RandomNumberGenerator.GetBytes(5000), Password, 256, HashAlgorithmName.SHA512, 1000);

        foreach (var length in new[] { 0, 4, 7, 8, 9, 24, 4104, streams.EncryptedPackage.Length - 1 })
        {
            Action act = () => OoxmlPackageEncryption.Decrypt(streams.EncryptionInfo, streams.EncryptedPackage[..length], Password);
            act.Should().Throw<OoxmlPackageEncryptionException>("package cut to {0} bytes", length);
        }

        for (var length = 0; length < streams.EncryptionInfo.Length; length += 37)
        {
            Action act = () => OoxmlPackageEncryption.Decrypt(streams.EncryptionInfo[..length], streams.EncryptedPackage, Password);
            act.Should().Throw<OoxmlPackageEncryptionException>("header cut to {0} bytes", length);
        }
    }

    [Theory]
    [InlineData("spinCount=\"1000\"", "spinCount=\"2000000000\"")]
    [InlineData("spinCount=\"1000\"", "spinCount=\"-1\"")]
    [InlineData("keyBits=\"256\"", "keyBits=\"99999999\"")]
    [InlineData("blockSize=\"16\"", "blockSize=\"0\"")]
    [InlineData("hashSize=\"64\"", "hashSize=\"2000000000\"")]
    [InlineData("saltSize=\"16\"", "saltSize=\"2000000000\"")]
    [InlineData("cipherAlgorithm=\"AES\"", "cipherAlgorithm=\"RC4\"")]
    [InlineData("hashAlgorithm=\"SHA512\"", "hashAlgorithm=\"MD5\"")]
    [InlineData("<keyData", "<!DOCTYPE x [<!ENTITY a \"b\">]><keyData")]
    public void Agile_HostileHeaderValues_AreRejectedBeforeAnyWorkIsDone(string find, string replace)
    {
        var streams = OoxmlPackageEncryption.Encrypt([1, 2, 3, 4], Password, 256, HashAlgorithmName.SHA512, 1000);
        var xml = Encoding.UTF8.GetString(streams.EncryptionInfo, 8, streams.EncryptionInfo.Length - 8);
        xml.Should().Contain(find);
        byte[] hostile = [.. streams.EncryptionInfo.AsSpan(0, 8), .. Encoding.UTF8.GetBytes(xml.Replace(find, replace))];

        Action act = () => OoxmlPackageEncryption.Decrypt(hostile, streams.EncryptedPackage, Password);

        act.Should().Throw<OoxmlPackageEncryptionException>()
            .Where(ex => ex.Failure != OoxmlPackageEncryptionFailure.InvalidPassword);
    }

    [Fact]
    public void Decrypt_DeclaredPackageSizeBeyondTheCiphertext_IsRejectedNotAllocated()
    {
        var streams = OoxmlPackageEncryption.Encrypt([1, 2, 3, 4], Password, 128, HashAlgorithmName.SHA1, 1000);
        var standard = StandardEncrypted(new byte[100], "pw");
        BitConverter.TryWriteBytes(standard.EncryptedPackage.AsSpan(0, 8), long.MaxValue).Should().BeTrue();

        Action act = () => OoxmlPackageEncryption.Decrypt(standard.EncryptionInfo, standard.EncryptedPackage, "pw");

        act.Should().Throw<OoxmlPackageEncryptionException>()
            .Where(ex => ex.Failure == OoxmlPackageEncryptionFailure.Corrupt);
        streams.EncryptedPackage.Length.Should().Be(8 + 16);
    }

    [Fact]
    public void Decrypt_UnknownEncryptionVersion_IsReportedAsUnsupported()
    {
        Action act = () => OoxmlPackageEncryption.Decrypt([3, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0], new byte[64], Password);

        act.Should().Throw<OoxmlPackageEncryptionException>()
            .Where(ex => ex.Failure == OoxmlPackageEncryptionFailure.Unsupported);
    }

    [Fact]
    public void Standard_DecryptsAPackageWrittenByAnIndependentImplementation()
    {
        // NPOI's StandardEncryptor (a port of Apache POI's, which interoperates with Office 2007)
        // is the independent writer; FreeW only reads Standard Encryption.
        var document = Document("Standard encryption body");
        using var plain = new MemoryStream();
        DocxWriter.Write(document, plain);
        var bytes = StandardEncryptedFile(plain.ToArray(), "Ständ@rd");
        var adapter = new DocxFileAdapter();

        adapter.IsPasswordProtected(new MemoryStream(bytes)).Should().BeTrue();
        var loaded = adapter.Load(new MemoryStream(bytes), "Ständ@rd");

        PlainText(loaded).Should().Contain("Standard encryption body");
        Action wrong = () => adapter.Load(new MemoryStream(bytes), "standard");
        wrong.Should().Throw<OoxmlPackageEncryptionException>()
            .Where(ex => ex.Failure == OoxmlPackageEncryptionFailure.InvalidPassword);
    }

    [Fact]
    public void Standard_TruncatedStreams_FailWithTheReadersOwnError()
    {
        var streams = StandardEncrypted(RandomNumberGenerator.GetBytes(300), "pw");

        for (var length = 0; length < streams.EncryptionInfo.Length; length += 9)
        {
            Action act = () => OoxmlPackageEncryption.Decrypt(streams.EncryptionInfo[..length], streams.EncryptedPackage, "pw");
            act.Should().Throw<OoxmlPackageEncryptionException>("header cut to {0} bytes", length);
        }

        foreach (var length in new[] { 0, 7, 8, 9, 100 })
        {
            Action act = () => OoxmlPackageEncryption.Decrypt(streams.EncryptionInfo, streams.EncryptedPackage[..length], "pw");
            act.Should().Throw<OoxmlPackageEncryptionException>("package cut to {0} bytes", length);
        }
    }

    [Fact]
    public void Load_TruncatedEncryptedFile_NeverSurfacesARawException()
    {
        var document = Document(string.Join(' ', Enumerable.Repeat("truncate me", 400)));
        document.EncryptionPassword = Password;
        var bytes = Save(new DocxFileAdapter(), document);
        var adapter = new DocxFileAdapter();

        var failures = 0;
        for (var length = 0; length < bytes.Length; length += 331)
        {
            var cut = bytes[..length];

            // A cut that only removes trailing container padding still opens; every other cut must
            // fail with the reader's own exception type, never a raw NPOI/crypto/zip exception.
            try
            {
                PlainText(adapter.Load(new MemoryStream(cut), Password)).Should().Contain("truncate me");
            }
            catch (OoxmlPackageEncryptionException)
            {
                failures++;
            }

            Action withoutPassword = () => adapter.Load(new MemoryStream(cut));
            withoutPassword.Should().Throw<Exception>("file cut to {0} bytes", length)
                .Where(ex => ex is DocumentPasswordRequiredException || ex is InvalidDataException);
        }

        failures.Should().BeGreaterThan(5);
    }

    [Fact]
    public void Encrypt_RejectsEmptyAndOverlongPasswords()
    {
        Action empty = () => OoxmlPackageEncryption.Encrypt([1], string.Empty);
        Action tooLong = () => OoxmlPackageEncryption.Encrypt([1], new string('x', OoxmlPackageEncryption.MaxPasswordLength + 1));

        empty.Should().Throw<ArgumentException>();
        tooLong.Should().Throw<ArgumentException>();
    }

    private static DocxFileAdapter Adapter(string variant) => variant switch
    {
        ".docm" => DocxFileAdapter.Docm(),
        ".dotx" => DocxFileAdapter.Dotx(),
        ".dotm" => DocxFileAdapter.Dotm(),
        "strict" => DocxFileAdapter.Strict(),
        _ => DocxFileAdapter.Docx(),
    };

    private static TextDocument Document(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static string PlainText(TextDocument document) =>
        string.Join('\n', document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText));

    private static byte[] Save(IDocumentFileAdapter adapter, TextDocument document)
    {
        using var stream = new MemoryStream();
        adapter.Save(document, stream);
        return stream.ToArray();
    }

    private static byte[] WordFixture()
    {
        using var resource = typeof(EncryptedDocumentPackageTests).Assembly.GetManifestResourceStream("word-agile-encrypted.docx")!;
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static OoxmlEncryptedPackageStreams ReadStreams(byte[] compoundFile)
    {
        var fileSystem = new POIFSFileSystem(new MemoryStream(compoundFile));
        try
        {
            return new OoxmlEncryptedPackageStreams(
                ReadStream(fileSystem, "EncryptionInfo"),
                ReadStream(fileSystem, "EncryptedPackage"));
        }
        finally
        {
            fileSystem.Close();
        }
    }

    private static byte[] ReadStream(POIFSFileSystem fileSystem, string name)
    {
        var entry = (DocumentEntry)fileSystem.Root.GetEntry(name);
        var bytes = new byte[entry.Size];
        using var input = fileSystem.Root.CreateDocumentInputStream(entry);
        input.ReadFully(bytes);
        return bytes;
    }

    private static byte[] StandardEncryptedFile(byte[] package, string password)
    {
        var fileSystem = new POIFSFileSystem();
        var info = new EncryptionInfo(EncryptionMode.Standard);
        var encryptor = info.Encryptor;
        encryptor.ConfirmPassword(password);
        using (var data = encryptor.GetDataStream(fileSystem))
            data.Write(package, 0, package.Length);

        using var output = new MemoryStream();
        fileSystem.WriteFileSystem(output);
        fileSystem.Close();
        return output.ToArray();
    }

    private static OoxmlEncryptedPackageStreams StandardEncrypted(byte[] package, string password) =>
        ReadStreams(StandardEncryptedFile(package, password));
}
