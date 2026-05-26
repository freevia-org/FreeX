using System.Security.Cryptography;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private sealed record XlsxSourcePackage(byte[] Buffer, int Offset, int Count, string ModelFingerprint)
    {
        public static XlsxSourcePackage Capture(MemoryStream stream, Workbook workbook)
        {
            var fingerprint = CreateModelFingerprint(workbook);
            if (stream.TryGetBuffer(out var buffer))
                return new XlsxSourcePackage(buffer.Array!, buffer.Offset, buffer.Count, fingerprint);

            var bytes = new byte[stream.Length];
            var previousPosition = stream.Position;
            stream.Position = 0;
            stream.ReadExactly(bytes);
            stream.Position = previousPosition;
            return new XlsxSourcePackage(bytes, 0, bytes.Length, fingerprint);
        }

        public MemoryStream OpenRead() => new(Buffer, Offset, Count, writable: false);

        public bool Matches(Workbook workbook) =>
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

        private static string CreateModelFingerprint(Workbook workbook)
        {
            using var stream = new MemoryStream();
            new NativeJsonAdapter().Save(workbook, stream);
            return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length)));
        }
    }
}
