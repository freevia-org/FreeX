using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R27-protection-eval-deep-1: real Excel keeps the Format Cells > Protection tab's Locked/Hidden
/// checkboxes disabled whenever the sheet is protected, no matter which sheet-protection
/// permissions (including "Format cells") are granted -- the sheet must be unprotected first to
/// change either flag. ApplyStyleCommand previously only checked the FormatCells permission before
/// applying the *entire* StyleDiff (including Locked/Hidden), so granting FormatCells (a common,
/// benign-sounding permission meant for recoloring cells) let a user progressively unlock or hide
/// arbitrary cells while the sheet stayed "protected", defeating the protection. ApplyStyleCommand
/// must always reject a Locked/Hidden change while the sheet is protected, regardless of the
/// FormatCells permission, while still allowing ordinary formatting-only changes (the sibling,
/// already-working case) when FormatCells is granted.
/// </summary>
public sealed class R27_ApplyStyleLockedHiddenProtectionTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx, CellAddress Addr) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new TextValue("value"));
        return (wb, sheet, ctx, addr);
    }

    [Fact]
    public void ApplyLockedFalse_OnProtectedSheet_RejectedEvenWithFormatCellsPermission()
    {
        var (wb, sheet, ctx, addr) = Setup();
        var originalStyleId = sheet.GetCell(addr)!.StyleId;
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatCells);

        var outcome = new ApplyStyleCommand(
            sheet.Id,
            new GridRange(addr, addr),
            new StyleDiff(Locked: false)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.GetCell(addr)!.StyleId.Should().Be(originalStyleId);
        wb.GetStyle(sheet.GetCell(addr)!.StyleId).Locked.Should().NotBe(false);
    }

    [Fact]
    public void ApplyHiddenTrue_OnProtectedSheet_RejectedEvenWithFormatCellsPermission()
    {
        var (wb, sheet, ctx, addr) = Setup();
        sheet.SetFormula(addr, "A2*2");
        var originalStyleId = sheet.GetCell(addr)!.StyleId;
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatCells);

        var outcome = new ApplyStyleCommand(
            sheet.Id,
            new GridRange(addr, addr),
            new StyleDiff(Hidden: true)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.GetCell(addr)!.StyleId.Should().Be(originalStyleId);
        wb.GetStyle(sheet.GetCell(addr)!.StyleId).Hidden.Should().NotBe(true);
    }

    [Fact]
    public void ApplyBold_OnProtectedSheetWithFormatCellsPermission_StillAllowed()
    {
        // Sibling already-working case: ordinary formatting (no Locked/Hidden) must remain allowed
        // on a protected sheet once FormatCells permission is granted -- the new guard must not
        // over-reject unrelated style changes.
        var (wb, sheet, ctx, addr) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatCells);

        var outcome = new ApplyStyleCommand(
            sheet.Id,
            new GridRange(addr, addr),
            new StyleDiff(Bold: true)).Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.GetStyle(sheet.GetCell(addr)!.StyleId).Bold.Should().BeTrue();
    }

    [Fact]
    public void ApplyLockedFalse_OnProtectedSheetWithoutFormatCellsPermission_StillRejected()
    {
        // Sibling already-working case: the pre-existing FormatCells-permission guard must still
        // reject any style change (including Locked/Hidden) when the permission is not granted.
        var (wb, sheet, ctx, addr) = Setup();
        var originalStyleId = sheet.GetCell(addr)!.StyleId;
        sheet.IsProtected = true;

        var outcome = new ApplyStyleCommand(
            sheet.Id,
            new GridRange(addr, addr),
            new StyleDiff(Locked: false)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.GetCell(addr)!.StyleId.Should().Be(originalStyleId);
    }

    [Fact]
    public void ApplyLockedFalse_OnUnprotectedSheet_StillAllowed()
    {
        // Sibling already-working case (unchanged from before the fix): no protection at all means
        // Locked/Hidden changes go through exactly as before.
        var (wb, sheet, ctx, addr) = Setup();

        var outcome = new ApplyStyleCommand(
            sheet.Id,
            new GridRange(addr, addr),
            new StyleDiff(Locked: false)).Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.GetStyle(sheet.GetCell(addr)!.StyleId).Locked.Should().BeFalse();
    }
}
