using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression test for freex-workbook-protection F1: a workbook that is window-protected with a
/// password but NOT structure-protected (Workbook.IsStructureProtected == false, matching Excel's
/// &lt;workbookProtection lockWindows="1" workbookPassword="..."/&gt; shape, lockStructure absent)
/// must not have its password silently dropped by a round trip through NativeJsonAdapter -- the
/// exact save/load pair AutosaveService and RecoverySnapshotLoader/StartupRecoveryWorkflow use for
/// periodic autosave and crash-recovery snapshots (also the user-facing native .fxl save format,
/// see WorkbookFileAdapterCatalog).
///
/// Before the fix, both NativeJsonAdapter.Save.cs (dto side) and NativeJsonAdapter.cs (workbook
/// side) gated StructureProtectionPassword on IsStructureProtected, so this exact shape lost its
/// password on save, and even a leftover in-flight dto password would additionally get nulled on
/// load -- leaving Windows protection nominally active with no password guarding it, a silent
/// security downgrade (contrast XlsxWorkbookMetadataReader/Writer, which already treat the two
/// independently, see WorkbookWindowsOnlyProtectionPasswordTests.cs).
/// </summary>
public sealed class NativeJsonAdapterWindowsOnlyProtectionPasswordTests
{
    [Fact]
    public void NativeJsonAdapter_SaveLoad_WindowsOnlyProtection_PreservesPassword()
    {
        // Arrange: mirrors what XlsxFileAdapter loads from Excel's Windows-only protection shape --
        // IsStructureProtected is false, but a real password is stored (see
        // WorkbookWindowsOnlyProtectionPasswordTests.XlsxAdapter_LoadWindowsOnlyProtectionWithPassword_PreservesPasswordAndResavesIt).
        var workbook = new Workbook("WindowsOnlyNativeRoundTrip");
        workbook.IsStructureProtected = false;
        workbook.StructureProtectionPassword = "CC81";
        workbook.AddSheet("S1");

        var adapter = new NativeJsonAdapter();

        // Act: exactly the Save+Load pair AutosaveService / RecoverySnapshotLoader perform.
        var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        // Assert: the password must survive even though structure protection was never on.
        loaded.IsStructureProtected.Should().BeFalse();
        loaded.StructureProtectionPassword.Should().Be("CC81",
            "a window-only protection password must not be silently dropped by a native round trip");
    }

    [Fact]
    public void NativeJsonAdapter_SaveLoadSave_WindowsOnlyProtection_PasswordSurvivesRepeatedRoundTrips()
    {
        // A second autosave tick (Save -> Load -> Save -> Load) must not progressively lose the
        // password either -- exercises both the Save-side and Load-side gate together, twice.
        var workbook = new Workbook("WindowsOnlyNativeRepeatedRoundTrip");
        workbook.IsStructureProtected = false;
        workbook.StructureProtectionPassword = "CC81";
        workbook.AddSheet("S1");

        var adapter = new NativeJsonAdapter();

        var first = new MemoryStream();
        adapter.Save(workbook, first);
        first.Position = 0;
        var loadedOnce = adapter.Load(first);

        var second = new MemoryStream();
        adapter.Save(loadedOnce, second);
        second.Position = 0;
        var loadedTwice = adapter.Load(second);

        loadedTwice.IsStructureProtected.Should().BeFalse();
        loadedTwice.StructureProtectionPassword.Should().Be("CC81");
    }

    [Fact]
    public void NativeJsonAdapter_SaveLoad_StructureProtection_StillWorks()
    {
        // Sibling no-regression test: the ordinary case (IsStructureProtected == true with a
        // password) must keep working exactly as before -- both flag and password survive, and
        // the password is hashed (not stored as plaintext) as NativeJsonAdapter_RoundTrip_ProtectionState
        // already asserts elsewhere in this project.
        var workbook = new Workbook("StructureProtectedNativeRoundTrip");
        workbook.IsStructureProtected = true;
        workbook.StructureProtectionPassword = "structure-secret";
        workbook.AddSheet("S1");

        var adapter = new NativeJsonAdapter();
        var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        loaded.IsStructureProtected.Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword(loaded.StructureProtectionPassword, "structure-secret")
            .Should().BeTrue("the stored hash must verify against the original password");
    }

    [Fact]
    public void NativeJsonAdapter_SaveLoad_NoProtectionAtAll_StaysUnprotectedWithNoPassword()
    {
        // Sibling no-regression test: an ordinary unprotected workbook (both IsStructureProtected
        // and the password are their defaults) must not spuriously acquire a password from the
        // no-longer-IsStructureProtected-gated save/load path.
        var workbook = new Workbook("UnprotectedNativeRoundTrip");
        workbook.AddSheet("S1");

        var adapter = new NativeJsonAdapter();
        var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        loaded.IsStructureProtected.Should().BeFalse();
        loaded.StructureProtectionPassword.Should().BeNull();
    }
}
