using FreeX.App.Presentation.Protection;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Protection;

public sealed class ProtectSheetOptionsTests
{
    [Fact]
    public void Default_has_no_password_and_only_select_toggles()
    {
        ProtectSheetOptions.Default.HasPassword.Should().BeFalse();
        ProtectSheetOptions.Default.EnabledPermissions.Should().Equal(
            SheetProtectionPermission.SelectLockedCells,
            SheetProtectionPermission.SelectUnlockedCells);
    }

    [Fact]
    public void ToCorePermissions_normalises_order_and_drops_duplicates()
    {
        var options = ProtectSheetOptions.FromCorePermissions(
        [
            SheetProtectionPermission.Sort,
            SheetProtectionPermission.FormatCells,
            SheetProtectionPermission.Sort,
            SheetProtectionPermission.SelectLockedCells,
        ]);

        options.ToCorePermissions().Should().Equal(
            SheetProtectionPermission.SelectLockedCells,
            SheetProtectionPermission.FormatCells,
            SheetProtectionPermission.Sort);
    }

    [Fact]
    public void Round_trips_through_core_permission_list()
    {
        var core = new[]
        {
            SheetProtectionPermission.SelectUnlockedCells,
            SheetProtectionPermission.FormatColumns,
            SheetProtectionPermission.UseAutoFilter,
            SheetProtectionPermission.EditScenarios,
        };

        var options = ProtectSheetOptions.FromCorePermissions(core);

        options.ToCorePermissions().Should().BeEquivalentTo(core);
    }

    [Fact]
    public void Applying_to_a_sheet_via_core_command_round_trips()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var options = ProtectSheetOptions.Default with
        {
            Password = "pw",
            PasswordConfirmation = "pw",
            EnabledPermissions =
            [
                SheetProtectionPermission.SelectLockedCells,
                SheetProtectionPermission.FormatCells,
            ],
        };

        // Mirror what a host does once the dialog is accepted.
        sheet.IsProtected = true;
        sheet.ProtectionPassword = options.ToCorePassword();
        sheet.ProtectionPermissions.Clear();
        sheet.ProtectionPermissions.AddRange(options.ToCorePermissions());

        var restored = ProtectSheetOptions.FromCorePermissions(sheet.ProtectionPermissions);
        restored.EnabledPermissions.Should().Equal(options.ToCorePermissions());
        options.ToCorePassword().Should().Be("pw");
    }

    [Fact]
    public void ToCorePassword_is_null_when_no_password()
    {
        ProtectSheetOptions.Default.ToCorePassword().Should().BeNull();

        var withEmpty = ProtectSheetOptions.Default with { Password = "" };
        withEmpty.ToCorePassword().Should().BeNull();
    }

    [Fact]
    public void IsEnabled_reflects_the_enabled_set()
    {
        var options = ProtectSheetOptions.FromCorePermissions([SheetProtectionPermission.Sort]);

        options.IsEnabled(SheetProtectionPermission.Sort).Should().BeTrue();
        options.IsEnabled(SheetProtectionPermission.FormatCells).Should().BeFalse();
    }

    [Fact]
    public void ValidatePassword_flags_mismatch()
    {
        var options = ProtectSheetOptions.Default with { Password = "a", PasswordConfirmation = "b" };

        options.ValidatePassword().ConfirmationMismatch.Should().BeTrue();
    }
}
