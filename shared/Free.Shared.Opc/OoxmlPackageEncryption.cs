using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Free.Shared.Opc;

/// <summary>Why an encrypted OOXML package could not be opened.</summary>
public enum OoxmlPackageEncryptionFailure
{
    /// <summary>The supplied password does not match the package's password verifier.</summary>
    InvalidPassword,

    /// <summary>The encryption metadata or payload is malformed or truncated.</summary>
    Corrupt,

    /// <summary>The payload decrypted, but its HMAC data-integrity check failed (the file was altered).</summary>
    IntegrityCheckFailed,

    /// <summary>The package uses an encryption scheme this implementation does not support.</summary>
    Unsupported,
}

/// <summary>
/// Raised for every failure to decrypt an "Encrypt with Password" OOXML package, so callers present
/// this message instead of a raw cryptography/framework exception.
/// </summary>
public sealed class OoxmlPackageEncryptionException : Exception
{
    public OoxmlPackageEncryptionException(
        OoxmlPackageEncryptionFailure failure,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public OoxmlPackageEncryptionFailure Failure { get; }
}

/// <summary>The two streams of an [MS-OFFCRYPTO] encrypted package, before they are placed in a compound file.</summary>
public sealed record OoxmlEncryptedPackageStreams(byte[] EncryptionInfo, byte[] EncryptedPackage);

/// <summary>
/// ECMA-376 / [MS-OFFCRYPTO] document encryption over the raw <c>EncryptionInfo</c> and
/// <c>EncryptedPackage</c> stream bytes (the OLE compound-file container is the caller's concern).
/// Reads Agile Encryption (AES-128/192/256 CBC; SHA-1/SHA-256/SHA-384/SHA-512; password verifier and
/// HMAC data-integrity check) and Standard Encryption (AES ECB, SHA-1); writes Agile Encryption
/// (AES-256 CBC, SHA-512, spin count 100000). Every cipher, hash and HMAC is a
/// <see cref="System.Security.Cryptography"/> primitive; this type only implements the key schedule
/// and stream framing the specification defines around them.
/// </summary>
public static class OoxmlPackageEncryption
{
    /// <summary>Word's own limit for an "Encrypt with Password" password.</summary>
    public const int MaxPasswordLength = 255;

    /// <summary>Spin count written by <see cref="Encrypt"/> (Office's default).</summary>
    public const int WriteSpinCount = 100_000;

    /// <summary>[MS-OFFCRYPTO] 2.3.4.10: the largest spin count a conforming file may declare.</summary>
    public const int MaxSpinCount = 10_000_000;

    private const int SegmentLength = 4096;
    private const int AesBlockLength = 16;
    private const int PackageSizePrefixLength = 8;
    private const int MaxSaltLength = 65_536;
    private const int MaxEncryptionInfoLength = 1024 * 1024;
    private const int StandardSpinCount = 50_000;

    private static readonly XNamespace EncryptionNs = "http://schemas.microsoft.com/office/2006/encryption";
    private static readonly XNamespace PasswordNs = "http://schemas.microsoft.com/office/2006/keyEncryptor/password";
    private const string PasswordKeyEncryptorUri = "http://schemas.microsoft.com/office/2006/keyEncryptor/password";

    private static readonly byte[] VerifierHashInputBlockKey = [0xFE, 0xA7, 0xD2, 0x76, 0x3B, 0x4B, 0x9E, 0x79];
    private static readonly byte[] VerifierHashValueBlockKey = [0xD7, 0xAA, 0x0F, 0x6D, 0x30, 0x61, 0x34, 0x4E];
    private static readonly byte[] KeyValueBlockKey = [0x14, 0x6E, 0x0B, 0xE7, 0xAB, 0xAC, 0xD0, 0xD6];
    private static readonly byte[] HmacKeyBlockKey = [0x5F, 0xB2, 0xAD, 0x01, 0x0C, 0xB9, 0xE1, 0xF6];
    private static readonly byte[] HmacValueBlockKey = [0xA0, 0x67, 0x7F, 0x02, 0xB2, 0x2C, 0x84, 0x33];

