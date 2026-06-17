using FreeX.App.Presentation.Protection;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Protection;

public sealed class ProtectWorkbookOptionsTests
{
    [Fact]
    public void Default_protects_structure_only()
    {
        ProtectWorkbookOptions.Default.ProtectStructure.Should().BeTrue();
        ProtectWorkbookOptions.Default.ProtectWindows.Should().BeFalse();
        ProtectWorkbookOptions.Default.HasPassword.Should().BeFalse();
    }

    [Fact]
    public void ToCoreStructureProtected_reflects_the_structure_toggle()
    {
        var on = ProtectWorkbookOptions.Default;
        var off = ProtectWorkbookOptions.Default with { ProtectStructure = false };

        on.ToCoreStructureProtected().Should().BeTrue();
        off.ToCoreStructureProtected().Should().BeFalse();
    }

    [Fact]
    public void Applying_to_a_workbook_round_trips_structure_and_password()
    {
        var workbook = new Workbook("Book1");
        var options = ProtectWorkbookOptions.Default with
        {
            Password = "pw",
            PasswordConfirmation = "pw",
        };

        workbook.IsStructureProtected = options.ToCoreStructureProtected();
        workbook.StructureProtectionPassword = options.ToCorePassword();

        var restored = ProtectWorkbookOptions.FromCore(workbook.IsStructureProtected);
        restored.ProtectStructure.Should().BeTrue();
        workbook.StructureProtectionPassword.Should().Be("pw");
    }

    [Fact]
    public void Windows_toggle_is_not_persisted_by_core_so_it_does_not_round_trip()
    {
        var options = ProtectWorkbookOptions.Default with { ProtectWindows = true };

        // Core only stores structure; rebuilding from Core loses the windows toggle.
        var restored = ProtectWorkbookOptions.FromCore(options.ToCoreStructureProtected());
        restored.ProtectWindows.Should().BeFalse();
    }

    [Fact]
    public void ToCorePassword_is_null_when_no_password()
    {
        ProtectWorkbookOptions.Default.ToCorePassword().Should().BeNull();
    }

    [Fact]
    public void ValidatePassword_matches_and_mismatches()
    {
        (ProtectWorkbookOptions.Default with { Password = "x", PasswordConfirmation = "x" })
            .ValidatePassword().IsValid.Should().BeTrue();

        (ProtectWorkbookOptions.Default with { Password = "x", PasswordConfirmation = "y" })
            .ValidatePassword().ConfirmationMismatch.Should().BeTrue();
    }
}
