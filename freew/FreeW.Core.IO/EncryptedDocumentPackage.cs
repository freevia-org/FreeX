using System.Buffers.Binary;
using System.IO;
using System.Text;
using Free.Shared.Opc;
using NPOI.POIFS.FileSystem;

namespace FreeW.Core.IO;

/// <summary>
/// Raised when a WordprocessingML package is wrapped in "Encrypt with Password" encryption and no
/// password was supplied. Hosts catch this to prompt; anything that does not prompt still shows a
/// truthful message instead of the old "legacy .doc" guess.
/// </summary>
public sealed class DocumentPasswordRequiredException : Exception
{
    public DocumentPasswordRequiredException()
        : base("This document is password-protected. A password is required to open it.")
    {
    }
}

/// <summary>
/// The OLE compound-file container of an ECMA-376 / [MS-OFFCRYPTO] password-encrypted Word package
/// (.docx/.docm/.dotx/.dotm): detection, unwrapping + decryption, and encryption + wrapping. The
/// cryptography lives in the shared <see cref="OoxmlPackageEncryption"/>; this type only moves its
/// two streams (<c>EncryptionInfo</c>, <c>EncryptedPackage</c>) in and out of the compound file.
/// </summary>
public static class EncryptedDocumentPackage
{
    /// <summary>
    /// Largest encrypted file accepted. The container and the decrypted package are both held in
    /// memory, so this is deliberately below the 2 GiB plain-package limit.
    /// </summary>
    public const long MaxEncryptedFileBytes = 1L * 1024 * 1024 * 1024;

    private const string EncryptionInfoStreamName = "EncryptionInfo";
    private const string EncryptedPackageStreamName = "EncryptedPackage";
    private const int MaxEncryptionInfoBytes = 1024 * 1024;

    private static readonly byte[] CompoundFileSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>
    /// Whether <paramref name="stream"/> (seekable) is an encrypted OOXML package: an OLE compound
    /// file that carries BOTH the <c>EncryptionInfo</c> and <c>EncryptedPackage</c> streams. A legacy
    /// .doc or any other compound file is not. The stream position is preserved.
    /// </summary>
    public static bool IsEncryptedPackage(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
            return false;

        var start = stream.Position;
        try
        {
            Span<byte> header = stackalloc byte[8];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length
                || !header.SequenceEqual(CompoundFileSignature)
                || stream.Length - start > MaxEncryptedFileBytes)
            {
                return false;
            }

            stream.Position = start;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;
            var fileSystem = new POIFSFileSystem(buffer);
            try
            {
                return fileSystem.Root.HasEntry(EncryptionInfoStreamName)
                    && fileSystem.Root.HasEntry(EncryptedPackageStreamName);
            }
            finally
            {
                fileSystem.Close();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Not a parseable compound file; the caller reports its own format error.
            return false;
        }
        finally
        {
            stream.Position = start;
        }
    }

    /// <summary>
    /// Decrypts the encrypted package in <paramref name="stream"/> and returns the plain OOXML package.
    /// </summary>
    /// <exception cref="OoxmlPackageEncryptionException">
    /// Wrong password, a damaged/truncated container, a failed integrity check, or an unsupported scheme.
    /// </exception>
    public static MemoryStream Decrypt(Stream stream, string password)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(password);

        byte[] encryptionInfo;
        byte[] encryptedPackage;
        try
        {
            using var buffer = new MemoryStream();
            WorkbookOpenSizeGuard.CopyToWithLimit(
                stream,
                buffer,
                MaxEncryptedFileBytes,
                static limit => new WorkbookTooLargeException(
                    $"The password-protected document exceeds the {limit / (1024 * 1024)} MB open limit."));
            buffer.Position = 0;
            // POIFSFileSystem closes the stream it is given, so capture the bound first.
            var containerLength = (int)buffer.Length;
            var fileSystem = new POIFSFileSystem(buffer);
            try
            {
                encryptionInfo = ReadStream(fileSystem.Root, EncryptionInfoStreamName, MaxEncryptionInfoBytes);
                encryptedPackage = ReadStream(fileSystem.Root, EncryptedPackageStreamName, containerLength);
            }
            finally
            {
                fileSystem.Close();
            }
        }
        catch (Exception ex) when (ex is not (OoxmlPackageEncryptionException or WorkbookTooLargeException or OutOfMemoryException))
        {
            throw Corrupt(ex);
        }

