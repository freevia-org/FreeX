using FluentAssertions;
using System.Diagnostics;
using Free.Shared.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r164 remediation, unbounded declared quantity -- shared tier. The OOXML spin count is a COUNT the
/// document declares (xlsx <c>sheetProtection/@spinCount</c>, docx
/// <c>w:documentProtection/@w:cryptSpinCount</c>) and it reached the hashing loop with only "not
/// negative" checked. Measured before the fix on this machine: 100,000 rounds (what Office writes)
/// took 44ms, 10,000,000 took 2.6s, and a declared 2,000,000,000 had not returned after 20s --
/// extrapolating to hours of CPU for one "unprotect this sheet" click. The classic iteration-count
/// denial of service, and it is shared by all three apps, so the ceiling lives at the one helper both
/// verify and derive funnel through.
/// </summary>
public sealed class R164_ProtectionSpinCountTests
{
    private static readonly byte[] Salt = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

    [Fact]
    public void Derive_SpinCountBeyondTheCeiling_IsRefusedInsteadOfHashingForHours()
    {
        var stopwatch = Stopwatch.StartNew();

        var act = () => OoxmlProtectionPasswordHash.Derive("SHA-512", "pw", Salt, 2_000_000_000);

        act.Should().Throw<NotSupportedException>().WithMessage("*spin count*");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TryDerive_SpinCountBeyondTheCeiling_FailsTheSameWayAnUnsupportedAlgorithmDoes()
    {
        OoxmlProtectionPasswordHash
            .TryDerive("SHA-1", "pw", Salt, OoxmlProtectionPasswordHash.MaximumSpinCount + 1, out var digest)
            .Should().BeFalse();

        digest.Should().BeEmpty();
    }

    [Fact]
    public void Verify_SpinCountBeyondTheCeiling_ReturnsFalseWithoutHashing()
    {
        var stopwatch = Stopwatch.StartNew();

        OoxmlProtectionPasswordHash
            .Verify("SHA-1", "pw", Salt, int.MaxValue, new byte[20])
            .Should().BeFalse();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Derive_TheSpinCountOfficeActuallyWrites_StillRoundTrips()
    {
        // Sibling/no-regression: Office writes 100,000 and FreeW's own writer 50,000, both two orders
        // of magnitude below the ceiling, so real documents verify exactly as before.
        const int officeSpinCount = 100_000;
        var digest = OoxmlProtectionPasswordHash.Derive("SHA-1", "secret", Salt, officeSpinCount);

        OoxmlProtectionPasswordHash.Verify("SHA-1", "secret", Salt, officeSpinCount, digest).Should().BeTrue();
        OoxmlProtectionPasswordHash.Verify("SHA-1", "wrong", Salt, officeSpinCount, digest).Should().BeFalse();
    }
}
