using System.Security.Cryptography;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private sealed record XlsxSourcePackage(byte[] Buffer, int Offset, int Count, string? ModelFingerprint)
    {
        private const int FingerprintCellLimit = 10_000;

        public static XlsxSourcePackage Capture(Stream stream, Workbook workbook)
        {
            if (stream is MemoryStream memoryStream)
                return Capture(memoryStream, workbook);

            var fingerprint = ShouldCaptureModelFingerprint(workbook)
                ? CreateModelFingerprint(workbook)
                : null;
            var bytes = ReadBytes(stream);
            return new XlsxSourcePackage(bytes, 0, bytes.Length, fingerprint);
        }

        public static XlsxSourcePackage Capture(MemoryStream stream, Workbook workbook)
        {
            var fingerprint = ShouldCaptureModelFingerprint(workbook)
                ? CreateModelFingerprint(workbook)
                : null;
            if (stream.TryGetBuffer(out var buffer))
            {
                var copiedBytes = buffer.Array is not null &&
                    stream.Length <= int.MaxValue &&
                    buffer.Offset >= 0 &&
                    buffer.Offset + (int)stream.Length <= buffer.Array.Length
                    ? buffer.Array.AsSpan(buffer.Offset, (int)stream.Length).ToArray()
                    : ReadBytes(stream);
                return new XlsxSourcePackage(copiedBytes, 0, copiedBytes.Length, fingerprint);
            }

            var bytes = ReadBytes(stream);
            return new XlsxSourcePackage(bytes, 0, bytes.Length, fingerprint);
        }

        private static byte[] ReadBytes(Stream stream)
        {
            if (!stream.CanSeek)
            {
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }

            var previousPosition = stream.Position;
            var bytes = new byte[checked((int)stream.Length)];
            try
            {
                stream.Position = 0;
                stream.ReadExactly(bytes);
            }
            finally
            {
                stream.Position = previousPosition;
            }

            return bytes;
        }

        public MemoryStream OpenRead() => new(Buffer, Offset, Count, writable: false);

        public bool Matches(Workbook workbook) =>
            ModelFingerprint is not null &&
            string.Equals(ModelFingerprint, CreateModelFingerprint(workbook), StringComparison.Ordinal);

        public void CopyTo(Stream stream)
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
                if (stream.CanWrite)
                    stream.SetLength(0);
            }

            stream.Write(Buffer, Offset, Count);
            if (stream.CanSeek)
                stream.Position = Count;
        }

        private static bool ShouldCaptureModelFingerprint(Workbook workbook)
        {
            var cellCount = 0;
            foreach (var sheet in workbook.Sheets)
            {
                cellCount += sheet.CellCount;
                if (cellCount > FingerprintCellLimit)
                    return false;
            }

            return true;
        }

        private static string CreateModelFingerprint(Workbook workbook)
        {
            using var hash = SHA256.Create();
            using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write, leaveOpen: true);
            new NativeJsonAdapter().Save(workbook, stream);
            stream.FlushFinalBlock();
            return Convert.ToHexString(hash.Hash ?? []);
        }
    }
}
