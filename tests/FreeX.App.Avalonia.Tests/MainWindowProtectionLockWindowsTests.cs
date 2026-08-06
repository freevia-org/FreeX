using FluentAssertions;

using FreeX.App.Presentation.Protection;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression tests for R48-io-workbook-sheet-protection-3-1: the Avalonia Protect Workbook
/// dialog's "Windows" checkbox must actually round-trip to
/// <c>&lt;workbookProtection lockWindows="1"/&gt;</c> on save instead of being silently dropped.
/// The shared <see cref="ProtectionWorkflowSession"/> threads the dialog's Windows choice onto
/// <see cref="Workbook.ProtectionMetadata"/> inside the composed command.
/// </summary>
public sealed class MainWindowProtectionLockWindowsTests
{
    [Fact]
    public void ApplyWorkbookLockWindows_WindowsChecked_OnBrandNewWorkbook_WritesLockWindowsAttribute()
    {
        // Failure scenario: a never-before-protected workbook, Protect Workbook with both
        // Structure and Windows checked. Pre-fix, ProtectWorkbookOptions.ProtectWindows was
        // collected from the checkbox but never reached any saved-file representation, so
        // workbook.ProtectionMetadata stayed null / carried no lockWindows attribute at all.
        var workbook = new Workbook("Book");
        workbook.ProtectionMetadata.Should().BeNull("a brand-new workbook has no preserved protection metadata yet");

        ApplyWorkbookProtection(workbook, lockWindows: true);

        workbook.ProtectionMetadata.Should().NotBeNull();
        var raw = workbook.ProtectionMetadata!.Get("workbookProtection");
        raw.Should().NotBeNullOrWhiteSpace();
        raw.Should().Contain("lockWindows=\"1\"",
            "checking Windows in the dialog must produce the same on-disk attribute real Excel writes");
    }

    [Fact]
    public void ApplyWorkbookLockWindows_WindowsUnchecked_NoRegression_NoLockWindowsAttributeWritten()
    {
        // Sibling no-regression case: the previously-correct path (Windows left unchecked) must
        // continue to leave no lockWindows attribute behind.
        var workbook = new Workbook("Book");

        ApplyWorkbookProtection(workbook, lockWindows: false);

        var raw = workbook.ProtectionMetadata?.Get("workbookProtection");
        (raw is null || !raw.Contains("lockWindows")).Should().BeTrue(
            "leaving Windows unchecked must not fabricate a lockWindows attribute");
    }

    [Fact]
    public void ApplyWorkbookLockWindows_PreservesUnrelatedAttributesAlreadyInTheBag()
    {
        // A prior load may have preserved unrelated workbookProtection attributes FreeX doesn't
        // model at all (e.g. lockRevision) -- setting lockWindows must not clobber them.
        var workbook = new Workbook("Book");
        var bag = new NativeXmlPreserveBag();
        bag.Set("workbookProtection", "<e lockRevision=\"1\"/>");
        workbook.ProtectionMetadata = bag;

        ApplyWorkbookProtection(workbook, lockWindows: true);

        var raw = workbook.ProtectionMetadata!.Get("workbookProtection");
        raw.Should().Contain("lockRevision=\"1\"");
        raw.Should().Contain("lockWindows=\"1\"");
    }

    [Fact]
    public void ApplyWorkbookLockWindows_DoesNotMutateThePreExistingBagInstance()
    {
        // Guards the shared transition command: ProtectWorkbookCommand's own undo snapshots the
        // pre-command ProtectionMetadata *reference*. If the transition mutated
        // that same instance in place instead of cloning, Undo (Revert restoring the "previous"
        // snapshot) would incorrectly restore a bag that already carries the new lockWindows
        // attribute.
        var workbook = new Workbook("Book");
        var originalBag = new NativeXmlPreserveBag();
        originalBag.Set("workbookProtection", "<e/>");
        workbook.ProtectionMetadata = originalBag;

        ApplyWorkbookProtection(workbook, lockWindows: true);

        // The helper must have replaced ProtectionMetadata with a distinct object...
        workbook.ProtectionMetadata.Should().NotBeSameAs(originalBag);
        // ...leaving the original (what an undo snapshot would have captured) untouched.
        originalBag.Get("workbookProtection").Should().NotContain("lockWindows");
    }

    private static void ApplyWorkbookProtection(Workbook workbook, bool lockWindows)
    {
        var options = ProtectWorkbookOptions.Default with { ProtectWindows = lockWindows };
        var command = ProtectionWorkflowSession.CreateWorkbookCommandPlan(workbook, options).Command!;
        command.Apply(new LockWindowsTestCommandContext(workbook)).Success.Should().BeTrue();
    }

    private sealed class LockWindowsTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException();
    }
}
