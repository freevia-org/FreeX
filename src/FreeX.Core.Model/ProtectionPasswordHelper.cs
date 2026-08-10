using System.Globalization;
using Free.Shared.IO;

namespace FreeX.Core.Model;

public static class ProtectionPasswordHelper
{
    /// <summary>
    /// Prefix for the ISO/IEC 29500 (ECMA-376) "modern" salted, iterated password-verifier scheme
    /// Excel writes as the <c>algorithmName</c>/<c>hashValue</c>/<c>saltValue</c>/<c>spinCount</c>
    /// attributes of <c>&lt;workbookProtection&gt;</c>/<c>&lt;sheetProtection&gt;</c> (the default
    /// scheme since Excel 2013). Stored form: <c>iso29500:{algorithmName}:{spinCount}:{saltBase64}:{hashBase64}</c>.
    /// </summary>
    private const string Iso29500Prefix = "iso29500:";

    public static string HashNativePassword(string plain) =>
        Sha256PasswordStorage.Encode(plain);

    /// <summary>
    /// Encodes an ISO 29500 modern hash (as read verbatim from a workbook's/worksheet's protection
    /// element) into the stored-password string form that <see cref="VerifyStoredPassword"/> and
    /// round-trip persistence understand.
    /// </summary>
    public static string EncodeIso29500Hash(string? algorithmName, string? spinCount, string? saltValue, string? hashValue) =>
        string.Join(
            ':',
            Iso29500Prefix[..^1],
            algorithmName ?? "",
            spinCount ?? "",
            saltValue ?? "",
            hashValue ?? "");

    /// <summary>True when <paramref name="stored"/> holds an encoded ISO 29500 modern hash.</summary>
    public static bool IsIso29500Hash(string? stored) =>
        stored is not null && stored.StartsWith(Iso29500Prefix, StringComparison.Ordinal);

