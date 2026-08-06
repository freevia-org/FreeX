using FluentAssertions;

using FreeX.App.Presentation.Protection;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-17 regression tests for two protection-password findings:
/// <list type="bullet">
/// <item>
/// R17-protection-io-1: <see cref="ProtectionPasswordHelper"/>'s legacy password hash must never
/// overflow <see cref="int"/> and must always format as exactly 4 hex digits, even for long
/// passwords, so a correct password is never rejected on reload.
/// </item>
/// <item>
/// R17-protection-io-2: the Avalonia Protect Workbook glue must not build a command that stores a
/// password on a workbook whose structure isn't actually protected (a "Windows only" request,
/// which Core cannot model), since that would leave a passworded-but-unlocked
/// <c>workbookProtection</c> element on disk.
/// </item>
/// </list>
/// </summary>
public sealed class R17_protection_Tests
{
    // ── R17-protection-io-1: legacy password hash overflow ────────────────────

    [Fact]
    public void ToLegacyPasswordHash_MatchesKnownGoldenValues()
    {
        // These golden values pin the ECMA-376/MS-OFFCRYPTO "Binary Document Password Verifier
        // Derivation Method 1" algorithm; they must survive any refactor of the internal
        // implementation untouched.
        ProtectionPasswordHelper.ToLegacyPasswordHash("password").Should().Be("83AF");
        ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash("1234").Should().Be("CC3D");
    }

    [Fact]
    public void ToVerifiedLegacyPasswordHash_ForLongPassword_ProducesFourHexDigitVerifierThatRoundTrips()
    {
        // 26 characters: index reaches 25, which overflowed Int32 and sign-extended under the old
        // "value = password[index] << (index + 1)" formulation, producing more than 4 hex digits
        // (or a value IsLegacyPasswordHash would reject on reload).
        var longPassword = "abcdefghijklmnopqrstuvwxyz";

        var hash = ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash(longPassword);

        hash.Should().HaveLength(4);
        hash.Should().MatchRegex("^[0-9A-F]{4}$");

        // The stored verifier must accept the correct password back (this is the "can never
        // unprotect" failure mode from the finding: IsLegacyPasswordHash requires Length == 4, so
        // a >4-digit verifier would never even reach the hash comparison on reload).
        ProtectionPasswordHelper.VerifyStoredPassword(hash, longPassword).Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword(hash, "wrong-password").Should().BeFalse();
    }

    [Fact]
    public void ToVerifiedLegacyPasswordHash_ForVeryLongPassword_StaysWithinFourHexDigits()
    {
        // Exercises indices well past 24 (where "index + 1" as a shift amount used to overflow
        // Int32) and past 31 (where C#'s "shift count mod 32" semantics used to wrap the shift
        // back to a small amount for a plain "<<" formulation).
        var veryLongPassword = new string('x', 60);

        var hash = ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash(veryLongPassword);

        hash.Should().HaveLength(4);
        ProtectionPasswordHelper.VerifyStoredPassword(hash, veryLongPassword).Should().BeTrue();
    }

    // ── R17-protection-io-2: workbook "Windows only" protection ───────────────

    [Fact]
    public void PlanWorkbook_WithStructureUncheckedAndWindowsChecked_DoesNotProtectOrStorePassword()
    {
        var workbook = new Workbook("Book");
        var options = new ProtectWorkbookOptions
        {
            ProtectStructure = false,
            ProtectWindows = true,
            Password = "pw",
            PasswordConfirmation = "pw",
        };

        var plan = ProtectionWorkflowSession.CreateWorkbookCommandPlan(workbook, options);

        plan.CanExecute.Should().BeFalse();
        plan.Issue.Should().Be(ProtectionWorkflowIssue.WorkbookStructureRequired);
        plan.NormalizedPassword.Should().BeNull();
        // Core has no model for window-only protection: structure must stay unprotected, and no
        // stray password may be written for a workbook nothing actually locks.
        workbook.IsStructureProtected.Should().BeFalse();
        workbook.StructureProtectionPassword.Should().BeNull();
    }

    [Fact]
    public void PlanWorkbook_WithStructureCheckedAndPassword_StillProtectsAsBefore()
    {
        var workbook = new Workbook("Book");
        var options = new ProtectWorkbookOptions
        {
            ProtectStructure = true,
            ProtectWindows = false,
            Password = "pw",
            PasswordConfirmation = "pw",
        };

        var command = ProtectionWorkflowSession.CreateWorkbookCommandPlan(workbook, options).Command!;
        var outcome = command.Apply(new R17ProtectionTestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        workbook.IsStructureProtected.Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword(workbook.StructureProtectionPassword, "pw").Should().BeTrue();
    }

    /// <summary>A minimal <see cref="ICommandContext"/> for running protection commands against a workbook.</summary>
    private sealed class R17ProtectionTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
