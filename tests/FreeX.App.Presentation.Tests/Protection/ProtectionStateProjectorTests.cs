using FreeX.App.Presentation.Protection;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Protection;

public sealed class ProtectionStateProjectorTests
{
    [Fact]
    public void Unprotected_sheet_projects_default_options()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");

        var state = ProtectionStateProjector.ForSheet(sheet);

        state.IsProtected.Should().BeFalse();
        state.HasPassword.Should().BeFalse();
        state.Options.EnabledPermissions.Should().Equal(
            SheetProtectionOptions.DefaultEnabledPermissions);
    }

    [Fact]
    public void Protected_sheet_projects_current_permissions_and_password_presence()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1")
        {
            IsProtected = true,
            ProtectionPassword = "stored",
        };
        sheet.ProtectionPermissions.Clear();
        sheet.ProtectionPermissions.AddRange(
        [
            SheetProtectionPermission.Sort,
            SheetProtectionPermission.SelectLockedCells,
        ]);

        var state = ProtectionStateProjector.ForSheet(sheet);

        state.IsProtected.Should().BeTrue();
        state.HasPassword.Should().BeTrue();
        // Projected back in canonical dialog order.
        state.Options.EnabledPermissions.Should().Equal(
            SheetProtectionPermission.SelectLockedCells,
            SheetProtectionPermission.Sort);
    }

    [Fact]
    public void Protected_sheet_without_password_reports_no_password()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { IsProtected = true };

        ProtectionStateProjector.ForSheet(sheet).HasPassword.Should().BeFalse();
    }

    [Fact]
    public void Unprotected_workbook_projects_defaults()
    {
        var workbook = new Workbook("Book1");

        var state = ProtectionStateProjector.ForWorkbook(workbook);

        state.IsStructureProtected.Should().BeFalse();
        state.HasPassword.Should().BeFalse();
        state.Options.ProtectStructure.Should().BeTrue();
    }

    [Fact]
    public void Protected_workbook_projects_structure_and_password_presence()
    {
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