    /// <summary>
    /// Decrypts an encrypted package and returns the plain OOXML (zip) package bytes.
    /// </summary>
    /// <exception cref="OoxmlPackageEncryptionException">
    /// Wrong password, malformed/truncated streams, a failed integrity check, or an unsupported scheme.
    /// </exception>
    public static byte[] Decrypt(byte[] encryptionInfo, byte[] encryptedPackage, string password)
    {
        ArgumentNullException.ThrowIfNull(encryptionInfo);
        ArgumentNullException.ThrowIfNull(encryptedPackage);
        ArgumentNullException.ThrowIfNull(password);

        if (encryptionInfo.Length < 8 || encryptionInfo.Length > MaxEncryptionInfoLength)
            throw Corrupt("The encryption header is missing or truncated.");

        var versionMajor = BinaryPrimitives.ReadUInt16LittleEndian(encryptionInfo);
        var versionMinor = BinaryPrimitives.ReadUInt16LittleEndian(encryptionInfo.AsSpan(2));

        try
        {
            if (versionMajor == 4 && versionMinor == 4)
                return DecryptAgile(encryptionInfo, encryptedPackage, password);
            if (versionMinor == 2 && versionMajor is 2 or 3 or 4)
                return DecryptStandard(encryptionInfo, encryptedPackage, password);
        }
        catch (CryptographicException ex)
        {
            throw Corrupt("The encrypted content is damaged.", ex);
        }

        throw new OoxmlPackageEncryptionException(
            OoxmlPackageEncryptionFailure.Unsupported,
            string.Create(
                CultureInfo.InvariantCulture,
                $"This file is protected with an encryption scheme that is not supported (version {versionMajor}.{versionMinor})."));
    }

    /// <summary>
    /// Encrypts a plain OOXML package with Agile Encryption (AES-256 CBC, SHA-512, spin count
    /// <see cref="WriteSpinCount"/>), producing the two streams Office expects in the compound file.
    /// </summary>
    public static OoxmlEncryptedPackageStreams Encrypt(ReadOnlySpan<byte> package, string password) =>
        Encrypt(package, password, keyBits: 256, HashAlgorithmName.SHA512, WriteSpinCount);

    /// <summary>
    /// Agile Encryption with explicit parameters. Production always writes the public overload's
    /// AES-256/SHA-512/100000; the other combinations a reader must accept are exercised through this.
    /// </summary>
    internal static OoxmlEncryptedPackageStreams Encrypt(
        ReadOnlySpan<byte> package,
        string password,
        int keyBits,
        HashAlgorithmName hash,
        int spinCount)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (password.Length > MaxPasswordLength)
            throw new ArgumentException($"The password cannot be longer than {MaxPasswordLength} characters.", nameof(password));

        const int saltLength = 16;
        var hashLength = HashLength(hash);
        var hashName = hash.Name!;

