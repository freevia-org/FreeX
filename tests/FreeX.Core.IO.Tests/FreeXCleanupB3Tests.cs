using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for FreeX cleanup batch B3 (HIGH findings P92).
/// A native .fxl save must not re-hash a protection password that is already stored as a hash
/// (as it always is on the real command-layer/xlsx-reader path), or the real password can never
/// verify again after a save+load round trip.
/// </summary>
public class FreeXCleanupB3Tests
{
    // P92: Sheet.ProtectionPassword as set by the real command layer (ProtectSheetCommand) is
    // already a legacy 4-hex-digit hash, never plaintext. Saving to .fxl and reloading must
    // preserve a value that still verifies against the originally-typed password via the same
    // ProtectionPasswordHelper.VerifyStoredPassword path UnprotectSheetCommand uses.
    [Fact]
    public void NativeJsonAdapter_SaveLoad_SheetProtectionPasswordFromCommandLayer_StillVerifiesAfterRoundTrip()
    {
        const string typedPassword = "secret";
        var commandLayerHash = ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash(typedPassword);

        var workbook = new Workbook("SheetProtectionRoundTrip");
        var sheet = workbook.AddSheet("S1");
        sheet.IsProtected = true;
        sheet.ProtectionPassword = commandLayerHash;

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.GetSheetAt(0);

        ProtectionPasswordHelper.VerifyStoredPassword(loadedSheet.ProtectionPassword, typedPassword)
            .Should().BeTrue("the real command-layer hash must still verify against the original password after a .fxl round trip");
    }

    // Same scenario for workbook structure protection (StructureProtectionPassword).
    [Fact]
    public void NativeJsonAdapter_SaveLoad_WorkbookStructureProtectionPasswordFromCommandLayer_StillVerifiesAfterRoundTrip()
    {
        const string typedPassword = "structure-secret";
        var commandLayerHash = ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash(typedPassword);

        var workbook = new Workbook("WorkbookStructureProtectionRoundTrip");
        workbook.IsStructureProtected = true;
        workbook.StructureProtectionPassword = commandLayerHash;

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);

        ProtectionPasswordHelper.VerifyStoredPassword(loaded.StructureProtectionPassword, typedPassword)
            .Should().BeTrue("the real command-layer hash must still verify against the original password after a .fxl round trip");
    }

    // A sheet loaded from .xlsx caches its password as an "iso29500:..." hash; saving that
    // workbook to .fxl must not corrupt it either.
    [Fact]
    public void NativeJsonAdapter_SaveLoad_Iso29500HashedPassword_PreservedVerbatim()
    {
        var iso29500Hash = ProtectionPasswordHelper.EncodeIso29500Hash("SHA-512", "100000", "salt==", "hash==");

        var workbook = new Workbook("Iso29500RoundTrip");
        var sheet = workbook.AddSheet("S1");
        sheet.IsProtected = true;
        sheet.ProtectionPassword = iso29500Hash;

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.ProtectionPassword.Should().Be(iso29500Hash);
    }
}