    public static bool VerifyStoredPassword(string? stored, string? provided)
    {
        if (string.IsNullOrEmpty(stored))
            return true;

        provided ??= "";
        if (Sha256PasswordStorage.HasPrefix(stored))
            return Sha256PasswordStorage.Verify(stored, provided);

        if (stored.StartsWith(Iso29500Prefix, StringComparison.Ordinal))
            return VerifyIso29500Password(stored, provided);

        // A stored value that is literally identical to what the user typed is always a valid
        // match, independent of whether "stored" happens to also look like a legacy hex hash (a
        // freshly-set plaintext password such as "1234"/"abcd"/"c0de" is indistinguishable in
        // shape from a genuine 4-hex-digit legacy hash — see IsLegacyPasswordHash). Checking
        // plaintext equality first means that ambiguity can never cause a *correct* password to
        // be rejected; the legacy-hash interpretation below is only a fallback for the case where
        // "stored" really is a hash produced by a different password than the one just supplied.
        if (string.Equals(stored, provided, StringComparison.Ordinal))
            return true;

        return IsLegacyPasswordHash(stored) &&
            string.Equals(ComputeLegacyPasswordHash(provided), stored, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Encodes a legacy (pre-2013, XOR/rotate) password verifier for the ISO/IEC 29500
    /// <c>password</c> attribute. <paramref name="passwordOrHash"/> must be a value already known
    /// by the caller to be a genuine 4-hex-digit legacy hash (e.g. one read verbatim from an
    /// existing file's <c>password</c> attribute and being round-tripped unchanged) — callers must
    /// NOT pass a freshly-typed plaintext password here on the strength of it merely looking like
    /// hex, because a real password such as "1234"/"abcd"/"c0de" is indistinguishable in shape
    /// from an actual hash (see <see cref="IsLegacyPasswordHash"/>). When in doubt, callers should
    /// track provenance explicitly (e.g. "this string came from XML we just read" vs. "this string
    /// is what the user typed into the Protect dialog") rather than relying on this method to guess.
    /// <para>
    /// <b>Known unsafe callers (tracked, not yet fixed here — out of this file's scope):</b>
    /// <c>XlsxWorkbookMetadataWriter</c>/<c>XlsxWorksheetProtectionMetadataWriter</c> (via the
    /// duplicated <c>XlsxWorkbookMetadataXmlHelper.ToLegacyPasswordHash</c>) and
    /// <c>XlsxAllowEditRangeMapper.BuildProtectedRangeElement</c> all call the ambiguous overload on
    /// <c>Workbook.StructureProtectionPassword</c>/<c>Sheet.ProtectionPassword</c>/
    /// <c>Sheet.AllowEditRangePasswords</c> values at <b>save</b> time, where the value may be
    /// freshly-typed plaintext from <c>ProtectSheetCommand</c>/<c>ProtectWorkbookCommand</c>/
    /// <c>AllowEditRangeDialog</c> rather than a hash loaded from a file. A 4-hex-character typed
    /// password (e.g. "beef", "c0de") is therefore written to the saved .xlsx verbatim in cleartext
    /// instead of being hashed. Fixing this requires the command/dialog layer (out of this project's
    /// scope for this change) to hash a freshly-typed password immediately when it is set — e.g. via
    /// <see cref="ToVerifiedLegacyPasswordHash"/> below — so that by the time any writer sees
    /// <c>Sheet.ProtectionPassword</c>/<c>Workbook.StructureProtectionPassword</c>/an
    /// <c>AllowEditRangePasswords</c> value, it is unconditionally already a hash and this method is
    /// only ever asked to round-trip one.
    /// </para>
    /// </summary>
    public static string ToLegacyPasswordHash(string passwordOrHash)
    {
        if (IsLegacyPasswordHash(passwordOrHash))
            return passwordOrHash.ToUpperInvariant();

        return ComputeLegacyPasswordHash(passwordOrHash);
    }

    /// <summary>
    /// Unambiguously hashes a plaintext password the caller knows for certain was just typed by a
    /// user (e.g. in the Protect Sheet/Workbook or Allow-Edit-Range dialogs), never treating it as
    /// an already-computed hash even if it happens to have the 4-hex-digit shape of one. Intended
    /// for callers that currently set <c>Sheet.ProtectionPassword</c>/
    /// <c>Workbook.StructureProtectionPassword</c>/an <c>AllowEditRangePasswords</c> entry directly
    /// from typed input; hashing at that point (instead of at save time via the ambiguous
    /// <see cref="ToLegacyPasswordHash"/>) removes the plaintext-vs-hash ambiguity entirely, because
    /// every value downstream is then guaranteed to already be a hash.
    /// </summary>
    public static string ToVerifiedLegacyPasswordHash(string plaintextPassword) =>
        ComputeLegacyPasswordHash(plaintextPassword);

    /// <summary>
    /// Implements the ECMA-376/MS-OFFCRYPTO "Binary Document Password Verifier Derivation Method
    /// 1" legacy password hash exactly as specified: a 15-bit accumulator is rotated left by one
    /// bit and then XORed with each character, walking the password from its last character to
    /// its first, then rotated once more and XORed with the password length and the fixed
    /// constant 0xCE4B. Every intermediate value is masked to 15 bits by the rotate step itself
    /// (<c>&amp; 0x7fff</c>), so — unlike a naive "shift each character by its index" formulation
    /// — this can never overflow <see cref="int"/> regardless of password length, and the final
    /// accumulator is always within [0, 0xFFFF] so <c>ToString("X4")</c> is always exactly 4 hex
    /// digits for any input, including passwords far longer than 24 characters.
    /// </summary>
    private static string ComputeLegacyPasswordHash(string password)
    {
        var hash = 0;
        for (var index = password.Length - 1; index >= 0; index--)
        {
            hash = ((hash >> 14) & 0x1) | ((hash << 1) & 0x7fff);
            hash ^= password[index];
        }

        hash = ((hash >> 14) & 0x1) | ((hash << 1) & 0x7fff);
        hash ^= password.Length;
        hash ^= 0xCE4B;
        return hash.ToString("X4", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Verifies a password against the ISO/IEC 29500 "modern" iterated-hash scheme:
    /// H0 = Hash(salt || UTF16LE(password)); Hn = Hash(H(n-1) || LE32(n-1)) for n in [1, spinCount];
    /// the final iterate is compared to the stored hash. See ECMA-376 Part 1 §18.3.1.85/18.11.7.
    /// </summary>
    private static bool VerifyIso29500Password(string stored, string provided)
    {
        var parts = stored.Split(':', 5);
        if (parts.Length != 5)
            return false;

        var algorithmName = parts[1];
        if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var spinCount) || spinCount < 0)
            return false;

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expectedHash = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        return OoxmlProtectionPasswordHash.Verify(
            algorithmName,
            provided,
            salt,
            spinCount,
            expectedHash);
    }

    /// <summary>
    /// True when <paramref name="value"/> has the exact shape of a legacy (pre-2013) Excel
    /// password verifier: <see cref="ComputeLegacyPasswordHash"/> always formats its result with
    /// <c>"X4"</c>, i.e. exactly 4 hex digits, zero-padded. A shorter hex-looking string (1-3
    /// characters, e.g. "1", "ab", "abc") can therefore never be a genuine legacy hash and is
    /// always a real plaintext password — that case is unambiguous and rejected here.
    /// <para>
    /// A plaintext password that happens to be exactly 4 hex characters (e.g. "1234", "abcd",
    /// "c0de", "dead", "beef") is genuinely indistinguishable from a real hash by shape alone: this
    /// is a structural ambiguity in the legacy format itself, not something a smarter predicate can
    /// resolve from the string in isolation. Callers that know the provenance of the value (loaded
    /// verbatim from a file's <c>password</c> attribute vs. freshly typed by the user) must use
    /// that context instead of relying on this heuristic — see the caller notes on
    /// <see cref="ToLegacyPasswordHash"/> and <see cref="VerifyStoredPassword"/>, the latter of
    /// which checks literal equality before ever consulting this predicate so an exact-match
    /// password is never rejected because of the ambiguity.
    /// </para>
    /// </summary>
    public static bool IsLegacyPasswordHash(string? value) =>
        value is not null &&
        value.Length == 4 &&
        value.All(ch =>
            ch is >= '0' and <= '9' ||
            ch is >= 'A' and <= 'F' ||
            ch is >= 'a' and <= 'f');
}