        var keyDataSalt = RandomNumberGenerator.GetBytes(saltLength);
        var passwordSalt = RandomNumberGenerator.GetBytes(saltLength);
        var verifierInput = RandomNumberGenerator.GetBytes(saltLength);
        var secretKey = RandomNumberGenerator.GetBytes(keyBits / 8);
        var hmacKey = RandomNumberGenerator.GetBytes(hashLength);
        var spunHash = SpinPasswordHash(hash, passwordSalt, password, spinCount);
        try
        {
            var passwordIv = FitToLength(passwordSalt, AesBlockLength);
            var encryptedVerifierInput = AesCbc(
                DeriveAgileKey(hash, spunHash, VerifierHashInputBlockKey, keyBits), passwordIv, verifierInput, encrypt: true);
            var encryptedVerifierHash = AesCbc(
                DeriveAgileKey(hash, spunHash, VerifierHashValueBlockKey, keyBits),
                passwordIv,
                PadToBlock(Hash(hash, verifierInput)),
                encrypt: true);
            var encryptedKeyValue = AesCbc(
                DeriveAgileKey(hash, spunHash, KeyValueBlockKey, keyBits), passwordIv, PadToBlock(secretKey), encrypt: true);

            // EncryptedPackage: 8-byte little-endian plain length, then independently keyed 4096-byte segments.
            var paddedLength = checked((package.Length + AesBlockLength - 1) / AesBlockLength * AesBlockLength);
            var encryptedPackage = new byte[PackageSizePrefixLength + paddedLength];
            BinaryPrimitives.WriteInt64LittleEndian(encryptedPackage, package.Length);
            using (var aes = Aes.Create())
            {
                aes.Key = secretKey;
                var segment = new byte[SegmentLength];
                var segmentIndex = 0;
                for (var offset = 0; offset < package.Length; offset += SegmentLength, segmentIndex++)
                {
                    var plainLength = Math.Min(SegmentLength, package.Length - offset);
                    var cipherLength = (plainLength + AesBlockLength - 1) / AesBlockLength * AesBlockLength;
                    Array.Clear(segment);
                    package.Slice(offset, plainLength).CopyTo(segment);
                    aes.EncryptCbc(
                        segment.AsSpan(0, cipherLength),
                        SegmentIv(hash, keyDataSalt, segmentIndex, AesBlockLength),
                        encryptedPackage.AsSpan(PackageSizePrefixLength + offset, cipherLength),
                        PaddingMode.None);
                }

                CryptographicOperations.ZeroMemory(segment);
            }

            var hmacValue = Hmac(hash, hmacKey, encryptedPackage);
            var encryptedHmacKey = AesCbc(
                secretKey, BlockKeyIv(hash, keyDataSalt, HmacKeyBlockKey, AesBlockLength), PadToBlock(hmacKey), encrypt: true);
            var encryptedHmacValue = AesCbc(
                secretKey, BlockKeyIv(hash, keyDataSalt, HmacValueBlockKey, AesBlockLength), PadToBlock(hmacValue), encrypt: true);

            var cipherAttributes = string.Create(
                CultureInfo.InvariantCulture,
                $"saltSize=\"{saltLength}\" blockSize=\"{AesBlockLength}\" keyBits=\"{keyBits}\" hashSize=\"{hashLength}\" cipherAlgorithm=\"AES\" cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"{hashName}\"");
            var xml = new StringBuilder()
                .Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n")
                .Append("<encryption xmlns=\"").Append(EncryptionNs.NamespaceName)
                .Append("\" xmlns:p=\"").Append(PasswordNs.NamespaceName).Append("\">")
                .Append("<keyData ").Append(cipherAttributes)
                .Append(" saltValue=\"").Append(Convert.ToBase64String(keyDataSalt)).Append("\"/>")
                .Append("<dataIntegrity encryptedHmacKey=\"").Append(Convert.ToBase64String(encryptedHmacKey))
                .Append("\" encryptedHmacValue=\"").Append(Convert.ToBase64String(encryptedHmacValue)).Append("\"/>")
                .Append("<keyEncryptors><keyEncryptor uri=\"").Append(PasswordKeyEncryptorUri).Append("\">")
                .Append("<p:encryptedKey spinCount=\"").Append(spinCount.ToString(CultureInfo.InvariantCulture)).Append("\" ")
                .Append(cipherAttributes)
                .Append(" saltValue=\"").Append(Convert.ToBase64String(passwordSalt))
                .Append("\" encryptedVerifierHashInput=\"").Append(Convert.ToBase64String(encryptedVerifierInput))
                .Append("\" encryptedVerifierHashValue=\"").Append(Convert.ToBase64String(encryptedVerifierHash))
                .Append("\" encryptedKeyValue=\"").Append(Convert.ToBase64String(encryptedKeyValue)).Append("\"/>")
                .Append("</keyEncryptor></keyEncryptors></encryption>")
                .ToString();

            var xmlBytes = Encoding.UTF8.GetBytes(xml);
            var encryptionInfo = new byte[8 + xmlBytes.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(encryptionInfo, 4);
            BinaryPrimitives.WriteUInt16LittleEndian(encryptionInfo.AsSpan(2), 4);
            BinaryPrimitives.WriteUInt32LittleEndian(encryptionInfo.AsSpan(4), 0x40);
            xmlBytes.CopyTo(encryptionInfo, 8);
            return new OoxmlEncryptedPackageStreams(encryptionInfo, encryptedPackage);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretKey);
            CryptographicOperations.ZeroMemory(hmacKey);
            CryptographicOperations.ZeroMemory(spunHash);
        }
    }

