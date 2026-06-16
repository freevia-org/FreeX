using FreeX.App.Presentation.Protection;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Protection;

public sealed class SheetProtectionOptionsTests
{
    [Fact]
    public void All_lists_every_permission_in_dialog_order()
    {
        var expected = new[]
        {
            SheetProtectionPermission.SelectLockedCells,
            SheetProtectionPermission.SelectUnlockedCells,
            SheetProtectionPermission.FormatCells,
            SheetProtectionPermission.FormatColumns,
            SheetProtectionPermission.FormatRows,
            SheetProtectionPermission.InsertColumns,
            SheetProtectionPermission.InsertRows,
            SheetProtectionPermission.InsertHyperlinks,
            SheetProtectionPermission.DeleteColumns,
            SheetProtectionPermission.DeleteRows,
            SheetProtectionPermission.Sort,
            SheetProtectionPermission.UseAutoFilter,
            SheetProtectionPermission.UsePivotTableReports,
            SheetProtectionPermission.EditObjects,
            SheetProtectionPermission.EditScenarios,
        };

        SheetProtectionOptions.OrderedPermissions.Should().Equal(expected);
    }

    [Fact]
    public void All_covers_every_core_permission_exactly_once()
    {
        var coreValues = Enum.GetValues<SheetProtectionPermission>();

        SheetProtectionOptions.OrderedPermissions
            .Should().HaveCount(coreValues.Length)
            .And.OnlyHaveUniqueItems()
            .And.BeEquivalentTo(coreValues);
    }

    [Fact]
    public void Only_the_two_select_toggles_default_on()
    {
        SheetProtectionOptions.DefaultEnabledPermissions.Should().Equal(
            SheetProtectionPermission.SelectLockedCells,
            SheetProtectionPermission.SelectUnlockedCells);
    }

    [Fact]
    public void Default_enabled_flags_match_the_default_permission_list()
    {
        var enabledFromFlags = SheetProtectionOptions.All
            .Where(option => option.DefaultEnabled)
            .Select(option => option.Permission);

        enabledFromFlags.Should().Equal(SheetProtectionOptions.DefaultEnabledPermissions);
    }
}
