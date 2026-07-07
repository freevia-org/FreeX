using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Focused regression test for FreeX cleanup batch MED10 (round-10 MED/LOW findings).
/// </summary>
public sealed class FreeXCleanupMED10Tests
{
    /// <summary>
    /// P95: a native .fxl round trip must not silently drop an Allow-Edit-Range's own "Range
    /// Password" (Excel's per-range password, distinct from the sheet password). Before the fix,
    /// NativeJsonAdapter serialized only the range addresses (SheetDto.AllowEditRanges) and never
    /// Sheet.AllowEditRangePasswords, so a password-protected range came back from a save/reload with
    /// no password at all — CommandGuards.IsPasswordProtected would then report it unprotected and
    /// any user could edit it without a prompt.
    /// </summary>
    [Fact]
    public void NativeJsonAdapter_RoundTrip_PreservesAllowEditRangePassword()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var protectedRange = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));
        sheet.IsProtected = true;
        sheet.AllowEditRanges.Add(protectedRange);
        var storedPassword = ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash("secret");
        sheet.AllowEditRangePasswords[protectedRange] = storedPassword;

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;
        var loaded = new NativeJsonAdapter().Load(stream);

        var loadedSheet = loaded.GetSheetAt(0);
        var loadedRange = loadedSheet.AllowEditRanges.Should().ContainSingle().Subject;
        var expectedRange = new GridRange(
            new CellAddress(loadedSheet.Id, 2, 2),
            new CellAddress(loadedSheet.Id, 3, 3));
        loadedRange.Should().Be(expectedRange);
        loadedSheet.AllowEditRangePasswords.Should().ContainKey(loadedRange);
        var reloadedPassword = loadedSheet.AllowEditRangePasswords[loadedRange];

        // The password itself must still verify against the original plaintext (proving it round
        // tripped as a real, checkable password) and must reject a wrong one.
        ProtectionPasswordHelper.VerifyStoredPassword(reloadedPassword, "secret").Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword(reloadedPassword, "wrong").Should().BeFalse();
    }
}