    // ---- Agile Encryption ([MS-OFFCRYPTO] 2.3.4.10 - 2.3.4.15) -------------------------------------

    private sealed record AgileCipher(
        int SaltLength, int BlockLength, int KeyBits, int HashLength, HashAlgorithmName Hash, byte[] Salt);

    private static byte[] DecryptAgile(byte[] encryptionInfo, byte[] encryptedPackage, string password)
    {
        XElement root;
        try
        {
            using var stream = new MemoryStream(encryptionInfo, 8, encryptionInfo.Length - 8, writable: false);
            using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create(MaxEncryptionInfoLength));
            root = XDocument.Load(reader).Root ?? throw Corrupt("The encryption header is empty.");
        }
        catch (XmlException ex)
        {
            throw Corrupt("The encryption header is damaged.", ex);
        }

        if (root.Name != EncryptionNs + "encryption")
            throw Corrupt("The encryption header is not recognized.");

        var keyDataElement = root.Element(EncryptionNs + "keyData")
            ?? throw Corrupt("The encryption header has no key data.");
        var integrityElement = root.Element(EncryptionNs + "dataIntegrity")
            ?? throw Corrupt("The encryption header has no data-integrity record.");
        var encryptedKeyElement = root.Element(EncryptionNs + "keyEncryptors")?
            .Elements(EncryptionNs + "keyEncryptor")
            .Where(element => (string?)element.Attribute("uri") == PasswordKeyEncryptorUri)
            .Select(element => element.Element(PasswordNs + "encryptedKey"))
            .FirstOrDefault(element => element is not null)
            ?? throw new OoxmlPackageEncryptionException(
                OoxmlPackageEncryptionFailure.Unsupported,
                "This file is not protected with a password (it uses certificate-based encryption), which is not supported.");

        var keyData = ReadAgileCipher(keyDataElement);
        var passwordKey = ReadAgileCipher(encryptedKeyElement);
        var spinCount = ReadInt(encryptedKeyElement, "spinCount", 0, MaxSpinCount);
        var encryptedVerifierInput = ReadCipherText(encryptedKeyElement, "encryptedVerifierHashInput", passwordKey.BlockLength);
        var encryptedVerifierHash = ReadCipherText(encryptedKeyElement, "encryptedVerifierHashValue", passwordKey.BlockLength);
        var encryptedKeyValue = ReadCipherText(encryptedKeyElement, "encryptedKeyValue", passwordKey.BlockLength);
        var encryptedHmacKey = ReadCipherText(integrityElement, "encryptedHmacKey", keyData.BlockLength);
        var encryptedHmacValue = ReadCipherText(integrityElement, "encryptedHmacValue", keyData.BlockLength);

        if (encryptedVerifierInput.Length < passwordKey.SaltLength
            || encryptedVerifierHash.Length < passwordKey.HashLength
            || encryptedKeyValue.Length < passwordKey.KeyBits / 8
            || keyData.KeyBits != passwordKey.KeyBits
            || encryptedHmacKey.Length < keyData.HashLength
            || encryptedHmacValue.Length < keyData.HashLength)
        {
            throw Corrupt("The encryption header is inconsistent.");
        }

