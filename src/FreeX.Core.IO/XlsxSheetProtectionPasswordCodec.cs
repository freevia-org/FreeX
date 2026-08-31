using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal readonly record struct XlsxSheetProtectionPasswordAttributes(
    string? LegacyPassword,
    string? AlgorithmName,
    string? SpinCount,
    string? SaltValue,
    string? HashValue);

internal static class XlsxSheetProtectionPasswordCodec
{
    public static string? Decode(in XlsxSheetProtectionPasswordAttributes attributes)
    {
        if (!string.IsNullOrEmpty(attributes.LegacyPassword))
            return attributes.LegacyPassword;

        return string.IsNullOrEmpty(attributes.HashValue)
            ? null
            : ProtectionPasswordHelper.EncodeIso29500Hash(
                attributes.AlgorithmName,
                attributes.SpinCount,
                attributes.SaltValue,
                attributes.HashValue);
    }
}