        return new MemoryStream(OoxmlPackageEncryption.Decrypt(encryptionInfo, encryptedPackage, password), writable: false);
    }

    /// <summary>
    /// Encrypts the plain OOXML <paramref name="package"/> with <paramref name="password"/> (Agile
    /// Encryption, AES-256, SHA-512) and writes the resulting compound file to <paramref name="output"/>.
    /// </summary>
    public static void Encrypt(ReadOnlySpan<byte> package, Stream output, string password)
    {
        ArgumentNullException.ThrowIfNull(output);
        var streams = OoxmlPackageEncryption.Encrypt(package, password);

        var fileSystem = new POIFSFileSystem();
        try
        {
            // [MS-OFFCRYPTO] 2.3.4.1-2.3.4.4: the data-space map that tells Office the EncryptedPackage
            // stream is transformed by the password-based "StrongEncryptionTransform".
            var dataSpaces = fileSystem.Root.CreateDirectory("\u0006DataSpaces");
            CreateStream(dataSpaces, "Version", BuildVersionStream());
            CreateStream(dataSpaces, "DataSpaceMap", BuildDataSpaceMapStream());
            CreateStream(dataSpaces.CreateDirectory("DataSpaceInfo"), "StrongEncryptionDataSpace", BuildDataSpaceDefinitionStream());
            CreateStream(
                dataSpaces.CreateDirectory("TransformInfo").CreateDirectory("StrongEncryptionTransform"),
                "\u0006Primary",
                BuildPrimaryTransformStream());
            CreateStream(fileSystem.Root, EncryptionInfoStreamName, streams.EncryptionInfo);
            CreateStream(fileSystem.Root, EncryptedPackageStreamName, streams.EncryptedPackage);
            fileSystem.WriteFileSystem(output);
        }
        finally
        {
            fileSystem.Close();
        }
    }

    private static byte[] ReadStream(DirectoryNode root, string name, int maxLength)
    {
        if (!root.HasEntry(name) || root.GetEntry(name) is not DocumentEntry entry)
            throw Corrupt(null);

        // The size comes from the file's directory: bound it by the bytes actually present before allocating.
        var size = entry.Size;
        if (size < 0 || size > maxLength)
            throw Corrupt(null);

        var bytes = new byte[size];
        using var input = root.CreateDocumentInputStream(entry);
        input.ReadFully(bytes);
        return bytes;
    }

    private static void CreateStream(DirectoryEntry directory, string name, byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        directory.CreateDocument(name, stream);
    }

    private static OoxmlPackageEncryptionException Corrupt(Exception? innerException) =>
        new(
            OoxmlPackageEncryptionFailure.Corrupt,
            "This password-protected document cannot be opened because it is corrupted or truncated.",
            innerException);

    private static byte[] BuildVersionStream()
    {
        using var stream = new MemoryStream();
        WriteUnicodeLpp4(stream, "Microsoft.Container.DataSpaces");
        WriteVersions(stream);
        return stream.ToArray();
    }

    private static byte[] BuildDataSpaceMapStream()
    {
        using var entry = new MemoryStream();
        WriteInt32(entry, 1); // reference component count
        WriteInt32(entry, 0); // component type: stream
        WriteUnicodeLpp4(entry, EncryptedPackageStreamName);
        WriteUnicodeLpp4(entry, "StrongEncryptionDataSpace");

        using var stream = new MemoryStream();
        WriteInt32(stream, 8); // header length
        WriteInt32(stream, 1); // entry count
        WriteInt32(stream, (int)entry.Length + 4);
        entry.WriteTo(stream);
        return stream.ToArray();
    }

    private static byte[] BuildDataSpaceDefinitionStream()
    {
        using var stream = new MemoryStream();
        WriteInt32(stream, 8); // header length
        WriteInt32(stream, 1); // transform reference count
        WriteUnicodeLpp4(stream, "StrongEncryptionTransform");
        return stream.ToArray();
    }

    private static byte[] BuildPrimaryTransformStream()
    {
        using var header = new MemoryStream();
        WriteInt32(header, 1); // transform type: password-based
        WriteUnicodeLpp4(header, "{FF9A3F03-56EF-4613-BDD5-5A41C1D07246}");

        using var stream = new MemoryStream();
        WriteInt32(stream, (int)header.Length + 4);
        header.WriteTo(stream);
        WriteUnicodeLpp4(stream, "Microsoft.Container.EncryptionTransform");
        WriteVersions(stream);
        WriteInt32(stream, 0); // extensibility header
        WriteInt32(stream, 0); // empty XrML licence (length)
        WriteInt32(stream, 0);
        WriteInt32(stream, 4);
        return stream.ToArray();
    }

    /// <summary>Reader, updater and writer versions, each 1.0.</summary>
    private static void WriteVersions(Stream stream)
    {
        for (var index = 0; index < 3; index++)
        {
            stream.WriteByte(1);
            stream.WriteByte(0);
            stream.WriteByte(0);
            stream.WriteByte(0);
        }
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    /// <summary>[MS-OFFCRYPTO] 2.1.2 UNICODE-LP-P4: byte length, UTF-16LE text, zero padding to 4 bytes.</summary>
    private static void WriteUnicodeLpp4(Stream stream, string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
        for (var padding = (4 - bytes.Length % 4) % 4; padding > 0; padding--)
            stream.WriteByte(0);
    }
}