        var spunHash = SpinPasswordHash(passwordKey.Hash, passwordKey.Salt, password, spinCount);
        byte[]? secretKey = null;
        byte[]? hmacKey = null;
        try
        {
            var passwordIv = FitToLength(passwordKey.Salt, passwordKey.BlockLength);
            var verifierInput = AesCbc(
                DeriveAgileKey(passwordKey.Hash, spunHash, VerifierHashInputBlockKey, passwordKey.KeyBits),
                passwordIv, encryptedVerifierInput, encrypt: false)[..passwordKey.SaltLength];
            var verifierHash = AesCbc(
                DeriveAgileKey(passwordKey.Hash, spunHash, VerifierHashValueBlockKey, passwordKey.KeyBits),
                passwordIv, encryptedVerifierHash, encrypt: false)[..passwordKey.HashLength];
            if (!CryptographicOperations.FixedTimeEquals(Hash(passwordKey.Hash, verifierInput), verifierHash))
                throw InvalidPassword();

            secretKey = AesCbc(
                DeriveAgileKey(passwordKey.Hash, spunHash, KeyValueBlockKey, passwordKey.KeyBits),
                passwordIv, encryptedKeyValue, encrypt: false)[..(passwordKey.KeyBits / 8)];

            // Data integrity: HMAC over the whole EncryptedPackage stream, checked BEFORE the payload
            // is decrypted so tampered ciphertext is never interpreted.
            hmacKey = AesCbc(
                secretKey, BlockKeyIv(keyData.Hash, keyData.Salt, HmacKeyBlockKey, keyData.BlockLength),
                encryptedHmacKey, encrypt: false)[..keyData.HashLength];
            var expectedHmac = AesCbc(
                secretKey, BlockKeyIv(keyData.Hash, keyData.Salt, HmacValueBlockKey, keyData.BlockLength),
                encryptedHmacValue, encrypt: false)[..keyData.HashLength];
            if (!CryptographicOperations.FixedTimeEquals(Hmac(keyData.Hash, hmacKey, encryptedPackage), expectedHmac))
            {
                throw new OoxmlPackageEncryptionException(
                    OoxmlPackageEncryptionFailure.IntegrityCheckFailed,
                    "The file failed its data-integrity check: the encrypted content was changed or damaged after it was saved.");
            }

            var (plainLength, cipherLength) = ReadPackageLengths(encryptedPackage);
            var package = new byte[cipherLength];
            using var aes = Aes.Create();
            aes.Key = secretKey;
            var segmentIndex = 0;
            for (var offset = 0; offset < cipherLength; offset += SegmentLength, segmentIndex++)
            {
                var length = Math.Min(SegmentLength, cipherLength - offset);
                aes.DecryptCbc(
                    encryptedPackage.AsSpan(PackageSizePrefixLength + offset, length),
                    SegmentIv(keyData.Hash, keyData.Salt, segmentIndex, keyData.BlockLength),
                    package.AsSpan(offset, length),
                    PaddingMode.None);
            }

            Array.Resize(ref package, plainLength);
            return package;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(spunHash);
            if (secretKey is not null)
                CryptographicOperations.ZeroMemory(secretKey);
            if (hmacKey is not null)
                CryptographicOperations.ZeroMemory(hmacKey);
        }
    }

    private static AgileCipher ReadAgileCipher(XElement element)
    {
        var cipher = (string?)element.Attribute("cipherAlgorithm");
        var chaining = (string?)element.Attribute("cipherChaining");
        if (!string.Equals(cipher, "AES", StringComparison.Ordinal)
            || !string.Equals(chaining, "ChainingModeCBC", StringComparison.Ordinal))
        {
            throw new OoxmlPackageEncryptionException(
                OoxmlPackageEncryptionFailure.Unsupported,
                $"This file is protected with an unsupported cipher ({cipher ?? "unknown"}/{chaining ?? "unknown"}). Only AES in CBC mode is supported.");
        }

        var hashName = (string?)element.Attribute("hashAlgorithm");
        var hash = hashName switch
        {
            "SHA1" => HashAlgorithmName.SHA1,
            "SHA256" => HashAlgorithmName.SHA256,
            "SHA384" => HashAlgorithmName.SHA384,
            "SHA512" => HashAlgorithmName.SHA512,
            _ => throw new OoxmlPackageEncryptionException(
                OoxmlPackageEncryptionFailure.Unsupported,
                $"This file is protected with an unsupported hash algorithm ({hashName ?? "unknown"})."),
        };

        var saltLength = ReadInt(element, "saltSize", 1, MaxSaltLength);
        var blockLength = ReadInt(element, "blockSize", AesBlockLength, AesBlockLength);
        var keyBits = ReadInt(element, "keyBits", 128, 256);
        var hashLength = ReadInt(element, "hashSize", 1, 64);
        if (keyBits is not (128 or 192 or 256) || hashLength != HashLength(hash))
            throw Corrupt("The encryption header declares an invalid key or hash size.");

        var salt = ReadBase64(element, "saltValue");
        if (salt.Length != saltLength)
            throw Corrupt("The encryption header declares an invalid salt.");

        return new AgileCipher(saltLength, blockLength, keyBits, hashLength, hash, salt);
    }

    private static int ReadInt(XElement element, string attribute, int minimum, int maximum)
    {
        if (!int.TryParse((string?)element.Attribute(attribute), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value < minimum
            || value > maximum)
        {
            throw Corrupt($"The encryption header has an invalid '{attribute}' value.");
        }

        return value;
    }

    private static byte[] ReadBase64(XElement element, string attribute)
    {
        var text = (string?)element.Attribute(attribute);
        if (string.IsNullOrEmpty(text))
            throw Corrupt($"The encryption header is missing '{attribute}'.");

        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException ex)
        {
            throw Corrupt($"The encryption header has an invalid '{attribute}' value.", ex);
        }
    }

    private static byte[] ReadCipherText(XElement element, string attribute, int blockLength)
    {
        var bytes = ReadBase64(element, attribute);
        if (bytes.Length == 0 || bytes.Length % blockLength != 0)
            throw Corrupt($"The encryption header has an invalid '{attribute}' value.");

        return bytes;
    }

    /// <summary>H0 = H(salt + password); Hn = H(LE32(n-1) + Hn-1), <paramref name="spinCount"/> times.</summary>
    private static byte[] SpinPasswordHash(HashAlgorithmName hash, byte[] salt, string password, int spinCount)
    {
        var hashLength = HashLength(hash);
        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var seed = new byte[salt.Length + passwordBytes.Length];
        var buffer = new byte[4 + hashLength];
        try
        {
            salt.CopyTo(seed, 0);
            passwordBytes.CopyTo(seed, salt.Length);
            Span<byte> digest = stackalloc byte[64];
            digest = digest[..hashLength];
            CryptographicOperations.HashData(hash, seed, digest);
            for (var iteration = 0; iteration < spinCount; iteration++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer, iteration);
                digest.CopyTo(buffer.AsSpan(4));
                CryptographicOperations.HashData(hash, buffer, digest);
            }

            var result = digest.ToArray();
            CryptographicOperations.ZeroMemory(digest);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(seed);
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static byte[] DeriveAgileKey(HashAlgorithmName hash, byte[] spunHash, byte[] blockKey, int keyBits) =>
        FitToLength(Hash(hash, Concat(spunHash, blockKey)), keyBits / 8);

    private static byte[] BlockKeyIv(HashAlgorithmName hash, byte[] salt, byte[] blockKey, int blockLength) =>
        FitToLength(Hash(hash, Concat(salt, blockKey)), blockLength);

    private static byte[] SegmentIv(HashAlgorithmName hash, byte[] salt, int segmentIndex, int blockLength)
    {
        var input = new byte[salt.Length + 4];
        salt.CopyTo(input, 0);
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(salt.Length), segmentIndex);
        return FitToLength(Hash(hash, input), blockLength);
    }

    /// <summary>[MS-OFFCRYPTO] 2.3.4.11: truncate, or pad with 0x36, to exactly <paramref name="length"/> bytes.</summary>
    private static byte[] FitToLength(byte[] value, int length)
    {
        if (value.Length == length)
            return value;

        var result = new byte[length];
        Array.Fill(result, (byte)0x36);
        value.AsSpan(0, Math.Min(value.Length, length)).CopyTo(result);
        return result;
    }

    private static byte[] PadToBlock(byte[] value)
    {
        if (value.Length % AesBlockLength == 0)
            return value;

        var result = new byte[(value.Length / AesBlockLength + 1) * AesBlockLength];
        value.CopyTo(result, 0);
        return result;
    }

    // ---- Standard Encryption ([MS-OFFCRYPTO] 2.3.4.5 - 2.3.4.9) ------------------------------------

    private static byte[] DecryptStandard(byte[] encryptionInfo, byte[] encryptedPackage, string password)
    {
        const uint externalFlag = 0x10;
        const uint aesFlag = 0x20;
        const int verifierLength = 16;
        const int sha1Length = 20;
        const int encryptedVerifierHashLength = 32;

        // Version(4) Flags(4) HeaderSize(4) EncryptionHeader(HeaderSize) EncryptionVerifier
        if (encryptionInfo.Length < 12)
            throw Corrupt("The encryption header is truncated.");

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(encryptionInfo.AsSpan(4));
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(encryptionInfo.AsSpan(8));
        if (headerSize < 32 || headerSize > encryptionInfo.Length - 12)
            throw Corrupt("The encryption header is truncated.");

        var header = encryptionInfo.AsSpan(12, (int)headerSize);
        var algorithmId = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var hashAlgorithmId = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        var keyBits = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        var expectedKeyBits = algorithmId switch
        {
            0x660E => 128u,
            0x660F => 192u,
            0x6610 => 256u,
            _ => 0u,
        };
        if ((flags & externalFlag) != 0
            || (flags & aesFlag) == 0
            || expectedKeyBits == 0
            || hashAlgorithmId is not (0 or 0x8004))
        {
            throw new OoxmlPackageEncryptionException(
                OoxmlPackageEncryptionFailure.Unsupported,
                "This file is protected with an unsupported cipher. Only AES password encryption is supported.");
        }

        if (keyBits != expectedKeyBits)
            throw Corrupt("The encryption header declares an invalid key size.");

        var verifier = encryptionInfo.AsSpan(12 + (int)headerSize);
        // SaltSize(4) Salt(16) EncryptedVerifier(16) VerifierHashSize(4) EncryptedVerifierHash(32)
        if (verifier.Length < 4 + verifierLength + verifierLength + 4 + encryptedVerifierHashLength
            || BinaryPrimitives.ReadUInt32LittleEndian(verifier) != verifierLength
            || BinaryPrimitives.ReadUInt32LittleEndian(verifier[36..]) != sha1Length)
        {
            throw Corrupt("The encryption header is truncated.");
        }

        var salt = verifier.Slice(4, verifierLength).ToArray();
        var encryptedVerifier = verifier.Slice(20, verifierLength).ToArray();
        var encryptedVerifierHash = verifier.Slice(40, encryptedVerifierHashLength).ToArray();

        var spunHash = SpinPasswordHash(HashAlgorithmName.SHA1, salt, password, StandardSpinCount);
        var key = DeriveStandardKey(spunHash, (int)keyBits / 8);
        try
        {
            using var aes = Aes.Create();
            aes.Key = key;
            var verifierValue = aes.DecryptEcb(encryptedVerifier, PaddingMode.None);
            var verifierHash = aes.DecryptEcb(encryptedVerifierHash, PaddingMode.None);
            if (!CryptographicOperations.FixedTimeEquals(SHA1.HashData(verifierValue), verifierHash.AsSpan(0, sha1Length)))
                throw InvalidPassword();

            var (plainLength, cipherLength) = ReadPackageLengths(encryptedPackage);
            var package = aes.DecryptEcb(
                encryptedPackage.AsSpan(PackageSizePrefixLength, cipherLength), PaddingMode.None);
            Array.Resize(ref package, plainLength);
            return package;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(spunHash);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>[MS-OFFCRYPTO] 2.3.4.7: final hash with block 0, then the CryptDeriveKey ipad/opad expansion.</summary>
    private static byte[] DeriveStandardKey(byte[] spunHash, int keyLength)
    {
        var finalHash = SHA1.HashData(Concat(spunHash, [0, 0, 0, 0]));
        var inner = new byte[64];
        var outer = new byte[64];
        Array.Fill(inner, (byte)0x36);
        Array.Fill(outer, (byte)0x5C);
        for (var index = 0; index < finalHash.Length; index++)
        {
            inner[index] ^= finalHash[index];
            outer[index] ^= finalHash[index];
        }

        var derived = Concat(SHA1.HashData(inner), SHA1.HashData(outer));
        CryptographicOperations.ZeroMemory(finalHash);
        CryptographicOperations.ZeroMemory(inner);
        CryptographicOperations.ZeroMemory(outer);
        return derived[..keyLength];
    }

    // ---- Shared helpers ---------------------------------------------------------------------------

    /// <summary>
    /// Validates the EncryptedPackage framing and returns the declared plain length together with the
    /// number of ciphertext bytes that cover it. The declared length comes from the file, so it is
    /// bounded by the ciphertext actually present before anything is allocated from it.
    /// </summary>
    private static (int PlainLength, int CipherLength) ReadPackageLengths(byte[] encryptedPackage)
    {
        if (encryptedPackage.Length < PackageSizePrefixLength)
            throw Corrupt("The encrypted content is truncated.");

        var available = encryptedPackage.Length - PackageSizePrefixLength;
        var declared = BinaryPrimitives.ReadInt64LittleEndian(encryptedPackage);
        if (declared < 0 || declared > available)
            throw Corrupt("The encrypted content is truncated.");

        var cipherLength = checked(((int)declared + AesBlockLength - 1) / AesBlockLength * AesBlockLength);
        if (cipherLength > available)
            throw Corrupt("The encrypted content is truncated.");

        return ((int)declared, cipherLength);
    }

    private static byte[] AesCbc(byte[] key, byte[] iv, byte[] data, bool encrypt)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return encrypt
            ? aes.EncryptCbc(data, iv, PaddingMode.None)
            : aes.DecryptCbc(data, iv, PaddingMode.None);
    }

    private static byte[] Hash(HashAlgorithmName hash, byte[] data) => CryptographicOperations.HashData(hash, data);

    private static byte[] Hmac(HashAlgorithmName hash, byte[] key, byte[] data) =>
        CryptographicOperations.HmacData(hash, key, data);

    private static int HashLength(HashAlgorithmName hash) =>
        hash == HashAlgorithmName.SHA1 ? 20
        : hash == HashAlgorithmName.SHA256 ? 32
        : hash == HashAlgorithmName.SHA384 ? 48
        : 64;

    private static byte[] Concat(byte[] first, byte[] second)
    {
        var result = new byte[first.Length + second.Length];
        first.CopyTo(result, 0);
        second.CopyTo(result, first.Length);
        return result;
    }

    private static OoxmlPackageEncryptionException InvalidPassword() =>
        new(OoxmlPackageEncryptionFailure.InvalidPassword, "The password is incorrect.");

    private static OoxmlPackageEncryptionException Corrupt(string detail, Exception? innerException = null) =>
        new(
            OoxmlPackageEncryptionFailure.Corrupt,
            "This password-protected file cannot be opened because it is corrupted or truncated. " + detail,
            innerException);
}
