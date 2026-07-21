using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for R62-meta-3: a workbook protected only via Excel's "Windows" checkbox
/// (lockStructure absent, lockWindows + a password -- IsStructureProtected stays false, but the r61
/// fix preserves StructureProtectionPassword on read) must still gate the Protect Workbook dialog
/// into unprotect-mode requiring the existing password, not silently reopen in protect-mode and let
/// ProtectWorkbookCommand overwrite the never-invalid password with whatever the user types (or
/// nothing).
/// </summary>
public sealed class R62_ProtectionStateProjectorWindowsOnlyTests
{
    [Fact]
    public void ForWorkbook_WindowsOnlyProtectedWithStoredPassword_ReportsProtectedRequiringPassword()
    {
        // Mirrors the state XlsxWorkbookMetadataReader now produces for a "Windows only" protected
        // workbook: IsStructureProtected is false, but a password is still stored.
        var workbook = new Workbook("Book1") { StructureProtectionPassword = "stored" };

        var state = ProtectionStateProjector.ForWorkbook(workbook);

        state.IsStructureProtected.Should().BeTrue(
            "a stored protection password (e.g. from a Windows-only lock) must gate the dialog into " +
            "unprotect-mode instead of silently reopening in protect-mode and overwriting the password");
        state.HasPassword.Should().BeTrue();
    }

    [Fact]
    public void ForWorkbook_TrulyUnprotectedWorkbook_StillProjectsUnprotectedDefaults()
    {
        // Sibling no-regression test: a workbook with neither structure protection nor a stored
        // password must still project as unprotected (the ordinary "first time protecting" case).
        var workbook = new Workbook("Book1");

        var state = ProtectionStateProjector.ForWorkbook(workbook);

        state.IsStructureProtected.Should().BeFalse();
        state.HasPassword.Should().BeFalse();
        state.Options.ProtectStructure.Should().BeTrue();
    }

    [Fact]
    public void ForWorkbook_StructureProtectedWithPassword_StillReportsProtected()
    {
        // Sibling no-regression test: the ordinary structure-protected+password case (already
        // covered by ProtectionStateProjectorTests in FreeX.App.Presentation.Tests) must keep working
        // unchanged after broadening the "protected" check to include windows-only+password.
        var workbook = new Workbook("Book1")
        {
            IsStructureProtected = true,
            StructureProtectionPassword = "stored",
        };

        var state = ProtectionStateProjector.ForWorkbook(workbook);

        state.IsStructureProtected.Should().BeTrue();
        state.HasPassword.Should().BeTrue();
        state.Options.ProtectStructure.Should().BeTrue();
    }
}
