using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Free.Shared.IO;

public static class OoxmlProtectionPasswordHash
{
    public static byte[] Derive(
        string algorithmName,
        string password,
        ReadOnlySpan<byte> salt,
        int spinCount)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentOutOfRangeException.ThrowIfNegative(spinCount);

        if (!TryDerive(algorithmName, password, salt, spinCount, out var digest))
            throw new ArgumentException($"Unsupported OOXML password hash algorithm '{algorithmName}'.", nameof(algorithmName));

        return digest;
    }

    public static bool TryDerive(
        string algorithmName,
        string password,
        ReadOnlySpan<byte> salt,
        int spinCount,
        out byte[] digest)
    {
        ArgumentNullException.ThrowIfNull(password);
        digest = [];
        if (spinCount < 0 || !TryResolveAlgorithm(algorithmName, out var algorithm))
            return false;

        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var initialBuffer = new byte[salt.Length + passwordBytes.Length];
        salt.CopyTo(initialBuffer);
        passwordBytes.CopyTo(initialBuffer, salt.Length);
        digest = Hash(algorithm, initialBuffer);

        var iterationBuffer = new byte[digest.Length + sizeof(int)];
        for (var iteration = 0; iteration < spinCount; iteration++)
        {
            digest.CopyTo(iterationBuffer, 0);
            BinaryPrimitives.WriteInt32LittleEndian(
                iterationBuffer.AsSpan(digest.Length, sizeof(int)),
                iteration);
            digest = Hash(algorithm, iterationBuffer);
        }

        return true;
    }

    public static bool Verify(
        string algorithmName,
        string password,
        ReadOnlySpan<byte> salt,
        int spinCount,
        ReadOnlySpan<byte> expectedHash) =>
        TryDerive(algorithmName, password, salt, spinCount, out var digest) &&
        digest.Length == expectedHash.Length &&
        CryptographicOperations.FixedTimeEquals(digest, expectedHash);

    private static byte[] Hash(OoxmlHashAlgorithm algorithm, ReadOnlySpan<byte> data) => algorithm switch
    {
        OoxmlHashAlgorithm.Md5 => MD5.HashData(data),
        OoxmlHashAlgorithm.Sha1 => SHA1.HashData(data),
        OoxmlHashAlgorithm.Sha256 => SHA256.HashData(data),
        OoxmlHashAlgorithm.Sha384 => SHA384.HashData(data),
        OoxmlHashAlgorithm.Sha512 => SHA512.HashData(data),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
    };

    private static bool TryResolveAlgorithm(string? name, out OoxmlHashAlgorithm algorithm)
    {
        algorithm = name?.Trim().ToUpperInvariant() switch
        {
            "MD5" => OoxmlHashAlgorithm.Md5,
            "SHA-1" or "SHA1" => OoxmlHashAlgorithm.Sha1,
            "SHA-256" or "SHA256" => OoxmlHashAlgorithm.Sha256,
            "SHA-384" or "SHA384" => OoxmlHashAlgorithm.Sha384,
            "SHA-512" or "SHA512" => OoxmlHashAlgorithm.Sha512,
            _ => OoxmlHashAlgorithm.Unsupported
        };
        return algorithm != OoxmlHashAlgorithm.Unsupported;
    }

    private enum OoxmlHashAlgorithm
    {
        Unsupported,
        Md5,
        Sha1,
        Sha256,
        Sha384,
        Sha512
    }
}
